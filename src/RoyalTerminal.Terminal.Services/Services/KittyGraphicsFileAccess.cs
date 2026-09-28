// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RoyalTerminal.Terminal.Services;

internal static partial class KittyGraphicsFileAccess
{
    internal sealed class OpenedFile : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private readonly FileStatus _status;
        internal string Path { get; }
        internal bool IsRegular { get; }
        internal bool HasAllowedPath => OperatingSystem.IsWindows()
            ? KittyGraphicsPathPolicy.IsAllowedCanonicalWindowsPath(Path)
            : KittyGraphicsPathPolicy.IsAllowedCanonicalUnixPath(Path);

        internal OpenedFile(SafeFileHandle handle, string path, bool regular, FileStatus status)
        {
            _handle = handle;
            Path = path;
            IsRegular = regular;
            _status = status;
        }

        internal byte[] Read(uint offset, uint size, int? expectedBytes, int limit)
            => ReadRange(_handle, offset, size, expectedBytes, limit);

        internal unsafe void DeleteIfUnchanged()
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows disposes the exact opened object, not a path that could be replaced.
                byte delete = 1;
                _ = SetFileInformation(_handle, 4 /* FileDispositionInfo */, &delete, sizeof(byte));
                return;
            }

            // Best-effort hardening of Ghostty's path-based cleanup: skip an entry already
            // observed to have been replaced. POSIX has no portable unlink-by-open-handle;
            // a same-user actor can still race the final identity check and unlink.
            if (LStat(Path, out FileStatus current) == 0 && current.Device == _status.Device &&
                current.Inode == _status.Inode && (current.Mode & 0xf000) == 0x8000)
            {
                _ = Unlink(Path);
            }
        }

        public void Dispose() => _handle.Dispose();
    }

    internal static OpenedFile Open(string path, bool temporary, bool directory = false)
    {
        SafeFileHandle handle;
        if (OperatingSystem.IsWindows())
        {
            handle = CreateFile(path, 0x80000000u | (temporary ? 0x10000u : 0),
                7 /* share read/write/delete */, 0, 3 /* open existing */,
                directory ? 0x02000000u /* backup semantics */ : 0, 0);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // O_NONBLOCK prevents a terminal-controlled FIFO from hanging before fstat.
            int flags = OperatingSystem.IsMacOS() ? 0x01000004 : 0x00080800;
            handle = new SafeFileHandle(OpenUnix(path, flags), ownsHandle: true);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }

        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new IOException("Unable to open Kitty image object.");
        }

        try
        {
            FileStatus status = default;
            bool regular;
            if (OperatingSystem.IsWindows())
            {
                regular = GetFileType(handle) == 1 && (File.GetAttributes(handle) & FileAttributes.Directory) == 0;
            }
            else
            {
                if (FStat(handle, out status) != 0) throw new IOException("Unable to stat Kitty image object.");
                regular = (status.Mode & 0xf000) == 0x8000;
            }
            return new OpenedFile(handle, GetOpenedPath(handle), regular, status);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static byte[] ReadSharedMemory(string name, uint offset, uint size, int? expectedBytes, int limit)
    {
        int fd;
        try { fd = ShmOpen(name, 0); }
        catch (EntryPointNotFoundException) when (OperatingSystem.IsLinux()) { fd = ShmOpenRealtime(name, 0); }
        if (fd < 0) throw new IOException("Unable to open Kitty shared memory.");
        using SafeFileHandle handle = new(fd, ownsHandle: true);
        try
        {
            // Darwin shm descriptors are not seekable, so RandomAccess.GetLength rejects
            // them even though fstat reports their length correctly.
            if (FStat(handle, out FileStatus status) != 0) throw new IOException("Unable to stat Kitty shared memory.");
            long length = status.Size;
            if (length <= 0) throw new IOException("Empty Kitty shared memory.");
            if (!OperatingSystem.IsMacOS()) return ReadRange(handle, offset, size, expectedBytes, limit);

            // Darwin POSIX shared-memory descriptors support mmap, not pread. Map only
            // the validated range (plus page alignment), never an unbounded stat length.
            int count = GetRangeLength(length, offset, size, expectedBytes, limit);
            if (count == 0) return [];
            long alignedOffset = offset - offset % Environment.SystemPageSize;
            int pageOffset = checked((int)(offset - alignedOffset));
            nuint mappedLength = checked((nuint)((long)count + pageOffset));
            nint memory = Map(0, mappedLength, 1 /* read */, 1 /* shared */, handle, alignedOffset);
            if (memory == -1) throw new IOException("Unable to map Kitty shared memory.");
            try
            {
                byte[] bytes = GC.AllocateUninitializedArray<byte>(count);
                Marshal.Copy(memory + pageOffset, bytes, 0, count);
                return bytes;
            }
            finally { _ = Unmap(memory, mappedLength); }
        }
        finally
        {
            // Ghostty consumes the name after opening even when range validation fails.
            try { _ = ShmUnlink(name); }
            catch (EntryPointNotFoundException) when (OperatingSystem.IsLinux()) { _ = ShmUnlinkRealtime(name); }
        }
    }

    private static byte[] ReadRange(SafeFileHandle handle, uint offset, uint size, int? expectedBytes, int limit)
    {
        long length = RandomAccess.GetLength(handle);
        int count = GetRangeLength(length, offset, size, expectedBytes, limit);
        byte[] bytes = GC.AllocateUninitializedArray<byte>(count);
        int read = 0;
        while (read < count)
        {
            int current = RandomAccess.Read(handle, bytes.AsSpan(read), (long)offset + read);
            if (current == 0) throw new IOException("Truncated Kitty image object.");
            read += current;
        }
        if (size == 0 && expectedBytes is null)
        {
            Span<byte> extra = stackalloc byte[1];
            if (RandomAccess.Read(handle, extra, (long)offset + count) != 0)
                throw new IOException("Kitty image object changed during transmission.");
        }
        return bytes;
    }

    internal static int GetRangeLength(long objectLength, uint offset, uint size, int? expectedBytes, int limit)
    {
        if (objectLength < 0 || offset > objectLength || expectedBytes < 0 || limit < 0) throw new IOException("Invalid Kitty image range.");
        long available = objectLength - offset;
        long count = size > 0 ? size : expectedBytes.HasValue ? expectedBytes.Value : available;
        if (count > available || count > limit) throw new IOException("Kitty image range exceeds its bounds.");
        return checked((int)count);
    }

    private static unsafe string GetOpenedPath(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows())
        {
            char[] buffer = new char[32768];
            fixed (char* pointer = buffer)
            {
                uint length = GetFinalPath(handle, pointer, (uint)buffer.Length, 0);
                if (length == 0 || length >= buffer.Length) throw new IOException("Unable to resolve Kitty image path.");
                ReadOnlySpan<char> path = buffer.AsSpan(0, (int)length);
                if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) && path.Length >= 7 && path[5] == ':') path = path[4..];
                return path.ToString();
            }
        }
        if (OperatingSystem.IsLinux())
        {
            string path = File.ResolveLinkTarget($"/proc/self/fd/{handle.DangerousGetHandle()}", returnFinalTarget: false)?.FullName
                ?? throw new IOException("Unable to resolve Kitty image path.");
            if (path.EndsWith(" (deleted)", StringComparison.Ordinal)) throw new IOException("Kitty image file was removed.");
            return path;
        }
        Span<byte> bytes = stackalloc byte[1024];
        fixed (byte* buffer = bytes)
        {
            int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? FcntlMacArm64(handle, 50, 0, 0, 0, 0, 0, 0, buffer)
                : FcntlMac(handle, 50, buffer);
            if (result != 0) throw new IOException("Unable to resolve Kitty image path.");
        }
        int end = bytes.IndexOf((byte)0);
        if (end < 0) throw new IOException("Unterminated Kitty image path.");
        return new UTF8Encoding(false, true).GetString(bytes[..end]);
    }

    // Stable .NET 10 System.Native FileStatus ABI avoids platform-specific struct stat layouts.
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileStatus
    {
        public int Flags, Mode;
        public uint UserId, GroupId;
        public long Size, AccessTime, AccessTimeNanoseconds, ModifiedTime, ModifiedTimeNanoseconds;
        public long ChangeTime, ChangeTimeNanoseconds, BirthTime, BirthTimeNanoseconds, Device, RawDevice, Inode;
        public uint UserFlags;
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenUnix(string path, int flags);
    [LibraryImport("System.Native", EntryPoint = "SystemNative_FStat")]
    private static partial int FStat(SafeFileHandle handle, out FileStatus status);
    [LibraryImport("System.Native", EntryPoint = "SystemNative_LStat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStat(string path, out FileStatus status);
    [LibraryImport("System.Native", EntryPoint = "SystemNative_Unlink", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Unlink(string path);
    [LibraryImport("libSystem.dylib", EntryPoint = "fcntl")]
    private static unsafe partial int FcntlMac(SafeFileHandle handle, int command, byte* path);
    // Apple arm64 puts variadic arguments on the stack, after all eight integer registers.
    [LibraryImport("libSystem.dylib", EntryPoint = "fcntl")]
    private static unsafe partial int FcntlMacArm64(SafeFileHandle handle, int command, nint p2, nint p3, nint p4, nint p5, nint p6, nint p7, byte* path);
    [LibraryImport("libc", EntryPoint = "shm_open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int ShmOpen(string name, int flags);
    [LibraryImport("libc", EntryPoint = "shm_unlink", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int ShmUnlink(string name);
    [LibraryImport("librt.so.1", EntryPoint = "shm_open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int ShmOpenRealtime(string name, int flags);
    [LibraryImport("librt.so.1", EntryPoint = "shm_unlink", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int ShmUnlinkRealtime(string name);
    [LibraryImport("libc", EntryPoint = "mmap")]
    private static partial nint Map(nint address, nuint length, int protection, int flags, SafeFileHandle file, long offset);
    [LibraryImport("libc", EntryPoint = "munmap")]
    private static partial int Unmap(nint address, nuint length);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileType")]
    private static partial uint GetFileType(SafeFileHandle handle);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW")]
    private static unsafe partial uint GetFinalPath(SafeFileHandle handle, char* path, uint length, uint flags);
    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformation(SafeFileHandle handle, int informationClass, void* information, int size);
}
