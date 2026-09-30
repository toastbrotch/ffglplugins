#include "VideoPongBrowserWindow.h"

#if defined( __APPLE__ )

// macOS/Cocoa implementation of the clip-search popup - the counterpart to the Win32+GDI+ one in
// VideoPongBrowserWindow.cpp (see the #elif !defined(__APPLE__) branch there for the generic
// stub used on any other, non-Windows-non-Apple platform).
//
// NOTE: written without access to a Mac or Xcode - nothing in this file has been compiled or run.
// It follows the same design as the Windows implementation (search field, paginated thumbnail
// grid, Play Selected button, background search/download work handed off to the UI thread) but
// uses idiomatic Cocoa/AppKit/GCD instead of Win32/GDI+/manual threads+polling: AppKit requires
// all window/view work to happen on the main thread, so this dispatches to dispatch_get_main_queue()
// instead of running its own message-loop thread, and uses dispatch_async to a background queue
// instead of std::thread for search/thumbnail/download work. Expect real bugs on first build.
//
// Standard controls (buttons, text fields, labels) use the system's dark appearance
// (NSAppearanceNameDarkAqua) rather than hand-drawn owner-draw buttons like the Windows version -
// AppKit themes those for free. Only the thumbnail grid is custom-drawn, since there's no stock
// AppKit control for "photo grid with a per-cell download-state badge".

#import <Cocoa/Cocoa.h>

#include <atomic>
#include <functional>
#include <string>
#include <vector>

#include <FFGLSDK.h>//For FFGLLog::LogToHost.

#include "VideoPongApi.h"

namespace videopong
{
namespace
{
	enum class ClipDownloadState
	{
		NotDownloaded,
		Downloading,
		Downloaded,
	};

	const NSInteger kGridColumns = 4;
	const NSInteger kGridRows    = 3;
	const NSInteger kMaxResults  = kGridColumns * kGridRows;
	const CGFloat kCellWidth     = 150;
	const CGFloat kCellHeight    = 140;
	const CGFloat kImageWidth    = 130;
	const CGFloat kImageHeight   = 90;

	//How many thumbnails to fetch/decode at once - matches the Windows implementation's reasoning:
	//downloading them one at a time made the grid feel slow to fill in.
	const size_t kThumbnailDownloadConcurrency = 6;

	NSColor* ColorWindowBg() { return [NSColor colorWithCalibratedWhite:32.0 / 255.0 alpha:1.0]; }
	NSColor* ColorPanelBg() { return [NSColor colorWithCalibratedWhite:45.0 / 255.0 alpha:1.0]; }
	NSColor* ColorBorder() { return [NSColor colorWithCalibratedWhite:75.0 / 255.0 alpha:1.0]; }
	NSColor* ColorText() { return [NSColor colorWithCalibratedWhite:225.0 / 255.0 alpha:1.0]; }
	NSColor* ColorAccent() { return [NSColor colorWithCalibratedRed:0x00 / 255.0 green:0xdc / 255.0 blue:0xef / 255.0 alpha:1.0]; }
	NSColor* ColorDownloaded() { return [NSColor colorWithCalibratedRed:0x2e / 255.0 green:0xc4 / 255.0 blue:0x62 / 255.0 alpha:1.0]; }
	NSColor* ColorDownloading() { return ColorAccent(); }
}//anonymous namespace
}//namespace videopong

//--- Thumbnail grid: the one custom-drawn, custom-hit-tested piece. ------------------------------

@interface VideoPongThumbGridView : NSView
@property( nonatomic, copy ) NSArray< NSString* >* titles;
@property( nonatomic, strong ) NSMutableArray* thumbnails;//NSImage*, or NSNull for "not loaded yet".
@property( nonatomic, strong ) NSMutableArray< NSNumber* >* downloadStates;//videopong::ClipDownloadState, boxed.
@property( nonatomic, assign ) NSInteger selectedIndex;
@property( nonatomic, copy ) void ( ^onSelect )( NSInteger index );
@property( nonatomic, copy ) void ( ^onPlay )( NSInteger index );
@end

@implementation VideoPongThumbGridView

- (instancetype)initWithFrame:(NSRect)frameRect
{
	self = [ super initWithFrame:frameRect ];
	if( self )
	{
		_titles         = @[];
		_thumbnails     = [ NSMutableArray array ];
		_downloadStates = [ NSMutableArray array ];
		_selectedIndex  = -1;
	}
	return self;
}

