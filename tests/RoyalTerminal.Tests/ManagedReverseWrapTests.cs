// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedReverseWrapTests(ITestOutputHelper output)
{
    private const string ModesOff = "\u001b[?45;1045l";

    public static TheoryData<string, int, int> Cases => new()
    {
        { "ABCDE\b", 3, 0 },
        { "\u001b[?45hABCDE\b", 4, 0 },
        { "\u001b[?45hABCDE\u001b[D", 4, 0 },
        { "\u001b[?1045hABCDE\u001b[2D", 3, 0 },
        { "\u001b[?45hABCDE1\u001b[2D", 4, 0 },
        { "\u001b[?45hABCDE\r\n\b", 0, 1 },
        { "\u001b[?1045hABCDE\r\n\b", 4, 0 },
        { "\u001b[?1045h\b", 4, 4 },
        { "\u001b[?45h\b", 0, 0 },
        { "\u001b[?45;1045h\b", 4, 4 },
        { "\u001b[?1045h\u001b[?7l\b", 0, 0 },
        { "\u001b[?45hABCDE\u001b[?7l\u001b[D", 3, 0 },
        { "\u001b[?1045h\u001b[2;4r\u001b[2;1H\b", 4, 3 },
        { "\u001b[?1045h\u001b[2;4r\u001b[1;1H\b", 0, 0 },
        { "\u001b[?45h\u001b[3;5r\u001b[1;1H\b", 0, 2 },
        { "\u001b[?1045h\u001b[2;4r\u001b[5;1H\b", 4, 3 },
        { "\u001b[?1045;69h\u001b[2;4s\u001b[1;2H\b", 3, 4 },
        { "\u001b[?1045;69h\u001b[2;4s\u001b[2;1H\b", 3, 0 },
        { "\u001b[?1045h\u001b[26D", 4, 4 },
        { "\u001b[?45hABCDE1\u001b[99D", 0, 0 },
        // #14390: restored pending wrap at the left margin above the region.
        { "\u001b[?45;69h\u001b[1;2sAB\u001b7\u001b[2;5s\u001b[3;5r\u001b8\b", 1, 0 },
        { "\u001b[?45;69h\u001b[1;2sAB\u001b7\u001b[2;5s\u001b[3;5r\u001b8\u001b[D", 1, 0 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void CursorAndNextPrintRespectReverseWrap(string input, int column, int row)
    {
        TerminalScreen screen = new(5, 5, 0);
        using BasicVtProcessor processor = new(screen);
        byte[] bytes = Encoding.UTF8.GetBytes(ModesOff + input);
        // Exercise both the batch parser and a fragmented control stream.
        foreach (bool fragmented in new[] { false, true })
        {
            processor.Reset();
            if (fragmented)
                for (int index = 0; index < bytes.Length; index++) processor.Process(bytes.AsSpan(index, 1));
            else processor.Process(bytes);
            Assert.Equal(column, processor.CursorCol);
            Assert.Equal(row, processor.CursorRow);
            processor.Process("X"u8);
            Assert.Equal('X', screen.GetViewportRow(row)[column].Codepoint);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MovementAndContinuationMatchGhostty(string input, int column, int row)
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            output.WriteLine("Native reverse-wrap differential unavailable; not counted as native validation.");
            return;
        }
        TerminalScreen expected = new(5, 5, 0), actual = new(5, 5, 0);
        using GhosttyVtProcessor native = new(expected);
        using BasicVtProcessor managed = new(actual);
        byte[] bytes = Encoding.UTF8.GetBytes(ModesOff + input);
        native.Process(bytes); managed.Process(bytes);
        Assert.Equal(column, native.CursorCol);
        Assert.Equal(row, native.CursorRow);
        Assert.Equal(native.CursorCol, managed.CursorCol);
        Assert.Equal(native.CursorRow, managed.CursorRow);
        native.Process("X\bY"u8); managed.Process("X\bY"u8);
        Assert.Equal(native.CursorCol, managed.CursorCol);
        Assert.Equal(native.CursorRow, managed.CursorRow);
        for (int y = 0; y < 5; y++)
        {
            Assert.Equal(expected.GetViewportRow(y).WrapsToNext, actual.GetViewportRow(y).WrapsToNext);
            for (int x = 0; x < 5; x++)
                Assert.Equal(expected.GetViewportRow(y)[x].Codepoint, actual.GetViewportRow(y)[x].Codepoint);
        }
    }
}
