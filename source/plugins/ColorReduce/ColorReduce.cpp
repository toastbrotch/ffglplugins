#include "ColorReduce.h"
#include <cstring>
using namespace ffglex;

enum ParamType : FFUInt32
{
	PT_COLORS,
	PT_PALROT
};

static CFFGLPluginInfo PluginInfo(
	PluginFactory< ColorReduce >,
	"CR01",                          // Plugin unique ID
	"ColorReduce",                   // Plugin name
	2,                                // API major version number
	1,                                // API minor version number
	1,                                // Plugin major version number
	000,                              // Plugin minor version number
	FF_EFFECT,                        // Plugin type
	"Dominant N-colour reduction",
	"Finds the N most dominant colours in the frame and remaps every pixel to its nearest one."
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

// PaletteSize/Shift are uniforms rather than compile-time constants, but that's fine in core
// profile GLSL (unlike the GLSL ES 1.00 shaders elsewhere in this project) - loops with a
// non-constant, uniform-derived bound are allowed here.
static const char remapShaderCode[] = R"(#version 410 core
uniform sampler2D InputTexture;
uniform vec3 Palette[64];
uniform int PaletteSize;
uniform int Shift;
in vec2 uv;
out vec4 fragColor;
void main()
{
	vec4 c = texture( InputTexture, uv );
	if( PaletteSize <= 0 )
	{
		fragColor = c;// passthrough: N >= MAX_COLORS in the original disables reduction entirely
		return;
	}
	if( c.a > 0.0 )
		c.rgb /= c.a;

	float bestDist = 1.0e9;
	int bestIdx = 0;
	for( int j = 0; j < PaletteSize; ++j )
	{
		vec3 d = c.rgb - Palette[ j ];
		float dist = dot( d, d );
		if( dist < bestDist )
		{
			bestDist = dist;
			bestIdx  = j;
		}
	}
	int rotIdx = ( bestIdx + Shift ) % PaletteSize;
	vec3 outColor = Palette[ rotIdx ];

	fragColor = vec4( outColor * c.a, c.a );
}
)";

ColorReduce::ColorReduce() :
	colorsNorm( 0.1f ),// default near 2 so the effect is obvious, matches the original
	palRotNorm( 0.0f )
{
	SetMinInputs( 1 );
	SetMaxInputs( 1 );

	SetParamInfof( PT_COLORS, "Colors", FF_TYPE_STANDARD );
	SetParamInfof( PT_PALROT, "Pal.Rotate", FF_TYPE_STANDARD );

	displayBuf[ 0 ] = '\0';
}

int ColorReduce::NormToColors( float norm ) const
{
	if( norm < 0.0f ) norm = 0.0f;
	if( norm > 1.0f ) norm = 1.0f;
	int c = (int)( norm * (float)( MAX_COLORS - MIN_COLORS ) + 0.5f ) + MIN_COLORS;
	if( c < MIN_COLORS ) c = MIN_COLORS;
	if( c > MAX_COLORS ) c = MAX_COLORS;
	return c;
}