//Puts (0,0) at the top-left with y growing downward, matching the row/column math below (and the
//Windows implementation's GDI coordinate convention).
- (BOOL)isFlipped
{
	return YES;
}

- (void)drawRect:(NSRect)dirtyRect
{
	[ videopong::ColorPanelBg() set ];
	NSRectFill( self.bounds );

	NSMutableParagraphStyle* centered = [ [ NSMutableParagraphStyle alloc ] init ];
	centered.alignment                = NSTextAlignmentCenter;
	NSDictionary* titleAttrs          = @{
        NSForegroundColorAttributeName : videopong::ColorText(),
        NSFontAttributeName : [ NSFont systemFontOfSize:11 ],
        NSParagraphStyleAttributeName : centered
    };

	NSUInteger count = self.titles.count;
	for( NSUInteger i = 0; i < count; ++i )
	{
		NSInteger column = i % kGridColumns;
		NSInteger row    = i / kGridColumns;
		CGFloat x        = column * kCellWidth;
		CGFloat y        = row * kCellHeight;

		CGFloat imageX = x + ( kCellWidth - kImageWidth ) / 2.0;
		CGFloat imageY = y + 5.0;
		NSRect imageRect = NSMakeRect( imageX, imageY, kImageWidth, kImageHeight );

		id thumb = i < self.thumbnails.count ? self.thumbnails[ i ] : nil;
		if( [ thumb isKindOfClass:[ NSImage class ] ] )
		{
			//drawInRect: draws in the view's own (flipped) coordinate space, so no manual flip
			//of the image itself is needed here.
			[ (NSImage*)thumb drawInRect:imageRect fromRect:NSZeroRect operation:NSCompositingOperationSourceOver fraction:1.0 ];
		}
		else
		{
			[ videopong::ColorBorder() setStroke ];
			NSFrameRectWithWidth( imageRect, 1.0 );
		}

		videopong::ClipDownloadState state = i < self.downloadStates.count
		    ? (videopong::ClipDownloadState)self.downloadStates[ i ].intValue
		    : videopong::ClipDownloadState::NotDownloaded;
		[ self drawBadgeAtRight:imageX + kImageWidth top:imageY state:state ];

		//Selection is an accent-colored outline around the cell, not a fill, so the thumbnail
		//underneath stays fully visible.
		if( (NSInteger)i == self.selectedIndex )
		{
			NSBezierPath* outline = [ NSBezierPath bezierPathWithRect:NSInsetRect( NSMakeRect( x, y, kCellWidth, kCellHeight ), 2, 2 ) ];
			outline.lineWidth     = 3.0;
			[ videopong::ColorAccent() setStroke ];
			[ outline stroke ];
		}

		NSString* title  = i < self.titles.count ? self.titles[ i ] : @"";
		NSRect textRect  = NSMakeRect( x + 4, imageY + kImageHeight + 2, kCellWidth - 8, kCellHeight - ( imageY + kImageHeight + 2 - y ) - 2 );
		[ title drawInRect:textRect withAttributes:titleAttrs ];
	}
}

//A plain filled circle (not an icon) so it doesn't depend on any particular font having a
//checkmark/arrow glyph - same reasoning as the Windows version's DrawDownloadBadge.
- (void)drawBadgeAtRight:(CGFloat)right top:(CGFloat)top state:(videopong::ClipDownloadState)state
{
	if( state == videopong::ClipDownloadState::NotDownloaded )
		return;

	const CGFloat radius = 8.0;
	CGFloat cx           = right - radius - 4;
	CGFloat cy           = top + radius + 4;
	NSRect circleRect    = NSMakeRect( cx - radius, cy - radius, radius * 2, radius * 2 );

	NSBezierPath* circle = [ NSBezierPath bezierPathWithOvalInRect:circleRect ];
	NSColor* fill        = state == videopong::ClipDownloadState::Downloaded ? videopong::ColorDownloaded() : videopong::ColorDownloading();
	[ fill setFill ];
	[ circle fill ];
	[ [ NSColor colorWithCalibratedWhite:0 alpha:0.78 ] setStroke ];
	[ circle stroke ];

	if( state == videopong::ClipDownloadState::Downloaded )
	{
		NSBezierPath* check = [ NSBezierPath bezierPath ];
		[ check moveToPoint:NSMakePoint( cx - 3.5, cy ) ];
		[ check lineToPoint:NSMakePoint( cx - 0.5, cy + 3.0 ) ];
		[ check lineToPoint:NSMakePoint( cx + 4.0, cy - 3.5 ) ];
		check.lineWidth = 2.0;
		[ [ NSColor whiteColor ] setStroke ];
		[ check stroke ];
	}
	else
	{
		NSBezierPath* wedge = [ NSBezierPath bezierPath ];
		[ wedge moveToPoint:NSMakePoint( cx - 3, cy - 3 ) ];
		[ wedge lineToPoint:NSMakePoint( cx + 3, cy - 3 ) ];
		[ wedge lineToPoint:NSMakePoint( cx, cy + 3.5 ) ];
		[ wedge closePath ];
		[ [ NSColor whiteColor ] setFill ];
		[ wedge fill ];
	}
}

