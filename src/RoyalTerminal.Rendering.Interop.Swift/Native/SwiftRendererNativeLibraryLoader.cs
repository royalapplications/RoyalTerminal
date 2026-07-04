// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Rendering.Interop.Swift.Native;

/// <summary>
/// Native library resolver for swift_terminal_renderer.
/// </summary>
public static class SwiftRendererNativeLibraryLoader
{
    private const string LibraryPathEnv = "SWIFT_RENDERER_LIBRARY_PATH";
    private const string LibraryDirectoryEnv = "SWIFT_RENDERER_LIBRARY_DIR";

    private static bool s_initialized;
    private static readonly object s_lock = new();

    /// <summary>
    /// Initializes dynamic library import resolution. Safe to call multiple times.
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

            NativeLibrary.SetDllImportResolver(typeof(SwiftRendererNative).Assembly, ResolveLibrary);
            s_initialized = true;
        }
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != SwiftRendererNative.LibraryName)
        {
            return nint.Zero;
        }

        foreach (string candidatePath in GetCandidatePaths(assembly))
        {
            if (NativeLibrary.TryLoad(candidatePath, out nint handle))
            {
                return handle;
            }
        }

        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out nint assemblyResolvedHandle))
        {
            return assemblyResolvedHandle;
        }

        if (NativeLibrary.TryLoad(libraryName, out nint defaultHandle))
        {
            return defaultHandle;
        }

        return nint.Zero;
    }

    private static IReadOnlyList<string> GetCandidatePaths(Assembly assembly)
    {
        List<string> candidates = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string libraryFileName = "libswift_terminal_renderer.dylib";
        string assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? string.Empty;

        AddPath(candidates, seen, Environment.GetEnvironmentVariable(LibraryPathEnv));

        string? configuredLibraryDirectory = Environment.GetEnvironmentVariable(LibraryDirectoryEnv);
        if (!string.IsNullOrWhiteSpace(configuredLibraryDirectory))
        {
            AddPath(candidates, seen, Path.Combine(configuredLibraryDirectory, libraryFileName));
        }

        AddPath(candidates, seen, Path.Combine(AppContext.BaseDirectory, "runtimes", "osx-arm64", "native", libraryFileName));
        AddPath(candidates, seen, Path.Combine(AppContext.BaseDirectory, libraryFileName));
        AddPath(candidates, seen, Path.Combine(assemblyDirectory, "runtimes", "osx-arm64", "native", libraryFileName));
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
        catch
        {
            return;
        }

        if (seen.Add(fullPath))
        {
            candidates.Add(fullPath);
        }
    }
}
