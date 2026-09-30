#include "VideoPongBrowserWindow.h"

#if defined( _WIN32 )

#include <windows.h>
#include <windowsx.h>//For GET_X_LPARAM/GET_Y_LPARAM.
#include <objidl.h>  //For IStream.
#include <gdiplus.h>

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <cstring>
#include <mutex>
#include <thread>
#include <utility>
#include <vector>

#include <FFGLSDK.h>//For FFGLLog::LogToHost.

namespace videopong
{
namespace
{
	enum ControlId : int
	{
		ID_EDIT_SEARCH = 100,
		ID_BUTTON_SEARCH,
		ID_BUTTON_PLAY,
		ID_STATIC_STATUS,
		ID_BUTTON_PREV_PAGE,
		ID_BUTTON_NEXT_PAGE,
		ID_STATIC_PAGE,
	};

	const wchar_t* const kWindowClassName = L"VideoPongBrowserWindowClass";
	const wchar_t* const kGridClassName   = L"VideoPongThumbGridClass";

	//Not for animation (thumbnails are static images) - just how often the UI thread checks
	//whether the search/play worker threads published something new (see ApplyPendingHandoff).
	const UINT_PTR kPollTimerId = 1;
	const UINT kPollTimerMs     = 150;

	//Thumbnail grid layout. Kept fixed (no scrolling) to keep the hit-testing/painting code
	//simple, so a search only shows its first kMaxResults hits.
	const int kGridColumns = 4;
	const int kGridRows    = 3;
	const int kMaxResults  = kGridColumns * kGridRows;
	const int kCellWidth   = 150;
	const int kCellHeight  = 140;
	const int kImageWidth  = 130;
	const int kImageHeight = 90;

	//How many thumbnails to fetch/decode at once. Downloading them one at a time (12 sequential
	//round-trips) was the main reason the grid felt slow to fill in.
	const size_t kThumbnailDownloadConcurrency = 6;

	//Dark theme, accented with the requested cyan/teal pair.
	const COLORREF kColorWindowBg   = RGB( 32, 32, 32 );
	const COLORREF kColorPanelBg    = RGB( 45, 45, 45 );
	const COLORREF kColorFieldBg    = RGB( 60, 60, 60 );
	const COLORREF kColorBorder     = RGB( 75, 75, 75 );
	const COLORREF kColorText       = RGB( 225, 225, 225 );
	const COLORREF kColorTextDim    = RGB( 150, 150, 150 );
	const COLORREF kColorAccent     = RGB( 0x00, 0xdc, 0xef );//Selection outline, pressed buttons.
	const COLORREF kColorAccentDark = RGB( 0x00, 0x64, 0x74 );//Button faces.
	const COLORREF kColorDownloaded  = RGB( 0x2e, 0xc4, 0x62 );//Corner badge: clip already on disk.
	const COLORREF kColorDownloading = kColorAccent;           //Corner badge: download in progress.

	//Whether a clip's mp4 is on disk already, currently being fetched, or neither. Checked
	//against the cache as soon as search results come back, so already-downloaded clips are
	//marked before their thumbnail has even loaded.
	enum class ClipDownloadState
	{
		NotDownloaded,
		Downloading,
		Downloaded,
	};

	std::wstring Utf8ToWide( const std::string& text )
	{
		if( text.empty() )
			return std::wstring();
		int len = MultiByteToWideChar( CP_UTF8, 0, text.c_str(), (int)text.size(), nullptr, 0 );
		std::wstring result( len, L'\0' );
		MultiByteToWideChar( CP_UTF8, 0, text.c_str(), (int)text.size(), &result[ 0 ], len );
		return result;
	}

	std::string WideToUtf8( const std::wstring& text )
	{
		if( text.empty() )
			return std::string();
		int len = WideCharToMultiByte( CP_UTF8, 0, text.c_str(), (int)text.size(), nullptr, 0, nullptr, nullptr );
		std::string result( len, '\0' );
		WideCharToMultiByte( CP_UTF8, 0, text.c_str(), (int)text.size(), &result[ 0 ], len, nullptr, nullptr );
		return result;
	}

	//Retrieves this DLL's own module handle (rather than the host executable's) so the window
	//classes and controls are associated with the right module.
	HMODULE GetOwnModuleHandle()
	{
		HMODULE moduleHandle = nullptr;
		GetModuleHandleExW(
		    GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
		    reinterpret_cast< LPCWSTR >( &GetOwnModuleHandle ),
		    &moduleHandle );
		return moduleHandle;
	}

	//One decoded static thumbnail image.
	struct Thumbnail
	{
		std::unique_ptr< Gdiplus::Bitmap > bitmap;
	};

	//Decodes an in-memory image (the downloaded url_thumb bytes) via GDI+. Returns a Thumbnail
	//with a null bitmap on any failure - the grid just draws a placeholder box for that cell in
	//that case.
	Thumbnail LoadThumbnail( const std::string& bytes )
	{
		Thumbnail result;
		if( bytes.empty() )
			return result;

		HGLOBAL globalMem = GlobalAlloc( GMEM_MOVEABLE, bytes.size() );
		if( globalMem == nullptr )
			return result;

		void* dest = GlobalLock( globalMem );
		memcpy( dest, bytes.data(), bytes.size() );
		GlobalUnlock( globalMem );

		IStream* stream = nullptr;
		//TRUE: the stream takes ownership of globalMem and frees it when released.
		if( CreateStreamOnHGlobal( globalMem, TRUE, &stream ) != S_OK )
		{
			GlobalFree( globalMem );
			return result;
		}

		std::unique_ptr< Gdiplus::Bitmap > bitmap( new Gdiplus::Bitmap( stream ) );
		stream->Release();

		if( bitmap->GetLastStatus() == Gdiplus::Ok )
			result.bitmap = std::move( bitmap );

		return result;
	}

