// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class KittyGraphicsMediumReaderTests
{
    [Theory]
    [InlineData(KittyGraphicsMedium.File)]
    [InlineData(KittyGraphicsMedium.TemporaryFile)]
    [InlineData(KittyGraphicsMedium.SharedMemory)]
    public void DefaultPolicyRejectsEveryHostMedium(KittyGraphicsMedium medium)
    {
        Assert.False(new LocalKittyGraphicsMediumReader().TryRead(new(medium, "/missing"u8.ToArray(), 0, 0, null), 64, out _, out string? error));
        Assert.Equal("EINVAL: unsupported medium", error);
    }

    [Fact]
    public void RegularFileReadsExactOffsetRangeWithoutDeletingSenderFile()
    {
        using Files files = new();
        string path = files.Create("image.data", [1, 2, 3, 4, 5]);
        Assert.True(files.Reader.TryRead(Request(path, offset: 1, size: 3), 3, out byte[]? data, out string? error));
        Assert.Null(error);
        Assert.Equal(new byte[] { 2, 3, 4 }, data);
        Assert.True(File.Exists(path));
        Assert.True(files.Reader.TryRead(Request(path, offset: 2), 3, out data, out _));
        Assert.Equal(new byte[] { 3, 4, 5 }, data);
    }

    [Theory]
    [InlineData(0, 6, 64)]
    [InlineData(6, 0, 64)]
    [InlineData(uint.MaxValue, 1, 64)]
    [InlineData(0, uint.MaxValue, 64)]
    [InlineData(0, 0, 4)]
    public void RegularFileRejectsShortReadsOverflowAndBudgets(uint offset, uint size, int limit)
    {
        using Files files = new();
        string path = files.Create("image.data", [1, 2, 3, 4, 5]);
        Assert.False(files.Reader.TryRead(Request(path, offset: offset, size: size), limit, out byte[]? data, out _));
        Assert.Null(data);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void OwnedTemporaryFileIsDeletedAfterSuccessOrReadFailure()
    {
        using Files files = new();
        string path = files.Create("tty-graphics-protocol-image", [1, 2, 3]);
        Assert.True(files.Reader.TryRead(Request(path, KittyGraphicsMedium.TemporaryFile), 3, out _, out _));
        Assert.False(File.Exists(path));
        path = files.Create("tty-graphics-protocol-short", [1, 2, 3]);
        Assert.False(files.Reader.TryRead(Request(path, KittyGraphicsMedium.TemporaryFile, size: 4), 4, out _, out _));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TemporaryFileRequiresNameAndCanonicalDirectoryBeforeTakingOwnership()
    {
        using Files files = new();
        string unnamed = files.Create("ordinary-file", [1]);
        Assert.False(files.Reader.TryRead(Request(unnamed, KittyGraphicsMedium.TemporaryFile), 4, out _, out string? error));
        Assert.Equal("EINVAL: temporary file not named correctly", error);
        Assert.True(File.Exists(unnamed));

        string outside = System.IO.Path.Combine(files.Root, "tty-graphics-protocol-outside");
        File.WriteAllBytes(outside, [2]);
        Assert.False(files.Reader.TryRead(Request(outside, KittyGraphicsMedium.TemporaryFile), 4, out _, out error));
        Assert.Equal("EINVAL: temporary file not in temp dir", error);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void SymlinkIsValidatedByOpenedTargetRatherThanLinkLocation()
    {
        if (OperatingSystem.IsWindows()) return;
        using Files files = new();
        string outside = System.IO.Path.Combine(files.Root, "tty-graphics-protocol-outside");
        File.WriteAllBytes(outside, [2]);
        string link = System.IO.Path.Combine(files.Directory, "tty-graphics-protocol-link");
        File.CreateSymbolicLink(link, outside);
        Assert.False(files.Reader.TryRead(Request(link, KittyGraphicsMedium.TemporaryFile), 4, out _, out _));
        Assert.True(File.Exists(outside));
        Assert.True(files.Reader.TryRead(Request(link), 4, out byte[]? data, out _));
        Assert.Equal(new byte[] { 2 }, data);
        File.Delete(link);
        File.CreateSymbolicLink(link, "/dev/zero");
        Assert.False(files.Reader.TryRead(Request(link), 4, out _, out _));
    }

    [Fact]
    public void TemporaryCleanupDoesNotDeleteReplacedDirectoryEntry()
    {
        if (OperatingSystem.IsWindows()) return;
        using Files files = new();
        string path = files.Create("tty-graphics-protocol-replaced", [1]);
        using KittyGraphicsFileAccess.OpenedFile opened = KittyGraphicsFileAccess.Open(path, temporary: true);
        string moved = System.IO.Path.Combine(files.Directory, "original-moved");
        File.Move(path, moved);
        File.WriteAllBytes(path, [2]);
        opened.DeleteIfUnchanged();
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(path));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(moved));
    }

    [Fact]
    public async Task FifoIsRejectedWithoutWaitingForAWriter()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using Files files = new();
        string path = System.IO.Path.Combine(files.Directory, "fifo");
        Assert.Equal(0, MakeFifo(path, 0x180));
        bool accepted = await Task.Run(() => files.Reader.TryRead(Request(path), 16, out _, out _)).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(accepted);
    }

    [Theory]
    [InlineData("/dev/zero")]
    [InlineData("/dev/null")]
    [InlineData("/proc/self/status")]
    [InlineData("/sys/kernel/uevent_seqnum")]
    public void UnixVirtualAndDevicePathsAreRejected(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        LocalKittyGraphicsMediumReader reader = new(new() { FileEnabled = true });
        Assert.False(reader.TryRead(Request(path), 4096, out _, out _));
    }

    [Fact]
    public void DarwinSharedMemoryCannotBeTruncatedAfterInitialAllocation()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using SharedMemory memory = new();
        Assert.Equal(-1, memory.TryResize(0));
        Assert.Equal(new byte[] { 1, 2, 3 }, KittyGraphicsFileAccess.ReadSharedMemory(memory.Name, 7, 0, 3, 3));
    }

    [Fact]
    public void SharedMemoryPlatformReadSupportsNativeDescriptors()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using SharedMemory memory = new();
        Assert.Equal(new byte[] { 1, 2, 3 }, KittyGraphicsFileAccess.ReadSharedMemory(memory.Name, 7, 0, 3, 3));
    }

    [Fact]
    public void SharedMemoryCopiesValidatedRawRangeAndUnlinksName()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using SharedMemory memory = new();
        LocalKittyGraphicsMediumReader reader = new(new() { SharedMemoryEnabled = true });
        Assert.True(reader.TryRead(Request(memory.Name, KittyGraphicsMedium.SharedMemory, offset: 7, expected: 3), 3, out byte[]? data, out _));
        Assert.Equal(new byte[] { 1, 2, 3 }, data);
        Assert.False(reader.TryRead(Request(memory.Name, KittyGraphicsMedium.SharedMemory, expected: 3), 3, out _, out _));
    }

    [Fact]
    public void SharedMemoryInvalidRangeStillUnlinksButInvalidNameDoesNotOpen()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        using SharedMemory memory = new();
        LocalKittyGraphicsMediumReader reader = new(new() { SharedMemoryEnabled = true });
        Assert.False(reader.TryRead(Request(memory.Name[1..], KittyGraphicsMedium.SharedMemory), 4096, out _, out _));
        Assert.False(reader.TryRead(Request(memory.Name, KittyGraphicsMedium.SharedMemory, offset: uint.MaxValue), 4096, out _, out _));
        Assert.False(reader.TryRead(Request(memory.Name, KittyGraphicsMedium.SharedMemory), 4096, out _, out _));
    }

    [Theory]
    [InlineData(0, 0, 3, 3)]
    [InlineData(7, 2, 3, 2)]
    [InlineData(4096, 0, 0, 0)]
    public void SharedMemoryRangeIgnoresPagePadding(uint offset, uint size, int expected, int count)
        => Assert.Equal(count, KittyGraphicsFileAccess.GetRangeLength(4096, offset, size, expected, 4096));

    private static KittyGraphicsMediumRequest Request(string path, KittyGraphicsMedium medium = KittyGraphicsMedium.File,
        uint offset = 0, uint size = 0, int? expected = null)
        => new(medium, Encoding.UTF8.GetBytes(path), offset, size, expected);

    private sealed class Files : IDisposable
    {
        // Outside OS temp roots so the test can distinguish configured-root boundaries.
        internal string Root { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "kitty-media-" + Guid.NewGuid().ToString("N"));
        internal string Directory { get; }
        internal LocalKittyGraphicsMediumReader Reader { get; }
        internal Files()
        {
            Directory = System.IO.Path.Combine(Root, "allowed");
            System.IO.Directory.CreateDirectory(Directory);
            Reader = new(new() { FileEnabled = true, TemporaryDirectory = Directory, SharedMemoryEnabled = true });
        }
        internal string Create(string name, byte[] bytes)
        {
            string path = System.IO.Path.Combine(Directory, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        public void Dispose() => System.IO.Directory.Delete(Root, recursive: true);
    }

    private sealed class SharedMemory : IDisposable
    {
        internal string Name { get; } = "/rt-" + Guid.NewGuid().ToString("N")[..20];
        internal SharedMemory()
        {
            int fd = OperatingSystem.IsMacOS()
                ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? CreateShmMacArm64(Name, 0xa02, 0, 0, 0, 0, 0, 0, 0x180)
                    : CreateShmMac(Name, 0xa02, 0x180)
                : CreateShmLinux(Name, 0xc2, 0x180);
            Assert.True(fd >= 0);
            using SafeFileHandle handle = new(fd, ownsHandle: true);
            Assert.Equal(0, Truncate(handle, 4096));
            nint map = Map(0, 4096, 3, 1, handle, 0);
            Assert.NotEqual((nint)(-1), map);
            try { Marshal.Copy(new byte[] { 1, 2, 3 }, 0, map + 7, 3); }
            finally { Assert.Equal(0, Unmap(map, 4096)); }
        }
        public void Dispose()
        {
            if (OperatingSystem.IsMacOS()) _ = UnlinkShmMac(Name);
            else _ = UnlinkShmLinux(Name);
        }
        internal int TryResize(long length)
        {
            int fd = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? CreateShmMacArm64(Name, 2, 0, 0, 0, 0, 0, 0, 0)
                : CreateShmMac(Name, 2, 0);
            Assert.True(fd >= 0);
            using SafeFileHandle handle = new(fd, ownsHandle: true);
            return Truncate(handle, length);
        }
    }

    [LibraryImport("libc", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MakeFifo(string path, uint mode);
    [LibraryImport("libSystem.dylib", EntryPoint = "shm_open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int CreateShmMac(string name, int flags, uint mode);
    [LibraryImport("libSystem.dylib", EntryPoint = "shm_open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int CreateShmMacArm64(string name, int flags, nint p2, nint p3, nint p4, nint p5, nint p6, nint p7, uint mode);
    [LibraryImport("librt.so.1", EntryPoint = "shm_open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int CreateShmLinux(string name, int flags, uint mode);
    [LibraryImport("libSystem.dylib", EntryPoint = "shm_unlink", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnlinkShmMac(string name);
    [LibraryImport("librt.so.1", EntryPoint = "shm_unlink", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnlinkShmLinux(string name);
    [LibraryImport("libc", EntryPoint = "ftruncate")]
    private static partial int Truncate(SafeFileHandle file, long length);
    [LibraryImport("libc", EntryPoint = "mmap")]
    private static partial nint Map(nint address, nuint length, int protection, int flags, SafeFileHandle file, long offset);
    [LibraryImport("libc", EntryPoint = "munmap")]
    private static partial int Unmap(nint address, nuint length);
}
