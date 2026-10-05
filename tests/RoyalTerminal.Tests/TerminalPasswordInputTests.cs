// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalPasswordInputTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostStateIsLiveAndFullResetClearsIt(bool native)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native password input available: {available}");
        if (native && !available) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalPasswordInputState state = Assert.IsAssignableFrom<ITerminalPasswordInputState>(processor);
        Assert.False(state.PasswordInput);
        state.PasswordInput = true;
        processor.Process("\u001b[?2026h\u001b[!p\u001b[?47h\u001b[?47l"u8);
        Assert.True(state.PasswordInput);
        processor.Process("\u001bc"u8);
        Assert.False(state.PasswordInput);
        foreach (bool preserve in new[] { true, false })
        {
            state.PasswordInput = true;
            ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserve);
            Assert.False(state.PasswordInput);
        }
    }

    [Fact]
    public unsafe void NativeAbiRejectsInvalidArgumentsAndSnapshotsPreserveTheFlag()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyTerminal terminal = new(8, 3);
        byte outputValue = 123;
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.PasswordInputGet(0, &outputValue));
        Assert.Equal(123, outputValue);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.PasswordInputGet(terminal.Handle, null));
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.PasswordInputSet(terminal.Handle, 2));
        Assert.False(terminal.PasswordInput);
        terminal.PasswordInput = true;
        using GhosttyTerminal restored = GhosttySnapshot.Decode(GhosttySnapshot.Encode(terminal));
        Assert.True(restored.PasswordInput);
        terminal.Dispose();
        Assert.Throws<ObjectDisposedException>(() => terminal.PasswordInput);
        Assert.Throws<ObjectDisposedException>(() => terminal.PasswordInput = false);
    }
}
