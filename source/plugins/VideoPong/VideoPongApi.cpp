#include "VideoPongApi.h"
#include "VideoPongJson.h"

#include <cstdio>
#include <filesystem>
#include <fstream>

//Two HTTP transports are supported: WinHTTP (built into Windows, so the Windows build doesn't
//need to pull in libcurl at all) and libcurl (everywhere else). Both sides just need to provide
//HttpGet(), HttpDownloadToFile() and UrlEncodePathSegment() with the same signatures below.
#if defined( _WIN32 )
#define _WIN32_WINNT 0x0601//Windows 7+, needed for SHGetKnownFolderPath below (mingw defaults to the XP-era API surface).
#include <windows.h>
#include <winhttp.h>
#define INITGUID//Pulls in the FOLDERID_* GUID values below instead of just declaring them, so we don't need to link a separate import lib for them.
#include <knownfolders.h>
#include <shlobj.h>

#include <algorithm>
#include <cctype>
#include <functional>
#include <vector>
#else
#include <curl/curl.h>
#endif

namespace fs = std::filesystem;

namespace videopong
{
namespace
{
	const char* const kApiBaseUrl = "https://www.videopong.net/api";

#if defined( _WIN32 )
	std::wstring Utf8ToWide( const std::string& text )
	{
		if( text.empty() )
			return std::wstring();
		int len = MultiByteToWideChar( CP_UTF8, 0, text.c_str(), (int)text.size(), nullptr, 0 );
		std::wstring result( len, L'\0' );
		MultiByteToWideChar( CP_UTF8, 0, text.c_str(), (int)text.size(), &result[ 0 ], len );
		return result;
	}

	struct ParsedUrl
	{
		std::wstring host;
		std::wstring path;
		INTERNET_PORT port;
		bool secure;
	};

	bool ParseUrl( const std::string& url, ParsedUrl& out, std::string& errorOut )
	{
		std::wstring wide = Utf8ToWide( url );

		wchar_t hostBuffer[ 256 ]      = {};
		wchar_t pathBuffer[ 2048 ]     = {};
		wchar_t extraInfoBuffer[ 2048 ] = {};

		URL_COMPONENTS components         = {};
		components.dwStructSize           = sizeof( components );
		components.lpszHostName           = hostBuffer;
		components.dwHostNameLength       = 256;
		components.lpszUrlPath            = pathBuffer;
		components.dwUrlPathLength        = 2048;
		components.lpszExtraInfo          = extraInfoBuffer;
		components.dwExtraInfoLength      = 2048;

		if( !WinHttpCrackUrl( wide.c_str(), (DWORD)wide.size(), 0, &components ) )
		{
			errorOut = "Failed to parse URL '" + url + "'";
			return false;
		}

		out.host   = hostBuffer;
		out.path   = std::wstring( pathBuffer ) + extraInfoBuffer;
		out.port   = components.nPort;
		out.secure = components.nScheme == INTERNET_SCHEME_HTTPS;
		return true;
	}