	//Draws the download-state badge in a thumbnail's top-right corner: a plain filled circle so
	//it doesn't depend on any particular font having a checkmark/arrow glyph. Downloaded is a
	//solid dot, downloading adds a small white wedge "clock hand" so the two aren't just
	//different colors (in case of colorblindness or a monochrome screenshot).
	void DrawDownloadBadge( Gdiplus::Graphics& graphics, int cellRight, int cellTop, ClipDownloadState state )
	{
		if( state == ClipDownloadState::NotDownloaded )
			return;

		const float radius  = 8.0f;
		const float centerX = (float)( cellRight - radius - 4 );
		const float centerY = (float)( cellTop + radius + 4 );

		COLORREF fillColor = state == ClipDownloadState::Downloaded ? kColorDownloaded : kColorDownloading;
		Gdiplus::SolidBrush fillBrush( Gdiplus::Color( 255, GetRValue( fillColor ), GetGValue( fillColor ), GetBValue( fillColor ) ) );
		Gdiplus::RectF circleRect( centerX - radius, centerY - radius, radius * 2.0f, radius * 2.0f );
		graphics.FillEllipse( &fillBrush, circleRect );

		Gdiplus::Pen outlinePen( Gdiplus::Color( 200, 0, 0, 0 ), 1.0f );
		graphics.DrawEllipse( &outlinePen, circleRect );

		if( state == ClipDownloadState::Downloaded )
		{
			//Checkmark, drawn as two joined line segments.
			Gdiplus::Pen checkPen( Gdiplus::Color( 255, 255, 255, 255 ), 2.0f );
			Gdiplus::PointF p1( centerX - 3.5f, centerY );
			Gdiplus::PointF p2( centerX - 0.5f, centerY + 3.0f );
			Gdiplus::PointF p3( centerX + 4.0f, centerY - 3.5f );
			graphics.DrawLine( &checkPen, p1, p2 );
			graphics.DrawLine( &checkPen, p2, p3 );
		}
		else
		{
			//Small downward-pointing wedge to read as "in progress" rather than "done".
			Gdiplus::PointF wedge[ 3 ] = {
				Gdiplus::PointF( centerX - 3.0f, centerY - 3.0f ),
				Gdiplus::PointF( centerX + 3.0f, centerY - 3.0f ),
				Gdiplus::PointF( centerX, centerY + 3.5f )
			};
			Gdiplus::SolidBrush wedgeBrush( Gdiplus::Color( 255, 255, 255, 255 ) );
			graphics.FillPolygon( &wedgeBrush, wedge, 3 );
		}
	}
}//anonymous namespace

struct BrowserWindow::Impl : std::enable_shared_from_this< Impl >
{
	std::thread thread;//Owns the window and its message loop.
	std::mutex hwndMutex;
	std::condition_variable hwndCv;
	HWND hwnd = nullptr;

	//Search and clip downloads run on their own threads so the window (and its message loop)
	//never blocks on network I/O. They only ever touch UI state indirectly, through the
	//handoff below - handoffMutex is the only thing shared with the UI thread.
	//
	//Bumped every time a new search or page is requested. Thumbnail-download workers are
	//detached (not joined) so paging isn't stuck waiting for a slow batch of thumbnails to
	//finish; instead they check this before doing any more work and quietly stop once a newer
	//generation has started, so a stale page's downloads can't clobber a newer one's grid. Each
	//detached worker keeps this object alive (via shared_from_this) for as long as it runs, even
	//past Close().
	std::atomic< uint64_t > searchGeneration{ 0 };
	std::thread searchThread;
	std::thread playThread;
	std::atomic< bool > playInProgress{ false };

	std::mutex handoffMutex;
	std::string pendingStatus;
	bool hasPendingStatus = false;
	std::vector< ClipInfo > pendingResults;//Published once, right after the search API call returns.
	std::vector< ClipDownloadState > pendingResultStates;//Parallel to pendingResults - the on-disk check done up front.
	bool pendingHasNextPage = false;
	bool hasPendingResults  = false;
	std::vector< std::pair< int, Thumbnail > > pendingThumbnailUpdates;//Appended to as each one finishes downloading.
	std::vector< std::pair< int, ClipDownloadState > > pendingDownloadStateUpdates;//Appended to as a play download starts/finishes.

	ULONG_PTR gdiplusToken = 0;

	HBRUSH windowBrush = nullptr;
	HBRUSH panelBrush  = nullptr;
	HBRUSH fieldBrush  = nullptr;

	HWND editSearch       = nullptr;
	WNDPROC originalEditProc = nullptr;//So Enter in the search box can trigger a search (see EditSubclassProc).
	HWND gridWindow       = nullptr;
	HWND statusLabel      = nullptr;
	HWND prevPageButton   = nullptr;
	HWND nextPageButton   = nullptr;
	HWND pageLabel        = nullptr;

	//Everything below is owned by the UI thread alone (painting, hit-testing, animating) - never
	//touched directly by searchThread/playThread, only ever updated via the handoff above.
	std::vector< ClipInfo > currentResults;
	std::vector< Thumbnail > thumbnails;
	std::vector< ClipDownloadState > downloadStates;
	int selectedIndex = -1;

