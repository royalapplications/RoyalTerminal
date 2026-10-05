// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class ManagedEraseParityTests
{
    [Theory]
    [InlineData("\u001b[3J", false)]
    [InlineData("\u001b[3J", true)]
    [InlineData("\u001b[?3J", false)]
    [InlineData("\u001b[?3J", true)]
    [InlineData("\u001b[3;1H\n", false)]
    [InlineData("\u001b[3;1H\n", true)]
    [InlineData("\u001b[2S", false)]
    [InlineData("\u001b[2S", true)]
    [InlineData("\u001b[2T", false)]
    [InlineData("\u001b[2T", true)]
    [InlineData("\u001b[2;1H\u001b[2L", false)]
    [InlineData("\u001b[2;1H\u001b[2L", true)]
    [InlineData("\u001b[2;1H\u001b[2M", false)]
    [InlineData("\u001b[2;1H\u001b[2M", true)]
    [InlineData("\u001b[2J", false)]
    [InlineData("\u001b[2J", true)]
    [InlineData("\u001b[?47l\u001b[?47h", false)]
    [InlineData("\u001b[?47l\u001b[?47h", true)]
    [InlineData("\u001b[?1047l\u001b[?47h", false)]
    [InlineData("\u001b[?1047l\u001b[?47h", true)]
    [InlineData("\u001bc", false)]
    [InlineData("\u001bc", true)]
    public void AlternateHistorySurvivesOnlyTheAppropriateOperations(string operation, bool held)
    {
        // Ghostty ED3 erases the active screen's history even on alternate.
        // xterm.js likewise trims the active buffer; WT delegates ED3 to its
        // scrollback erase. Their ordinary alternate buffers do not expose
        // ED22 page-local history, so Ghostty defines the extension lifecycle.
        RequireNative();
        using GhosttyTerminal source = new(8, 3, 1024 * 1024);
        source.Write("primary\u001b[?47h\u001b[Hhistory1\r\nhistory2\r\nhistory3\u001b[22Jview1\r\nview2\r\nlast\u001b[2;4H\u001b[3;44m\u001b]8;;https://cursor\a"u8);
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        TerminalCell[] retainedCells = retained.GetRow(0).ReadOnlyCells.ToArray();
        byte[] command = Encoding.UTF8.GetBytes(operation);
        if (held) managed.Processor.Process("\u001b[?2026h"u8);
        native.Write(command);
        managed.Processor.Process(command);
        if (held) managed.Processor.Process("\u001b[?2026l"u8);
        AssertNative(native, managed, "alternate history " + Convert.ToHexString(command));
        Assert.Equal(retainedCells, retained.GetRow(0).ReadOnlyCells.ToArray());

        // ED3 makes physical row slots reusable without changing the surviving
        // page or cursor identity. Subsequent growth must consume those slots
        // before allocating another page and restarting an implicit hyperlink.
        for (int iteration = 0; iteration < 3; iteration++)
        {
            native.Write("\u001b[3;1HX\u001b[22J"u8);
            managed.Processor.Process("\u001b[3;1HX\u001b[22J"u8);
            AssertNative(native, managed, $"after {Convert.ToHexString(command)}, growth={iteration}");
        }
    }
}
