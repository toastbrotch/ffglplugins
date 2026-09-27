#include "VideoPong.h"
#include "VideoPongApi.h"

#if defined( _WIN32 )
#include <windows.h>
#include <shellapi.h>
//FFGL.h (via FFGLSDK.h, included above through VideoPong.h) temporarily defines NOAPISET and
//NOSHOWWINDOW around its own <windows.h> include and undefines them afterwards, but by then
//winnls.h/winuser.h's include guards are already set, permanently skipping MultiByteToWideChar's
//declaration (in stringapiset.h) and SW_SHOWNORMAL's for the rest of this translation unit.
//SW_SHOWNORMAL (a stable, never-changing Win32 constant: 1) is just spelled out below.
//Including stringapiset.h directly used to work around the missing declaration, but on mingw-w64
//that header also declares CompareStringEx, which needs NLSVERSIONINFO - itself skipped by the
//same NOAPISET/winnls.h include-guard problem, so the header no longer compiles standalone.
//Declaring just the one function we need sidesteps that; the signature is copied verbatim from
//stringapiset.h. WINBASEAPI (a dllimport marker) comes from apisetcconv.h, which - unlike
//winnls.h - isn't NOAPISET-gated and so is safe to include here.
#include <apisetcconv.h>
extern "C" WINBASEAPI int WINAPI MultiByteToWideChar( UINT CodePage, DWORD dwFlags, LPCCH lpMultiByteStr, int cbMultiByte, LPWSTR lpWideCharStr, int cchWideChar );
#endif

using namespace ffglex;

enum ParamType : FFUInt32
{
	PT_BROWSE,
	PT_OPEN_CACHE,
	PT_PLAY,
	PT_LOOP,
};

static CFFGLPluginInfo PluginInfo(
	PluginFactory< VideoPong >,// Create method
	"VPG1",                    // Plugin unique ID
	"videopong.net",           // Plugin name
	2,                         // API major version number
	1,                         // API minor version number
	1,                         // Plugin major version number
	000,                       // Plugin minor version number
	FF_SOURCE,                 // Plugin type
	"Finds and plays clips from videopong.net",// Plugin description
	"Resolume FFGL Example"    // About
);

static const char vertexShaderCode[] = R"(#version 410 core
layout( location = 0 ) in vec4 vPosition;
layout( location = 1 ) in vec2 vUV;

out vec2 uv;

void main()
{
	gl_Position = vPosition;
	uv = vUV;
}
)";

static const char fragmentShaderCode[] = R"(#version 410 core
uniform sampler2D InputTexture;

in vec2 uv;

out vec4 fragColor;

void main()
{
	vec4 color = texture( InputTexture, uv );
	color.a = 1.0;//Decoded video frames are always fully opaque.
	fragColor = color;
}
)";

VideoPong::VideoPong() :
	playing( true ),
	looping( true ),
	pendingClipReady( false ),
	videoTexture( 0 ),
	videoTextureWidth( 0 ),
	videoTextureHeight( 0 )
{
	// Input properties
	SetMinInputs( 0 );
	SetMaxInputs( 0 );

	// Parameters
	SetParamInfof( PT_BROWSE, "Browse Clips", FF_TYPE_EVENT );//Button: opens a popup window to search and pick a clip (Windows only).
	SetParamInfof( PT_OPEN_CACHE, "Open Cache Folder", FF_TYPE_EVENT );//Button: opens the downloaded-clips folder in Explorer (Windows only).
	SetParamInfo( PT_PLAY, "Play", FF_TYPE_BOOLEAN, playing );
	SetParamInfo( PT_LOOP, "Loop", FF_TYPE_BOOLEAN, looping );

	FFGLLog::LogToHost( "Created VideoPong generator" );
}
VideoPong::~VideoPong()
{
}