	//Paging through a search: lastQuery/currentOffset describe the page currently shown,
	//hasNextPage is a heuristic (the last fetch returned a full page, so there might be more -
	//the API doesn't report a total result count).
	std::string lastQuery;
	int currentOffset  = 0;
	bool hasNextPage    = false;

	ClipSelectedCallback onClipSelected;

	void CreateControls( HWND window )
	{
		HINSTANCE moduleHandle = (HINSTANCE)GetOwnModuleHandle();

		//Edit field + Search button together span the same width as the grid below them.
		const int searchButtonWidth = 80;
		const int searchRowGap      = 8;
		const int searchRowWidth    = kGridColumns * kCellWidth;
		const int searchEditWidth   = searchRowWidth - searchButtonWidth - searchRowGap;

		editSearch = CreateWindowExW( 0, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_BORDER,
		    10, 10, searchEditWidth, 22, window, (HMENU)(INT_PTR)ID_EDIT_SEARCH, moduleHandle, nullptr );
		SetWindowLongPtrW( editSearch, GWLP_USERDATA, reinterpret_cast< LONG_PTR >( this ) );
		originalEditProc = reinterpret_cast< WNDPROC >( SetWindowLongPtrW( editSearch, GWLP_WNDPROC, reinterpret_cast< LONG_PTR >( &Impl::EditSubclassProc ) ) );

		CreateWindowExW( 0, L"BUTTON", L"Search", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
		    10 + searchEditWidth + searchRowGap, 9, searchButtonWidth, 24, window, (HMENU)(INT_PTR)ID_BUTTON_SEARCH, moduleHandle, nullptr );

		gridWindow = CreateWindowExW( WS_EX_CLIENTEDGE, kGridClassName, L"",
		    WS_CHILD | WS_VISIBLE,
		    10, 42, kGridColumns * kCellWidth, kGridRows * kCellHeight,
		    window, nullptr, moduleHandle, nullptr );
		SetWindowLongPtrW( gridWindow, GWLP_USERDATA, reinterpret_cast< LONG_PTR >( this ) );
		SetTimer( gridWindow, kPollTimerId, kPollTimerMs, nullptr );

		//A page of results is at most kMaxResults clips; Prev/Next step through further pages of
		//the same search via the API's offset parameter.
		const int paginationRowY = 42 + kGridRows * kCellHeight + 8;
		const int pageButtonWidth = 80;

		prevPageButton = CreateWindowExW( 0, L"BUTTON", L"< Prev", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
		    10, paginationRowY, pageButtonWidth, 26, window, (HMENU)(INT_PTR)ID_BUTTON_PREV_PAGE, moduleHandle, nullptr );

		pageLabel = CreateWindowExW( 0, L"STATIC", L"", WS_CHILD | WS_VISIBLE | SS_CENTER,
		    10 + pageButtonWidth + searchRowGap, paginationRowY + 4, searchRowWidth - 2 * ( pageButtonWidth + searchRowGap ), 20,
		    window, (HMENU)(INT_PTR)ID_STATIC_PAGE, moduleHandle, nullptr );

		nextPageButton = CreateWindowExW( 0, L"BUTTON", L"Next >", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
		    10 + searchRowWidth - pageButtonWidth, paginationRowY, pageButtonWidth, 26, window, (HMENU)(INT_PTR)ID_BUTTON_NEXT_PAGE, moduleHandle, nullptr );

		const int bottomRowY = paginationRowY + 26 + 8;

		CreateWindowExW( 0, L"BUTTON", L"Play Selected", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW,
		    10, bottomRowY, 130, 28, window, (HMENU)(INT_PTR)ID_BUTTON_PLAY, moduleHandle, nullptr );

		statusLabel = CreateWindowExW( 0, L"STATIC", L"", WS_CHILD | WS_VISIBLE,
		    150, bottomRowY + 4, kGridColumns * kCellWidth - 140, 30,
		    window, (HMENU)(INT_PTR)ID_STATIC_STATUS, moduleHandle, nullptr );

		UpdatePaginationControls();
	}

	//--- Handoff from the search/play worker threads to the UI thread. ---------------------------

	//Every Publish* below is tagged with the generation it was produced for and silently dropped
	//if a newer search/page has since started - see searchGeneration's comment above.

	void PublishStatus( uint64_t generation, const std::string& text )
	{
		if( generation != searchGeneration.load() )
			return;
		std::lock_guard< std::mutex > lock( handoffMutex );
		pendingStatus    = text;
		hasPendingStatus = true;
	}

	//Publishes the clip list (with each one's on-disk state already checked) as soon as the
	//search API call returns, before any thumbnail has even started downloading - the grid can
	//show titles, download badges and placeholder boxes immediately rather than waiting for
	//every thumbnail to finish first.
	void PublishResults( uint64_t generation, std::vector< ClipInfo > results, std::vector< ClipDownloadState > states, bool hasNext )
	{
		if( generation != searchGeneration.load() )
			return;
		std::lock_guard< std::mutex > lock( handoffMutex );
		pendingResults      = std::move( results );
		pendingResultStates = std::move( states );
		pendingHasNextPage  = hasNext;
		hasPendingResults   = true;
	}

	//Publishes one decoded thumbnail as soon as it's ready, so it can appear in the grid without
	//waiting for the others - called concurrently from several thumbnail-download threads.
	void PublishThumbnailUpdate( uint64_t generation, int index, Thumbnail thumbnail )
	{
		if( generation != searchGeneration.load() )
			return;
		std::lock_guard< std::mutex > lock( handoffMutex );
		pendingThumbnailUpdates.emplace_back( index, std::move( thumbnail ) );
	}