- (NSInteger)hitTest:(NSPoint)point
{
	NSInteger column = (NSInteger)( point.x / kCellWidth );
	NSInteger row    = (NSInteger)( point.y / kCellHeight );
	if( column < 0 || column >= kGridColumns || row < 0 || row >= kGridRows )
		return -1;
	NSInteger index = row * kGridColumns + column;
	return index < (NSInteger)self.titles.count ? index : -1;
}

- (void)mouseDown:(NSEvent*)event
{
	NSPoint point   = [ self convertPoint:event.locationInWindow fromView:nil ];
	NSInteger index = [ self hitTest:point ];
	if( index < 0 )
		return;

	self.selectedIndex = index;
	[ self setNeedsDisplay:YES ];

	if( event.clickCount >= 2 )
	{
		if( self.onPlay )
			self.onPlay( index );
	}
	else
	{
		if( self.onSelect )
			self.onSelect( index );
	}
}

@end

//--- The popup window itself: search field, grid, pagination, play button, status label. ---------

@interface VideoPongBrowserWindowController : NSObject< NSWindowDelegate >
- (void)openWithInitialQuery:(const std::string&)initialQuery
               onClipSelected:(videopong::BrowserWindow::ClipSelectedCallback)onClipSelected;
- (void)closeAndCleanUp;
@end

//Private helpers - declared up front (rather than relying on same-@implementation visibility)
//so dot-syntax and every selector below resolves against a real declared signature, not an
//implicit/guessed one.
@interface VideoPongBrowserWindowController ()
@property( nonatomic, readonly ) uint64_t generationValue;
- (void)updatePaginationControls;
- (void)searchButtonClicked:(id)sender;
- (void)prevPageClicked:(id)sender;
- (void)nextPageClicked:(id)sender;
- (void)performSearch:(std::string)query offset:(int)offset;
- (void)searchWorker:(std::string)query offset:(int)offset generation:(uint64_t)generation;
- (void)applySearchResults:(std::vector< videopong::ClipInfo >)clips
                     states:(std::vector< videopong::ClipDownloadState >)states
                hasNextPage:(bool)hasNext
                 generation:(uint64_t)generation;
- (void)startThumbnailDownloads:(std::vector< videopong::ClipInfo >)clips generation:(uint64_t)generation;
- (void)applyThumbnail:(NSImage*)image atIndex:(NSInteger)index generation:(uint64_t)generation;
- (void)onGridSelect:(NSInteger)index;
- (void)onGridPlay:(NSInteger)index;
- (void)playButtonClicked:(id)sender;
- (void)requestPlayAtIndex:(NSInteger)index;
- (void)playWorker:(videopong::ClipInfo)clip atIndex:(NSInteger)index generation:(uint64_t)generation;
- (void)applyDownloadState:(videopong::ClipDownloadState)state atIndex:(NSInteger)index generation:(uint64_t)generation;
@end

