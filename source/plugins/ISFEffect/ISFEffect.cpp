#include "ISFEffect.h"
#include "IsfJson.h"

#include <windows.h>
#include <algorithm>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <cmath>

using namespace ffglex;

// Counts plugin instances created this process, so simultaneously-loaded copies get distinct
// uInstanceSeed values even when GetTickCount() ties between them (mirrors ISFBrowser/FreeFrame).
static volatile LONG s_instanceCounter = 0;

// Any address inside this DLL works as the anchor for GetModuleHandleExA's FROM_ADDRESS lookup.
static void ModuleAnchor() {}

static CFFGLPluginInfo PluginInfo(
	PluginFactory< FFGLISFEffect >,
	"ISFE",                          // Plugin unique ID
	"ISF Effect",                    // Plugin name
	2,                                // API major version number
	1,                                // API minor version number
	1,                                // Plugin major version number
	000,                              // Plugin minor version number
	FF_EFFECT,                        // Plugin type
	"Loads and hot-swaps ISF/GLSL filter shaders from disk",
	"Effect sibling of ISF Browser: applies to a clip/layer instead of generating standalone. "
	"A shader's own \"image\" INPUT (usually named inputImage) is bound to the incoming video; "
	"every float/bool/long INPUT becomes its own named, ranged parameter automatically."
);

static const char vertexShaderCode[] = R"(#version 410 core
uniform vec2 MaxUV;
layout( location = 0 ) in vec4 vPosition;
layout( location = 1 ) in vec2 vUV;
out vec2 isf_FragNormCoord;
void main()
{
	gl_Position       = vPosition;
	isf_FragNormCoord = vUV * MaxUV;
}
)";

FFGLISFEffect::FFGLISFEffect() :
	currentIdx( -1 ),
	pendingIdx( -1 ),
	needReload( false ),
	startTime( std::chrono::steady_clock::now() ),
	localTime( 0.0 ),
	lastElapsed( 0.0 ),
	frameIndex( 0 ),
	instanceSeed( 0.0f )
{
	SetMinInputs( 1 );
	SetMaxInputs( 1 );

	displayBuf[ 0 ] = '\0';
	for( unsigned int i = 0; i < MAX_INPUTS; ++i )
		inputNorm[ i ] = 0.0f;

	LONG instNum = InterlockedIncrement( &s_instanceCounter );
	instanceSeed = (float)( GetTickCount() % 100000 ) * 0.01f + (float)instNum * 3.371f;

	ScanShaderFolder();

	int n = (int)fsPaths.size();
	SetOptionParamInfo( PARAM_SHADER, "Shader", n > 0 ? n : 1, 0.0f );
	if( n > 0 )
	{
		for( int i = 0; i < n; ++i )
		{
			float val = ( n == 1 ) ? 0.0f : (float)i / (float)( n - 1 );
			SetParamElementInfo( PARAM_SHADER, i, fsLabels[ i ].c_str(), val );
		}
		pendingIdx = 0;
		needReload = true;
	}
	else
	{
		SetParamElementInfo( PARAM_SHADER, 0, "(no .fs files found)", 0.0f );
	}

	for( unsigned int i = 0; i < MAX_INPUTS; ++i )
	{
		char nm[ 16 ];
		snprintf( nm, sizeof( nm ), "Input %u", i + 1 );
		SetParamInfo( PARAM_INPUT0 + i, nm, FF_TYPE_STANDARD, 0.0f );
		SetParamVisibility( PARAM_INPUT0 + i, false, false );
	}
}

