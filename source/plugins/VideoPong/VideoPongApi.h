#pragma once
#include <string>
#include <vector>

//Thin client for the videopong.net public JSON API (https://www.videopong.net/api/), covering
//just what VideoPong needs: find clips and download a clip's mp4 preview to a local cache file.
//See https://docs.google.com/document/d/1OAcp2bY42Sx3FhoqBhmUGnbGsTUNWKTMjzQpuE97Nw4/pub for the
//API docs. No API key is required.
namespace videopong
{
struct ClipInfo
{
	std::string id;
	std::string title;
	std::string urlPreviewMp4;
	std::string urlThumb;
};

class Api
{
public:
	//Fetches up to `count` clips matching `query`, starting at result number `offset`, via
	///clip/search/<query>/<offset>/<count> - `offset` is how the browser window pages through
	//more than one screenful of results. `count` is capped at 50 by the API.
	static bool SearchClips( const std::string& query, int offset, int count, std::vector< ClipInfo >& outClips, std::string& errorOut );

	//Downloads the clip's preview mp4 into the local cache folder, skipping the download if it's
	//already there from a previous run. On success `outLocalPath` holds the path to the local file.
	static bool DownloadClipToCache( const ClipInfo& clip, std::string& outLocalPath, std::string& errorOut );

	//Whether the clip's preview mp4 is already sitting in the local cache (a cheap disk check,
	//no network), so a browser window can mark it as already-downloaded before it even starts
	//loading thumbnails.
	static bool IsClipCached( const ClipInfo& clip );

	//Downloads arbitrary content straight into memory.
	static bool DownloadToMemory( const std::string& url, std::string& outBytes, std::string& errorOut );

	//Fetches a clip's thumbnail (its url_thumb) for the browser window's grid, via a local,
	//hidden on-disk cache: returns the cached copy if one exists from a previous search, or
	//downloads and caches it otherwise. Separate from DownloadClipToCache's cache - this one is
	//just an internal performance cache, not meant for the user to browse.
	static bool DownloadThumbnail( const ClipInfo& clip, std::string& outBytes, std::string& errorOut );

	//Where DownloadClipToCache() puts (and looks for) downloaded clips.
	static std::string GetCacheDirectoryPath();

private:
	static bool RequestClips( const std::string& url, std::vector< ClipInfo >& outClips, std::string& errorOut );
	static bool ParseClipsFromResponse( const std::string& json, std::vector< ClipInfo >& outClips, std::string& errorOut );
};

}//namespace videopong
