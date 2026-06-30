#!/usr/bin/env bash
# BuildMonoSDK.sh
# Build Mono runtime + libs from dotnet/runtime source, then copy artifacts
# into this SDK repository's platform subdirectory.
#
# Usage:
#   ./BuildMonoSDK.sh <dotnet-src-dir> [platform] [build-type]
#
# Arguments:
#   dotnet-src-dir   Path to the dotnet/runtime repository root.
#   platform         Target platform: macos | android | ios | iossimulator
#                    Defaults to the host platform (macos on macOS).
#   build-type       Debug (default) | Release
#                    Both Debug and Release output to the same {Platform}/ directory.
#                    To keep a Release build, copy it out before running Debug.
#
# Examples:
#   ./BuildMonoSDK.sh ~/Documents/Code/DotNet
#   ./BuildMonoSDK.sh ~/Documents/Code/DotNet android
#   ./BuildMonoSDK.sh ~/Documents/Code/DotNet macos Release
#   ./BuildMonoSDK.sh ~/Documents/Code/DotNet android Release
#   ./BuildMonoSDK.sh ~/Documents/Code/DotNet iossimulator Release

set -euo pipefail

# ── Arguments ─────────────────────────────────────────────────────────────────
DOTNET_SRC="${1:-}"
if [[ -z "$DOTNET_SRC" ]]; then
    echo "Error: dotnet source directory is required." >&2
    echo "Usage: $0 <dotnet-src-dir> [platform] [build-type]" >&2
    exit 1
fi
DOTNET_SRC="$(cd "$DOTNET_SRC" && pwd)"

case "$(uname -s)" in
    Darwin) HOST_PLATFORM="macos" ;;
    Linux)  HOST_PLATFORM="linux" ;;
    *)      HOST_PLATFORM="macos" ;;
esac
PLATFORM="${2:-$HOST_PLATFORM}"

# Build type: Debug (default) or Release.
# Case-normalise to capitalised form expected by dotnet/runtime artifact paths.
# Use tr for lowercase conversion (compatible with bash 3.x on macOS).
BUILD_TYPE_RAW="${3:-Debug}"
BUILD_TYPE_LOWER="$(echo "$BUILD_TYPE_RAW" | tr '[:upper:]' '[:lower:]')"
case "$BUILD_TYPE_LOWER" in
    debug)   BUILD_TYPE="Debug" ;;
    release) BUILD_TYPE="Release" ;;
    *)
        echo "Error: unknown build-type '${BUILD_TYPE_RAW}'. Use Debug or Release." >&2
        exit 1
        ;;
esac

SDK_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== BuildMonoSDK ==="
echo "  Source    : $DOTNET_SRC"
echo "  Platform  : $PLATFORM"
echo "  Build type: $BUILD_TYPE"
echo "  SDK dir   : $SDK_DIR"
echo ""

# ── Build ──────────────────────────────────────────────────────────────────────
cd "$DOTNET_SRC"

# Cross-platform builds (ios/android) leave stale NuGet restore caches that
# reference the previously built host TFM (e.g. net10.0-osx). If present they
# cause NETSDK1005 "doesn't have a target for net10.0-<target>" errors.
# Delete them before every non-host build so NuGet re-restores for the right TFM.
if [[ "$PLATFORM" != "macos" ]]; then
    echo ">>> Clearing stale NuGet restore caches for cross-platform build..."
    rm -f "$DOTNET_SRC/artifacts/obj/sfx-src/project.assets.json"
    rm -f "$DOTNET_SRC/artifacts/obj/sfx-finish/project.assets.json"
fi

case "$PLATFORM" in
    macos)
        echo ">>> Building Mono + libs for macOS (arm64, $BUILD_TYPE)..."
        ./build.sh mono+libs -configuration "$BUILD_TYPE"
        ;;
    ios)
        echo ">>> Building Mono + libs for iOS (arm64, $BUILD_TYPE)..."
        ./build.sh mono+libs -os ios -arch arm64 -configuration "$BUILD_TYPE"
        ;;
    iossimulator)
        echo ">>> Building Mono + libs for iOS Simulator (arm64, $BUILD_TYPE)..."
        ./build.sh mono+libs -os iossimulator -arch arm64 -configuration "$BUILD_TYPE"
        ;;
    android)
        echo ">>> Building Mono + libs for Android (arm64, $BUILD_TYPE)..."
        ./build.sh mono+libs -os android -arch arm64 -configuration "$BUILD_TYPE"
        ;;
    linux)
        echo ">>> Building Mono + libs for Linux (x64, $BUILD_TYPE)..."
        ./build.sh mono+libs -configuration "$BUILD_TYPE"
        ;;
    *)
        echo "Error: unknown platform '$PLATFORM'. Supported: macos | android | ios | iossimulator | linux" >&2
        exit 1
        ;;
