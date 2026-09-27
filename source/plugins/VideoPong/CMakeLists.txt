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

# std::filesystem (cache folder handling) and structured bindings need C++17.
target_compile_features(ffgl-plugin-videopong PRIVATE cxx_std_17)

find_package(Threads REQUIRED)
target_link_libraries(ffgl-plugin-videopong PRIVATE Threads::Threads)

# Talks to the videopong.net JSON API and downloads clips. On Windows this uses the built-in
# WinHTTP client instead (see VideoPongApi.cpp), so libcurl is only needed elsewhere.
if(WIN32)
    target_link_libraries(ffgl-plugin-videopong PRIVATE winhttp)
    # The clip browser popup window (VideoPongBrowserWindow.cpp) is Win32 + GDI+ only.
    target_link_libraries(ffgl-plugin-videopong PRIVATE gdi32 gdiplus)
    # SHGetKnownFolderPath (cache folder location) and ShellExecute (Open Cache Folder button).
    target_link_libraries(ffgl-plugin-videopong PRIVATE shell32)
else()
    find_package(CURL REQUIRED)
    target_link_libraries(ffgl-plugin-videopong PRIVATE CURL::libcurl)
endif()

# Decodes the downloaded mp4 clips. Pulled in via pkg-config, which is how vcpkg's ffmpeg port
# (and most system package managers) expose it.
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