	//Performs a GET request, streaming the body into `sink`. Returns whether the request
	//succeeded (transport ok AND a 2xx status).
	bool HttpGetCommon( const std::string& url, const std::function< void( const char*, size_t ) >& sink, long& outHttpStatus, std::string& errorOut )
	{
		ParsedUrl parsed;
		if( !ParseUrl( url, parsed, errorOut ) )
			return false;

		HINTERNET session = WinHttpOpen( L"FFGL-VideoPong/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0 );
		if( session == nullptr )
		{
			errorOut = "WinHttpOpen failed";
			return false;
		}

		HINTERNET connection = WinHttpConnect( session, parsed.host.c_str(), parsed.port, 0 );
		if( connection == nullptr )
		{
			errorOut = "WinHttpConnect failed";
			WinHttpCloseHandle( session );
			return false;
		}

		DWORD requestFlags = parsed.secure ? WINHTTP_FLAG_SECURE : 0;
		HINTERNET request  = WinHttpOpenRequest( connection, L"GET", parsed.path.c_str(), nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, requestFlags );
		if( request == nullptr )
		{
			errorOut = "WinHttpOpenRequest failed";
			WinHttpCloseHandle( connection );
			WinHttpCloseHandle( session );
			return false;
		}

		BOOL ok = WinHttpSendRequest( request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0 );
		if( ok )
			ok = WinHttpReceiveResponse( request, nullptr );

		if( !ok )
		{
			errorOut = "HTTP request failed (WinHTTP error " + std::to_string( GetLastError() ) + ")";
			WinHttpCloseHandle( request );
			WinHttpCloseHandle( connection );
			WinHttpCloseHandle( session );
			return false;
		}

		DWORD statusCode     = 0;
		DWORD statusCodeSize = sizeof( statusCode );
		WinHttpQueryHeaders( request, WINHTTP_QUERY_FLAG_NUMBER | WINHTTP_QUERY_STATUS_CODE, WINHTTP_HEADER_NAME_BY_INDEX, &statusCode, &statusCodeSize, WINHTTP_NO_HEADER_INDEX );
		outHttpStatus = (long)statusCode;

		if( outHttpStatus < 200 || outHttpStatus >= 300 )
		{
			errorOut = "HTTP request returned status " + std::to_string( outHttpStatus );
			WinHttpCloseHandle( request );
			WinHttpCloseHandle( connection );
			WinHttpCloseHandle( session );
			return false;
		}

		std::vector< char > buffer( 65536 );
		DWORD bytesAvailable = 0;
		while( WinHttpQueryDataAvailable( request, &bytesAvailable ) && bytesAvailable > 0 )
		{
			DWORD toRead   = (DWORD)std::min< size_t >( buffer.size(), bytesAvailable );
			DWORD bytesRead = 0;
			if( !WinHttpReadData( request, buffer.data(), toRead, &bytesRead ) || bytesRead == 0 )
				break;
			sink( buffer.data(), (size_t)bytesRead );
		}

		WinHttpCloseHandle( request );
		WinHttpCloseHandle( connection );
		WinHttpCloseHandle( session );
		return true;
	}

	bool HttpGet( const std::string& url, std::string& outBody, std::string& errorOut )
	{
		outBody.clear();
		long status = 0;
		return HttpGetCommon( url, [ &outBody ]( const char* data, size_t len ) { outBody.append( data, len ); }, status, errorOut );
	}

	bool HttpDownloadToFile( const std::string& url, const std::string& destPath, std::string& errorOut )
	{
		FILE* file = fopen( destPath.c_str(), "wb" );
		if( file == nullptr )
		{
			errorOut = "Could not open '" + destPath + "' for writing";
			return false;
		}

		long status = 0;
		bool ok     = HttpGetCommon( url, [ file ]( const char* data, size_t len ) { fwrite( data, 1, len, file ); }, status, errorOut );
		fclose( file );

		if( !ok )
		{
			std::error_code ec;
			fs::remove( destPath, ec );
			return false;
		}

		return true;
	}

	std::string UrlEncodePathSegment( const std::string& value )
	{
		static const char* const hexDigits = "0123456789ABCDEF";
		std::string result;
		for( unsigned char c : value )
		{
			if( isalnum( c ) || c == '-' || c == '_' || c == '.' || c == '~' )
				result += (char)c;
			else
			{
				result += '%';
				result += hexDigits[ ( c >> 4 ) & 0xF ];
				result += hexDigits[ c & 0xF ];
			}
		}
		return result;
	}
#else
	//curl_global_init/cleanup must only run once per process. A static local is initialised
	//thread-safely exactly once (C++11 magic statics) and torn down at process exit.
	void EnsureCurlInitialised()
	{
		struct CurlGlobalInit
		{
			CurlGlobalInit() { curl_global_init( CURL_GLOBAL_DEFAULT ); }
			~CurlGlobalInit() { curl_global_cleanup(); }
		};
		static CurlGlobalInit init;
		(void)init;
	}

	size_t WriteToString( char* ptr, size_t size, size_t nmemb, void* userdata )
	{
		std::string* out = static_cast< std::string* >( userdata );
		out->append( ptr, size * nmemb );
		return size * nmemb;
	}

	size_t WriteToFile( char* ptr, size_t size, size_t nmemb, void* userdata )
	{
		FILE* file = static_cast< FILE* >( userdata );
		return fwrite( ptr, size, nmemb, file );
	}

	std::string UrlEncodePathSegment( const std::string& value )
	{
		EnsureCurlInitialised();
		CURL* curl = curl_easy_init();
		if( curl == nullptr )
			return value;

		char* escaped       = curl_easy_escape( curl, value.c_str(), (int)value.size() );
		std::string result = escaped != nullptr ? escaped : value;
		if( escaped != nullptr )
			curl_free( escaped );
		curl_easy_cleanup( curl );
		return result;
	}

	//Performs a GET request, streaming the body into `writeCallback`/`writeUserData`.
	//Returns whether the request succeeded (curl transport ok AND a 2xx status).
	bool HttpGetCommon( const std::string& url, void* writeUserData, size_t ( *writeCallback )( char*, size_t, size_t, void* ), long& outHttpStatus, std::string& errorOut )
	{
		EnsureCurlInitialised();

		CURL* curl = curl_easy_init();
		if( curl == nullptr )
		{
			errorOut = "Failed to initialise curl";
			return false;
		}

		char curlError[ CURL_ERROR_SIZE ] = { 0 };
		curl_easy_setopt( curl, CURLOPT_URL, url.c_str() );
		curl_easy_setopt( curl, CURLOPT_WRITEFUNCTION, writeCallback );
		curl_easy_setopt( curl, CURLOPT_WRITEDATA, writeUserData );
		curl_easy_setopt( curl, CURLOPT_FOLLOWLOCATION, 1L );
		curl_easy_setopt( curl, CURLOPT_MAXREDIRS, 5L );
		curl_easy_setopt( curl, CURLOPT_CONNECTTIMEOUT, 10L );
		curl_easy_setopt( curl, CURLOPT_TIMEOUT, 60L );
		curl_easy_setopt( curl, CURLOPT_USERAGENT, "FFGL-VideoPong/1.0" );
		curl_easy_setopt( curl, CURLOPT_ERRORBUFFER, curlError );

		CURLcode result = curl_easy_perform( curl );
		curl_easy_getinfo( curl, CURLINFO_RESPONSE_CODE, &outHttpStatus );
		curl_easy_cleanup( curl );

		if( result != CURLE_OK )
		{
			errorOut = std::string( "HTTP request failed: " ) + ( curlError[ 0 ] != '\0' ? curlError : curl_easy_strerror( result ) );
			return false;
		}

		if( outHttpStatus < 200 || outHttpStatus >= 300 )
		{
			errorOut = "HTTP request returned status " + std::to_string( outHttpStatus );
			return false;
		}

		return true;
	}

	bool HttpGet( const std::string& url, std::string& outBody, std::string& errorOut )
	{
		outBody.clear();
		long status = 0;
		return HttpGetCommon( url, &outBody, &WriteToString, status, errorOut );
	}

	bool HttpDownloadToFile( const std::string& url, const std::string& destPath, std::string& errorOut )
	{
		FILE* file = fopen( destPath.c_str(), "wb" );
		if( file == nullptr )
		{
			errorOut = "Could not open '" + destPath + "' for writing";
			return false;
		}

		long status = 0;
		bool ok     = HttpGetCommon( url, file, &WriteToFile, status, errorOut );
		fclose( file );

		if( !ok )
		{
			std::error_code ec;
			fs::remove( destPath, ec );
			return false;
		}

		return true;
	}
#endif

	//Downloaded clips live under Documents\Resolume so they're easy for the user to find, and so
	//they can add that folder as a Sources favorite in Resolume itself for native drag-and-drop
	//access - a plain system temp folder (the previous default) isn't somewhere a host normally
	//points its media browser at, and can get cleared by the OS/other cleanup tools.
	fs::path GetCacheDirectory()
	{
		std::error_code ec;
		fs::path dir;

#if defined( _WIN32 )
		PWSTR documentsPath = nullptr;
		if( SUCCEEDED( SHGetKnownFolderPath( FOLDERID_Documents, 0, nullptr, &documentsPath ) ) )
		{
			dir = fs::path( documentsPath ) / "Resolume" / "VideoPong Clips";
			CoTaskMemFree( documentsPath );
		}
#endif

		if( dir.empty() )
			dir = fs::temp_directory_path( ec ) / "videopong-cache";

		fs::create_directories( dir, ec );
		return dir;
	}

	//Thumbnails are just an internal performance cache (avoid re-downloading the same images on
	//every search/page), not something the user needs to see or browse, so unlike the clip cache
	//above this lives in the per-user local app data folder rather than Documents, and the folder
	//is marked hidden.
	fs::path GetThumbnailCacheDirectory()
	{
		std::error_code ec;
		fs::path dir;

#if defined( _WIN32 )
		PWSTR localAppDataPath = nullptr;
		if( SUCCEEDED( SHGetKnownFolderPath( FOLDERID_LocalAppData, 0, nullptr, &localAppDataPath ) ) )
		{
			dir = fs::path( localAppDataPath ) / "VideoPong" / "ThumbnailCache";
			CoTaskMemFree( localAppDataPath );
		}
#endif

		if( dir.empty() )
			dir = fs::temp_directory_path( ec ) / "videopong-thumbnail-cache";

		fs::create_directories( dir, ec );

#if defined( _WIN32 )
		SetFileAttributesW( dir.wstring().c_str(), FILE_ATTRIBUTE_HIDDEN );
#endif

		return dir;
	}

	//The public API docs don't fully pin down how search/random results are wrapped, so this
	//walks a handful of plausible shapes (a bare clip object, an array of clips, or an object
	//with the clip(s) nested under a "clip"/"clips"/"results"/"result"/"data" key) rather than
	//assuming one exact schema. Stops descending into further candidate keys as soon as one of
	//them yields at least one clip.
	void CollectClipNodes( const JsonValue& node, std::vector< const JsonValue* >& out, int depthRemaining )
	{
		if( depthRemaining <= 0 )
			return;

		if( node.IsObject() && node.HasMember( "id" ) && node.HasMember( "url_preview_mp4" ) )
		{
			out.push_back( &node );
			return;
		}

		if( node.IsArray() )
		{
			for( size_t i = 0; i < node.Size(); ++i )
				CollectClipNodes( node[ i ], out, depthRemaining - 1 );
			return;
		}

		if( node.IsObject() )
		{
			static const char* const candidateKeys[] = { "clip", "clips", "results", "result", "data" };
			for( const char* key : candidateKeys )
			{
				if( !node.HasMember( key ) )
					continue;
				CollectClipNodes( node[ key ], out, depthRemaining - 1 );
				if( !out.empty() )
					return;
			}
		}
	}
}//anonymous namespace

bool Api::ParseClipsFromResponse( const std::string& json, std::vector< ClipInfo >& outClips, std::string& errorOut )
{
	JsonValue root;
	std::string parseError;
	if( !JsonValue::Parse( json, root, &parseError ) )
	{
		errorOut = "Failed to parse API response: " + parseError;
		return false;
	}

	if( root.IsObject() && root.HasMember( "status" ) )
	{
		const JsonValue& status = root[ "status" ];
		int errorCode           = status[ "error" ].AsInt( 0 );
		if( errorCode != 0 )
		{
			errorOut = "API error " + std::to_string( errorCode ) + ": " + status[ "errorstring" ].AsString( "unknown error" );
			return false;
		}
	}

	std::vector< const JsonValue* > clipNodes;
	CollectClipNodes( root, clipNodes, 4 );

	for( const JsonValue* clipNode : clipNodes )
	{
		ClipInfo clip;
		clip.id            = ( *clipNode )[ "id" ].AsString();
		clip.title         = ( *clipNode )[ "title" ].AsString();
		clip.urlPreviewMp4 = ( *clipNode )[ "url_preview_mp4" ].AsString();
		clip.urlThumb      = ( *clipNode )[ "url_thumb" ].AsString();

		if( !clip.id.empty() && !clip.urlPreviewMp4.empty() )
			outClips.push_back( std::move( clip ) );
	}

	if( outClips.empty() )
	{
		errorOut = "Could not find any clip data in the API response";
		return false;
	}

	return true;
}

bool Api::RequestClips( const std::string& url, std::vector< ClipInfo >& outClips, std::string& errorOut )
{
	std::string body;
	if( !HttpGet( url, body, errorOut ) )
		return false;

	return ParseClipsFromResponse( body, outClips, errorOut );
}

bool Api::SearchClips( const std::string& query, int offset, int count, std::vector< ClipInfo >& outClips, std::string& errorOut )
{
	std::string url = std::string( kApiBaseUrl ) + "/clip/search/" + UrlEncodePathSegment( query ) + "/" + std::to_string( offset ) + "/" + std::to_string( count );
	return RequestClips( url, outClips, errorOut );
}

bool Api::DownloadToMemory( const std::string& url, std::string& outBytes, std::string& errorOut )
{
	return HttpGet( url, outBytes, errorOut );
}

bool Api::DownloadThumbnail( const ClipInfo& clip, std::string& outBytes, std::string& errorOut )
{
	fs::path cachePath = GetThumbnailCacheDirectory() / ( clip.id + ".thumb" );

	std::error_code existsError;
	if( fs::exists( cachePath, existsError ) && fs::file_size( cachePath, existsError ) > 0 )
	{
		std::ifstream cacheFile( cachePath, std::ios::binary );
		if( cacheFile )
		{
			outBytes.assign( std::istreambuf_iterator< char >( cacheFile ), std::istreambuf_iterator< char >() );
			if( !outBytes.empty() )
				return true;
		}
	}

	if( clip.urlThumb.empty() )
	{
		errorOut = "Clip has no thumbnail image";
		return false;
	}

	if( !DownloadToMemory( clip.urlThumb, outBytes, errorOut ) )
		return false;

	//Best-effort write-through cache - failing to write it isn't fatal, we'd just re-download
	//next time this clip's thumbnail is needed.
	std::ofstream cacheFile( cachePath, std::ios::binary );
	if( cacheFile )
		cacheFile.write( outBytes.data(), (std::streamsize)outBytes.size() );

	return true;
}

std::string Api::GetCacheDirectoryPath()
{
	return GetCacheDirectory().string();
}

bool Api::IsClipCached( const ClipInfo& clip )
{
	fs::path finalPath = GetCacheDirectory() / ( clip.id + ".mp4" );
	std::error_code ec;
	return fs::exists( finalPath, ec ) && fs::file_size( finalPath, ec ) > 0;
}

bool Api::DownloadClipToCache( const ClipInfo& clip, std::string& outLocalPath, std::string& errorOut )
{
	fs::path cacheDir  = GetCacheDirectory();
	fs::path finalPath = cacheDir / ( clip.id + ".mp4" );

	if( IsClipCached( clip ) )
	{
		//Already downloaded by a previous run/fetch, no need to hit the network again.
		outLocalPath = finalPath.string();
		return true;
	}

	fs::path partialPath = cacheDir / ( clip.id + ".mp4.part" );
	if( !HttpDownloadToFile( clip.urlPreviewMp4, partialPath.string(), errorOut ) )
	{
		std::error_code ec;
		fs::remove( partialPath, ec );
		return false;
	}

	std::error_code renameError;
	fs::rename( partialPath, finalPath, renameError );
	if( renameError )
	{
		errorOut = "Failed to move downloaded file into cache: " + renameError.message();
		std::error_code ec;
		fs::remove( partialPath, ec );
		return false;
	}

	outLocalPath = finalPath.string();
	return true;
}

}//namespace videopong