FFResult ColorReduce::InitGL( const FFGLViewportStruct* vp )
{
	if( !remapShader.Compile( vertexShaderCode, remapShaderCode ) )
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

static inline int quantCh( int v )
{
	int q = v * ColorReduce::HIST_LEVELS / 256;
	if( q >= ColorReduce::HIST_LEVELS ) q = ColorReduce::HIST_LEVELS - 1;
	return q;
}
static inline unsigned char levelCenter( int q )
{
	int c = ( q * 256 + 128 ) / ColorReduce::HIST_LEVELS;
	if( c > 255 ) c = 255;
	return (unsigned char)c;
}
static void sortTopN( int* counts, unsigned char* rs, unsigned char* gs, unsigned char* bs, int total, int N )
{
	for( int i = 0; i < N && i < total; ++i )
	{
		int best = i;
		for( int j = i + 1; j < total; ++j )
			if( counts[ j ] > counts[ best ] ) best = j;
		int tc         = counts[ i ];
		counts[ i ]    = counts[ best ];
		counts[ best ] = tc;
		unsigned char t;
		t = rs[ i ]; rs[ i ] = rs[ best ]; rs[ best ] = t;
		t = gs[ i ]; gs[ i ] = gs[ best ]; gs[ best ] = t;
		t = bs[ i ]; bs[ i ] = bs[ best ]; bs[ best ] = t;
	}
}

FFResult ColorReduce::ProcessOpenGL( ProcessOpenGLStruct* pGL )
{
	if( pGL->numInputTextures < 1 || pGL->inputTextures[ 0 ] == NULL )
		return FF_FAIL;

	const FFGLTextureStruct& tex = *pGL->inputTextures[ 0 ];
	int W = (int)tex.Width, H = (int)tex.Height;
	int HW = (int)tex.HardwareWidth, HH = (int)tex.HardwareHeight;
	if( W <= 0 || H <= 0 || HW <= 0 || HH <= 0 )
		return FF_FAIL;

	int N = NormToColors( colorsNorm );

	FFGLTexCoords maxCoords = GetMaxGLTexCoords( tex );

	if( N < MAX_COLORS )
	{
		// ---- CPU readback of the whole (hardware-sized) texture ----
		readback.resize( (size_t)HW * HH * 3 );
		{
			Scoped2DTextureBinding textureBinding( tex.Handle );
			GLint prevAlign;
			glGetIntegerv( GL_PACK_ALIGNMENT, &prevAlign );
			glPixelStorei( GL_PACK_ALIGNMENT, 1 );// GL_RGB rows aren't 4-byte aligned in general
			glGetTexImage( GL_TEXTURE_2D, 0, GL_RGB, GL_UNSIGNED_BYTE, readback.data() );
			glPixelStorei( GL_PACK_ALIGNMENT, prevAlign );
		}

		// ---- Step 1: histogram over the coarse 6x6x6 RGB grid, used region only ----
		int counts[ HIST_BINS ];
		memset( counts, 0, sizeof( counts ) );
		for( int y = 0; y < H; ++y )
		{
			const unsigned char* row = readback.data() + (size_t)y * HW * 3;
			for( int x = 0; x < W; ++x )
			{
				const unsigned char* px = row + x * 3;
				int qr = quantCh( px[ 0 ] );
				int qg = quantCh( px[ 1 ] );
				int qb = quantCh( px[ 2 ] );
				counts[ qr * HIST_LEVELS * HIST_LEVELS + qg * HIST_LEVELS + qb ]++;
			}
		}

		// ---- Step 2: build palette arrays from non-empty bins ----
		int           palCounts[ HIST_BINS ];
		unsigned char palR[ HIST_BINS ], palG[ HIST_BINS ], palB[ HIST_BINS ];
		int           palSize = 0;
		for( int qr = 0; qr < HIST_LEVELS; ++qr )
			for( int qg = 0; qg < HIST_LEVELS; ++qg )
				for( int qb = 0; qb < HIST_LEVELS; ++qb )
				{
					int idx = qr * HIST_LEVELS * HIST_LEVELS + qg * HIST_LEVELS + qb;
					if( counts[ idx ] > 0 )
					{
						palCounts[ palSize ] = counts[ idx ];
						palR[ palSize ]      = levelCenter( qr );
						palG[ palSize ]      = levelCenter( qg );
						palB[ palSize ]      = levelCenter( qb );
						palSize++;
					}
				}

		// ---- Step 3: top-N dominant colours ----
		int useN = N < palSize ? N : palSize;
		if( useN < 1 ) useN = 1;
		sortTopN( palCounts, palR, palG, palB, palSize, useN );

		int shift = useN > 0 ? (int)( palRotNorm * (float)useN ) % useN : 0;

		ScopedShaderBinding shaderBinding( remapShader.GetGLID() );
		ScopedSamplerActivation activateSampler( 0 );
		Scoped2DTextureBinding textureBinding( tex.Handle );

		remapShader.Set( "InputTexture", 0 );
		remapShader.Set( "MaxUV", maxCoords.s, maxCoords.t );
		remapShader.Set( "PaletteSize", useN );
		remapShader.Set( "Shift", shift );
		for( int j = 0; j < useN; ++j )
		{
			char nm[ 24 ];
			snprintf( nm, sizeof( nm ), "Palette[%d]", j );
			remapShader.Set( nm, palR[ j ] / 255.0f, palG[ j ] / 255.0f, palB[ j ] / 255.0f );
		}

		quad.Draw();
	}
	else
	{
		// N >= MAX_COLORS: passthrough, matching the original's early-out. PaletteSize=0 makes
		// the shader output the sampled input unmodified (see remapShaderCode above).
		ScopedShaderBinding shaderBinding( remapShader.GetGLID() );
		ScopedSamplerActivation activateSampler( 0 );
		Scoped2DTextureBinding textureBinding( tex.Handle );

		remapShader.Set( "InputTexture", 0 );
		remapShader.Set( "MaxUV", maxCoords.s, maxCoords.t );
		remapShader.Set( "PaletteSize", 0 );
		quad.Draw();
	}

	return FF_SUCCESS;
}

FFResult ColorReduce::DeInitGL()
{
	remapShader.FreeGLResources();
	quad.Release();
	readback.clear();
	readback.shrink_to_fit();
	return FF_SUCCESS;
}

FFResult ColorReduce::SetFloatParameter( unsigned int dwIndex, float value )
{
	switch( dwIndex )
	{
	case PT_COLORS:
		colorsNorm = value;
		return FF_SUCCESS;
	case PT_PALROT:
		palRotNorm = value;
		return FF_SUCCESS;
	}
	return FF_FAIL;
}

float ColorReduce::GetFloatParameter( unsigned int index )
{
	switch( index )
	{
	case PT_COLORS: return colorsNorm;
	case PT_PALROT: return palRotNorm;
	}
	return 0.0f;
}

char* ColorReduce::GetParameterDisplay( unsigned int index )
{
	switch( index )
	{
	case PT_COLORS:
	{
		int N = NormToColors( colorsNorm );
		if( N >= MAX_COLORS )
			snprintf( displayBuf, sizeof( displayBuf ), "full" );
		else
			snprintf( displayBuf, sizeof( displayBuf ), "%d colors", N );
		return displayBuf;
	}
	case PT_PALROT:
	{
		int N     = NormToColors( colorsNorm );
		int shift = N > 0 ? (int)( palRotNorm * (float)N ) % N : 0;
		snprintf( displayBuf, sizeof( displayBuf ), "+%d/%d", shift, N );
		return displayBuf;
	}
	}
	return (char*)"";
}
