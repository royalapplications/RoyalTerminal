// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Services;

// Port of Ghostty terminal/kitty/windows.zig and graphics_image.zig path checks.
internal static class KittyGraphicsPathPolicy
{
    internal static bool IsAllowedWindowsPath(ReadOnlySpan<char> path)
    {
        if (path.Length >= 2 && IsWindowsSeparator(path[0]) && IsWindowsSeparator(path[1])) return false;
        if (path.Length >= 4 && IsWindowsSeparator(path[0]) && path[1] == '?' && path[2] == '?' && IsWindowsSeparator(path[3])) return false;
        foreach (Range range in path.SplitAny("\\/"))
        {
            ReadOnlySpan<char> component = path[range];
            int end = component.IndexOfAny('.', ':');
            ReadOnlySpan<char> stem = (end >= 0 ? component[..end] : component).TrimEnd(' ');
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return false;
            if (stem.Length == 4 && (stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) || stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase)) &&
                (char.IsAsciiDigit(stem[3]) || stem[3] is '\u00b9' or '\u00b2' or '\u00b3')) return false;
        }
        return true;
    }

    internal static bool IsAllowedCanonicalWindowsPath(ReadOnlySpan<char> path)
        => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' && IsAllowedWindowsPath(path);

    internal static bool IsAllowedCanonicalUnixPath(string path)
        => path.StartsWith('/') && !IsWithinDirectory("/proc", path, false) && !IsWithinDirectory("/sys", path, false) &&
            (!IsWithinDirectory("/dev", path, false) || IsWithinDirectory("/dev/shm", path, false));

    internal static bool IsWithinDirectory(string directory, string path, bool windows)
    {
        if (directory.Length == 0) return false;
        StringComparison comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(directory, comparison)) return false;
        return directory.Length == path.Length || IsSeparator(directory[^1], windows) || IsSeparator(path[directory.Length], windows);
    }

    internal static bool IsSharedMemoryName(ReadOnlySpan<byte> name)
        => name.Length is >= 2 and <= 255 && name[0] == '/' && name[1..].IndexOfAny((byte)'/', (byte)0) < 0;

    private static bool IsWindowsSeparator(char character) => character is '\\' or '/';
    private static bool IsSeparator(char character, bool windows) => character == '/' || (windows && character == '\\');
}
