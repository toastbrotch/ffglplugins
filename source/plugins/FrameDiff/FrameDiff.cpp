#include "FrameDiff.h"
#include <ffglex/FFGLScopedFBOBinding.h>//Not pulled in by FFGLSDK.h itself.
using namespace ffglex;

enum ParamType : FFUInt32
{
	PT_FRAMEDELAY,
	PT_HARDNESS
};

static CFFGLPluginInfo PluginInfo(
	PluginFactory< FrameDiff >,
	"FD01",                        // Plugin unique ID
	"FrameDiff",                   // Plugin name
	2,                              // API major version number
	1,                              // API minor version number
	1,                              // Plugin major version number
	000,                            // Plugin minor version number
	FF_EFFECT,                      // Plugin type
	"Frame differencing effect",
	"Outputs the per-pixel difference between the current frame and a delayed one; "
	"unchanged pixels go black, changed pixels go grey/white."
);

static const char vertexShaderCode[] = R"(#version 410 core
uniform vec2 MaxUV;
layout( location = 0 ) in vec4 vPosition;
layout( location = 1 ) in vec2 vUV;
out vec2 uv;
void main()
{
	gl_Position = vPosition;
	uv = vUV * MaxUV;
}
)";

// Pass 1: unpremultiply the live input and stash it, fully opaque, into this frame's ring slot.
// Storing straight (not premultiplied) colour here means the diff pass doesn't need to think
// about alpha at all.
static const char copyShaderCode[] = R"(#version 410 core
uniform sampler2D InputTexture;
in vec2 uv;
out vec4 fragColor;
void main()
{
	vec4 c = texture( InputTexture, uv );
	if( c.a > 0.0 )
		c.rgb /= c.a;
	fragColor = vec4( c.rgb, 1.0 );
}
)";

// Pass 2: Rec.601 luma of the abs difference between this frame's ring slot and the delayed one,
// then the same "Hardness" contrast curve as the original CPU version (scale=1 at hardness=0,
// full soft greyscale; scale=256 at hardness=1, any nonzero difference clips straight to white).
static const char diffShaderCode[] = R"(#version 410 core
uniform sampler2D CurrTex;
uniform sampler2D PastTex;
uniform float Hardness;
in vec2 uv;
out vec4 fragColor;
void main()
{
	vec3 cur  = texture( CurrTex, uv ).rgb;
	vec3 past = texture( PastTex, uv ).rgb;
	float luma  = dot( abs( cur - past ), vec3( 0.299, 0.587, 0.114 ) );
	float scale = 1.0 / ( 1.0 - Hardness + 1.0 / 256.0 );
	float v = clamp( luma * scale, 0.0, 1.0 );
	fragColor = vec4( v, v, v, 1.0 );
}
)";

FrameDiff::FrameDiff() :
	ringReady( false ),
	ringW( 0 ), ringH( 0 ),
	writeHead( 0 ),
	delayNorm( 0.0f ),
	hardnessNorm( 0.0f )
{
	SetMinInputs( 1 );
	SetMaxInputs( 1 );

	SetParamInfof( PT_FRAMEDELAY, "Frame Delay", FF_TYPE_STANDARD );
	SetParamInfof( PT_HARDNESS, "Hardness", FF_TYPE_STANDARD );

	displayBuf[ 0 ] = '\0';
}

int FrameDiff::NormToFrames( float norm ) const
{
	if( norm < 0.0f ) norm = 0.0f;
	if( norm > 1.0f ) norm = 1.0f;
	int f = (int)( norm * (float)( MAX_DELAY_FRAMES - MIN_DELAY_FRAMES ) + 0.5f ) + MIN_DELAY_FRAMES;
	if( f < MIN_DELAY_FRAMES ) f = MIN_DELAY_FRAMES;
	if( f > MAX_DELAY_FRAMES ) f = MAX_DELAY_FRAMES;
	return f;
}

FFResult FrameDiff::InitGL( const FFGLViewportStruct* vp )
{
	if( !copyShader.Compile( vertexShaderCode, copyShaderCode ) )
	{
		DeInitGL();
		return FF_FAIL;
	}
	if( !diffShader.Compile( vertexShaderCode, diffShaderCode ) )
	{
		DeInitGL();
		return FF_FAIL;
	}
	if( !quad.Initialise() )
	{
		DeInitGL();
		return FF_FAIL;
	}

	return CFFGLPlugin::InitGL( vp );
}