	//Publishes a change in a clip's download state (play starting/finishing/failing).
	void PublishDownloadStateUpdate( uint64_t generation, int index, ClipDownloadState state )
	{
		if( generation != searchGeneration.load() )
			return;
		std::lock_guard< std::mutex > lock( handoffMutex );
		pendingDownloadStateUpdates.emplace_back( index, state );
	}

	//Called from the UI thread (on the poll timer) to pick up anything a worker published.
	void ApplyPendingHandoff()
	{
		std::string statusToApply;
		bool applyStatus = false;
		std::vector< ClipInfo > resultsToApply;
		std::vector< ClipDownloadState > resultStatesToApply;
		bool resultsHasNextPage = false;
		bool applyResults       = false;
		std::vector< std::pair< int, Thumbnail > > thumbUpdatesToApply;
		std::vector< std::pair< int, ClipDownloadState > > stateUpdatesToApply;

		{
			std::lock_guard< std::mutex > lock( handoffMutex );
			if( hasPendingStatus )
			{
				statusToApply    = std::move( pendingStatus );
				applyStatus      = true;
				hasPendingStatus = false;
			}
			if( hasPendingResults )
			{
				resultsToApply      = std::move( pendingResults );
				resultStatesToApply = std::move( pendingResultStates );
				resultsHasNextPage  = pendingHasNextPage;
				applyResults        = true;
				hasPendingResults   = false;
			}
			if( !pendingThumbnailUpdates.empty() )
				thumbUpdatesToApply = std::move( pendingThumbnailUpdates );
			if( !pendingDownloadStateUpdates.empty() )
				stateUpdatesToApply = std::move( pendingDownloadStateUpdates );
		}

		if( applyStatus )
			SetWindowTextW( statusLabel, Utf8ToWide( statusToApply ).c_str() );

		bool needsRepaint = false;

		if( applyResults )
		{
			currentResults = std::move( resultsToApply );
			thumbnails.clear();
			thumbnails.resize( currentResults.size() );//Empty placeholders up front, filled in as thumbnails arrive.
			downloadStates = std::move( resultStatesToApply );
			downloadStates.resize( currentResults.size() );//In case of a size mismatch, never index out of range.
			selectedIndex = -1;
			needsRepaint  = true;

			hasNextPage = resultsHasNextPage;
			UpdatePaginationControls();
		}

		for( auto& indexAndThumbnail : thumbUpdatesToApply )
		{
			int index = indexAndThumbnail.first;
			if( index >= 0 && index < (int)thumbnails.size() )
			{
				thumbnails[ index ] = std::move( indexAndThumbnail.second );
				needsRepaint        = true;
			}
		}

		for( auto& indexAndState : stateUpdatesToApply )
		{
			int index = indexAndState.first;
			if( index >= 0 && index < (int)downloadStates.size() )
			{
				downloadStates[ index ] = indexAndState.second;
				needsRepaint            = true;
			}
		}

		if( needsRepaint )
			InvalidateRect( gridWindow, nullptr, TRUE );
	}

	//--- Search: runs entirely on searchThread, never touches the UI directly. -------------------

	void UpdatePaginationControls()
	{
		int pageNumber = currentOffset / kMaxResults + 1;
		SetWindowTextW( pageLabel, ( L"Page " + std::to_wstring( pageNumber ) ).c_str() );

		EnableWindow( prevPageButton, currentOffset > 0 );
		EnableWindow( nextPageButton, hasNextPage );
		InvalidateRect( prevPageButton, nullptr, TRUE );
		InvalidateRect( nextPageButton, nullptr, TRUE );
	}

	void StartSearch()
	{
		wchar_t buffer[ 256 ];
		GetWindowTextW( editSearch, buffer, 256 );
		std::string query = WideToUtf8( buffer );

		if( query.empty() )
		{
			//No random/unrequested clips - a search needs an actual search term.
			SetWindowTextW( statusLabel, L"Enter a search term" );
			return;
		}

		PerformSearch( query, 0 );
	}

	void GoToPrevPage()
	{
		if( lastQuery.empty() || currentOffset <= 0 )
			return;
		PerformSearch( lastQuery, std::max( 0, currentOffset - kMaxResults ) );
	}

	void GoToNextPage()
	{
		if( lastQuery.empty() || !hasNextPage )
			return;
		PerformSearch( lastQuery, currentOffset + kMaxResults );
	}

	void PerformSearch( std::string query, int offset )
	{
		//Invalidates any thumbnail downloads (and their eventual publishes) still in flight for
		//whatever page was showing before - that's what lets Prev/Next work immediately even
		//while the previous page's thumbnails are still loading, instead of being ignored until
		//they finish.
		uint64_t generation = ++searchGeneration;

		//SearchWorker itself is just the one API call now (thumbnail downloads run detached, see
		//StartThumbnailDownloads), so this join is quick even if a search was still in flight.
		if( searchThread.joinable() )
			searchThread.join();

		lastQuery     = query;
		currentOffset = offset;

		//Clear the grid and force it to actually repaint (blank) right now, on the UI thread,
		//before the worker even starts - otherwise the previous results would stay on screen
		//while the search runs and it'd look like they were never cleared.
		currentResults.clear();
		thumbnails.clear();
		downloadStates.clear();
		selectedIndex = -1;
		InvalidateRect( gridWindow, nullptr, TRUE );
		UpdateWindow( gridWindow );

		searchThread = std::thread( &Impl::SearchWorker, this, query, offset, generation );
	}