@implementation VideoPongBrowserWindowController
{
	NSWindow* _window;
	NSTextField* _searchField;
	NSTextField* _statusLabel;
	NSTextField* _pageLabel;
	NSButton* _prevButton;
	NSButton* _nextButton;
	VideoPongThumbGridView* _gridView;

	//Bumped every time a new search or page is requested. Background search/thumbnail/download
	//work checks this before publishing anything back to the main thread, and quietly stops once
	//a newer generation has started - see the Windows implementation's identical reasoning.
	std::atomic< uint64_t > _generation;
	std::atomic< bool > _playInProgress;

	//Entered right before dispatching a play/download to the background queue, left right after
	//onClipSelected has fired (or the download failed) - see closeAndCleanUp, which waits on this
	//before returning. Without it, a plugin unload could destroy the VideoPong instance that
	//onClipSelected's captured `this` points at while a download was still in flight, and firing
	//the callback afterwards would be a use-after-free. The Windows implementation gets the same
	//guarantee by joining playThread before ThreadMain (and so BrowserWindow::Close()) returns.
	dispatch_group_t _playGroup;

	std::vector< videopong::ClipInfo > _currentResults;
	std::string _lastQuery;
	int _currentOffset;
	bool _hasNextPage;

	videopong::BrowserWindow::ClipSelectedCallback _onClipSelected;
}

- (instancetype)init
{
	self = [ super init ];
	if( self )
	{
		_generation      = 0;
		_playInProgress  = false;
		_currentOffset   = 0;
		_hasNextPage     = false;
		_playGroup       = dispatch_group_create();
	}
	return self;
}

- (void)openWithInitialQuery:(const std::string&)initialQuery
               onClipSelected:(videopong::BrowserWindow::ClipSelectedCallback)onClipSelected
{
	_onClipSelected = std::move( onClipSelected );

	const CGFloat margin          = 10;
	const CGFloat gap             = 8;
	const CGFloat gridWidth       = kGridColumns * kCellWidth;
	const CGFloat gridHeight      = kGridRows * kCellHeight;
	const CGFloat searchRowHeight = 24;
	const CGFloat pageRowHeight   = 26;
	const CGFloat bottomRowHeight = 28;
	const CGFloat contentWidth    = gridWidth + margin * 2;
	const CGFloat contentHeight   = margin + searchRowHeight + gap + gridHeight + gap + pageRowHeight + gap + bottomRowHeight + margin;

	NSRect contentRect       = NSMakeRect( 0, 0, contentWidth, contentHeight );
	NSWindowStyleMask style  = NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskMiniaturizable;
	_window                  = [ [ NSWindow alloc ] initWithContentRect:contentRect styleMask:style backing:NSBackingStoreBuffered defer:NO ];
	_window.title            = @"videopong.net - Choose a Clip";
	_window.delegate         = self;
	_window.releasedWhenClosed = NO;
	_window.appearance      = [ NSAppearance appearanceNamed:NSAppearanceNameDarkAqua ];
	[ _window setBackgroundColor:videopong::ColorWindowBg() ];
	//Best-effort attempt to stay visible over a full-screen host (Resolume typically runs
	//full-screen) - unverified, full-screen Space behavior on macOS is notoriously finicky.
	_window.level             = NSFloatingWindowLevel;
	_window.collectionBehavior = NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary;

	NSView* content = _window.contentView;

	CGFloat searchEditWidth = gridWidth - 80 - gap;
	CGFloat y               = contentHeight - margin - searchRowHeight;

	_searchField          = [ [ NSTextField alloc ] initWithFrame:NSMakeRect( margin, y, searchEditWidth, searchRowHeight ) ];
	_searchField.target   = self;
	_searchField.action   = @selector( searchButtonClicked: );//A plain NSTextField fires its action on Return.
	[ content addSubview:_searchField ];

	NSButton* searchButton    = [ [ NSButton alloc ] initWithFrame:NSMakeRect( margin + searchEditWidth + gap, y, 80, searchRowHeight ) ];
	searchButton.title        = @"Search";
	searchButton.bezelStyle   = NSBezelStyleRounded;
	searchButton.target       = self;
	searchButton.action       = @selector( searchButtonClicked: );
	[ content addSubview:searchButton ];

	y -= gap + gridHeight;
	_gridView              = [ [ VideoPongThumbGridView alloc ] initWithFrame:NSMakeRect( margin, y, gridWidth, gridHeight ) ];
	__weak __typeof( self ) weakSelf = self;
	_gridView.onSelect     = ^( NSInteger index ) { [ weakSelf onGridSelect:index ]; };
	_gridView.onPlay       = ^( NSInteger index ) { [ weakSelf onGridPlay:index ]; };
	[ content addSubview:_gridView ];

	y -= gap + pageRowHeight;
	_prevButton              = [ [ NSButton alloc ] initWithFrame:NSMakeRect( margin, y, 80, pageRowHeight ) ];
	_prevButton.title        = @"< Prev";
	_prevButton.bezelStyle   = NSBezelStyleRounded;
	_prevButton.target       = self;
	_prevButton.action       = @selector( prevPageClicked: );
	[ content addSubview:_prevButton ];

	_pageLabel               = [ NSTextField labelWithString:@"" ];
	_pageLabel.frame         = NSMakeRect( margin + 80 + gap, y + 3, gridWidth - 2 * ( 80 + gap ), pageRowHeight - 6 );
	_pageLabel.alignment     = NSTextAlignmentCenter;
	_pageLabel.textColor     = videopong::ColorText();
	[ content addSubview:_pageLabel ];

	_nextButton              = [ [ NSButton alloc ] initWithFrame:NSMakeRect( margin + gridWidth - 80, y, 80, pageRowHeight ) ];
	_nextButton.title        = @"Next >";
	_nextButton.bezelStyle   = NSBezelStyleRounded;
	_nextButton.target       = self;
	_nextButton.action       = @selector( nextPageClicked: );
	[ content addSubview:_nextButton ];

	y -= gap + bottomRowHeight;
	NSButton* playButton     = [ [ NSButton alloc ] initWithFrame:NSMakeRect( margin, y, 130, bottomRowHeight ) ];
	playButton.title         = @"Play Selected";
	playButton.bezelStyle    = NSBezelStyleRounded;
	playButton.target        = self;
	playButton.action        = @selector( playButtonClicked: );
	[ content addSubview:playButton ];

	_statusLabel             = [ NSTextField labelWithString:@"" ];
	_statusLabel.frame       = NSMakeRect( margin + 140, y + 4, gridWidth - 130, bottomRowHeight - 4 );
	_statusLabel.textColor   = videopong::ColorText();
	[ content addSubview:_statusLabel ];

	[ self updatePaginationControls ];

	//Pre-fill the search box from whatever's in the plugin's own Search parameter, but never
	//search automatically - no clip (random or otherwise) loads until the user asks for one.
	if( !initialQuery.empty() )
		_searchField.stringValue = [ NSString stringWithUTF8String:initialQuery.c_str() ];

	[ _window center ];
	[ _window makeKeyAndOrderFront:nil ];
	[ NSApp activateIgnoringOtherApps:YES ];
}

