// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Terminal.eraseDisplay/eraseLine reset wrap for active-area erasure,
// but not history-only or ignored operations. Mode 1047 clears before copying
// the cursor; 1049 clears before an entering copy and on repeated activation.
// WT/xterm.js activation implementations differ; Ghostty defines this matrix.
public sealed class ManagedScreenSwitchWrapTests
{
    [Theory]
    [InlineData("\u001b[?47h\u001b[1;8HX\u001b[?1047l", false)]
    [InlineData("\u001b[?1049h\u001b[1;8HX\u001b[?1049h", false)]
    [InlineData("\u001b[1;8HX\u001b[?1049h", true)]
    [InlineData("\u001b[1;8HX\u001b[?47h", true)]
    [InlineData("\u001b[1;8HX\u001b[?1047h", true)]
    [InlineData("\u001b[?47h\u001b[1;8HX\u001b[?47l", true)]
    public void ModeClearsAndCursorCopiesHaveDistinctPendingWrapBoundaries(string input, bool wrapped)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes(input + "Y"));
        AssertWrittenPosition(screen, processor, wrapped);
    }

    [Theory]
    [InlineData("\u001b[3J", true)]
    [InlineData("\u001b[99J", true)]
    [InlineData("\u001b[99K", true)]
    [InlineData("\u001b[2J", false)]
    [InlineData("\u001b[K", false)]
    [InlineData("\u001b[?2J", false)]
    public void EraseResetsPendingWrapOnlyWhenItAffectsTheActiveArea(string erase, bool wrapped)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.ASCII.GetBytes("\u001b[1;8HX" + erase + "Y"));
        AssertWrittenPosition(screen, processor, wrapped);
    }

    [Theory]
    [InlineData("\u001b[?47h\u001b[1;8HX\u001b[?1047l")]
    [InlineData("\u001b[?1049h\u001b[1;8HX\u001b[?1049h")]
    [InlineData("\u001b[1;8HX\u001b[?1049h")]
    [InlineData("\u001b[1;8HX\u001b[?1047h")]
    [InlineData("\u001b[?47h\u001b[1;8HX\u001b[?47l")]
    [InlineData("\u001b[1;8HX\u001b[3J")]
    [InlineData("\u001b[1;8HX\u001b[99J")]
    [InlineData("\u001b[1;8HX\u001b[99K")]
    public void ModeSwitchAndEraseContinuationMatchNative(string input)
    {
        RequireNative();
        using BasicVtProcessor seed = new(new TerminalScreen(8, 2));
        byte[] snapshot = seed.GetBinarySnapshot();
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        byte[] bytes = Encoding.ASCII.GetBytes(input + "Y");
        native.Write(bytes);
        managed.Processor.Process(bytes);
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        int key = ready.Terminal.Header.ActiveScreenKey;
        Assert.Equal(key == 1, managed.Screen.AlternateBufferActive);
        foreach (GhosttySnapshotScreen expected in ready.Screens)
        {
            TerminalRowBuffer? actual = managed.Screen.GetSnapshotRows(expected.State.Key);
            Assert.NotNull(actual);
            int index = 0;
            foreach (GhosttySnapshotPage page in expected.Pages)
            foreach (TerminalRow row in GhosttySnapshotLivePage.Decode(page, new TerminalScreen(8, 2)))
            {
                for (int column = 0; column < 8; column++)
                    Assert.Equal(row.ReadOnlyCells[column].Codepoint, actual[index].ReadOnlyCells[column].Codepoint);
                index++;
            }
            if (expected.State.Key != key) continue;
            Assert.Equal(expected.State.CursorX, managed.Processor.CursorCol);
            Assert.Equal(expected.State.CursorY, managed.Processor.CursorRow);
        }
    }

    private static void AssertWrittenPosition(TerminalScreen screen, BasicVtProcessor processor, bool wrapped)
    {
        Assert.Equal('Y', screen.GetViewportRow(wrapped ? 1 : 0).ReadOnlyCells[wrapped ? 0 : 7].Codepoint);
        Assert.Equal(wrapped ? 1 : 0, processor.CursorRow);
        Assert.Equal(wrapped ? 1 : 7, processor.CursorCol);
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
