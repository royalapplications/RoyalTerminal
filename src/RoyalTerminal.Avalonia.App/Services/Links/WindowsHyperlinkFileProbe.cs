// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RoyalTerminal.Avalonia.App.Services.Links;

internal static partial class WindowsHyperlinkFileProbe
{
    internal static HyperlinkFileFacts Read(string path)
    {
        using SafeFileHandle handle = Open(path);
        if (handle.IsInvalid)
            return new(NativeHyperlinkFileProbe.CanonicalizeMissingPath(path, ResolveExisting), HyperlinkFileKind.Other, false, false);
        string canonical = CanonicalPath(handle);
        // A junction can resolve to a remote/device path despite a local input.
        if (!HyperlinkFilePolicy.IsLocalPath(canonical, windows: true)) return new(canonical, HyperlinkFileKind.Other, false, false);
        if (GetFileType(handle) != 1) return new(canonical, HyperlinkFileKind.Other, false, false);
        FileAttributes attributes = File.GetAttributes(handle);
        if ((attributes & FileAttributes.Device) != 0) return new(canonical, HyperlinkFileKind.Other, false, false);
        bool directory = (attributes & FileAttributes.Directory) != 0;
        bool executable = false;
        if (!directory)
        {
            executable = GetBinaryType("\\\\?\\" + canonical, out _) != 0;
            if (!executable && Marshal.GetLastPInvokeError() is not (0 or 193 /* ERROR_BAD_EXE_FORMAT */))
                throw new IOException("Unable to determine local binary type.");
        }
        return new(canonical, directory ? HyperlinkFileKind.Directory : HyperlinkFileKind.Regular, executable, false);
    }

    private static SafeFileHandle Open(string path) => CreateFile("\\\\?\\" + path, 0x80 /* FILE_READ_ATTRIBUTES */,
        7 /* share read/write/delete */, 0, 3 /* OPEN_EXISTING */, 0x02000000 /* BACKUP_SEMANTICS */, 0);

    private static string? ResolveExisting(string path)
    {
        using SafeFileHandle handle = Open(path);
        return handle.IsInvalid ? null : CanonicalPath(handle);
    }

    private static unsafe string CanonicalPath(SafeFileHandle handle)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent(32768);
        try
        {
            fixed (char* pointer = buffer)
            {
                uint length = GetFinalPathNameByHandle(handle, pointer, 32768, 0);
                if (length is 0 or >= 32768) throw new IOException("Unable to resolve local path.");
                ReadOnlySpan<char> path = buffer.AsSpan(0, (int)length);
                if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) && path.Length >= 7 && path[5] == ':') path = path[4..];
                return path.ToString();
            }
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll")] private static partial uint GetFileType(SafeFileHandle handle);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW")]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* path, uint count, uint flags);
    [LibraryImport("kernel32.dll", EntryPoint = "GetBinaryTypeW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetBinaryType(string path, out uint type);
}
