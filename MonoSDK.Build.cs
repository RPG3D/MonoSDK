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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using EpicGames.Core;
using Microsoft.Extensions.Logging;
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
		bool bUseMono = false;
		ConfigHierarchy EngineIni = ConfigCache.ReadHierarchy(
			ConfigHierarchyType.Engine,
			DirectoryReference.FromFile(Target.ProjectFile),
			Target.Platform);
		EngineIni.GetBool("UnrealSharp", "bUseMono", out bUseMono);

		if (bUseMono && Target.Platform == UnrealTargetPlatform.Mac)
		{
			PublicDefinitions.Add("UNREALSHARP_MONO=1");

			string monoLib = Path.Combine(monoSdkRoot, "Mac", "lib");
			string monoRuntime = Path.Combine(monoSdkRoot, "Mac", "runtime");

			PublicIncludePaths.Add(Path.Combine(monoSdkRoot, "include"));

			// Link libcoreclr.dylib (Mono runtime; named "coreclr" per Microsoft unified naming)
			string monoLibPath = Path.Combine(monoLib, "libcoreclr.dylib");
			PublicAdditionalLibraries.Add(monoLibPath);
			RuntimeDependencies.Add(monoLibPath);

			// Native interop dylibs — BCL DllImport targets, must be staged next to libcoreclr.dylib
			string[] nativeDylibs =
			{
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

			// Pre-sign the dylibs inside Mono.embeddedframework.zip so the packaged IPA
			// passes on-device code-sign validation. UE signs the outer app bundle but
			// does NOT recurse into the nested Mono.framework/Frameworks/ dylibs.
			// macOS host only (native iOS UBT); a no-op on Windows or for the Simulator.
			// See SignEmbeddedFrameworkZip() for the hash-cache skip scheme.
			if (!bIsSimulator)
			{
				SignEmbeddedFrameworkZip(monoLib);
			}

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
			string importLib = Path.Combine(monoLib, "coreclr.import.lib");
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

	// ── Embedded framework pre-signing (real iOS device, macOS host only) ──────
	//
	// UE stages Mono.embeddedframework.zip into the IPA and signs the outer app, but
	// does NOT recurse into the nested Mono.framework/Frameworks/ dylibs — pre-sign
	// them here so on-device validation passes. macOS host only (native iOS UBT).
	//
	// Deterministic skip via a cache (.framework_sign_cache.txt next to the zip):
	// one line per signed zip, "<sha256> 1". The stored hash is of the SIGNED zip
	// on disk, so the next UBT run hashes the same file and skips. A fresh SDK
	// download changes the hash and re-triggers signing. Cache capped at 64 lines
	// (FIFO drop from the head). Any failure logs and continues — never breaks the build.
	private void SignEmbeddedFrameworkZip(string monoLibDir)
	{
		if (!OperatingSystem.IsMacOS())
			return;

		string zipPath = Path.Combine(monoLibDir, "Mono.embeddedframework.zip");
		if (!File.Exists(zipPath))
			return;

		string cachePath = Path.Combine(monoLibDir, ".framework_sign_cache.txt");
		try
		{
			if (IsInSignCache(cachePath, ComputeSha256(zipPath)))
			{
				Logger.LogInformation("MonoSDK: Mono.embeddedframework.zip already signed; skipping.");
				return;
			}

			string identity = FindCodeSigningIdentity();
			if (identity == null)
			{
				Logger.LogWarning("MonoSDK: no code-signing identity found; framework signing skipped.");
				return;
			}

			// Unzip -> codesign every Mach-O -> repack with native zip.
			// Native zip keeps unix file modes (the Mono stub must stay executable)
			// and symlinks (-y); .NET's ZipArchive would flatten both.
			string workDir = Path.Combine(Path.GetTempPath(), "MonoSDKSign_" + Path.GetRandomFileName());
			string extractDir = Path.Combine(workDir, "extract");
			Directory.CreateDirectory(extractDir);
			File.Copy(zipPath, zipPath + ".bak", overwrite: true); // pristine original
			try
			{
				RunProcessCapture("/usr/bin/unzip", new[] { "-q", zipPath, "-d", extractDir }, checkExit: true);

				// Every Mach-O that needs a signature: the 5 nested interop dylibs
				// plus the stub Mono main executable.
				string fwRoot = Path.Combine(extractDir, "Mono.embeddedframework", "Mono.framework");
				foreach (string dylib in Directory.EnumerateFiles(Path.Combine(fwRoot, "Frameworks"), "*.dylib",
					SearchOption.TopDirectoryOnly))
				{
					RunProcessCapture("/usr/bin/codesign",
						new[] { "-f", "-s", identity, "--timestamp=none", dylib }, checkExit: true);
				}

				string monoStub = Path.Combine(fwRoot, "Mono");
				if (File.Exists(monoStub))
				{
					RunProcessCapture("/usr/bin/codesign",
						new[] { "-f", "-s", identity, "--timestamp=none", monoStub }, checkExit: true);
				}

				// Repack with the same top-level structure (Mono.embeddedframework/...).
				string newZip = Path.Combine(workDir, "Mono.embeddedframework.zip");
				RunProcessCapture("/usr/bin/zip", new[] { "-X", "-y", "-q", "-r", newZip, "Mono.embeddedframework" },
					extractDir, checkExit: true);
				File.Copy(newZip, zipPath, overwrite: true);
			}
			finally
			{
				Directory.Delete(workDir, true);
			}

			// Cache the hash of the signed zip now on disk so future UBT runs skip.
			AppendToSignCache(cachePath, ComputeSha256(zipPath));
			Logger.LogInformation("MonoSDK: Mono.embeddedframework.zip signed; pristine original kept at {0}.bak.",
				zipPath);
		}
		catch (Exception ex)
		{
			Logger.LogWarning("MonoSDK: framework signing skipped due to error: {0}", ex.Message);
		}
	}

	/// <summary>
	/// SHA-1 of the first valid code-signing identity from the keychain
	/// ("security find-identity -v -p codesigning"), or null when none exists.
	/// Returns the 40-hex SHA-1, not the display name: the hash uniquely pins one
	/// certificate instance, so codesign never resolves to a revoked or duplicate-name
	/// cert. Lines with a CSSMERR_ marker are expired/revoked certs and are skipped.
	/// Works for both manual ("iPhone Developer: ...") and automatic
	/// ("Apple Development: ...") signing setups.
	/// </summary>
	private string FindCodeSigningIdentity()
	{
		(_, string stdout) =
			RunProcessCapture("/usr/bin/security", new[] { "find-identity", "-v", "-p", "codesigning" });
		foreach (string line in stdout.Split('\n'))
		{
			// Skip expired/revoked certs (CSSMERR_TP_CERT_* marker); accept only a
			// well-formed 40-hex SHA-1 so a non-hash token is never used as identity.
			if (line.Contains("CSSMERR_", StringComparison.Ordinal))
				continue;
			string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 2 && parts[1].Length == 40 && parts[1].All(Uri.IsHexDigit))
				return parts[1];
		}

		return null;
	}

	private string ComputeSha256(string path)
	{
		return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
	}

	private bool IsInSignCache(string cachePath, string hash)
	{
		return File.Exists(cachePath)
			&& File.ReadAllLines(cachePath).Any(line => line.StartsWith(hash, StringComparison.OrdinalIgnoreCase));
	}

	private void AppendToSignCache(string cachePath, string hash)
	{
		const int MaxCacheLines = 64;
		List<string> lines = File.Exists(cachePath)
			? File.ReadAllLines(cachePath).ToList()
			: new List<string>();
		lines.Add(hash + " 1");
		if (lines.Count > MaxCacheLines)
			lines.RemoveRange(0, lines.Count - MaxCacheLines);

		File.WriteAllLines(cachePath, lines);
	}

	private (int ExitCode, string StdOut) RunProcessCapture(string exe, string[] args, string workingDir = null,
		bool checkExit = false)
	{
		using Process process = new();
		process.StartInfo.FileName = exe;
		process.StartInfo.UseShellExecute = false;
		process.StartInfo.CreateNoWindow = true;
		process.StartInfo.RedirectStandardOutput = true;
		process.StartInfo.RedirectStandardError = true;
		process.StartInfo.WorkingDirectory = workingDir ?? Path.GetTempPath();
		foreach (string arg in args)
			process.StartInfo.ArgumentList.Add(arg);

		process.Start();
		string stdout = process.StandardOutput.ReadToEnd();
		process.WaitForExit();
		string stderr = process.StandardError.ReadToEnd();
		if (checkExit && process.ExitCode != 0)
			throw new InvalidOperationException(
				$"{exe} failed (exit {process.ExitCode}): {(stderr + stdout).Trim()}");

		return (process.ExitCode, stdout);
	}
}
