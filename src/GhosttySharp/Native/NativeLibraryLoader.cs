// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Reflection;
using System.Runtime.InteropServices;

namespace GhosttySharp.Native;

/// <summary>
/// Handles cross-platform native library resolution for the Ghostty native library.
/// Registers a custom <see cref="NativeLibrary"/> resolver that searches platform-specific
/// runtime directories following the NuGet native package convention.
/// </summary>
public static class NativeLibraryLoader
{
    private static bool s_initialized;
    private static readonly object s_lock = new();

    /// <summary>
    /// Initializes the native library resolver. Safe to call multiple times.
    /// </summary>
    public static void Initialize()
    {
        if (s_initialized) return;

        lock (s_lock)
        {
            if (s_initialized) return;

            NativeLibrary.SetDllImportResolver(typeof(GhosttyNative).Assembly, ResolveLibrary);
            s_initialized = true;
        }
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != GhosttyNative.LibraryName)
            return nint.Zero;

        // Try standard resolution first
        if (NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var handle))
            return handle;

        // Try platform-specific paths
        var rid = GetRuntimeIdentifier();
        var libraryFileName = GetLibraryFileName();

        // Search paths in priority order
        string[] searchPaths =
        [
            // NuGet runtime package layout
            Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", libraryFileName),
            // Direct in base directory
            Path.Combine(AppContext.BaseDirectory, libraryFileName),
            // Relative to assembly
            Path.Combine(Path.GetDirectoryName(assembly.Location) ?? string.Empty, "runtimes", rid, "native", libraryFileName),
            Path.Combine(Path.GetDirectoryName(assembly.Location) ?? string.Empty, libraryFileName),
        ];

        foreach (var path in searchPaths)
        {
            if (NativeLibrary.TryLoad(path, out handle))
                return handle;
        }

        // Try without extension as fallback
        if (NativeLibrary.TryLoad(libraryName, out handle))
            return handle;

        return nint.Zero;
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                _ => "win-x64",
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "osx-arm64",
                Architecture.X64 => "osx-x64",
                _ => "osx-arm64",
            };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => "linux-x64",
            };

        return "unknown";
    }

    private static string GetLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "ghostty.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libghostty.dylib";
        return "libghostty.so";
    }
}
