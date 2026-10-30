// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalOctoberResetTests
{
    // Ghostty #14538: DECSTR leaves cursor, content, tabs, links and unrelated
    // modes intact. Windows Terminal and xterm.js also reset the saved cursor.
    [Theory]
    [InlineData("hello\u001b[3;5H\u001b[!pX")]
    [InlineData("12345678\u001b[!pX")]
    [InlineData("\u001b[3g\u001b[3G\u001bH\r\u001b[!p\tX")]
    [InlineData("\u001b]8;;https://example.test\u001b\\\u001b[!pX")]
    [InlineData("\u001b[3;5H\u001b[1;31m\u001b7\u001b[!p\u001b8X")]
    [InlineData("\u001b[3;5H\u001b7\u001b[?47h\u001b[2;3H\u001b7\u001b[!p\u001b8X\u001b[?47l\u001b8Y")]
    [InlineData("\u001b[1;31m\u001b[1\"q\u001b(0\u001b[!pq")]
    [InlineData("\u001b[1$}\u001b[!pX")]
    [InlineData("\u001b[?69h\u001b[3;6s\u001b[2;3r\u001b[?6h\u001b[!p\u001b[4;8HX")]
    [InlineData("\u001b[?2004;9001;1004;1003;1006;2027h\u001b[20h\u001b[12l\u001b[!pX\nY")]
    [InlineData("\u001b[?1h\u001b[?1s\u001b[!p\u001b[?1rX")]
    [InlineData("\u001b[>4;2m\u001b[>5u\u001b[!p\u001b[?u")]
    [InlineData("\u001b[?2026hHELD\u001b[!pX\u001b[?2026l")]
    [InlineData("\u001b]4;1;#123456\a\u001b]10;#abcdef\a\u001b[!p\u001b]4;1;?\a\u001b]10;?\a\u001b[31mX")]
    [InlineData("\u001b]4;1;#123456\a\u001b]10;#abcdef\a\u001bc\u001b]4;1;?\a\u001b]10;?\a\u001b[31mX")]
    public void ResetContractMatchesNative(string input)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen expected = new(8, 4), actual = new(8, 4);
        using GhosttyVtProcessor native = new(expected);
        using BasicVtProcessor managed = new(actual);
        List<string> nativeReplies = [], managedReplies = [];
        native.ResponseCallback = bytes => nativeReplies.Add(Convert.ToHexString(bytes));
        managed.ResponseCallback = bytes => managedReplies.Add(Convert.ToHexString(bytes));
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        native.Process(bytes);
        managed.Process(bytes);
        Assert.Equal(nativeReplies, managedReplies);
        Assert.Equal(native.ModeState, managed.ModeState);
        Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
        Assert.Equal(native.ModifyOtherKeys2, managed.ModifyOtherKeys2);
        Assert.Equal(native.KittyKeyboardFlags, managed.KittyKeyboardFlags);
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 8; column++)
        {
            TerminalCell e = expected.GetViewportRow(row)[column], a = actual.GetViewportRow(row)[column];
            Assert.Equal((e.Codepoint, e.Width, e.IsProtected, e.Grapheme), (a.Codepoint, a.Width, a.IsProtected, a.Grapheme));
            if (!e.HasContent) continue;
            Assert.Equal(e.Attributes, a.Attributes);
            Assert.Equal(e.Foreground, a.Foreground);
            expected.TryGetHyperlinkUrl(e.HyperlinkId, out string? eUrl);
            actual.TryGetHyperlinkUrl(a.HyperlinkId, out string? aUrl);
            Assert.Equal(eUrl, aUrl);
        }
    }
}