esac

echo ""
echo ">>> Build complete. Copying artifacts into SDK directory..."

# ── Copy artifacts ─────────────────────────────────────────────────────────────
# SDK directory layout:
#   MonoSDK/
#     Mac/              — include/  lib/  bin/  runtime/
#     Android/          — include/  lib/  bin/  runtime/
#     IOS/              — include/  lib/  bin/  runtime/
#     IOSSimulator/     — include/  lib/  bin/  runtime/
#     include/          (shared mono headers, always updated from the latest build)
#
# Both Debug and Release builds write to the same {Platform}/ directory.
# To preserve a Release build separately, copy it out before building Debug.

SRC_ARTIFACTS="$DOTNET_SRC/artifacts"

case "$PLATFORM" in
    macos)
        MONO_TRIPLE="osx.arm64.$BUILD_TYPE"
        RUNTIME_TFM="net10.0-osx-$BUILD_TYPE-arm64"
        DEST="$SDK_DIR/Mac"
        ;;
    ios)
        MONO_TRIPLE="ios.arm64.$BUILD_TYPE"
        RUNTIME_TFM="net10.0-ios-$BUILD_TYPE-arm64"
        DEST="$SDK_DIR/IOS"
        ;;
    iossimulator)
        MONO_TRIPLE="iossimulator.arm64.$BUILD_TYPE"
        RUNTIME_TFM="net10.0-iossimulator-$BUILD_TYPE-arm64"
        DEST="$SDK_DIR/IOSSimulator"
        ;;
    android)
        MONO_TRIPLE="android.arm64.$BUILD_TYPE"
        RUNTIME_TFM="net10.0-android-$BUILD_TYPE-arm64"
        DEST="$SDK_DIR/Android"
        ;;
    linux)
        MONO_TRIPLE="linux.x64.$BUILD_TYPE"
        RUNTIME_TFM="net10.0-linux-$BUILD_TYPE-x64"
        DEST="$SDK_DIR/Linux"
        ;;
esac

rm -rf "$DEST"
mkdir -p "$DEST"

# ── Copy artifacts (cross-platform safe) ───────────────────────────────────────
# Use rsync if available; fall back to cd + cp -Rf * pattern.
# The "cp -Rf source/. dest/" pattern behaves differently on macOS (BSD cp) vs Linux (GNU cp):
#   - macOS: copies contents of source/ into dest/  ✅
#   - Linux:  may nest source dir itself inside dest/  ❌
#
# Cross-platform safe pattern:
#   1. cd into source directory
#   2. cp -Rf * "$DEST/subdir/"   (NOT "." — use explicit glob)

# include/: strip mono-2.0/ prefix → files land in $DEST/include/ directly
if [[ -d "$SRC_ARTIFACTS/obj/mono/$MONO_TRIPLE/out/include/mono-2.0" ]]; then
    cd "$SRC_ARTIFACTS/obj/mono/$MONO_TRIPLE/out/include/mono-2.0"
    cp -Rf . "$DEST/include/" 2>/dev/null || true
    # Flatten: if mono-2.0/ itself got nested, fix it
    if [[ -d "$DEST/include/mono-2.0" ]]; then
        mv "$DEST/include/mono-2.0/"* "$DEST/include/" 2>/dev/null || true
        rm -rf "$DEST/include/mono-2.0"
    fi
    cd "$SDK_DIR"
fi

# lib/: handle both flat and nested (lib/lib/) layouts
if [[ -d "$SRC_ARTIFACTS/obj/mono/$MONO_TRIPLE/out/lib" ]]; then
    cd "$SRC_ARTIFACTS/obj/mono/$MONO_TRIPLE/out/lib"
    # If there's a lib/ subdir, copy from there; otherwise copy current dir contents
    if [[ -d "lib" ]]; then
        cd lib
        cp -Rf . "$DEST/lib/" 2>/dev/null || true
        cd ..
    else
        cp -Rf . "$DEST/lib/" 2>/dev/null || true
    fi
    # Flatten: if lib/ itself got nested, fix it
    if [[ -d "$DEST/lib/lib" ]]; then
        mv "$DEST/lib/lib/"* "$DEST/lib/" 2>/dev/null || true
        rm -rf "$DEST/lib/lib"
    fi
    cd "$SDK_DIR"
fi