- (void)closeAndCleanUp
{
	//Stop any in-flight background work from publishing anything once the window is gone - it
	//still holds a strong reference to self (via the blocks it was dispatched with), so it's safe
	//for it to keep running past this point, it just won't touch UI state anyone can see any more.
	++_generation;

	//Block until any in-flight play/download has actually fired onClipSelected (or failed) -
	//required so a caller (VideoPong's destructor, on plugin unload) can't destroy the object that
	//callback points at while it's still pending. Mirrors the Windows implementation's
	//playThread.join() before ThreadMain returns. Note this runs on whatever thread called
	//BrowserWindow::Close() (see its isMainThread check) - if that's the main thread, this blocks
	//it, exactly as the Windows join() blocks whatever thread called Close() there too.
	dispatch_group_wait( _playGroup, DISPATCH_TIME_FOREVER );

	if( _window )
	{
		_window.delegate = nil;
		[ _window close ];
		_window = nil;
	}
}

- (void)windowWillClose:(NSNotification*)notification
{
	//The user closed the window directly (titlebar close button) rather than through
	//BrowserWindow::Close() - same cleanup either way.
	++_generation;
	_window.delegate = nil;
}

//--- Pagination / search state, all on the main thread. -------------------------------------------

- (void)updatePaginationControls
{
	int pageNumber       = _currentOffset / (int)kMaxResults + 1;
	_pageLabel.stringValue = [ NSString stringWithFormat:@"Page %d", pageNumber ];
	_prevButton.enabled    = _currentOffset > 0;
	_nextButton.enabled    = _hasNextPage;
}

- (void)searchButtonClicked:(id)sender
{
	std::string query = _searchField.stringValue.UTF8String ? _searchField.stringValue.UTF8String : "";
	if( query.empty() )
	{
		//No random/unrequested clips - a search needs an actual search term.
		_statusLabel.stringValue = @"Enter a search term";
		return;
	}
	[ self performSearch:query offset:0 ];
}

