// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class KittyGraphicsPathPolicyTests
{
    [Fact]
    public void WindowsRejectsNetworkDeviceAndReservedNamesBeforeOpening()
    {
        string[] invalid =
        [
            @"\\server\share\image.png", "//server/share/image.png", @"\/server/share/image.png",
            @"\\?\C:\image.png", @"\\.\pipe\image", @"\\?\GLOBALROOT\Device\HarddiskVolume1\image",
            @"\??\C:\image.png", "/??/C:/image.png", "CON", "CON.png", "con .png", "NUL:stream",
            "PRN", "AUX", "COM0", "COM9.png", "LPT0", "LPT9", "COM¹", "LPT³.png",
            @"C:\safe\CON\image.png", @"C:\safe\com1", @"..\PRN",
        ];
        foreach (string path in invalid) Assert.False(KittyGraphicsPathPolicy.IsAllowedWindowsPath(path), path);
    }

    [Fact]
    public void WindowsAllowsPlainLocalPathsButRequiresDriveAbsoluteCanonicalPaths()
    {
        string[] valid = [@"C:\safe\image.png", "C:/safe/image.png", @"C:image.png", @"\safe\image.png", "image.png", "CONSOLE", "COM10", "COMA"];
        foreach (string path in valid) Assert.True(KittyGraphicsPathPolicy.IsAllowedWindowsPath(path), path);
        Assert.True(KittyGraphicsPathPolicy.IsAllowedCanonicalWindowsPath(@"c:\safe\image.png"));
        Assert.False(KittyGraphicsPathPolicy.IsAllowedCanonicalWindowsPath("C:/safe/image.png"));
        Assert.False(KittyGraphicsPathPolicy.IsAllowedCanonicalWindowsPath(@"\\server\image.png"));
        Assert.False(KittyGraphicsPathPolicy.IsAllowedCanonicalWindowsPath(@"\\?\Volume{1}\image.png"));
        Assert.False(KittyGraphicsPathPolicy.IsAllowedCanonicalWindowsPath(@"C:image.png"));
    }

    [Theory]
    [InlineData("/proc/self/fd/3", false)]
    [InlineData("/sys/kernel/x", false)]
    [InlineData("/dev/zero", false)]
    [InlineData("/dev/shm-evil/image", false)]
    [InlineData("/dev/shm/image", true)]
    [InlineData("/proc-image.png", true)]
    [InlineData("/safe/image", true)]
    [InlineData("relative/image", false)]
    public void UnixBlocksOnlyUnsafeCanonicalTrees(string path, bool expected)
        => Assert.Equal(expected, KittyGraphicsPathPolicy.IsAllowedCanonicalUnixPath(path));

    [Fact]
    public void TemporaryDirectoriesRequireSeparatorBoundaries()
    {
        Assert.True(KittyGraphicsPathPolicy.IsWithinDirectory("/tmp", "/tmp/image", false));
        Assert.True(KittyGraphicsPathPolicy.IsWithinDirectory("/tmp/", "/tmp/image", false));
        Assert.False(KittyGraphicsPathPolicy.IsWithinDirectory("/tmp", "/tmp-evil/image", false));
        Assert.False(KittyGraphicsPathPolicy.IsWithinDirectory("", "/image", false));
        Assert.False(KittyGraphicsPathPolicy.IsWithinDirectory("/Tmp", "/tmp/image", false));
        Assert.True(KittyGraphicsPathPolicy.IsWithinDirectory(@"C:\TEMP", @"c:\temp\image", true));
        Assert.False(KittyGraphicsPathPolicy.IsWithinDirectory(@"C:\TEMP", @"c:\temp-evil\image", true));
    }

    [Theory]
    [InlineData("/image", true)]
    [InlineData("image", false)]
    [InlineData("/", false)]
    [InlineData("/image/path", false)]
    [InlineData("/image\0name", false)]
    public void SharedMemoryNamesFollowPosixShape(string name, bool expected)
        => Assert.Equal(expected, KittyGraphicsPathPolicy.IsSharedMemoryName(Encoding.UTF8.GetBytes(name)));

    [Fact]
    public void SharedMemoryNameLengthAndInvalidPathsAreBoundedBeforeOpening()
    {
        Assert.True(KittyGraphicsPathPolicy.IsSharedMemoryName(Encoding.UTF8.GetBytes("/" + new string('a', 254))));
        Assert.False(KittyGraphicsPathPolicy.IsSharedMemoryName(Encoding.UTF8.GetBytes("/" + new string('a', 255))));
        LocalKittyGraphicsMediumReader reader = new(new() { FileEnabled = true });
        Assert.False(reader.TryRead(new(KittyGraphicsMedium.File, new byte[] { 0xff }, 0, 0, null), 16, out _, out _));
        Assert.False(reader.TryRead(new(KittyGraphicsMedium.File, "/file\0suffix"u8.ToArray(), 0, 0, null), 16, out _, out _));
        Assert.False(reader.TryRead(new(KittyGraphicsMedium.File, new byte[98305], 0, 0, null), 16, out _, out _));
        Assert.False(reader.TryRead(new(KittyGraphicsMedium.File, "/file"u8.ToArray(), 0, 0, -1), 16, out _, out _));
    }
}
