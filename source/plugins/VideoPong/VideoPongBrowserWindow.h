#pragma once
#include "VideoPongApi.h"

#include <functional>
#include <memory>
#include <string>

//A native popup window (Windows only) that lets the user search videopong.net and pick a clip to
//play, instead of the plugin just grabbing the first search/random result. Not available on
//other platforms - Open() just logs that and does nothing there.
//
//This header intentionally doesn't include any platform headers, they're only needed inside
//VideoPongBrowserWindow.cpp.
namespace videopong
{
class BrowserWindow
{
public:
	//Called once the user picks a clip: `localPath` already points at the downloaded file in the
	//cache, ready to hand to a decoder. Runs on the browser window's own background thread, not
	//the render thread - no GL calls allowed here.
	using ClipSelectedCallback = std::function< void( const ClipInfo& clip, const std::string& localPath ) >;

	BrowserWindow();
	~BrowserWindow();

	BrowserWindow( const BrowserWindow& )            = delete;
	BrowserWindow& operator=( const BrowserWindow& ) = delete;

	//Opens the window (closing any previous one first) with `initialQuery` pre-filled. No search
	//runs automatically - the user has to search themselves.
	void Open( const std::string& initialQuery, ClipSelectedCallback onClipSelected );

	//Closes the window and joins its thread, if one is open. Safe to call at any time. Any
	//thumbnail downloads still in flight for the page that was showing are abandoned rather than
	//waited on - see Impl::searchGeneration in the .cpp.
	void Close();

private:
	struct Impl;
	//shared_ptr, not unique_ptr: thumbnail-download worker threads capture a copy of this (via
	//std::enable_shared_from_this) so they can safely keep running detached in the background
	//even after Close() lets go of the window itself.
	std::shared_ptr< Impl > impl;
};

}//namespace videopong
