// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty - Native library resolver for ghostty-renderer-capi.

using System.Reflection;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Rendering.Interop.Ghostty.Native;

/// <summary>
/// Handles cross-platform native library resolution for <c>ghostty-renderer-capi</c>.
/// </summary>
public static class GhosttyRendererNativeLibraryLoader
{
    private const string LibraryPathEnv = "GHOSTTY_RENDERER_CAPI_LIBRARY_PATH";
    private const string LibraryDirectoryEnv = "GHOSTTY_RENDERER_CAPI_LIBRARY_DIR";

    private static bool s_initialized;
    private static readonly object s_lock = new();

    /// <summary>
    /// Initializes native resolution for renderer interop. Safe to call multiple times.
    /// </summary>
    public static void Initialize()
    {
        if (s_initialized)
        {
            return;
        }

        lock (s_lock)
        {
            if (s_initialized)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(typeof(GhosttyRendererNative).Assembly, ResolveLibrary);
            s_initialized = true;
        }
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != GhosttyRendererNative.LibraryName)
        {
            return nint.Zero;
        }

        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out nint handle))
        {
            return handle;
        }

        foreach (string candidatePath in GetCandidatePaths(assembly))
        {
            if (NativeLibrary.TryLoad(candidatePath, out handle))
            {
                return handle;
            }
        }

        if (NativeLibrary.TryLoad(libraryName, out handle))
        {
            return handle;
        }

        return nint.Zero;
    }

    private static IReadOnlyList<string> GetCandidatePaths(Assembly assembly)
    {
        List<string> candidates = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string libraryFileName = GetLibraryFileName();
        string runtimeIdentifier = GetRuntimeIdentifier();
        string assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? string.Empty;

        AddPath(candidates, seen, Environment.GetEnvironmentVariable(LibraryPathEnv));

        string? configuredLibraryDirectory = Environment.GetEnvironmentVariable(LibraryDirectoryEnv);
        if (!string.IsNullOrWhiteSpace(configuredLibraryDirectory))
        {
            AddPath(candidates, seen, Path.Combine(configuredLibraryDirectory, libraryFileName));
        }

        AddPath(candidates, seen, Path.Combine(AppContext.BaseDirectory, "runtimes", runtimeIdentifier, "native", libraryFileName));
        AddPath(candidates, seen, Path.Combine(AppContext.BaseDirectory, libraryFileName));
        AddPath(candidates, seen, Path.Combine(assemblyDirectory, "runtimes", runtimeIdentifier, "native", libraryFileName));
        AddPath(candidates, seen, Path.Combine(assemblyDirectory, libraryFileName));

        return candidates;
    }

    private static void AddPath(ICollection<string> candidates, ISet<string> seen, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            // Ignore malformed environment-provided path candidates.
            return;
        }
        catch (NotSupportedException)
        {
            // Ignore malformed or unsupported path candidates and continue probing.
            return;
        }
        catch (PathTooLongException)
        {
            // Ignore malformed or unsupported path candidates and continue probing.
            return;
        }
        catch (System.Security.SecurityException)
        {
            // Ignore malformed or unsupported path candidates and continue probing.
            return;
        }

        if (seen.Add(fullPath))
        {
            candidates.Add(fullPath);
        }
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                _ => "win-x64",
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "osx-arm64",
                Architecture.X64 => "osx-x64",
                _ => "osx-arm64",
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => "linux-x64",
            };
        }

        return "unknown";
    }

    private static string GetLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "ghostty-renderer-capi.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "libghostty-renderer-capi.dylib";
        }

        return "libghostty-renderer-capi.so";
    }
}
