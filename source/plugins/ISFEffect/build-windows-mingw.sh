#!/usr/bin/env bash
# Builds ISFEffect.dll for 64-bit Windows, from Linux or macOS, using a mingw-w64 cross compiler.
# See ISFBrowser/build-windows-mingw.sh (the source-plugin sibling) for more detail on this path.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
PLUGIN_DIR="$REPO_ROOT/source/plugins/ISFEffect"
GLEW_INC="$REPO_ROOT/deps/glew-2.1.0/include"
GLEW_LIB="$REPO_ROOT/deps/glew-2.1.0/lib/mingw-w64-x64"
OUT_DIR="$REPO_ROOT/binaries/x64/Debug"
CXX=x86_64-w64-mingw32-g++-posix

if ! command -v "$CXX" >/dev/null 2>&1; then
	if command -v apt-get >/dev/null 2>&1; then
		echo "$CXX not found, installing g++-mingw-w64-x86-64..."
		sudo apt-get update && sudo apt-get install -y g++-mingw-w64-x86-64
	else
		echo "error: $CXX not found. Install a mingw-w64 x86_64 cross compiler and try again." >&2
		exit 1
	fi
fi

if [ ! -f "$GLEW_LIB/libglew32s.a" ]; then
	echo "error: missing vendored dependency: $GLEW_LIB/libglew32s.a" >&2
	exit 1
fi

BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT

mkdir -p "$BUILD_DIR/case-shim"
echo '#include <windows.h>' > "$BUILD_DIR/case-shim/Windows.h"

COMMON_FLAGS=(
	-std=c++17 -O2 -Wall -DNDEBUG
	-I "$BUILD_DIR/case-shim"
	-I "$REPO_ROOT/source/lib"
	-I "$REPO_ROOT/source/lib/ffgl"
	-I "$GLEW_INC" -DGLEW_STATIC -DGLEW_NO_GLU
)

echo "Compiling ffgl-sdk..."
"$CXX" "${COMMON_FLAGS[@]}" -c "$REPO_ROOT/source/lib/FFGLSDK.cpp" -o "$BUILD_DIR/FFGLSDK.o"

echo "Compiling ISF Effect sources..."
for src in IsfJson ISFEffect; do
	"$CXX" "${COMMON_FLAGS[@]}" -I "$PLUGIN_DIR" \
		-c "$PLUGIN_DIR/$src.cpp" -o "$BUILD_DIR/$src.o"
done

echo "Linking ISFEffect.dll..."
mkdir -p "$OUT_DIR"
"$CXX" -shared -static \
	-o "$OUT_DIR/ISFEffect.dll" \
	"$BUILD_DIR"/*.o \
	-L "$GLEW_LIB" -lglew32s \
	-lopengl32 -lgdi32 -luser32 \
	-Wl,--out-implib,"$OUT_DIR/ISFEffect.lib"

echo
echo "Done: $OUT_DIR/ISFEffect.dll"
echo "Copy that file, plus your .fs shader files, into the same folder on Windows:"
echo '  %USERPROFILE%\Documents\Resolume\Extra Effects\'