	void SearchWorker( std::string query, int offset, uint64_t generation )
	{
		PublishStatus( generation, "Searching..." );

		std::vector< ClipInfo > clips;
		std::string error;
		if( !Api::SearchClips( query, offset, kMaxResults, clips, error ) )
		{
			PublishStatus( generation, "Error: " + error );
			PublishResults( generation, {}, {}, false );
			return;
		}

		//A full page came back, so there might be more - the API doesn't report a total count.
		bool hasNext = clips.size() >= (size_t)kMaxResults;
		if( clips.size() > (size_t)kMaxResults )
			clips.resize( kMaxResults );

		//Check the cache up front so already-downloaded clips are marked as such before their
		//thumbnail has even loaded - a plain disk check, no network involved.
		std::vector< ClipDownloadState > states;
		states.reserve( clips.size() );
		for( const ClipInfo& clip : clips )
			states.push_back( Api::IsClipCached( clip ) ? ClipDownloadState::Downloaded : ClipDownloadState::NotDownloaded );

		//Show titles/placeholders right away rather than waiting for every thumbnail to finish.
		PublishStatus( generation, std::to_string( clips.size() ) + " clip(s) found - loading thumbnails..." );
		PublishResults( generation, clips, states, hasNext );

		StartThumbnailDownloads( std::move( clips ), generation );
	}

	//Fetches thumbnails with several downloads in flight at once (url_thumb - a plain static
	//image, not the much heavier animated url_thumb_gif - so decoding is cheap too). Runs
	//detached rather than being joined by SearchWorker, specifically so a slow batch of
	//thumbnails can never block starting the next search or turning the page - each worker just
	//checks `generation` and quietly stops once it's stale. Each detached thread holds a
	//shared_ptr to this object (via shared_from_this) so it stays alive for as long as they run,
	//even if the window is closed in the meantime.
	void StartThumbnailDownloads( std::vector< ClipInfo > clips, uint64_t generation )
	{
		std::shared_ptr< Impl > self = shared_from_this();
		size_t total                 = clips.size();
		auto sharedClips             = std::make_shared< std::vector< ClipInfo > >( std::move( clips ) );
		auto nextIndex               = std::make_shared< std::atomic< size_t > >( 0 );
		auto completedCount          = std::make_shared< std::atomic< size_t > >( 0 );

		size_t workerCount = std::min( kThumbnailDownloadConcurrency, total );
		for( size_t w = 0; w < workerCount; ++w )
		{
			std::thread( [ self, sharedClips, nextIndex, completedCount, generation, total ]() {
				for( ;; )
				{
					if( self->searchGeneration.load() != generation )
						break;//Superseded by a newer search/page - stop spending requests on this one.

					size_t index = nextIndex->fetch_add( 1 );
					if( index >= sharedClips->size() )
						break;

					const ClipInfo& clip = ( *sharedClips )[ index ];
					std::string thumbBytes, thumbError;
					Thumbnail thumbnail;
					if( Api::DownloadThumbnail( clip, thumbBytes, thumbError ) )
						thumbnail = LoadThumbnail( thumbBytes );

					self->PublishThumbnailUpdate( generation, (int)index, std::move( thumbnail ) );

					size_t doneSoFar = completedCount->fetch_add( 1 ) + 1;
					if( doneSoFar >= total )
						self->PublishStatus( generation, std::to_string( total ) + " clip(s) found - click one, then Play Selected" );
					else
						self->PublishStatus( generation, "Loading thumbnails (" + std::to_string( doneSoFar ) + "/" + std::to_string( total ) + ")..." );
				}
			} ).detach();
		}
	}

	//--- Playback: runs entirely on playThread. --------------------------------------------------

	void RequestPlaySelected()
	{
		if( selectedIndex < 0 || selectedIndex >= (int)currentResults.size() )
		{
			SetWindowTextW( statusLabel, L"Select a clip first" );
			return;
		}
		if( playInProgress.exchange( true ) )
			return;//Already downloading a clip, let it finish first.

		if( playThread.joinable() )
			playThread.join();

		//Mark the badge immediately (we're already on the UI thread here) rather than waiting a
		//poll cycle for it to show up through the handoff.
		if( selectedIndex < (int)downloadStates.size() && downloadStates[ selectedIndex ] != ClipDownloadState::Downloaded )
		{
			downloadStates[ selectedIndex ] = ClipDownloadState::Downloading;
			InvalidateRect( gridWindow, nullptr, TRUE );
		}

		//Tag this download with the page it was requested from, so its status/badge updates get
		//dropped rather than corrupting a different page's grid if the user moves on before it
		//finishes - see searchGeneration's comment above.
		uint64_t generation = searchGeneration.load();
		ClipInfo clip       = currentResults[ selectedIndex ];
		playThread          = std::thread( &Impl::PlayWorker, this, selectedIndex, clip, generation );
	}

	void PlayWorker( int index, ClipInfo clip, uint64_t generation )
	{
		PublishStatus( generation, "Downloading '" + clip.title + "'..." );

		std::string localPath, error;
		if( !Api::DownloadClipToCache( clip, localPath, error ) )
		{
			PublishStatus( generation, "Error: " + error );
			PublishDownloadStateUpdate( generation, index, ClipDownloadState::NotDownloaded );
			playInProgress = false;
			return;
		}

		PublishStatus( generation, "Playing '" + clip.title + "'" );
		PublishDownloadStateUpdate( generation, index, ClipDownloadState::Downloaded );

		//Always fires, even if the user has since moved on to another page - they did ask for
		//this specific clip to play.
		if( onClipSelected )
			onClipSelected( clip, localPath );

		playInProgress = false;
	}

	//--- Grid interaction and painting, all on the UI thread. ------------------------------------