# bin/: only exists for macOS (mono-sgen); iOS/Android/Linux may not have it
if [[ -d "$SRC_ARTIFACTS/obj/mono/$MONO_TRIPLE/out/bin" ]]; then
    cd "$SRC_ARTIFACTS/obj/mono/$MONO_TRIPLE/out/bin"
    cp -Rf . "$DEST/bin/" 2>/dev/null || true
    # Flatten: if bin/ itself got nested, fix it
    if [[ -d "$DEST/bin/bin" ]]; then
        mv "$DEST/bin/bin/"* "$DEST/bin/" 2>/dev/null || true
        rm -rf "$DEST/bin/bin"
    fi
    cd "$SDK_DIR"
fi

# runtime/: copy BCL DLLs
if [[ -d "$SRC_ARTIFACTS/bin/runtime/$RUNTIME_TFM" ]]; then
    cd "$SRC_ARTIFACTS/bin/runtime/$RUNTIME_TFM"
    cp -Rf . "$DEST/runtime/" 2>/dev/null || true
    # Flatten: if $RUNTIME_TFM/ itself got nested, fix it
    NESTED=$(find "$DEST/runtime" -maxdepth 1 -type d ! -path "$DEST/runtime" | head -1)
    if [[ -n "$NESTED" && $(find "$DEST/runtime" -maxdepth 1 -type f | wc -l) -eq 0 ]]; then
        mv "$NESTED/"* "$DEST/runtime/" 2>/dev/null || true
        rm -rf "$NESTED"
    fi
    cd "$SDK_DIR"
fi

# IL/: copy IL assemblies
if [[ -d "$SRC_ARTIFACTS/bin/mono/$MONO_TRIPLE/IL" ]]; then
    cd "$SRC_ARTIFACTS/bin/mono/$MONO_TRIPLE/IL"
    cp -Rf . "$DEST/runtime/" 2>/dev/null || true
    cd "$SDK_DIR"
fi

# iOS/IOSSimulator: copy native interop libs from bin/native/<TFM>/.
# These are NOT in obj/mono/<TRIPLE>/out/lib/ and must be copied separately.
#   lib/*.a    — static libs (Globalization etc.) linked by MonoSDK.Build.cs
#   lib/*.dylib — staged alongside the app at runtime (RuntimeDependencies)
#   tools/*.dylib — framework素材，供 MakeMonoFramework.sh 打包 embedded framework
if [[ "$PLATFORM" == "ios" || "$PLATFORM" == "iossimulator" ]]; then
    NATIVE_TFM="net10.0-${PLATFORM}-${BUILD_TYPE}-arm64"
    mkdir -p "$DEST/tools"
    cp -f "$SRC_ARTIFACTS/bin/native/$NATIVE_TFM"/lib*.a    "$DEST/lib/"   2>/dev/null || true
    cp -f "$SRC_ARTIFACTS/bin/native/$NATIVE_TFM"/lib*.dylib "$DEST/lib/"  2>/dev/null || true
    cp -f "$SRC_ARTIFACTS/bin/native/$NATIVE_TFM"/lib*.dylib "$DEST/tools/" 2>/dev/null || true
fi

# Update shared include/ (mono headers are platform-independent)
rm -rf "$SDK_DIR/include"
cp -Rf "$SRC_ARTIFACTS/obj/mono/$MONO_TRIPLE/out/include/mono-2.0/" "$SDK_DIR/include"

# ── Write VERSION.txt into the platform directory ──────────────────────────────
DOTNET_BRANCH="$(cd "$DOTNET_SRC" && git rev-parse --abbrev-ref HEAD 2>/dev/null || echo 'unknown')"
DOTNET_COMMIT="$(cd "$DOTNET_SRC" && git rev-parse --short HEAD 2>/dev/null || echo 'unknown')"
DOTNET_REMOTE="$(cd "$DOTNET_SRC" && git remote get-url origin 2>/dev/null || echo 'unknown')"

case "$PLATFORM" in
    macos)   PLATFORM_LABEL="macOS (arm64)";         BUILD_CMD="./build.sh mono+libs -configuration $BUILD_TYPE" ;;
    ios)     PLATFORM_LABEL="iOS (arm64)";           BUILD_CMD="./build.sh mono+libs -os ios -arch arm64 -configuration $BUILD_TYPE" ;;
    iossimulator) PLATFORM_LABEL="iOS Simulator (arm64)"; BUILD_CMD="./build.sh mono+libs -os iossimulator -arch arm64 -configuration $BUILD_TYPE" ;;
    android) PLATFORM_LABEL="Android (arm64)";       BUILD_CMD="./build.sh mono+libs -os android -arch arm64 -configuration $BUILD_TYPE" ;;
    linux)   PLATFORM_LABEL="Linux (x64)";           BUILD_CMD="./build.sh mono+libs -configuration $BUILD_TYPE" ;;
