#!/usr/bin/env bash
# CopySDKFromSrc.sh
# Copy Mono SDK artifacts from dotnet/runtime build output into this SDK repository's
# platform subdirectory.
#
# This script does NOT build dotnet/runtime — it only copies pre-built artifacts.
# Use this in CI workflows where the build step is done separately.
#
# Usage:
#   ./CopySDKFromSrc.sh <dotnet-src-dir> <platform> [build-type]
#
# Arguments:
#   dotnet-src-dir   Path to the dotnet/runtime repository root (must be already built).
#   platform         Target platform: macos | android | ios | iossimulator | linux
#   build-type       Debug (default) | Release
#
# Examples:
#   ./CopySDKFromSrc.sh ~/dotnet-runtime macos
#   ./CopySDKFromSrc.sh ~/dotnet-runtime linux Debug
#   ./CopySDKFromSrc.sh ~/dotnet-runtime android Release

set -euo pipefail

# ── Arguments ─────────────────────────────────────────────────────────────────
DOTNET_SRC="${1:-}"
PLATFORM="${2:-}"
if [[ -z "$DOTNET_SRC" || -z "$PLATFORM" ]]; then
    echo "Error: missing required arguments." >&2
    echo "Usage: $0 <dotnet-src-dir> <platform> [build-type]" >&2
    exit 1
fi
DOTNET_SRC="$(cd "$DOTNET_SRC" && pwd)"

# Build type: Debug (default) or Release.
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

echo "=== CopySDKFromSrc ==="
echo "  Source    : $DOTNET_SRC"
echo "  Platform  : $PLATFORM"
echo "  Build type: $BUILD_TYPE"
echo "  SDK dir   : $SDK_DIR"
echo ""

# ── Validate platform ────────────────────────────────────────────────────────
case "$PLATFORM" in
    macos|ios|iossimulator|android|linux) ;;
    *)
        echo "Error: unknown platform '$PLATFORM'. Supported: macos | android | ios | iossimulator | linux" >&2
        exit 1
        ;;
esac

# ── Set platform-specific variables ──────────────────────────────────────────
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

echo ">>> Copying artifacts into SDK directory..."
rm -rf "$DEST"
mkdir -p "$DEST"

# ── Copy artifacts (cross-platform safe) ───────────────────────────────────────
# Use cd + cp -Rf . pattern to avoid BSD/GNU cp inconsistency:
#   - macOS (BSD cp): cp -Rf source/. dest/  → copies contents ✅
#   - Linux (GNU cp): cp -Rf source/. dest/  → may nest source dir ❌
#
# Safe pattern:
#   1. cd into source directory
#   2. cp -Rf . "$DEST/subdir/"

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

# ── Write VERSION.txt ──────────────────────────────────────────────────────────
DOTNET_BRANCH="$(cd "$DOTNET_SRC" && git rev-parse --abbrev-ref HEAD 2>/dev/null || echo 'unknown')"
DOTNET_COMMIT="$(cd "$DOTNET_SRC" && git rev-parse --short HEAD 2>/dev/null || echo 'unknown')"

case "$PLATFORM" in
    macos)   PLATFORM_LABEL="macOS (arm64)" ;;
    ios)     PLATFORM_LABEL="iOS (arm64)" ;;
    iossimulator) PLATFORM_LABEL="iOS Simulator (arm64)" ;;
    android) PLATFORM_LABEL="Android (arm64)" ;;
    linux)   PLATFORM_LABEL="Linux (x64)" ;;
esac

cat > "$DEST/VERSION.txt" <<EOF
dotnet/runtime source
  repo:   $(cd "$DOTNET_SRC" && git remote get-url origin 2>/dev/null || echo 'unknown')
  branch: $DOTNET_BRANCH
  commit: $DOTNET_COMMIT

Platform: $PLATFORM_LABEL
Build type: $BUILD_TYPE
EOF

echo ">>> Done. SDK updated at: $DEST"