	int HitTest( int x, int y ) const
	{
		int col = x / kCellWidth;
		int row = y / kCellHeight;
		if( col < 0 || col >= kGridColumns || row < 0 || row >= kGridRows )
			return -1;
		int index = row * kGridColumns + col;
		return index < (int)currentResults.size() ? index : -1;
	}

	void OnGridClick( int x, int y )
	{
		int index = HitTest( x, y );
		if( index < 0 )
			return;
		selectedIndex = index;
		SetWindowTextW( statusLabel, Utf8ToWide( currentResults[ index ].title.empty() ? currentResults[ index ].id : currentResults[ index ].title ).c_str() );
		InvalidateRect( gridWindow, nullptr, TRUE );
	}

	void OnGridDoubleClick( int x, int y )
	{
		int index = HitTest( x, y );
		if( index < 0 )
			return;
		selectedIndex = index;
		InvalidateRect( gridWindow, nullptr, TRUE );
		RequestPlaySelected();
	}

	//Draws the whole grid into an off-screen bitmap and blits it in one shot, rather than issuing
	//several separate GDI+ draw calls straight to the window (which visibly flickers).
	void PaintGrid( HWND window )
	{
		PAINTSTRUCT paintStruct;
		HDC hdc = BeginPaint( window, &paintStruct );

		RECT clientRect;
		GetClientRect( window, &clientRect );
		int width  = clientRect.right - clientRect.left;
		int height = clientRect.bottom - clientRect.top;

		HDC memDC        = CreateCompatibleDC( hdc );
		HBITMAP memBitmap = CreateCompatibleBitmap( hdc, width, height );
		HBITMAP oldBitmap = (HBITMAP)SelectObject( memDC, memBitmap );

		FillRect( memDC, &clientRect, panelBrush );

		{
			Gdiplus::Graphics graphics( memDC );
			graphics.SetInterpolationMode( Gdiplus::InterpolationModeHighQualityBicubic );

			SetBkMode( memDC, TRANSPARENT );
			SetTextColor( memDC, kColorText );

			for( size_t i = 0; i < currentResults.size(); ++i )
			{
				int column = (int)( i % kGridColumns );
				int row    = (int)( i / kGridColumns );
				int x      = column * kCellWidth;
				int y      = row * kCellHeight;

				int imageX = x + ( kCellWidth - kImageWidth ) / 2;
				int imageY = y + 5;

				if( i < thumbnails.size() && thumbnails[ i ].bitmap )
				{
					graphics.DrawImage( thumbnails[ i ].bitmap.get(), imageX, imageY, kImageWidth, kImageHeight );
				}
				else
				{
					RECT imageRect{ imageX, imageY, imageX + kImageWidth, imageY + kImageHeight };
					HBRUSH borderBrush = CreateSolidBrush( kColorBorder );
					FrameRect( memDC, &imageRect, borderBrush );
					DeleteObject( borderBrush );
				}

				if( i < downloadStates.size() )
					DrawDownloadBadge( graphics, imageX + kImageWidth, imageY, downloadStates[ i ] );

				//Selection is drawn as an accent-colored outline around the cell rather than a
				//fill, so the thumbnail underneath stays fully visible.
				if( (int)i == selectedIndex )
				{
					Gdiplus::Pen accentPen( Gdiplus::Color( 255, GetRValue( kColorAccent ), GetGValue( kColorAccent ), GetBValue( kColorAccent ) ), 3.0f );
					graphics.DrawRectangle( &accentPen, x + 2, y + 2, kCellWidth - 4, kCellHeight - 4 );
				}

				RECT textRect{ x + 4, imageY + kImageHeight + 2, x + kCellWidth - 4, y + kCellHeight - 2 };
				std::wstring title = Utf8ToWide( currentResults[ i ].title.empty() ? currentResults[ i ].id : currentResults[ i ].title );
				DrawTextW( memDC, title.c_str(), -1, &textRect, DT_CENTER | DT_WORDBREAK | DT_END_ELLIPSIS );
			}
		}

		BitBlt( hdc, 0, 0, width, height, memDC, 0, 0, SRCCOPY );

		SelectObject( memDC, oldBitmap );
		DeleteObject( memBitmap );
		DeleteDC( memDC );

		EndPaint( window, &paintStruct );
	}

	void DrawButton( const DRAWITEMSTRUCT* drawItem )
	{
		bool pressed  = ( drawItem->itemState & ODS_SELECTED ) != 0;
		bool disabled = ( drawItem->itemState & ODS_DISABLED ) != 0;

		HBRUSH backgroundBrush = CreateSolidBrush( pressed ? kColorAccent : kColorAccentDark );
		FillRect( drawItem->hDC, &drawItem->rcItem, backgroundBrush );
		DeleteObject( backgroundBrush );

		HBRUSH borderBrush = CreateSolidBrush( kColorBorder );
		FrameRect( drawItem->hDC, &drawItem->rcItem, borderBrush );
		DeleteObject( borderBrush );

		wchar_t text[ 64 ];
		GetWindowTextW( drawItem->hwndItem, text, 64 );

		SetBkMode( drawItem->hDC, TRANSPARENT );
		SetTextColor( drawItem->hDC, disabled ? kColorTextDim : kColorText );
		RECT textRect = drawItem->rcItem;
		DrawTextW( drawItem->hDC, text, -1, &textRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE );
	}