- (void)prevPageClicked:(id)sender
{
	if( _lastQuery.empty() || _currentOffset <= 0 )
		return;
	[ self performSearch:_lastQuery offset:std::max( 0, _currentOffset - (int)kMaxResults ) ];
}

- (void)nextPageClicked:(id)sender
{
	if( _lastQuery.empty() || !_hasNextPage )
		return;
	[ self performSearch:_lastQuery offset:_currentOffset + (int)kMaxResults ];
}

- (void)performSearch:(std::string)query offset:(int)offset
{
	uint64_t generation = ++_generation;

	_lastQuery     = query;
	_currentOffset = offset;

	//Clear the grid immediately, before the background search even starts, so old results don't
	//stay on screen looking as if they were never cleared.
	_currentResults.clear();
	_gridView.titles         = @[];
	_gridView.thumbnails     = [ NSMutableArray array ];
	_gridView.downloadStates = [ NSMutableArray array ];
	_gridView.selectedIndex  = -1;
	[ _gridView setNeedsDisplay:YES ];

	__weak __typeof( self ) weakSelf = self;
	dispatch_async( dispatch_get_global_queue( QOS_CLASS_USER_INITIATED, 0 ), ^{
		[ weakSelf searchWorker:query offset:offset generation:generation ];
	} );
}

//--- Search: runs entirely on a background queue, only ever touches the UI via dispatch_async to
//--- the main queue (guarded by the generation check, mirroring the Windows implementation's
//--- handoff-with-staleness-check design).

- (void)searchWorker:(std::string)query offset:(int)offset generation:(uint64_t)generation
{
	__weak __typeof( self ) weakSelf     = self;
	void ( ^publishStatus )( std::string ) = ^( std::string text ) {
		NSString* nsText = [ NSString stringWithUTF8String:text.c_str() ];
		dispatch_async( dispatch_get_main_queue(), ^{
			__typeof( weakSelf ) strongSelf = weakSelf;
			if( strongSelf && generation == strongSelf.generationValue )
				strongSelf->_statusLabel.stringValue = nsText;
		} );
	};

	publishStatus( "Searching..." );

	std::vector< videopong::ClipInfo > clips;
	std::string error;
	if( !videopong::Api::SearchClips( query, offset, (int)kMaxResults, clips, error ) )
	{
		publishStatus( "Error: " + error );
		return;
	}

	//A full page came back, so there might be more - the API doesn't report a total count.
	bool hasNext = clips.size() >= (size_t)kMaxResults;
	if( clips.size() > (size_t)kMaxResults )
		clips.resize( kMaxResults );

	std::vector< videopong::ClipDownloadState > states;
	states.reserve( clips.size() );
	for( const videopong::ClipInfo& clip : clips )
		states.push_back( videopong::Api::IsClipCached( clip ) ? videopong::ClipDownloadState::Downloaded : videopong::ClipDownloadState::NotDownloaded );

	publishStatus( std::to_string( clips.size() ) + " clip(s) found - loading thumbnails..." );

	dispatch_async( dispatch_get_main_queue(), ^{
		[ weakSelf applySearchResults:clips states:states hasNextPage:hasNext generation:generation ];
	} );

	[ self startThumbnailDownloads:clips generation:generation ];
}

- (void)applySearchResults:(std::vector< videopong::ClipInfo >)clips
                     states:(std::vector< videopong::ClipDownloadState >)states
                hasNextPage:(bool)hasNext
                 generation:(uint64_t)generation
{
	if( generation != self.generationValue )
		return;

	_currentResults = clips;
	_hasNextPage    = hasNext;

	NSMutableArray< NSString* >* titles = [ NSMutableArray arrayWithCapacity:clips.size() ];
	NSMutableArray* thumbnails          = [ NSMutableArray arrayWithCapacity:clips.size() ];
	NSMutableArray< NSNumber* >* boxedStates = [ NSMutableArray arrayWithCapacity:clips.size() ];
	for( size_t i = 0; i < clips.size(); ++i )
	{
		const std::string& label = clips[ i ].title.empty() ? clips[ i ].id : clips[ i ].title;
		[ titles addObject:[ NSString stringWithUTF8String:label.c_str() ] ];
		[ thumbnails addObject:[ NSNull null ] ];
		[ boxedStates addObject:@( (int)( i < states.size() ? states[ i ] : videopong::ClipDownloadState::NotDownloaded ) ) ];
	}

	_gridView.titles         = titles;
	_gridView.thumbnails     = thumbnails;
	_gridView.downloadStates = boxedStates;
	_gridView.selectedIndex  = -1;
	[ _gridView setNeedsDisplay:YES ];

	[ self updatePaginationControls ];
}

