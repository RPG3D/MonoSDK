// MonoSDK.Build.cs
// UBT External-module wrapper for the Mono runtime SDK.
//
// Responsibilities:
//   - Reads bUseMono from DefaultEngine.ini [UnrealSharp]
//   - Defines UNREALSHARP_MONO=1/0 (propagated via Public dependency chain)
//   - Links all Mono native libraries / frameworks per platform
//   - Exposes MonoSDK headers via PublicIncludePaths
//
// Consumers:
//   - UnrealSharpCore       PublicDependencyModuleNames  — links Mono + macro propagates to all
//                           downstream modules that include UnrealSharpCore public headers
//   - UnrealSharpProcHelper PrivateDependencyModuleNames — needs the macro in its own .cpp files;
//                           cannot inherit it from UnrealSharpCore (dep direction is reversed:
//                           Core depends on ProcHelper, not the other way around)
//
// SDK files and this Build.cs all live together in Source/ThirdParty/MonoSDK/,
// which is the standard UE5 plugin ThirdParty layout (UBT only scans Source/).
// ModuleDirectory == the SDK root, so no path indirection is needed.
//
// SDK directory layout (Source/ThirdParty/MonoSDK/):
//   include/               shared Mono headers (all platforms except Win64)
//   Mac/           lib/  runtime/
//   Android/       lib/  runtime/
//   IOS/           lib/  runtime/
//   IOSSimulator/  lib/  runtime/
//   Win64/         include/  lib/  runtime/  bin/  PDB/
//   BuildMonoSDK.sh / MakeMonoFramework.sh   (build scripts)
//
// To add a new platform: add an else-if branch following the existing pattern.

using System.IO;
using EpicGames.Core;
using UnrealBuildTool;

