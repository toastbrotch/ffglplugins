#!/usr/bin/env bash
# Builds VideoPong.dll for 64-bit Windows, from Linux or macOS, using a mingw-w64 cross compiler.
#
# This is NOT the officially supported build path for this repo (see the top-level README for
# the Visual Studio / Xcode workflow) - it exists because VideoPong was originally built and
# verified without access to a Windows machine. Prefer Visual Studio when you have it; use this
# script when you don't.
#
# GLEW and FFmpeg are vendored as prebuilt mingw-w64 static libraries under deps/ (see
# deps/glew-2.1.0/lib/mingw-w64-x64 and deps/ffmpeg-6.1/lib/win64-mingw), so this script only
# needs a compiler - it doesn't fetch or build either of those itself.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PLUGIN_DIR="$REPO_ROOT/source/plugins/VideoPong"
GLEW_INC="$REPO_ROOT/deps/glew-2.1.0/include"
GLEW_LIB="$REPO_ROOT/deps/glew-2.1.0/lib/mingw-w64-x64"
FFMPEG_INC="$REPO_ROOT/deps/ffmpeg-6.1/include"
FFMPEG_LIB="$REPO_ROOT/deps/ffmpeg-6.1/lib/win64-mingw"
OUT_DIR="$REPO_ROOT/binaries/x64/Debug"
CXX=x86_64-w64-mingw32-g++-posix

# The "posix" variant is required (not the "win32" one some distros default to) - it's the one
# with a working std::thread/std::mutex, which VideoPong's background download/decode needs.
if ! command -v "$CXX" >/dev/null 2>&1; then
	if command -v apt-get >/dev/null 2>&1; then
		echo "$CXX not found, installing g++-mingw-w64-x86-64..."
		sudo apt-get update && sudo apt-get install -y g++-mingw-w64-x86-64
	else
		echo "error: $CXX not found. Install a posix-threads mingw-w64 x86_64 cross compiler and try again." >&2
		exit 1
	fi
fi

for dep in "$GLEW_LIB/libglew32s.a" "$FFMPEG_LIB/libavformat.a"; do
	if [ ! -f "$dep" ]; then
		echo "error: missing vendored dependency: $dep" >&2
		exit 1
	fi
done

BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT

# mingw's own header is windows.h (lowercase); some FFGL SDK files spell it Windows.h, which
# only resolves on Windows' case-insensitive filesystem. This shim makes it resolve when
# cross-compiling from a case-sensitive filesystem (Linux/macOS) too.
mkdir -p "$BUILD_DIR/case-shim"
echo '#include <windows.h>' > "$BUILD_DIR/case-shim/Windows.h"

COMMON_FLAGS=(
	-std=c++17 -O2 -Wall
	-I "$BUILD_DIR/case-shim"
	-I "$REPO_ROOT/source/lib"
	-I "$REPO_ROOT/source/lib/ffgl"
	-I "$GLEW_INC" -DGLEW_STATIC -DGLEW_NO_GLU
)

echo "Compiling ffgl-sdk..."
"$CXX" "${COMMON_FLAGS[@]}" -c "$REPO_ROOT/source/lib/FFGLSDK.cpp" -o "$BUILD_DIR/FFGLSDK.o"

echo "Compiling VideoPong sources..."
for src in VideoPongJson VideoPongApi VideoDecoder VideoPongBrowserWindow VideoPong; do
	"$CXX" "${COMMON_FLAGS[@]}" -I "$FFMPEG_INC" -I "$PLUGIN_DIR" \
		-c "$PLUGIN_DIR/$src.cpp" -o "$BUILD_DIR/$src.o"
done

echo "Linking VideoPong.dll..."
mkdir -p "$OUT_DIR"
"$CXX" -shared -static \
	-o "$OUT_DIR/VideoPong.dll" \
	"$BUILD_DIR"/*.o \
	-L "$GLEW_LIB" -lglew32s \
	-L "$FFMPEG_LIB" -lavformat -lavcodec -lavutil -lswscale -lswresample \
	-lopengl32 -lgdi32 -lgdiplus -lwinhttp -lole32 -lshell32 -luser32 -lbcrypt -lws2_32 \
	-Wl,--out-implib,"$OUT_DIR/VideoPong.lib"

echo
echo "Done: $OUT_DIR/VideoPong.dll"
echo "Copy that one file to Resolume's addon folder on Windows:"
echo '  %USERPROFILE%\Documents\Resolume\Extra Effects\'
echo "It's statically linked (only depends on standard Windows DLLs), so nothing else needs to go with it."