// ------------------------------------------------------------------
void FFGLISFEffect::ScanShaderFolder()
{
	HMODULE hMod = NULL;
	GetModuleHandleExA(
		GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
		(LPCSTR)ModuleAnchor, &hMod );

	char dllPath[ MAX_PATH ] = {};
	GetModuleFileNameA( hMod, dllPath, MAX_PATH );
	char* lastSep = strrchr( dllPath, '\\' );
	if( !lastSep )
		return;
	lastSep[ 1 ] = '\0';
	std::string dir( dllPath );

	WIN32_FIND_DATAA fd;
	HANDLE hFind = FindFirstFileA( ( dir + "*.fs" ).c_str(), &fd );
	if( hFind == INVALID_HANDLE_VALUE )
		return;
	do
	{
		if( !( fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY ) )
			fsPaths.push_back( dir + fd.cFileName );
	} while( FindNextFileA( hFind, &fd ) );
	FindClose( hFind );
	std::sort( fsPaths.begin(), fsPaths.end() );

	for( const std::string& p : fsPaths )
	{
		size_t sep  = p.find_last_of( '\\' );
		size_t dot  = p.find_last_of( '.' );
		std::string base = ( sep != std::string::npos && dot != std::string::npos && dot > sep )
		                        ? p.substr( sep + 1, dot - sep - 1 )
		                        : p;
		fsLabels.push_back( base );
	}
}

// ------------------------------------------------------------------
bool FFGLISFEffect::LoadShader( int idx )
{
	if( idx < 0 || idx >= (int)fsPaths.size() )
		return false;

	FILE* f = fopen( fsPaths[ idx ].c_str(), "rb" );
	if( !f )
	{
		FFGLLog::LogToHost( ( "ISF Effect: could not open " + fsPaths[ idx ] ).c_str() );
		return false;
	}
	fseek( f, 0, SEEK_END );
	long sz = ftell( f );
	fseek( f, 0, SEEK_SET );
	std::string raw( (size_t)sz, '\0' );
	fread( &raw[ 0 ], 1, (size_t)sz, f );
	fclose( f );

	size_t hdrStart = raw.find( "/*" );
	size_t hdrEnd   = ( hdrStart == std::string::npos ) ? std::string::npos : raw.find( "*/", hdrStart );
	std::string header = ( hdrStart != std::string::npos && hdrEnd != std::string::npos )
	                          ? raw.substr( hdrStart + 2, hdrEnd - ( hdrStart + 2 ) )
	                          : std::string( "{}" );
	std::string body = ( hdrEnd != std::string::npos ) ? raw.substr( hdrEnd + 2 ) : raw;
	if( !body.empty() && body[ 0 ] == '\n' )
		body.erase( 0, 1 );

	isfjson::JsonValue root;
	std::string jsonErr;
	if( !isfjson::JsonValue::Parse( header, root, &jsonErr ) )
	{
		FFGLLog::LogToHost( ( "ISF Effect: bad ISF header in " + fsLabels[ idx ] + ": " + jsonErr ).c_str() );
		return false;
	}

	std::vector< IsfInput > newInputs;
	std::string newImageInputName;
	const isfjson::JsonValue& inputsArr = root[ "INPUTS" ];
	for( size_t i = 0; i < inputsArr.Size() && newInputs.size() < MAX_INPUTS; ++i )
	{
		const isfjson::JsonValue& item = inputsArr[ i ];
		std::string type = item[ "TYPE" ].AsString( "float" );
		std::string name = item[ "NAME" ].AsString( "" );
		if( name.empty() )
			continue;

		if( type == "image" )
		{
			// Only the first image input is wired up - real ISF filters declare exactly one
			// (conventionally named inputImage). A second would need a second video input, which
			// FFGL effects with MinInputs(1)/MaxInputs(1) don't have.
			if( newImageInputName.empty() )
				newImageInputName = name;
			continue;
		}

		// v1 scope: numeric slider types only (same as ISFBrowser). color/point2D/audio/audioFFT/
		// event ISF inputs aren't declared as uniforms here.
		if( type != "float" && type != "bool" && type != "long" )
			continue;

		IsfInput inp;
		inp.name       = name;
		inp.label      = item[ "LABEL" ].AsString( inp.name );
		inp.minVal     = (float)item[ "MIN" ].AsDouble( 0.0 );
		inp.maxVal     = (float)item[ "MAX" ].AsDouble( 1.0 );
		inp.defaultVal = (float)item[ "DEFAULT" ].AsDouble( 0.0 );
		if( inp.maxVal <= inp.minVal )
			inp.maxVal = inp.minVal + 1.0f;
		newInputs.push_back( inp );
	}

	std::string preamble =
		"#version 410 core\n"
		"uniform float TIME;\n"
		"uniform float TIMEDELTA;\n"
		"uniform int   FRAMEINDEX;\n"
		"uniform vec2  RENDERSIZE;\n"
		"uniform float uInstanceSeed;\n"
		"in vec2 isf_FragNormCoord;\n"
		"out vec4 fragColor;\n"
		"#define gl_FragColor fragColor\n";
	if( !newImageInputName.empty() )
		preamble += "uniform sampler2D " + newImageInputName + ";\n";
	for( const IsfInput& inp : newInputs )
		preamble += "uniform float " + inp.name + ";\n";

	std::string fsSrc = preamble + body;

	shader.FreeGLResources();
	if( !shader.Compile( vertexShaderCode, fsSrc.c_str() ) )
	{
		FFGLLog::LogToHost( ( "ISF Effect: GLSL compile failed for " + fsLabels[ idx ] ).c_str() );
		return false;
	}

	imageInputName = newImageInputName;
	inputs         = newInputs;
	for( unsigned int i = 0; i < MAX_INPUTS; ++i )
	{
		if( i < inputs.size() )
		{
			const IsfInput& inp = inputs[ i ];
			float range         = inp.maxVal - inp.minVal;
			inputNorm[ i ]      = range > 0.0f ? ( inp.defaultVal - inp.minVal ) / range : 0.5f;
			if( inputNorm[ i ] < 0.0f ) inputNorm[ i ] = 0.0f;
			if( inputNorm[ i ] > 1.0f ) inputNorm[ i ] = 1.0f;
			SetParamDisplayName( PARAM_INPUT0 + i, inp.label, true );
			SetParamRange( PARAM_INPUT0 + i, inp.minVal, inp.maxVal );
			SetParamVisibility( PARAM_INPUT0 + i, true, true );
			// Changing inputNorm[i] above changes what GetFloatParameter reports, but the host
			// only re-reads a parameter's value on its own initiative or in response to this
			// event - without it, Resolume's slider stays wherever it was left for the *previous*
			// shader even though the value driving the new shader has already changed underneath it.
			RaiseParamEvent( PARAM_INPUT0 + i, FF_EVENT_FLAG_VALUE );
		}
		else
		{
			inputNorm[ i ] = 0.0f;
			SetParamVisibility( PARAM_INPUT0 + i, false, true );
			RaiseParamEvent( PARAM_INPUT0 + i, FF_EVENT_FLAG_VALUE );
		}
	}

	currentIdx = idx;
	return true;
}

