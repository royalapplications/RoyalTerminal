// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class ManagedCharacterEditParityTests
{
    // Ghostty Terminal.{insertColumns,deleteColumns,backIndex,forwardIndex}
    // defines margins, wrap state, wide cells and metadata. WT's
    // AdaptDispatch uses rectangle scrolling; xterm.js supports DECIC/DECDC
    // but does not implement the same horizontal-margin/index behavior.
    [Theory]
    [InlineData("\u001b[2'}", false)]
    [InlineData("\u001b[2'}", true)]
    [InlineData("\u001b[2'~", false)]
    [InlineData("\u001b[2'~", true)]
    [InlineData("\u001b6", false)]
    [InlineData("\u001b6", true)]
    [InlineData("\u001b9", false)]
    [InlineData("\u001b9", true)]
    [InlineData("\u001b[0'}", true)]
    [InlineData("\u001b[999'~", true)]
    public void ColumnControlsMatchNativeAtEveryCursorPosition(string operation, bool margins)
    {
        RequireNative();
        string setup = Seed + "\r\n" + Seed + "\r\nABCDEFGH" +
            (margins ? "\u001b[?69h\u001b[2;7s\u001b[2;3r" : "") + "\u001b[0;44m";
        using GhosttyTerminal source = new(8, 3);
        source.Write(Encoding.UTF8.GetBytes(setup));
        byte[] snapshot = GhosttySnapshot.Encode(source);
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 8; column++)
        {
            using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
            using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
            TerminalScreen retained = managed.Screen.CreateStateCopy();
            TerminalCell[] before = retained.GetViewportRow(row).ReadOnlyCells.ToArray();
            byte[] command = Encoding.UTF8.GetBytes($"\u001b[{row + 1};{column + 1}H{operation}");
            native.Write(command);
            managed.Processor.Process(command);
            AssertNative(native, managed, $"{operation}, margins={margins}, row={row}, column={column}");
            Assert.Equal(before, retained.GetViewportRow(row).ReadOnlyCells.ToArray());
            native.Write("Z\u0301"u8);
            managed.Processor.Process("Z\u0301"u8);
            AssertNative(native, managed, "write after column edit");
        }
    }
}