	void OnCommand( int controlId, int notifyCode )
	{
		if( controlId == ID_BUTTON_SEARCH && notifyCode == BN_CLICKED )
			StartSearch();
		else if( controlId == ID_BUTTON_PLAY && notifyCode == BN_CLICKED )
			RequestPlaySelected();
		else if( controlId == ID_BUTTON_PREV_PAGE && notifyCode == BN_CLICKED )
			GoToPrevPage();
		else if( controlId == ID_BUTTON_NEXT_PAGE && notifyCode == BN_CLICKED )
			GoToNextPage();
	}

	//Subclasses the search edit box just to catch Enter - a plain EDIT control has no other way
	//to tell us the user pressed it, since this isn't a dialog with IsDialogMessage translating
	//it to a default-button click for us.
	static LRESULT CALLBACK EditSubclassProc( HWND window, UINT message, WPARAM wParam, LPARAM lParam )
	{
		Impl* self = reinterpret_cast< Impl* >( GetWindowLongPtrW( window, GWLP_USERDATA ) );

		if( message == WM_KEYDOWN && wParam == VK_RETURN )
		{
			if( self != nullptr )
				self->StartSearch();
			return 0;
		}

		if( self != nullptr && self->originalEditProc != nullptr )
			return CallWindowProcW( self->originalEditProc, window, message, wParam, lParam );
		return DefWindowProcW( window, message, wParam, lParam );
	}

	static LRESULT CALLBACK GridWndProc( HWND window, UINT message, WPARAM wParam, LPARAM lParam )
	{
		Impl* self = reinterpret_cast< Impl* >( GetWindowLongPtrW( window, GWLP_USERDATA ) );

		switch( message )
		{
		case WM_PAINT:
			if( self != nullptr )
				self->PaintGrid( window );
			return 0;
		case WM_ERASEBKGND:
			return 1;//We repaint the whole client area ourselves in WM_PAINT, avoids flicker.
		case WM_TIMER:
			if( self != nullptr && wParam == kPollTimerId )
				self->ApplyPendingHandoff();
			return 0;
		case WM_LBUTTONDOWN:
			if( self != nullptr )
				self->OnGridClick( GET_X_LPARAM( lParam ), GET_Y_LPARAM( lParam ) );
			return 0;
		case WM_LBUTTONDBLCLK:
			if( self != nullptr )
				self->OnGridDoubleClick( GET_X_LPARAM( lParam ), GET_Y_LPARAM( lParam ) );
			return 0;
		}

		return DefWindowProcW( window, message, wParam, lParam );
	}

	static LRESULT CALLBACK WndProc( HWND window, UINT message, WPARAM wParam, LPARAM lParam )
	{
		if( message == WM_CREATE )
		{
			CREATESTRUCTW* createStruct = reinterpret_cast< CREATESTRUCTW* >( lParam );
			Impl* self                  = reinterpret_cast< Impl* >( createStruct->lpCreateParams );
			SetWindowLongPtrW( window, GWLP_USERDATA, reinterpret_cast< LONG_PTR >( self ) );
			self->CreateControls( window );
			return 0;
		}

		Impl* self = reinterpret_cast< Impl* >( GetWindowLongPtrW( window, GWLP_USERDATA ) );

		switch( message )
		{
		case WM_COMMAND:
			if( self != nullptr )
				self->OnCommand( LOWORD( wParam ), HIWORD( wParam ) );
			return 0;
		case WM_DRAWITEM:
			if( self != nullptr )
				self->DrawButton( reinterpret_cast< const DRAWITEMSTRUCT* >( lParam ) );
			return TRUE;
		case WM_CTLCOLORSTATIC:
			SetTextColor( (HDC)wParam, kColorText );
			SetBkMode( (HDC)wParam, TRANSPARENT );
			return self != nullptr ? (LRESULT)self->windowBrush : (LRESULT)GetStockObject( NULL_BRUSH );
		case WM_CTLCOLOREDIT:
			SetTextColor( (HDC)wParam, kColorText );
			SetBkColor( (HDC)wParam, kColorFieldBg );
			return self != nullptr ? (LRESULT)self->fieldBrush : (LRESULT)GetStockObject( NULL_BRUSH );
		case WM_CLOSE:
			DestroyWindow( window );
			return 0;
		case WM_DESTROY:
			PostQuitMessage( 0 );
			return 0;
		}

		return DefWindowProcW( window, message, wParam, lParam );
	}

