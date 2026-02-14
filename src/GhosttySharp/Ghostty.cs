// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Runtime.InteropServices;
using System.Text;
using GhosttySharp.Native;

namespace GhosttySharp;

/// <summary>
/// Static utility class for Ghostty library initialization and global operations.
/// </summary>
public static class Ghostty
{
    private static bool s_initialized;
    private static readonly object s_lock = new();

    /// <summary>
    /// Initializes the Ghostty library. Must be called before any other Ghostty operations.
    /// Safe to call multiple times.
    /// </summary>
    /// <returns>True if initialization succeeded; false when the native library is unavailable or incompatible.</returns>
    public static unsafe bool Initialize()
    {
        if (s_initialized) return true;

        lock (s_lock)
        {
            if (s_initialized) return true;

            try
            {
                NativeLibraryLoader.Initialize();
                var result = GhosttyNative.Init(0, null);
                s_initialized = result == 0;
                return s_initialized;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
        }
    }

    /// <summary>Gets build and version information from the Ghostty library.</summary>
    public static unsafe GhosttyLibraryInfo GetInfo()
    {
        NativeLibraryLoader.Initialize();
        var info = GhosttyNative.Info();
        var version = info.Version != null && info.VersionLen > 0
            ? Encoding.UTF8.GetString(new ReadOnlySpan<byte>(info.Version, (int)info.VersionLen))
            : "unknown";

        return new GhosttyLibraryInfo(info.BuildMode, version);
    }

    /// <summary>Translates a string using Ghostty's i18n system.</summary>
    public static unsafe string? Translate(string text)
    {
        NativeLibraryLoader.Initialize();
        var bytes = Encoding.UTF8.GetBytes(text + '\0');
        fixed (byte* ptr = bytes)
        {
            var result = GhosttyNative.Translate(ptr);
            return result != null ? Marshal.PtrToStringUTF8((nint)result) : null;
        }
    }

    /// <summary>Tries the CLI action parsing. Used for command-line tool integration.</summary>
    public static void CliTryAction()
    {
        NativeLibraryLoader.Initialize();
        GhosttyNative.CliTryAction();
    }
}

/// <summary>Library build and version information.</summary>
/// <param name="BuildMode">The build mode of the library.</param>
/// <param name="Version">The version string.</param>
public readonly record struct GhosttyLibraryInfo(GhosttyBuildMode BuildMode, string Version);
