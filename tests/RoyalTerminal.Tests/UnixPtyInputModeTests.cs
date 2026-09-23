// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("PtyContractTests")]
public sealed class UnixPtyInputModeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyCanonicalWithoutEchoIndicatesPasswordInput(bool macOS)
    {
        ulong canonical = macOS ? 0x100UL : 2UL;
        Assert.False(UnixPtyInputMode.IsPasswordInput(0, macOS));
        Assert.False(UnixPtyInputMode.IsPasswordInput(8, macOS));
        Assert.True(UnixPtyInputMode.IsPasswordInput(canonical, macOS));
        Assert.False(UnixPtyInputMode.IsPasswordInput(canonical | 8, macOS));
        Assert.True(UnixPtyInputMode.IsPasswordInput(canonical | 0x80000000UL, macOS));
    }

    [Theory]
    [InlineData("icanon -echo", true)]
    [InlineData("icanon echo", false)]
    [InlineData("-icanon -echo", false)]
    [InlineData("-icanon echo", false)]
    public void LivePtyModesAreDetectedWithoutAllocating(string mode, bool expected)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return;
        using UnixPty pty = new();
        Assert.True(pty.SupportsPasswordInputDetection);
        Assert.False(pty.TryGetPasswordInput(out bool before));
        Assert.False(before);
        using ManualResetEventSlim ready = new();
        pty.DataReceived += (_, _) => ready.Set();
        pty.Start(shell: "/bin/sh", arguments: ["-c", $"stty {mode}; printf READY; sleep 30"]);
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(pty.TryGetPasswordInput(out bool actual));
        Assert.Equal(expected, actual);
        for (int i = 0; i < 100; i++) pty.TryGetPasswordInput(out _);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) pty.TryGetPasswordInput(out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        pty.Dispose();
        Assert.False(pty.TryGetPasswordInput(out bool after));
        Assert.False(after);
    }

    [Fact]
    public async Task ConcurrentProbesCannotOutliveDescriptorOwnership()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) return;
        using UnixPty pty = new();
        pty.Start(shell: "/bin/sh", arguments: ["-c", "sleep 30"]);
        await Task.WhenAll(Task.Run(() => { for (int i = 0; i < 1000; i++) pty.TryGetPasswordInput(out _); }),
            Task.Run(pty.Dispose)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pty.TryGetPasswordInput(out bool detected));
        Assert.False(detected);
        Assert.False(UnixPtyInputMode.TryGetPasswordInput(-1, out detected));
        Assert.False(detected);
    }
}