	void ThreadMain( std::string initialQuery )
	{
		Gdiplus::GdiplusStartupInput gdiplusStartupInput;
		Gdiplus::GdiplusStartup( &gdiplusToken, &gdiplusStartupInput, nullptr );

		windowBrush = CreateSolidBrush( kColorWindowBg );
		panelBrush  = CreateSolidBrush( kColorPanelBg );
		fieldBrush  = CreateSolidBrush( kColorFieldBg );

		static bool classesRegistered = false;
		if( !classesRegistered )
		{
			WNDCLASSEXW windowClass   = {};
			windowClass.cbSize        = sizeof( windowClass );
			windowClass.lpfnWndProc   = &Impl::WndProc;
			windowClass.hInstance     = (HINSTANCE)GetOwnModuleHandle();
			windowClass.lpszClassName = kWindowClassName;
			windowClass.hCursor       = LoadCursor( nullptr, IDC_ARROW );
			windowClass.hbrBackground = CreateSolidBrush( kColorWindowBg );
			RegisterClassExW( &windowClass );

			WNDCLASSEXW gridClass   = {};
			gridClass.cbSize        = sizeof( gridClass );
			gridClass.style         = CS_DBLCLKS;//Without this, Windows never generates WM_LBUTTONDBLCLK at all.
			gridClass.lpfnWndProc   = &Impl::GridWndProc;
			gridClass.hInstance     = (HINSTANCE)GetOwnModuleHandle();
			gridClass.lpszClassName = kGridClassName;
			gridClass.hCursor       = LoadCursor( nullptr, IDC_HAND );
			gridClass.hbrBackground = CreateSolidBrush( kColorPanelBg );
			RegisterClassExW( &gridClass );

			classesRegistered = true;
		}

		DWORD windowStyle   = ( WS_OVERLAPPEDWINDOW & ~WS_THICKFRAME & ~WS_MAXIMIZEBOX ) | WS_VISIBLE;
		//WS_EX_TOPMOST: Resolume tends to be full-screen/always-focused, so without this the
		//browser window can end up hidden behind it as soon as you click back into Resolume.
		DWORD windowExStyle = WS_EX_TOOLWINDOW | WS_EX_TOPMOST;

		RECT desiredClientRect{ 0, 0, kGridColumns * kCellWidth + 20, 46 + kGridRows * kCellHeight + 34 + 46 };
		AdjustWindowRectEx( &desiredClientRect, windowStyle, FALSE, windowExStyle );

		HWND window = CreateWindowExW(
		    windowExStyle,
		    kWindowClassName,
		    L"videopong.net - Choose a Clip",
		    windowStyle,
		    CW_USEDEFAULT, CW_USEDEFAULT,
		    desiredClientRect.right - desiredClientRect.left,
		    desiredClientRect.bottom - desiredClientRect.top,
		    nullptr, nullptr, (HINSTANCE)GetOwnModuleHandle(), this );

		if( window == nullptr )
		{
			FFGLLog::LogToHost( "videopong.net: failed to create the clip browser window" );
			Gdiplus::GdiplusShutdown( gdiplusToken );
			return;
		}

		{
			std::lock_guard< std::mutex > lock( hwndMutex );
			hwnd = window;
		}
		hwndCv.notify_all();

		ShowWindow( window, SW_SHOW );
		UpdateWindow( window );

		//Pre-fill the search box from whatever's in the plugin's own Search parameter, but never
		//search automatically - no clip (random or otherwise) loads until the user asks for one.
		if( !initialQuery.empty() )
			SetWindowTextW( editSearch, Utf8ToWide( initialQuery ).c_str() );

		MSG msg;
		while( GetMessageW( &msg, nullptr, 0, 0 ) > 0 )
		{
			TranslateMessage( &msg );
			DispatchMessageW( &msg );
		}

		//Stop any detached thumbnail-download workers from doing further (now pointless) network
		//requests once the window is gone. They still hold their own shared_ptr to this object,
		//so it's safe for them to keep running past this point - they just won't publish
		//anything anyone will ever see.
		++searchGeneration;

		//searchThread only ran the (quick) search API call, so this won't block on thumbnails.
		//playThread can still take a while if a download was in flight - unavoidable, since
		//onClipSelected has to actually fire for a clip the user explicitly asked to play.
		if( searchThread.joinable() )
			searchThread.join();
		if( playThread.joinable() )
			playThread.join();

		thumbnails.clear();
		DeleteObject( windowBrush );
		DeleteObject( panelBrush );
		DeleteObject( fieldBrush );
		Gdiplus::GdiplusShutdown( gdiplusToken );

		std::lock_guard< std::mutex > lock( hwndMutex );
		hwnd = nullptr;
	}
};

BrowserWindow::BrowserWindow() = default;
BrowserWindow::~BrowserWindow()
{
	Close();
}

void BrowserWindow::Open( const std::string& initialQuery, ClipSelectedCallback onClipSelected )
{
	Close();

	impl                 = std::make_shared< Impl >();
	impl->onClipSelected = std::move( onClipSelected );

	Impl* implPtr = impl.get();
	impl->thread  = std::thread( &Impl::ThreadMain, implPtr, initialQuery );

	//Wait until the window actually exists before returning, so a caller that immediately does
	//something else (or fails) gets a window that's really there. Bounded so a failure to create
	//the window (logged above) can't hang the caller forever.
	std::unique_lock< std::mutex > lock( implPtr->hwndMutex );
	implPtr->hwndCv.wait_for( lock, std::chrono::seconds( 3 ), [ implPtr ] { return implPtr->hwnd != nullptr; } );
}

void BrowserWindow::Close()
{
	if( !impl )
		return;

	HWND hwndToClose = nullptr;
	{
		std::lock_guard< std::mutex > lock( impl->hwndMutex );
		hwndToClose = impl->hwnd;
	}
	if( hwndToClose != nullptr )
		PostMessageW( hwndToClose, WM_CLOSE, 0, 0 );

	//ThreadMain joins searchThread/playThread itself once its message loop exits. Any detached
	//thumbnail-download workers may still be finishing up in the background after this returns -
	//that's fine, they hold their own shared_ptr to Impl (see searchGeneration's comment) and
	//won't publish anything anyone can see any more.
	if( impl->thread.joinable() )
		impl->thread.join();

	impl.reset();
}

}//namespace videopong

#elif !defined( __APPLE__ )//macOS has its own implementation in VideoPongBrowserWindow.mm.

#include <FFGLSDK.h>//For FFGLLog::LogToHost.

namespace videopong
{
struct BrowserWindow::Impl
{
};

BrowserWindow::BrowserWindow()  = default;
BrowserWindow::~BrowserWindow() = default;

void BrowserWindow::Open( const std::string&, ClipSelectedCallback )
{
	FFGLLog::LogToHost( "videopong.net: the clip browser window is only available on Windows right now" );
}

void BrowserWindow::Close()
{
}

}//namespace videopong

#endif
