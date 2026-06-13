# MonoSDK for UnrealSharp

Mono runtime SDK for [UnrealSharp](https://github.com/RPG3D/UnrealSharp). This repository contains build scripts, CI workflows, and UBT integration files. Pre-built binaries are distributed via [GitHub Releases](https://github.com/RPG3D/MonoSDK/releases).

## Download (Recommended)

1. Go to [Releases](https://github.com/RPG3D/MonoSDK/releases)
2. Download `MonoSDK-{version}-{build-type}.zip`
3. Extract to UnrealSharp's `Source/ThirdParty/MonoSDK/`

## Build from Source

```bash
# Clone dotnet/runtime
git clone --depth 1 --branch release/10.0 https://github.com/dotnet/runtime.git

# macOS / Linux / iOS / Android
./BuildMonoSDK.sh ~/dotnet-runtime <platform> <build-type>
# platform: macos | android | ios | iossimulator

# Windows
BuildMonoSDK.bat C:\dotnet-runtime
```

Prerequisites: Visual Studio 2022 (Windows), Xcode (macOS/iOS), Android NDK (Android), CMake + Ninja.

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