// ------------------------------------------------------------------
FFResult FFGLISFEffect::InitGL( const FFGLViewportStruct* vp )
{
	if( !quad.Initialise() )
		return FF_FAIL;

	if( pendingIdx >= 0 )
		needReload = true;

	return CFFGLPlugin::InitGL( vp );
}

FFResult FFGLISFEffect::ProcessOpenGL( ProcessOpenGLStruct* pGL )
{
	if( pGL->numInputTextures < 1 || pGL->inputTextures[ 0 ] == NULL )
		return FF_FAIL;
	const FFGLTextureStruct& tex = *pGL->inputTextures[ 0 ];

	double elapsed = std::chrono::duration< double >( std::chrono::steady_clock::now() - startTime ).count();
	double delta   = elapsed - lastElapsed;
	lastElapsed    = elapsed;
	localTime      = elapsed;

	if( needReload )
	{
		LoadShader( pendingIdx );
		needReload = false;
	}

	if( !shader.IsReady() )
		return FF_FAIL;

	GLint vp[ 4 ];
	glGetIntegerv( GL_VIEWPORT, vp );

	ScopedShaderBinding shaderBinding( shader.GetGLID() );

	shader.Set( "TIME", (float)localTime );
	shader.Set( "TIMEDELTA", (float)delta );
	shader.Set( "FRAMEINDEX", frameIndex++ );
	shader.Set( "RENDERSIZE", (float)vp[ 2 ], (float)vp[ 3 ] );
	shader.Set( "uInstanceSeed", instanceSeed );
	for( size_t i = 0; i < inputs.size(); ++i )
	{
		const IsfInput& inp = inputs[ i ];
		float value         = inp.minVal + inputNorm[ i ] * ( inp.maxVal - inp.minVal );
		shader.Set( inp.name.c_str(), value );
	}

	// Only bind a texture if this shader actually declared an image input - the scope has to
	// wrap quad.Draw(), hence the branch here rather than an early return above.
	if( !imageInputName.empty() )
	{
		ScopedSamplerActivation activateSampler( 0 );
		Scoped2DTextureBinding textureBinding( tex.Handle );
		FFGLTexCoords maxCoords = GetMaxGLTexCoords( tex );
		shader.Set( imageInputName.c_str(), 0 );
		shader.Set( "MaxUV", maxCoords.s, maxCoords.t );
		quad.Draw();
	}
	else
	{
		// No image input declared - this shader ignores the incoming video entirely (a generator
		// used in an effect slot). MaxUV is irrelevant since nothing samples it.
		shader.Set( "MaxUV", 1.0f, 1.0f );
		quad.Draw();
	}

	return FF_SUCCESS;
}

