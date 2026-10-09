// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class OctoberInputRegressionTests
{
    [Theory]
    [InlineData(false, 1, 0, 2)]
    [InlineData(true, 1, 0, 2)]
    [InlineData(false, 2, 0, 48)]
    [InlineData(true, 2, 0, 48)]
    [InlineData(false, 2, 10, 38)]
    [InlineData(true, 2, 10, 38)]
    public void PromptClickUsesAbsoluteRowsAcrossPagesAndScrolledViewports(bool native, int policy, int scroll, int expectedRow)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(8, 4, 1000);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process(Encoding.ASCII.GetBytes(new string('\n', 100) + $"\u001b]133;A;click_events={policy}\a" +
            string.Join("\r\n", Enumerable.Repeat("p", 50)) + "\u001b]133;B\a"));
        if (processor is ITerminalViewportScrollSource viewport)
            viewport.SetViewportOffsetRows(viewport.ViewportScrollState.MaxOffsetRows - (ulong)scroll);
        else screen.ScrollOffset = scroll;
        ITerminalPromptClickEncoderSource source = Assert.IsAssignableFrom<ITerminalPromptClickEncoderSource>(processor);
        Assert.True(source.TryEncodePromptClick(2, 1, out byte[] bytes));
        Assert.Equal($"\u001b[<0;3;{expectedRow}M", Encoding.ASCII.GetString(bytes));
        Assert.False(source.TryEncodePromptClick(8, 1, out _));
        Assert.False(source.TryEncodePromptClick(-1, 1, out _));
        Assert.False(source.TryEncodePromptClick(0, 4, out _));
        processor.Process("\u001b[?1049h"u8);
        Assert.False(source.TryEncodePromptClick(0, 0, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromptClickRejectsEarlierOutputAndExpiredPrompt(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new(8, 4)) : new BasicVtProcessor(new(8, 4));
        ITerminalPromptClickEncoderSource source = (ITerminalPromptClickEncoderSource)processor;
        Assert.False(source.TryEncodePromptClick(0, 0, out _));
        processor.Process("out\r\n\u001b]133;A;click_events=2\a$ \u001b]133;B\acmd"u8);
        Assert.False(source.TryEncodePromptClick(0, 0, out _));
        Assert.True(source.TryEncodePromptClick(0, 3, out byte[] bytes));
        Assert.Equal("\u001b[<0;1;3M", Encoding.ASCII.GetString(bytes));
        processor.Process("\u001b]133;C\a\r\noutput"u8);
        Assert.False(source.TryEncodePromptClick(0, 3, out _));
        processor.Process("\u001bc"u8);
        Assert.False(source.TryEncodePromptClick(0, 0, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1005)]
    [InlineData(1006)]
    [InlineData(1015)]
    [InlineData(1016)]
    public void ExtendedMouseButtonsMatchNativeInEveryEncoding(int encoding)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new(8, 4));
        using BasicVtProcessor managed = new(new(8, 4));
        byte[] modes = Encoding.ASCII.GetBytes("\u001b[?1003h" + (encoding == 0 ? "" : $"\u001b[?{encoding}h"));
        native.Process(modes); managed.Process(modes);
        TerminalPointerEncodingContext context = new(80, 80, 10, 20);
        for (int button = 8; button <= 9; button++)
        foreach (TerminalModifiers modifiers in new[] { TerminalModifiers.None, TerminalModifiers.Alt | TerminalModifiers.Control | TerminalModifiers.Shift })
        foreach (TerminalPointerEventKind kind in new[] { TerminalPointerEventKind.Button, TerminalPointerEventKind.Move })
        foreach (TerminalInputAction action in new[] { TerminalInputAction.Press, TerminalInputAction.Release })
        {
            TerminalPointerEvent pointer = new(kind, 11, 21, (TerminalMouseButton)button, action, modifiers);
            Assert.Equal(native.TryEncodePointer(pointer, context, out byte[] expected), managed.TryEncodePointer(pointer, context, out byte[] actual));
            Assert.Equal(expected, actual);
            if (encoding == 1005 && kind == TerminalPointerEventKind.Button && action == TerminalInputAction.Press)
            {
                Assert.InRange(actual[3], (byte)0xc2, (byte)0xc3);
                Assert.Equal(6, Encoding.UTF8.GetString(actual).Length);
            }
        }
    }
}
