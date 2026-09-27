// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.App.Services.Links;

internal sealed partial class NativeHyperlinkFileProbe : IHyperlinkFileProbe, IHyperlinkPathResolver
{
    public string ResolvePath(string path)
    {
        if (!HyperlinkFilePolicy.IsLocalPath(path, OperatingSystem.IsWindows())) throw new IOException("Not a local path.");
        if (OperatingSystem.IsWindows()) return WindowsHyperlinkFileProbe.ResolvePath(path);
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        return ResolveUnix(path) ?? CanonicalizeMissingPath(path, ResolveUnix);
    }

    public HyperlinkFileFacts Read(string path)
    {
        if (!HyperlinkFilePolicy.IsLocalPath(path, OperatingSystem.IsWindows())) throw new IOException("Not a local path.");
        if (OperatingSystem.IsWindows()) return WindowsHyperlinkFileProbe.Read(path);
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        string? canonical = ResolveUnix(path);
        if (canonical is null)
            return new(CanonicalizeMissingPath(path, ResolveUnix), HyperlinkFileKind.Other, false, false);
        return OperatingSystem.IsMacOS()
            ? MacOsHyperlinkFileProbe.Read(canonical)
            : LinuxHyperlinkFileProbe.Read(canonical);
    }

    // A missing leaf can still have a symlinked parent. Normalize the preview
    // through the longest accessible prefix, without ever opening the leaf.
    internal static string CanonicalizeMissingPath(string path, Func<string, string?> resolveExisting)
    {
        string full = Path.GetFullPath(path);
        string? parent = Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(parent))
        {
            string? canonical = resolveExisting(parent);
            if (canonical is not null)
                return Path.Combine(canonical, full.AsSpan(parent.Length).TrimStart(Path.DirectorySeparatorChar).ToString());
            parent = Path.GetDirectoryName(parent);
        }
        return full;
    }

    private static string? ResolveUnix(string path)
    {
        nint canonical = realpath(path, 0);
        if (canonical == 0) return null;
        try { return Marshal.PtrToStringUTF8(canonical); }
        finally { free(canonical); }
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] private static partial nint realpath(string path, nint buffer);
    [LibraryImport("libc")] private static partial void free(nint memory);
}