FFResult FrameDiff::ProcessOpenGL( ProcessOpenGLStruct* pGL )
{
	if( pGL->numInputTextures < 1 || pGL->inputTextures[ 0 ] == NULL )
		return FF_FAIL;

	const FFGLTextureStruct& tex = *pGL->inputTextures[ 0 ];

	if( !ringReady || ringW != (int)tex.Width || ringH != (int)tex.Height )
	{
		for( int i = 0; i < MAX_DELAY_FRAMES; ++i )
		{
			ring[ i ].Release();
			if( !ring[ i ].Initialise( tex.Width, tex.Height ) )
				return FF_FAIL;
		}
		ringW      = (int)tex.Width;
		ringH      = (int)tex.Height;
		writeHead  = 0;
		ringReady  = true;
	}

	// ScopedFBOBinding reverts the framebuffer binding for us, but not the viewport - save/restore
	// that ourselves so pass 2 renders at the host's actual output size, not the ring slot's.
	GLint savedViewport[ 4 ];
	glGetIntegerv( GL_VIEWPORT, savedViewport );

	FFGLTexCoords maxCoords = GetMaxGLTexCoords( tex );

	// Pass 1: copy the live (unpremultiplied) input into this frame's ring slot.
	{
		ScopedFBOBinding fboBinding( ring[ writeHead ].GetGLID(), ScopedFBOBinding::RB_REVERT );
		ring[ writeHead ].ResizeViewPort();

		ScopedShaderBinding shaderBinding( copyShader.GetGLID() );
		ScopedSamplerActivation activateSampler( 0 );
		Scoped2DTextureBinding textureBinding( tex.Handle );

		copyShader.Set( "InputTexture", 0 );
		copyShader.Set( "MaxUV", maxCoords.s, maxCoords.t );
		quad.Draw();
	}

	glViewport( savedViewport[ 0 ], savedViewport[ 1 ], savedViewport[ 2 ], savedViewport[ 3 ] );

	int delay    = NormToFrames( delayNorm );
	int readHead = ( ( writeHead - delay ) % MAX_DELAY_FRAMES + MAX_DELAY_FRAMES ) % MAX_DELAY_FRAMES;

	// Pass 2: diff this frame's ring slot against the delayed one, straight to the real output.
	// Ring textures are exact-size (no letterboxing), so MaxUV is just (1,1) here.
	{
		ScopedShaderBinding shaderBinding( diffShader.GetGLID() );

		ScopedSamplerActivation curUnit( 0 );
		Scoped2DTextureBinding curBinding( ring[ writeHead ].GetTextureInfo().Handle );
		ScopedSamplerActivation pastUnit( 1 );
		Scoped2DTextureBinding pastBinding( ring[ readHead ].GetTextureInfo().Handle );

		diffShader.Set( "CurrTex", 0 );
		diffShader.Set( "PastTex", 1 );
		diffShader.Set( "Hardness", hardnessNorm );
		diffShader.Set( "MaxUV", 1.0f, 1.0f );
		quad.Draw();
	}

	writeHead = ( writeHead + 1 ) % MAX_DELAY_FRAMES;

	return FF_SUCCESS;
}

FFResult FrameDiff::DeInitGL()
{
	copyShader.FreeGLResources();
	diffShader.FreeGLResources();
	quad.Release();
	for( int i = 0; i < MAX_DELAY_FRAMES; ++i )
		ring[ i ].Release();
	ringReady = false;
	return FF_SUCCESS;
}

FFResult FrameDiff::SetFloatParameter( unsigned int dwIndex, float value )
{
	switch( dwIndex )
	{
	case PT_FRAMEDELAY:
		delayNorm = value;
		return FF_SUCCESS;
	case PT_HARDNESS:
		hardnessNorm = value;
		return FF_SUCCESS;
	}
	return FF_FAIL;
}

float FrameDiff::GetFloatParameter( unsigned int index )
{
	switch( index )
	{
	case PT_FRAMEDELAY: return delayNorm;
	case PT_HARDNESS:   return hardnessNorm;
	}
	return 0.0f;
}

char* FrameDiff::GetParameterDisplay( unsigned int index )
{
	switch( index )
	{
	case PT_FRAMEDELAY:
	{
		int frames = NormToFrames( delayNorm );
		if( frames == 1 )
			snprintf( displayBuf, sizeof( displayBuf ), "1 frame" );
		else
			snprintf( displayBuf, sizeof( displayBuf ), "%d frames", frames );
		return displayBuf;
	}
	case PT_HARDNESS:
		snprintf( displayBuf, sizeof( displayBuf ), "%d%%", (int)( hardnessNorm * 100.0f + 0.5f ) );
		return displayBuf;
	}
	return (char*)"";
}
