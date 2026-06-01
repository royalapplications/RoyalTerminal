// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Session history and scrollback preservation coverage.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public class TerminalSessionHistoryTests
{
    [Fact]
    public void TerminalScreen_ClearScrollback_PreservesVisibleRowsAndDropsHistory()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 3, scrollbackLimit: 10);
        SetAscii(screen.GetViewportRow(0), "A");
        SetAscii(screen.GetViewportRow(1), "B");
        SetAscii(screen.GetViewportRow(2), "C");
        SetAscii(screen.AddRow(), "D");
        SetAscii(screen.AddRow(), "E");

        Assert.True(screen.TotalRows > screen.ViewportRows);

        screen.ClearScrollback();

        Assert.Equal(screen.ViewportRows, screen.TotalRows);
        Assert.Equal(0, screen.MaxScrollOffset);
        Assert.Equal("C", ReadAscii(screen.GetViewportRow(0), 1));
        Assert.Equal("D", ReadAscii(screen.GetViewportRow(1), 1));
        Assert.Equal("E", ReadAscii(screen.GetViewportRow(2), 1));
    }

    [Fact]
    public void TerminalScreen_MoveViewportToScrollbackAndClear_PreservesFormattedViewportRows()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 3, scrollbackLimit: 10);
        SetAscii(screen.GetViewportRow(0), "OLD");
        screen.GetViewportRow(0)[0].Foreground = 0xFFFF0000;
        screen.GetViewportRow(0)[0].Attributes = CellAttributes.Bold;
        SetAscii(screen.GetViewportRow(1), "NEXT");

        screen.MoveViewportToScrollbackAndClear();

        Assert.True(screen.TotalRows > screen.ViewportRows);
        Assert.Equal("OLD", ReadAscii(screen.GetRow(0), 3));
        Assert.Equal(0xFFFF0000u, screen.GetRow(0)[0].Foreground);
        Assert.True(screen.GetRow(0)[0].Attributes.HasFlag(CellAttributes.Bold));
        Assert.Equal("NEXT", ReadAscii(screen.GetRow(1), 4));
        Assert.True(string.IsNullOrWhiteSpace(ReadAscii(screen.GetViewportRow(0), screen.Columns)));
        Assert.True(string.IsNullOrWhiteSpace(ReadAscii(screen.GetViewportRow(1), screen.Columns)));
        Assert.True(string.IsNullOrWhiteSpace(ReadAscii(screen.GetViewportRow(2), screen.Columns)));
    }

    [Fact]
    public void BasicVtProcessor_Csi3J_ClearsScrollbackOnly()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 3, scrollbackLimit: 10);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "L0\r\nL1\r\nL2\r\nL3\r\nL4");

        Assert.True(screen.TotalRows > screen.ViewportRows);
        string beforeViewport = ReadViewport(screen);

        Process(processor, "\u001b[3J");

        Assert.Equal(screen.ViewportRows, screen.TotalRows);
        Assert.Equal(0, screen.MaxScrollOffset);
        Assert.Equal(beforeViewport, ReadViewport(screen));
    }

    [Fact]
    public void BasicVtProcessor_Csi22J_MovesViewportToScrollbackAndClearsDisplay()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 3, scrollbackLimit: 10);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[31mRED\u001b[0m\r\nNEXT");

        Process(processor, "\u001b[22J");

        Assert.True(screen.TotalRows > screen.ViewportRows);
        Assert.Equal("RED", ReadAscii(screen.GetRow(0), 3));
        Assert.NotEqual(screen.DefaultForeground, screen.GetRow(0)[0].Foreground);
        Assert.Equal("NEXT", ReadAscii(screen.GetRow(1), 4));
        Assert.True(string.IsNullOrWhiteSpace(ReadViewport(screen)));
    }

    private static void Process(BasicVtProcessor processor, string text)
    {
        processor.Process(Encoding.UTF8.GetBytes(text));
    }

    private static void SetAscii(TerminalRow row, string text)
    {
        int columnCount = Math.Min(row.Columns, text.Length);
        for (int column = 0; column < columnCount; column++)
        {
            row[column].Codepoint = text[column];
            row[column].Width = 1;
        }
    }

    private static string ReadViewport(TerminalScreen screen)
    {
        StringBuilder builder = new();
        for (int row = 0; row < screen.ViewportRows; row++)
        {
            builder.AppendLine(ReadAscii(screen.GetViewportRow(row), screen.Columns));
        }

        return builder.ToString();
    }

    private static string ReadAscii(TerminalRow row, int length)
    {
        char[] chars = new char[length];
        for (int column = 0; column < length; column++)
        {
            int codepoint = row[column].Codepoint;
            chars[column] = codepoint <= 0 ? ' ' : (char)codepoint;
        }

        return new string(chars);
    }
}
