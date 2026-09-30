add_library(ffgl-plugin-videopong STATIC)
add_library(ffgl::plugin::videopong ALIAS ffgl-plugin-videopong)
target_sources(ffgl-plugin-videopong PRIVATE
    VideoPong.h
    VideoPong.cpp
    VideoPongApi.h
    VideoPongApi.cpp
    VideoPongJson.h
    VideoPongJson.cpp
    VideoDecoder.h
    VideoDecoder.cpp
    VideoPongBrowserWindow.h
    VideoPongBrowserWindow.cpp
)
target_link_libraries(ffgl-plugin-videopong PRIVATE ffgl::sdk)

if(APPLE)
    # Cocoa/AppKit implementation of the clip browser popup (VideoPongBrowserWindow.cpp's own
    # window code is Win32-only; see its #elif !defined(__APPLE__) guard). Needs ARC, which isn't
    # CMake's default for Objective-C++ sources.
    target_sources(ffgl-plugin-videopong PRIVATE VideoPongBrowserWindow.mm)
    set_source_files_properties(VideoPongBrowserWindow.mm PROPERTIES COMPILE_FLAGS "-fobjc-arc")
    target_link_libraries(ffgl-plugin-videopong PRIVATE "-framework Cocoa")
endif()

# std::filesystem (cache folder handling) and structured bindings need C++17.
target_compile_features(ffgl-plugin-videopong PRIVATE cxx_std_17)

find_package(Threads REQUIRED)
target_link_libraries(ffgl-plugin-videopong PRIVATE Threads::Threads)

# Talks to the videopong.net JSON API and downloads clips. On Windows this uses the built-in
# WinHTTP client instead (see VideoPongApi.cpp); on macOS and Linux this links the system
# libcurl (macOS ships one; no vendoring needed there either, same as winhttp on Windows).
if(WIN32)
    target_link_libraries(ffgl-plugin-videopong PRIVATE winhttp)
    # The clip browser popup window (VideoPongBrowserWindow.cpp) is Win32 + GDI+ only. On other
    # platforms BrowserWindow::Open() is a no-op stub (logs and returns) - a native replacement
    # (e.g. Cocoa/AppKit on macOS) would need its own platform-specific .cpp/.mm added to
    # target_sources above and its own frameworks linked here, but nothing references those
    # symbols yet so there's nothing to link until that implementation exists.
    target_link_libraries(ffgl-plugin-videopong PRIVATE gdi32 gdiplus)
    # SHGetKnownFolderPath (cache folder location) and ShellExecute (Open Cache Folder button).
    target_link_libraries(ffgl-plugin-videopong PRIVATE shell32)
else()
    find_package(CURL REQUIRED)
    target_link_libraries(ffgl-plugin-videopong PRIVATE CURL::libcurl)
endif()

# Decodes the downloaded mp4 clips. Pulled in via pkg-config, which is how vcpkg's ffmpeg port
# (and most system package managers, including Homebrew) expose it. That's fine for local dev
# builds, but for a zero-install macOS binary (matching Windows' statically-linked .dll) this
# would need static macOS FFmpeg libs vendored the same way as Windows' under
# deps/ffmpeg-6.1/lib/win64-mingw - e.g. deps/ffmpeg-6.1/lib/macos-arm64 and macos-x64 - and
# linked directly instead of via pkg-config, the way build-windows-mingw.sh does it. No such
# static macOS build has been produced yet.
find_package(PkgConfig REQUIRED)
pkg_check_modules(FFMPEG REQUIRED IMPORTED_TARGET
    libavformat
    libavcodec
    libavutil
    libswscale
)
target_link_libraries(ffgl-plugin-videopong PRIVATE PkgConfig::FFMPEG)

install(
    TARGETS     ffgl-plugin-videopong
    EXPORT      ffgl--plugin-videopong-targets
    DESTINATION ${CMAKE_INSTALL_LIBDIR}
)