public class MonoSDK : ModuleRules
{
    public MonoSDK(ReadOnlyTargetRules Target) : base(Target)
    {
        Type = ModuleType.External;

        // ModuleDirectory IS the SDK root (Source/ThirdParty/MonoSDK/).
        // Build.cs and all SDK artifacts live in the same directory.
        string monoSdkRoot = ModuleDirectory;

        // ── Read ini switches from DefaultEngine.ini [UnrealSharp] ──────────────
        bool bUseMono    = false;
        ConfigHierarchy EngineIni = ConfigCache.ReadHierarchy(
            ConfigHierarchyType.Engine,
            DirectoryReference.FromFile(Target.ProjectFile),
            Target.Platform);
        EngineIni.GetBool("UnrealSharp", "bUseMono", out bUseMono);

        if (bUseMono && Target.Platform == UnrealTargetPlatform.Mac)
        {
            PublicDefinitions.Add("UNREALSHARP_MONO=1");

            string monoLib     = Path.Combine(monoSdkRoot, "Mac", "lib");
            string monoRuntime = Path.Combine(monoSdkRoot, "Mac", "runtime");

            PublicIncludePaths.Add(Path.Combine(monoSdkRoot, "include"));

            // Link libcoreclr.dylib (Mono runtime; named "coreclr" per Microsoft unified naming)
            string monoLibPath = Path.Combine(monoLib, "libcoreclr.dylib");
            PublicAdditionalLibraries.Add(monoLibPath);
            RuntimeDependencies.Add(monoLibPath);

            // Native interop dylibs — BCL DllImport targets, must be staged next to libcoreclr.dylib
            string[] nativeDylibs = {
                "libSystem.Native.dylib",
                "libSystem.Globalization.Native.dylib",
                "libSystem.IO.Compression.Native.dylib",
                "libSystem.IO.Ports.Native.dylib",
                "libSystem.Net.Security.Native.dylib",
                "libSystem.Security.Cryptography.Native.Apple.dylib",
            };
            foreach (string name in nativeDylibs)
            {
                string path = Path.Combine(monoRuntime, name);
                if (File.Exists(path))
                    RuntimeDependencies.Add(path);
            }

            // BCL managed DLLs — staged as NonUFS (outside PAK, alongside the executable).
            // BCL is tied to the Mono runtime version and should not be hot-updated via PAK.
            // Mono's assembly preload hook searches Saved/ override dir first, then these BCL DLLs.
            if (Directory.Exists(monoRuntime))
            {
                RuntimeDependencies.Add(Path.Combine(monoRuntime, "...*.dll"), StagedFileType.NonUFS);
            }
        }
        else if (bUseMono && Target.Platform == UnrealTargetPlatform.Android)
        {
            PublicDefinitions.Add("UNREALSHARP_MONO=1");

            string monoLib = Path.Combine(monoSdkRoot, "Android", "lib");
            string monoRuntime = Path.Combine(monoSdkRoot, "Android", "runtime");

            PublicIncludePaths.Add(Path.Combine(monoSdkRoot, "include"));

            // Link libmonosgen-2.0.so at compile time (linker -l flag).
            // All .so files are packaged into APK lib/arm64-v8a/ via MonoSDK_APL.xml.
            PublicAdditionalLibraries.Add(Path.Combine(monoLib, "libmonosgen-2.0.so"));

            // APL XML lives alongside this Build.cs in Source/ThirdParty/MonoSDK/.
            string aplPath = Path.Combine(ModuleDirectory, "MonoSDK_APL.xml");
            AdditionalPropertiesForReceipt.Add("AndroidPlugin", aplPath);

            // BCL managed DLLs — staged as NonUFS (outside PAK).
            if (Directory.Exists(monoRuntime))
            {
                RuntimeDependencies.Add(Path.Combine(monoRuntime, "...*.dll"), StagedFileType.NonUFS);
            }
        }
        else if (bUseMono && Target.Platform == UnrealTargetPlatform.IOS)
        {
            // ── iOS / iOSSimulator (arm64) ──────────────────────────────────────────────
            //
            // UBT uses Target.Platform == IOS for BOTH real device and Simulator.
            // The architecture distinguishes them:
            //   UnrealArch.Arm64         → real iOS device   (MonoSDK/IOS/)
            //   UnrealArch.IOSSimulator  → iOS Simulator     (MonoSDK/IOSSimulator/)
            //
            // IOS (platform 2, arm64) and IOSSimulator (platform 7, arm64) use separate SDK dirs.
            // The two sets of static libs are ABI-incompatible and MUST NOT be mixed.
            //
            // iOS forbids JIT (W^X); Mono runs in INTERP+AOT mode:
            //   - System.Private.CoreLib.dll.a : pre-AOT CoreLib (mono-aot-cross output)
            //   - libmonosgen-2.0.a            : Mono runtime (static)
            //   - stub component variants      : minimal IPA footprint
            //   - libSystem.Globalization.Native.a : Globalization stubs (INVARIANT mode)
            //   - Mono.embeddedframework.zip   : native interop dylibs → IPA Frameworks/

            PublicDefinitions.Add("UNREALSHARP_MONO=1");

            // Select the MonoSDK sub-directory based on architecture.
            // Default to IOSSimulator until real-device libs are built via BuildMonoSDK.sh ios.
            bool bIsSimulator = (Target.Architecture == UnrealArch.IOSSimulator);
            string platformDir = bIsSimulator ? "IOSSimulator" : "IOS";
            string monoLib = Path.Combine(monoSdkRoot, platformDir, "lib");

            PublicIncludePaths.Add(Path.Combine(monoSdkRoot, "include"));

            // CoreLib AOT static lib (REQUIRED: prevents W^X violation at runtime bootstrap)
            PublicAdditionalLibraries.Add(Path.Combine(monoLib, "System.Private.CoreLib.dll.a"));

            // Mono runtime (static)
            PublicAdditionalLibraries.Add(Path.Combine(monoLib, "libmonosgen-2.0.a"));

            // Mono components — stub variants keep IPA size small.
            // Only marshal-ilgen uses the full variant (IL code gen required for BCL interop).
            PublicAdditionalLibraries.Add(Path.Combine(monoLib, "libmono-component-debugger-stub-static.a"));
            PublicAdditionalLibraries.Add(Path.Combine(monoLib, "libmono-component-diagnostics_tracing-stub-static.a"));
            PublicAdditionalLibraries.Add(Path.Combine(monoLib, "libmono-component-hot_reload-stub-static.a"));
            PublicAdditionalLibraries.Add(Path.Combine(monoLib, "libmono-component-marshal-ilgen-static.a"));

            // Globalization stubs (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 is set at runtime).
            //
            // Link the STATIC variant (.a) via absolute path to avoid a dyld path mismatch
            // at runtime.  Mono.embeddedframework.zip places the dylib inside
            // Mono.framework/Frameworks/, but the main binary's @rpath only resolves to
            // the IPA root Frameworks/ dir.  Using PublicSystemLibraryPaths+PublicSystemLibraries
            // caused the Apple linker to prefer the .dylib over the .a when both are present in
            // monoLib, producing @rpath/libSystem.Globalization.Native.dylib in the binary which
            // dyld cannot resolve at runtime (path: Frameworks/libSystem.Globalization.Native.dylib
            // does not exist — the dylib is inside Mono.framework/Frameworks/).
            // Using the absolute .a path forces static linkage, eliminating the dyld dependency.
            // This matches UnrealCSharp's Mono.Build.cs approach for iOS.
            PublicAdditionalLibraries.Add(Path.Combine(monoLib, "libSystem.Globalization.Native.a"));

            // Native interop dylibs packaged as embedded framework into IPA Frameworks/.
            // FrameworkMode.Copy (not Link): Mono.embeddedframework.zip is an umbrella framework
            // whose top-level binary is intentionally empty — the runtime is already statically
            // linked via libmonosgen-2.0.a. We only need the sub-Frameworks copied into IPA.
            PublicAdditionalFrameworks.Add(new Framework(
                "Mono",
                Path.Combine(monoLib, "Mono.embeddedframework.zip"),
                Framework.FrameworkMode.Copy,
                null));

            // BCL managed DLLs — staged as NonUFS (outside PAK).
            // iOS uses static linking for native libs, but managed DLLs are loaded at runtime.
            string monoRuntime = Path.Combine(monoSdkRoot, platformDir, "runtime");
            if (Directory.Exists(monoRuntime))
            {
                RuntimeDependencies.Add(Path.Combine(monoRuntime, "...*.dll"), StagedFileType.NonUFS);
            }
        }
        else if (bUseMono && Target.Platform == UnrealTargetPlatform.Win64)
        {
            PublicDefinitions.Add("UNREALSHARP_MONO=1");

            string monoLib = Path.Combine(monoSdkRoot, "Win64", "lib");
            string monoRuntime = Path.Combine(monoSdkRoot, "Win64", "runtime");

            // Win64 has its own include/ (superset: adds jit.h variants not in shared include/)
            PublicIncludePaths.Add(Path.Combine(monoSdkRoot, "Win64", "include"));

            // Dynamic link: import lib at compile time; coreclr.dll copied to Binaries/Win64/.
            // Two-argument RuntimeDependencies.Add(dest, src) triggers the build-time copy.
            string importLib  = Path.Combine(monoLib, "coreclr.import.lib");
            string coreclrDll = Path.Combine(monoLib, "coreclr.dll");
            PublicAdditionalLibraries.Add(importLib);
            RuntimeDependencies.Add("$(BinaryOutputDir)/coreclr.dll", coreclrDll);

            // BCL managed DLLs — staged as NonUFS (outside PAK).
            if (Directory.Exists(monoRuntime))
            {
                RuntimeDependencies.Add(Path.Combine(monoRuntime, "...*.dll"), StagedFileType.NonUFS);
            }
        }
        else
        {
            // bUseMono=false (CoreCLR/hostfxr path) or unsupported platform.
            // Explicitly define UNREALSHARP_MONO=0 so #if guards compile cleanly
            // under -Werror,-Wundef even when Mono is not selected.
            PublicDefinitions.Add("UNREALSHARP_MONO=0");
        }

        // ── Project managed DLLs staging (all Mono platforms) ──────────────────
        // Register Content/Managed/{Platform}/ as UFS so project DLLs get cooked into PAK.
        // Match only the CURRENT platform's subdirectory — avoids staging Win64 DLLs into Android APK.
        // BCL is handled per-platform above as NonUFS (PAK 外).
        // This is in MonoSDK.Build.cs so game projects don't need any Build.cs changes.
        if (bUseMono && Target.ProjectFile != null)
        {
            string projectDir = Path.GetDirectoryName(Target.ProjectFile.FullName)!;
            string managedPlatformDir = Target.Platform.ToString();
            // IOS uses the same UnrealTargetPlatform for device and simulator;
            // distinguish via architecture so simulator builds pick up IOSSimulator/.
            if (Target.Platform == UnrealTargetPlatform.IOS && Target.Architecture == UnrealArch.IOSSimulator)
                managedPlatformDir = "IOSSimulator";
            string managedContentDir = Path.Combine(projectDir, "Content", "Managed", managedPlatformDir);
            if (Directory.Exists(managedContentDir))
            {
                RuntimeDependencies.Add(Path.Combine(managedContentDir, "*.dll"), StagedFileType.UFS);
                RuntimeDependencies.Add(Path.Combine(managedContentDir, "*.pdb"), StagedFileType.UFS);
                RuntimeDependencies.Add(Path.Combine(managedContentDir, "*.json"), StagedFileType.UFS);
            }
        }
    }
}
