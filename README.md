# MonoSDK for UnrealSharp

This repository contains **build scripts, CI/CD workflows, and UBT integration files** for the Mono runtime SDK used by [UnrealSharp](https://github.com/RPG3D/UnrealSharp). Pre-built SDK binaries are **not** stored in this repository — they are distributed via [GitHub Releases](https://github.com/RPG3D/MonoSDK/releases).

## 📦 Getting the SDK

### Option 1: Download from GitHub Releases (Recommended)

1. Go to [Releases](https://github.com/RPG3D/MonoSDK/releases)
2. Download the latest `MonoSDK-{version}-{build-type}.zip`
3. Extract to your UnrealSharp plugin's `Source/ThirdParty/MonoSDK/` directory

**ZIP structure**:
```
MonoSDK-{version}-Release.zip
├── Win64/              # Windows x64
├── Mac/                # macOS arm64
├── Android/            # Android arm64
├── IOS/                # iOS arm64
├── IOSSimulator/       # iOS Simulator arm64
├── include/            # Shared Mono C headers
├── BuildMonoSDK.sh     # Build script (macOS/Linux)
├── BuildMonoSDK.bat    # Build script (Windows)
├── MakeMonoFramework.sh # iOS framework packager
├── MonoSDK.Build.cs    # UBT external module
└── MonoSDK_APL.xml     # Android APL manifest
```

### Option 2: Build from Source

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

---

## 🔄 Automated Builds (GitHub Actions)

All platforms are built from a **single unified workflow**.

### Workflows

| Workflow | Purpose | Trigger |
|----------|---------|---------|
| **`build-all.yml`** | Build Mono for 1-6 platforms | Manual (`workflow_dispatch`) |
| **`package-release.yml`** | Assemble all platforms into a release ZIP | Manual or tag push |

### `build-all.yml` — Multi-Platform Build

Triggered manually from the [Actions tab](https://github.com/RPG3D/MonoSDK/actions/workflows/build-all.yml):

1. Select **"Build MonoSDK - All Platforms"**
2. Click **"Run workflow"**
3. Choose:
   - **Build type**: `Debug` or `Release`
   - **Platform toggles**: enable/disable `build_windows`, `build_linux`, `build_macos`, `build_android`, `build_ios`, `build_iossimulator`
4. Click **"Run workflow"**

Each platform builds in a parallel job, uploads its artifact with 30-day retention.

### `package-release.yml` — Release Packaging

Triggered after all platforms are built:

1. Select **"Package and Release MonoSDK"**
2. Enter **version** (e.g., `v0.2.0`) and **build type** (must match the `build-all.yml` run)
3. The workflow downloads all platform artifacts from the latest `build-all.yml` run, assembles them, and creates a GitHub Release with the ZIP attached.

---

## 🏗️ Repository Structure

```
MonoSDK/
├── .github/workflows/
│   ├── build-all.yml          # Unified multi-platform build
│   └── package-release.yml    # Release packaging
├── CopySDKFromSrc.sh          # CI: copy artifacts (macOS/Linux)
├── CopySDKFromSrc.bat         # CI: copy artifacts (Windows)
├── BuildMonoSDK.sh            # Local build script (macOS/Linux)
├── BuildMonoSDK.bat           # Local build script (Windows)
├── MakeMonoFramework.sh       # iOS embedded framework packager
├── MonoSDK.Build.cs           # UnrealBuildTool external module
├── MonoSDK_APL.xml            # Android Plugin Language manifest
└── include/                   # Shared Mono C headers
```

Platform directories (`Win64/`, `Mac/`, `Android/`, `IOS/`, `IOSSimulator/`) are **not** stored in git — they are build outputs downloaded from Releases or produced locally.

---

## 📋 Version Information

Each platform directory built by CI contains a `VERSION.txt`:

```
dotnet/runtime source
  repo:   https://github.com/dotnet/runtime.git
  branch: release/10.0
  commit: a1b2c3d

Platform: Windows (x64)
Build type: Release
```

---

## 📚 References

- [dotnet/runtime](https://github.com/dotnet/runtime) — .NET runtime source
- [UnrealSharp](https://github.com/RPG3D/UnrealSharp) — C# scripting for Unreal Engine
- [Mono Project](https://www.mono-project.com/) — Cross-platform .NET runtime

## 📄 License

MIT.