//Fetches thumbnails with several downloads in flight at once, mirroring the Windows
//implementation's reasoning: one-at-a-time made the grid feel slow to fill in. Each worker checks
//`generation` before publishing anything, so a stale page's downloads can't clobber a newer one's
//grid once the user has already moved on (Prev/Next, or a new search).
- (void)startThumbnailDownloads:(std::vector< videopong::ClipInfo >)clips generation:(uint64_t)generation
{
	auto sharedClips    = std::make_shared< std::vector< videopong::ClipInfo > >( std::move( clips ) );
	auto nextIndex      = std::make_shared< std::atomic< size_t > >( 0 );
	__weak __typeof( self ) weakSelf = self;

	size_t workerCount = std::min( kThumbnailDownloadConcurrency, sharedClips->size() );
	for( size_t w = 0; w < workerCount; ++w )
	{
		dispatch_async( dispatch_get_global_queue( QOS_CLASS_USER_INITIATED, 0 ), ^{
			for( ;; )
			{
				__typeof( weakSelf ) strongSelf = weakSelf;
				if( !strongSelf || generation != strongSelf.generationValue )
					break;//Superseded by a newer search/page - stop spending requests on this one.

				size_t index = nextIndex->fetch_add( 1 );
				if( index >= sharedClips->size() )
					break;

				const videopong::ClipInfo& clip = ( *sharedClips )[ index ];
				std::string thumbBytes, thumbError;
				NSImage* image = nil;
				if( videopong::Api::DownloadThumbnail( clip, thumbBytes, thumbError ) && !thumbBytes.empty() )
				{
					NSData* data = [ NSData dataWithBytes:thumbBytes.data() length:thumbBytes.size() ];
					image        = [ [ NSImage alloc ] initWithData:data ];
				}

				dispatch_async( dispatch_get_main_queue(), ^{
					[ weakSelf applyThumbnail:image atIndex:(NSInteger)index generation:generation ];
				} );
			}
		} );
	}
}

- (void)applyThumbnail:(NSImage*)image atIndex:(NSInteger)index generation:(uint64_t)generation
{
	if( generation != self.generationValue )
		return;
	if( index < 0 || index >= (NSInteger)_gridView.thumbnails.count )
		return;
	_gridView.thumbnails[ index ] = image ? image : (id)[ NSNull null ];
	[ _gridView setNeedsDisplay:YES ];
}

//--- Grid interaction. ------------------------------------------------------------------------

- (void)onGridSelect:(NSInteger)index
{
	if( index < 0 || index >= (NSInteger)_currentResults.size() )
		return;
	const videopong::ClipInfo& clip = _currentResults[ index ];
	_statusLabel.stringValue         = [ NSString stringWithUTF8String:( clip.title.empty() ? clip.id : clip.title ).c_str() ];
}

- (void)onGridPlay:(NSInteger)index
{
	[ self requestPlayAtIndex:index ];
}

- (void)playButtonClicked:(id)sender
{
	[ self requestPlayAtIndex:_gridView.selectedIndex ];
}

//--- Playback: the download runs on a background queue; onClipSelected fires from there too, not
//--- the main thread, matching the header's documented contract ("no GL calls allowed here").

- (void)requestPlayAtIndex:(NSInteger)index
{
	if( index < 0 || index >= (NSInteger)_currentResults.size() )
	{
		_statusLabel.stringValue = @"Select a clip first";
		return;
	}
	if( _playInProgress.exchange( true ) )
		return;//Already downloading a clip, let it finish first.

	if( index < (NSInteger)_gridView.downloadStates.count
	    && (videopong::ClipDownloadState)_gridView.downloadStates[ index ].intValue != videopong::ClipDownloadState::Downloaded )
	{
		_gridView.downloadStates[ index ] = @( (int)videopong::ClipDownloadState::Downloading );
		[ _gridView setNeedsDisplay:YES ];
	}

	uint64_t generation      = self.generationValue;
	videopong::ClipInfo clip = _currentResults[ index ];

	//Strong on purpose (not weakSelf): this specific play/download must run to completion and
	//fire onClipSelected even if the window is closed in the meantime - see _playGroup's comment.
	dispatch_group_enter( _playGroup );
	__typeof( self ) strongSelfForPlay = self;
	dispatch_async( dispatch_get_global_queue( QOS_CLASS_USER_INITIATED, 0 ), ^{
		[ strongSelfForPlay playWorker:clip atIndex:index generation:generation ];
		dispatch_group_leave( strongSelfForPlay->_playGroup );
	} );
}

