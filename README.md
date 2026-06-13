# MonoSDK for UnrealSharp

This repository contains **build scripts only** for building Mono runtime SDK from [dotnet/runtime](https://github.com/dotnet/runtime) source.

**Pre-built SDKs for all platforms are available as GitHub Releases** - you don't need to build from source unless you want to customize the build.

---

## 📦 Download Pre-Built SDK

### Option 1: Download from GitHub Releases (Recommended)

1. Go to [Releases](https://github.com/RPG3D/MonoSDK/releases)
2. Download the latest `MonoSDK-v*.zip`
3. Extract to your UnrealSharp plugin's `Source/ThirdParty/MonoSDK/` directory

**ZIP contains**:
```
MonoSDK-v0.1.0-Debug.zip
├── Win64/          # Windows x64 SDK
├── Mac/            # macOS arm64 SDK
├── Android/        # Android arm64 SDK
├── IOS/           # iOS arm64 SDK
├── IOSSimulator/  # iOS Simulator arm64 SDK
├── Linux/          # Linux x64 SDK
├── include/        # Shared Mono headers
├── BuildMonoSDK.sh      # Build script (macOS/Linux)
├── BuildMonoSDK.bat      # Build script (Windows)
└── MakeMonoFramework.sh  # iOS framework packager
```

### Option 2: Build from Source

If you need to customize the build or use a different dotnet/runtime version:

#### Prerequisites

- **Windows**: Visual Studio 2022, CMake, Ninja
- **macOS**: Xcode, CMake, Ninja
- **Linux**: Clang, CMake, Ninja, libicu-dev, libssl-dev
- **Android**: Android SDK/NDK
- **iOS**: Xcode, macOS host

#### Build Commands

**macOS/Linux**:
```bash
# Build for current platform
./BuildMonoSDK.sh <dotnet-runtime-source-dir> [platform] [build-type]

# Examples:
./BuildMonoSDK.sh ~/dotnet-runtime              # macOS arm64, Debug
./BuildMonoSDK.sh ~/dotnet-runtime macos Release
./BuildMonoSDK.sh ~/dotnet-runtime android Debug
./BuildMonoSDK.sh ~/dotnet-runtime ios Debug
```

**Windows**:
```cmd
BuildMonoSDK.bat <dotnet-runtime-source-dir>

REM Example:
BuildMonoSDK.bat C:\dotnet-runtime
```

---

## 🔄 Automated Builds (GitHub Actions)

This repository uses GitHub Actions to automatically build MonoSDK for all platforms.

### Workflows

| Workflow | Platform | Runner | Trigger |
|----------|----------|--------|---------|
| `build-linux.yml` | Linux x64 | `ubuntu-latest` | Manual / Push |
| `build-macos.yml` | macOS arm64 | `macos-latest` | Manual |
| `build-windows.yml` | Windows x64 | `windows-2022` | Manual |
| `build-ios.yml` | iOS & iOS Simulator | `macos-latest` | Manual |
| `build-android.yml` | Android arm64 | `ubuntu-latest` | Manual |
| `package-release.yml` | All platforms (ZIP) | `ubuntu-latest` | Manual / Tag |

### How to Trigger Builds

1. Go to [Actions](https://github.com/RPG3D/MonoSDK/actions)
2. Select a workflow (e.g., "Build MonoSDK - Linux x64")
3. Click **"Run workflow"**
4. Select branch `ci/auto-build` and build type (`Debug` or `Release`)
5. Click **"Run workflow"**

### Package All Platforms

After all platforms are built, package them into a single ZIP:

1. Go to [Actions](https://github.com/RPG3D/MonoSDK/actions)
2. Select **"Package and Release MonoSDK"**
3. Click **"Run workflow"**
4. Enter version (e.g., `v0.1.0`) and build type
5. Click **"Run workflow"**

This will:
- Collect all platform SDKs from `ci/auto-build` branch
- Package into a single ZIP
- Create a GitHub Release (optional)

---

## 🏗️ Repository Structure

```
MonoSDK/
├── .github/workflows/    # GitHub Actions workflows
│   ├── build-linux.yml
│   ├── build-macos.yml
│   ├── build-windows.yml
│   ├── build-ios.yml
│   ├── build-android.yml
│   ├── package-release.yml
│   └── test-trigger.yml
├── BuildMonoSDK.sh       # Build script (macOS/Linux)
├── BuildMonoSDK.bat       # Build script (Windows)
├── MakeMonoFramework.sh   # iOS framework packager
├── MonoSDK.Build.cs      # Unreal Engine build integration
├── MonoSDK_APL.xml       # Android packaging config
├── .gitignore            # Prevents SDK binaries from being committed
└── README.md             # This file
```

**Note**: This repository does **NOT** contain pre-built SDK binaries.
They are distributed via [GitHub Releases](https://github.com/RPG3D/MonoSDK/releases).

---

## 🔧 Building from Source

### Step 1: Clone dotnet/runtime

```bash
git clone --depth 1 --branch release/10.0 https://github.com/dotnet/runtime.git
```

### Step 2: Run Build Script

**macOS example** (build for macOS):
```bash
./BuildMonoSDK.sh ~/dotnet-runtime macos Debug
```

**Windows example**:
```cmd
BuildMonoSDK.bat C:\dotnet-runtime
```

### Step 3: Use the SDK

After building, the SDK will be in the corresponding platform directory:
- `Mac/` - macOS SDK
- `Win64/` - Windows SDK
- `Android/` - Android SDK
- etc.

Copy the platform directory to your UnrealSharp plugin's `Source/ThirdParty/MonoSDK/`.

---

## 📋 Version Information

Each platform directory contains a `VERSION.txt` file with:
- dotnet/runtime repository URL
- Branch and commit hash
- Platform and build type
- Build command used

Example:
```
dotnet/runtime source
  repo:   https://github.com/dotnet/runtime.git
  branch: release/10.0
  commit: a1b2c3d

Platform: macOS (arm64)
Build type: Debug
Build command: ./build.sh mono+libs -configuration Debug
```

---

## 🛠️ Customization

### Use a Different dotnet/runtime Version

Edit the `Clone dotnet/runtime` step in the workflow file:

```yaml
- name: Clone dotnet/runtime
  run: |
    git clone --depth 1 --branch <your-branch> https://github.com/dotnet/runtime.git dotnet-runtime
```

### Change Build Configuration

Pass `Release` instead of `Debug`:

```bash
./BuildMonoSDK.sh ~/dotnet-runtime macos Release
```

---

## 🐛 Troubleshooting

### Build Fails with "No space left on device"

GitHub Actions runners have limited disk space (~70GB).
The `dotnet/runtime` source + build artifacts can exceed this.

**Solution**: Use the `package-release.yml` workflow to package SDKs without storing them in the repo.

### iOS Build Fails

Ensure Xcode is properly installed and selected:

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
```

### Android Build Fails (NDK Not Found)

Install Android NDK manually:

```yaml
- name: Install Android NDK
  run: |
    $ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager "ndk;25.1.8937393"
```

---

## 📚 References

- [dotnet/runtime](https://github.com/dotnet/runtime) - .NET runtime source
- [UnrealSharp](https://github.com/UnrealSharp/UnrealSharp) - C# scripting for Unreal Engine
- [Mono Project](https://www.mono-project.com/) - Cross-platform .NET runtime

---

## 📄 License

This repository is licensed under the MIT License (same as UnrealSharp).

---

**Maintained by**: [RPG3D](https://github.com/RPG3D)
**Issues**: [GitHub Issues](https://github.com/RPG3D/MonoSDK/issues)