esac

cat > "$DEST/VERSION.txt" <<EOF
dotnet/runtime source
  repo:   $DOTNET_REMOTE
  branch: $DOTNET_BRANCH
  commit: $DOTNET_COMMIT

Platform: $PLATFORM_LABEL
Build type: $BUILD_TYPE
Build command: $BUILD_CMD
EOF

# ── iOS / IOSSimulator: AOT-compile System.Private.CoreLib ────────────────────
# iOS forbids JIT (W^X). Mono's INTERP mode requires at least System.Private.CoreLib
# to have an AOT module so the runtime can bootstrap without generating any code.
# We use mono-aot-cross (cross-compiler built alongside the runtime) to produce a
# static library that exports the symbol mono_aot_module_System_Private_CoreLib_info,
# which is then registered in CSMonoRuntime.cpp via mono_aot_register_module().
#
# The tools and input DLL are kept in <DEST>/tools/ so the SDK is self-contained
# and re-running AOT does not require the dotnet/runtime source tree.
#
# IOSSimulator clang invocation uses -target arm64-apple-ios15.0-simulator so the
# resulting .o carries the simulator ABI slice; without this the linker would reject
# it when building for the simulator target.
#
# MVID alignment: each platform AOT-compiles its own CoreLib from MONO_TRIPLE artifacts,
# so the MVID in the resulting .a always matches the DLL in {Platform}/runtime/.
# PackageProjectMono.cs stages each platform's own BCL without cross-platform substitution.
if [[ "$PLATFORM" == "ios" || "$PLATFORM" == "iossimulator" ]]; then
    echo ""
    echo ">>> AOT-compiling System.Private.CoreLib for $PLATFORM..."

    TOOLS_DIR="$DEST/tools"
    mkdir -p "$TOOLS_DIR"

    # Copy mono-aot-cross tool and input CoreLib into the SDK tools/ dir.
    # Use the platform's own CoreLib so the MVID in the .a matches {Platform}/runtime/.
    CROSS_SRC="$SRC_ARTIFACTS/obj/mono/$MONO_TRIPLE/cross/mono/mini"
    CORELIB_SRC="$SRC_ARTIFACTS/obj/mono/System.Private.CoreLib/$MONO_TRIPLE/System.Private.CoreLib.dll"

    if [[ ! -f "$CORELIB_SRC" ]]; then
        echo "Error: CoreLib not found at $CORELIB_SRC" >&2
        exit 1
    fi

    cp "$CROSS_SRC/mono-aot-cross"       "$TOOLS_DIR/mono-aot-cross"
    cp "$CROSS_SRC/mono-aot-cross.dwarf" "$TOOLS_DIR/mono-aot-cross.dwarf" 2>/dev/null || true
    chmod +x "$TOOLS_DIR/mono-aot-cross"
    cp "$CORELIB_SRC" "$TOOLS_DIR/System.Private.CoreLib.dll"

    # Run AOT cross-compiler: produces System.Private.CoreLib.dll.s
    cd "$TOOLS_DIR"
    ./mono-aot-cross --aot=asmonly,static,interp System.Private.CoreLib.dll

    # Assemble .s → .o with the correct SDK and ABI target.
    if [[ "$PLATFORM" == "ios" ]]; then
        xcrun -sdk iphoneos clang -arch arm64 \
            -o System.Private.CoreLib.dll.o \
            -c System.Private.CoreLib.dll.s
    else
        xcrun -sdk iphonesimulator clang -arch arm64 \
            -target arm64-apple-ios15.0-simulator \
            -o System.Private.CoreLib.dll.o \
            -c System.Private.CoreLib.dll.s
    fi

    # Pack into a static library and install into lib/.
    ar rcs System.Private.CoreLib.dll.a System.Private.CoreLib.dll.o
    cp System.Private.CoreLib.dll.a "$DEST/lib/System.Private.CoreLib.dll.a"

    cd "$DOTNET_SRC"
    echo ">>> AOT CoreLib static lib: $DEST/lib/System.Private.CoreLib.dll.a"
fi

# ── iOS / IOSSimulator: build Mono.embeddedframework.zip ──────────────────────
# Delegated to MakeMonoFramework.sh which can also be run standalone after a build.
if [[ "$PLATFORM" == "ios" || "$PLATFORM" == "iossimulator" ]]; then
    bash "$SDK_DIR/MakeMonoFramework.sh" "$PLATFORM" "$DEST"
fi

echo ">>> Done. SDK updated at: $DEST"