- (void)playWorker:(videopong::ClipInfo)clip atIndex:(NSInteger)index generation:(uint64_t)generation
{
	__weak __typeof( self ) weakSelf       = self;
	void ( ^publishStatus )( std::string ) = ^( std::string text ) {
		NSString* nsText = [ NSString stringWithUTF8String:text.c_str() ];
		dispatch_async( dispatch_get_main_queue(), ^{
			__typeof( weakSelf ) strongSelf = weakSelf;
			if( strongSelf && generation == strongSelf.generationValue )
				strongSelf->_statusLabel.stringValue = nsText;
		} );
	};

	publishStatus( "Downloading '" + clip.title + "'..." );

	std::string localPath, error;
	if( !videopong::Api::DownloadClipToCache( clip, localPath, error ) )
	{
		publishStatus( "Error: " + error );
		dispatch_async( dispatch_get_main_queue(), ^{
			[ weakSelf applyDownloadState:videopong::ClipDownloadState::NotDownloaded atIndex:index generation:generation ];
		} );
		_playInProgress = false;
		return;
	}

	publishStatus( "Playing '" + clip.title + "'" );
	dispatch_async( dispatch_get_main_queue(), ^{
		[ weakSelf applyDownloadState:videopong::ClipDownloadState::Downloaded atIndex:index generation:generation ];
	} );

	//Always fires, even if the user has since moved on to another page - they did ask for this
	//specific clip to play. Runs on this background queue, not the main thread.
	if( _onClipSelected )
		_onClipSelected( clip, localPath );

	_playInProgress = false;
}

- (void)applyDownloadState:(videopong::ClipDownloadState)state atIndex:(NSInteger)index generation:(uint64_t)generation
{
	if( generation != self.generationValue )
		return;
	if( index < 0 || index >= (NSInteger)_gridView.downloadStates.count )
		return;
	_gridView.downloadStates[ index ] = @( (int)state );
	[ _gridView setNeedsDisplay:YES ];
}

- (uint64_t)generationValue
{
	return _generation.load();
}

@end

//--- BrowserWindow: the public, platform-neutral interface (VideoPongBrowserWindow.h). -----------

namespace videopong
{

struct BrowserWindow::Impl
{
	VideoPongBrowserWindowController* controller = nil;
};

BrowserWindow::BrowserWindow() = default;
BrowserWindow::~BrowserWindow()
{
	Close();
}

void BrowserWindow::Open( const std::string& initialQuery, ClipSelectedCallback onClipSelected )
{
	Close();

	impl = std::make_shared< Impl >();

	VideoPongBrowserWindowController* controller = [ [ VideoPongBrowserWindowController alloc ] init ];
	impl->controller                             = controller;

	std::string query          = initialQuery;
	ClipSelectedCallback callback = std::move( onClipSelected );

	//AppKit requires window/view creation on the main thread. This plugin's Open() can be called
	//from whatever thread the host uses for parameter/event handling, which may or may not be the
	//main thread - hence the isMainThread check, to avoid dispatch_sync deadlocking against itself.
	void ( ^openBlock )( void ) = ^{
		[ controller openWithInitialQuery:query onClipSelected:callback ];
	};
	if( [ NSThread isMainThread ] )
		openBlock();
	else
		dispatch_sync( dispatch_get_main_queue(), openBlock );
}

void BrowserWindow::Close()
{
	if( !impl )
		return;

	VideoPongBrowserWindowController* controller = impl->controller;
	if( controller )
	{
		void ( ^closeBlock )( void ) = ^{
			[ controller closeAndCleanUp ];
		};
		if( [ NSThread isMainThread ] )
			closeBlock();
		else
			dispatch_sync( dispatch_get_main_queue(), closeBlock );
	}

	impl.reset();
}

}//namespace videopong

#endif//defined( __APPLE__ )