FFResult FFGLISFEffect::DeInitGL()
{
	shader.FreeGLResources();
	quad.Release();
	return FF_SUCCESS;
}

// ------------------------------------------------------------------
FFResult FFGLISFEffect::SetFloatParameter( unsigned int dwIndex, float value )
{
	if( dwIndex == PARAM_SHADER )
	{
		int n = (int)fsPaths.size();
		if( n > 0 )
		{
			int idx = ( n == 1 ) ? 0 : (int)roundf( value * (float)( n - 1 ) );
			if( idx < 0 ) idx = 0;
			if( idx >= n ) idx = n - 1;
			if( idx != currentIdx )
			{
				pendingIdx = idx;
				needReload = true;
			}
		}
		return FF_SUCCESS;
	}

	if( dwIndex >= PARAM_INPUT0 && dwIndex < PARAM_INPUT0 + MAX_INPUTS )
	{
		inputNorm[ dwIndex - PARAM_INPUT0 ] = value;
		return FF_SUCCESS;
	}

	return FF_FAIL;
}

float FFGLISFEffect::GetFloatParameter( unsigned int index )
{
	if( index == PARAM_SHADER )
	{
		int n = (int)fsPaths.size();
		if( n <= 1 || currentIdx < 0 )
			return 0.0f;
		return (float)currentIdx / (float)( n - 1 );
	}
	if( index >= PARAM_INPUT0 && index < PARAM_INPUT0 + MAX_INPUTS )
		return inputNorm[ index - PARAM_INPUT0 ];
	return 0.0f;
}

char* FFGLISFEffect::GetParameterDisplay( unsigned int index )
{
	if( index == PARAM_SHADER )
	{
		int n = (int)fsPaths.size();
		if( n == 0 )
			snprintf( displayBuf, sizeof( displayBuf ), "no .fs files found" );
		else
			snprintf( displayBuf, sizeof( displayBuf ), "%d/%d %s",
			          currentIdx + 1, n, fsLabels[ currentIdx ].c_str() );
		return displayBuf;
	}

	if( index >= PARAM_INPUT0 && index < PARAM_INPUT0 + MAX_INPUTS )
	{
		size_t i = index - PARAM_INPUT0;
		if( i < inputs.size() )
		{
			const IsfInput& inp = inputs[ i ];
			float value         = inp.minVal + inputNorm[ i ] * ( inp.maxVal - inp.minVal );
			snprintf( displayBuf, sizeof( displayBuf ), "%s = %.3f", inp.name.c_str(), value );
			return displayBuf;
		}
	}

	return (char*)"";
}