FFResult VideoPong::InitGL( const FFGLViewportStruct* vp )
{
	if( !shader.Compile( vertexShaderCode, fragmentShaderCode ) )
	{
		DeInitGL();
		return FF_FAIL;
	}
	//flipV: decoded video frames are top-row-first, like most image data, so we flip the quad's
	//v coordinate to match rather than flipping every frame's pixels ourselves.
	if( !quad.Initialise( true ) )
	{
		DeInitGL();
		return FF_FAIL;
	}

	//Use base-class init as success result so that it retains the viewport.
	return CFFGLPlugin::InitGL( vp );
}
FFResult VideoPong::ProcessOpenGL( ProcessOpenGLStruct* pGL )
{
	ConsumePendingClip();

	//FFGL requires us to leave the context in a default state on return, so use these scoped
	//bindings to help us do that.
	ScopedShaderBinding shaderBinding( shader.GetGLID() );
	ScopedSamplerActivation activateSampler( 0 );

	if( decoder )
	{
		std::vector< uint8_t > rgba;
		int w = 0, h = 0;
		if( decoder->TryGetLatestFrame( rgba, w, h ) )
		{
			if( videoTexture == 0 )
				glGenTextures( 1, &videoTexture );

			Scoped2DTextureBinding textureBinding( videoTexture );

			if( w != videoTextureWidth || h != videoTextureHeight )
			{
				glTexParameteri( GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR );
				glTexParameteri( GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR );
				glTexParameteri( GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE );
				glTexParameteri( GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE );
				glTexImage2D( GL_TEXTURE_2D, 0, GL_RGBA, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, rgba.data() );
				videoTextureWidth  = w;
				videoTextureHeight = h;
			}
			else
			{
				glTexSubImage2D( GL_TEXTURE_2D, 0, 0, 0, w, h, GL_RGBA, GL_UNSIGNED_BYTE, rgba.data() );
			}
		}
	}

	if( videoTexture != 0 )
	{
		Scoped2DTextureBinding textureBinding( videoTexture );
		shader.Set( "InputTexture", 0 );
		quad.Draw();
	}
	else
	{
		//Nothing decoded yet (still fetching, or fetching failed): output transparent rather
		//than leftover/undefined framebuffer content.
		glClearColor( 0.0f, 0.0f, 0.0f, 0.0f );
		glClear( GL_COLOR_BUFFER_BIT );
	}

	return FF_SUCCESS;
}
FFResult VideoPong::DeInitGL()
{
	browserWindow.reset();//Closes the popup window (if open) and joins its thread.
	decoder.reset();       //Stops and joins its decode thread.

	if( videoTexture != 0 )
	{
		glDeleteTextures( 1, &videoTexture );
		videoTexture = 0;
	}
	videoTextureWidth  = 0;
	videoTextureHeight = 0;

	shader.FreeGLResources();
	quad.Release();

	return FF_SUCCESS;
}

void VideoPong::OpenBrowser()
{
	if( !browserWindow )
		browserWindow = std::unique_ptr< videopong::BrowserWindow >( new videopong::BrowserWindow() );

	browserWindow->Open( std::string(), [ this ]( const videopong::ClipInfo& clip, const std::string& localPath ) {
		//Runs on the browser window's own thread - no GL calls here.
		{
			std::lock_guard< std::mutex > lock( pendingClipMutex );
			pendingLocalPath  = localPath;
			pendingClipReady  = true;
		}
		FFGLLog::LogToHost( ( "videopong.net: picked '" + clip.title + "' (" + clip.id + ")" ).c_str() );
	} );
}

void VideoPong::OpenCacheFolder()
{
	std::string path = videopong::Api::GetCacheDirectoryPath();

#if defined( _WIN32 )
	int wideLen = MultiByteToWideChar( 65001 /*CP_UTF8*/, 0, path.c_str(), (int)path.size(), nullptr, 0 );
	std::wstring widePath( wideLen, L'\0' );
	MultiByteToWideChar( 65001 /*CP_UTF8*/, 0, path.c_str(), (int)path.size(), &widePath[ 0 ], wideLen );

	ShellExecuteW( nullptr, L"open", widePath.c_str(), nullptr, nullptr, 1 /*SW_SHOWNORMAL*/ );
#else
	FFGLLog::LogToHost( ( "videopong.net: cache folder is at " + path ).c_str() );
#endif
}

void VideoPong::ConsumePendingClip()
{
	std::string newPath;
	bool hasNew = false;
	{
		std::lock_guard< std::mutex > lock( pendingClipMutex );
		if( pendingClipReady )
		{
			newPath           = pendingLocalPath;
			hasNew            = true;
			pendingClipReady  = false;
		}
	}

	if( !hasNew )
		return;

	decoder = std::unique_ptr< videopong::VideoDecoder >( new videopong::VideoDecoder() );
	decoder->Open( newPath );
	decoder->SetPlaying( playing );
	decoder->SetLooping( looping );
}

FFResult VideoPong::SetFloatParameter( unsigned int dwIndex, float value )
{
	switch( dwIndex )
	{
	case PT_BROWSE:
		if( value != 0.0f )
			OpenBrowser();
		break;
	case PT_OPEN_CACHE:
		if( value != 0.0f )
			OpenCacheFolder();
		break;
	case PT_PLAY:
		playing = value != 0.0f;
		if( decoder )
			decoder->SetPlaying( playing );
		break;
	case PT_LOOP:
		looping = value != 0.0f;
		if( decoder )
			decoder->SetLooping( looping );
		break;

	default:
		return FF_FAIL;
	}

	return FF_SUCCESS;
}

float VideoPong::GetFloatParameter( unsigned int index )
{
	switch( index )
	{
	case PT_PLAY:
		return playing ? 1.0f : 0.0f;
	case PT_LOOP:
		return looping ? 1.0f : 0.0f;
	}

	return 0.0f;
}
