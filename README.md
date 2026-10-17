# MonoSDK for UnrealSharp

Mono runtime SDK for [UnrealSharp](https://github.com/RPG3D/UnrealSharp). This repository contains build scripts, CI workflows, and UBT integration files. Pre-built binaries are distributed via [GitHub Releases](https://github.com/RPG3D/MonoSDK/releases).

## Download (Recommended)

1. Go to [Releases](https://github.com/RPG3D/MonoSDK/releases)
2. Download `MonoSDK-{version}-{build-type}.zip`
3. Extract to UnrealSharp's `Source/ThirdParty/MonoSDK/`

## Directory layout

Every platform uses the same depth — `<Platform>/<arch>/` — so there is one rule for all of them and a platform can gain an architecture without any consumer changing:

```
Win64/x64/                                  # Windows
Android/arm64/                              # Android
IOS/arm64/                                  # iOS device
Linux/x64/                                  # Linux
Mac/arm64/          Mac/x64/                # macOS: both architectures
IOSSimulator/arm64/ IOSSimulator/x64/       # iOS Simulator: both architectures
include/                                    # shared Mono C headers
```

Each `<Platform>/<arch>/` holds the same subdirectories: `lib/` (native libraries and interop dylibs), `runtime/` (BCL assemblies), `include/`, plus `bin/` on macOS/Windows and `tools/` on iOS. `MonoSDK.Build.cs` resolves the directory in `ResolveMonoPlatformDir()` from the target platform and architecture. A flat `<Platform>/` directory is still accepted as a fallback, so SDKs downloaded before this layout change keep working.

## Build from Source

```bash
# Clone dotnet/runtime (~/dotnet-runtime works on Windows Git Bash / WSL too)
git clone --depth 1 --branch release/10.0 https://github.com/dotnet/runtime.git ~/dotnet-runtime

# macOS / Linux / iOS / Android
./BuildMonoSDK.sh <dotnet-runtime-dir> <platform> <build-type> [arch]
# platform:   macos | android | ios | iossimulator | linux
# build-type: Debug | Release
# arch:       x64 | arm64   (defaults to the host architecture)

# Windows
BuildMonoSDK.bat <dotnet-runtime-dir>
```

Supported platform/arch combinations — anything else is rejected up front:

| Platform | Architectures | Output directory |
|----------|---------------|------------------|
| `macos` | `arm64`, `x64` | `Mac/arm64/`, `Mac/x64/` |
| `iossimulator` | `arm64`, `x64` | `IOSSimulator/arm64/`, `IOSSimulator/x64/` |
| `ios` | `arm64` | `IOS/` |
| `android` | `arm64` | `Android/` |
| `linux` | `x64` | `Linux/` |

Cross-arch builds (e.g. `macos x64` on an Apple Silicon machine) must pass `arch` explicitly; the default is the host architecture. The script clears stale NuGet restore caches on cross-platform/arch builds, which would otherwise fail with `NETSDK1005`.

Examples:

```bash
./BuildMonoSDK.sh ~/dotnet-runtime macos        Release arm64
./BuildMonoSDK.sh ~/dotnet-runtime macos        Release x64
./BuildMonoSDK.sh ~/dotnet-runtime iossimulator Release arm64
./BuildMonoSDK.sh ~/dotnet-runtime iossimulator Release x64
./BuildMonoSDK.sh ~/dotnet-runtime android      Release arm64
```

Prerequisites: Visual Studio 2022 (Windows), Xcode (macOS/iOS), Android NDK (Android), CMake + Ninja.

## CI

Both workflows are `workflow_dispatch`-only and run on GitHub-hosted runners — Mono is not built on developer machines for releases.

- `build-all.yml` — one job per platform. macOS and iOS Simulator build both architectures, each on a native runner (`macos-26` for arm64, `macos-26-intel` for x64), so no leg is cross-arch. Artifacts are named `MonoSDK-macOS-{arch}-{build-type}` and `MonoSDK-iOS-Simulator-{arch}-{build-type}`, uploaded from `Mac/{arch}/` and `IOSSimulator/{arch}/`.
- `package-release.yml` — downloads every platform artifact into the matching directory (`Mac/arm64`, `Mac/x64`, `IOSSimulator/arm64`, `IOSSimulator/x64`; flat for single-arch platforms), verifies them, and publishes a single release zip.

## Repository Structure

```
├── .github/workflows/        # CI: build-all.yml + package-release.yml
├── CopySDKFromSrc.sh / .bat  # CI artifact copy scripts
├── BuildMonoSDK.sh / .bat    # Local build scripts
├── MakeMonoFramework.sh      # iOS framework packager
├── MonoSDK.Build.cs          # UnrealBuildTool external module
├── MonoSDK_APL.xml           # Android APL manifest
└── include/                  # Shared Mono C headers
```

## References

- [dotnet/runtime](https://github.com/dotnet/runtime) — .NET runtime source
- [UnrealSharp](https://github.com/RPG3D/UnrealSharp)

## License

MIT.
