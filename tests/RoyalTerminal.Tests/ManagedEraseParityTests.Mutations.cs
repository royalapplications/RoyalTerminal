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
    [InlineData(17, 8, false)]
    [InlineData(17, 8, true)]
    [InlineData(29, 8, false)]
    [InlineData(29, 8, true)]
    [InlineData(17, 215, false)]
    [InlineData(17, 215, true)]
    [InlineData(29, 215, false)]
    [InlineData(29, 215, true)]
    public void MixedHistoryMutationsRetainNativeContentAndAllocationSemantics(int seed, int columns, bool alternate)
    {
        RequireNative();
        using GhosttyTerminal source = new((ushort)columns, 3, 1024 * 1024);
        if (alternate) source.Write("\u001b[?47h"u8);
        source.Write("first\r\nsecond\r\nthird\u001b[22Jview\u001b[H"u8);
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        string[] operations =
        [
            "line\r\n", "line\r\n", "\u001b[3J", "\u001b[22J", "\u001b[2J", "\u001b[1J", "\u001b[0J",
            "\u001b[3;1H\n", "\u001b[2S", "\u001b[1T", "\u001b[2L", "\u001b[1M", "\u001b[H", "\u001b[3;4H",
            "\u001b[31;1mX", "\u001b[0m", "\u001b]8;;https://cursor\a", "\u001b]8;;\a", "界a\u0301",
            "\u001b[?47h", "\u001b[?47l", "\u001bc",
        ];
        Random random = new(seed);
        for (int iteration = 0; iteration < 180; iteration++)
        {
            byte[] command = Encoding.UTF8.GetBytes(operations[random.Next(operations.Length)]);
            bool held = iteration % 3 == 0;
            TerminalScreen retained = managed.Screen.CreateStateCopy();
            TerminalCell[] retainedCells = retained.GetViewportRow(0).ReadOnlyCells.ToArray();
            if (held) managed.Processor.Process("\u001b[?2026h"u8);
            native.Write(command);
            managed.Processor.Process(command);
            if (held) managed.Processor.Process("\u001b[?2026l"u8);
            Assert.Equal(retainedCells, retained.GetViewportRow(0).ReadOnlyCells.ToArray());
            AssertNative(native, managed,
                $"seed={seed}, columns={columns}, alternate={alternate}, step={iteration}, command={Convert.ToHexString(command)}");
        }
    }
}
