// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalPromptLineClickTests
{
    private const string Prompt = "\u001b]133;A;cl=line\a> \u001b]133;B\a";

    // Expected distances follow the pinned Ghostty Screen.promptClickLine
    // examples, including its input-cell (not grapheme) counting contract.
    public static TheoryData<bool, bool, string, int, string, int, int, int> LineClicks()
    {
        (string Name, int Columns, string Input, int Column, int Row, int Movement)[] cases =
        [
            ("left from end", 20, Prompt + "hello", 2, 0, -5),
            ("right within input", 20, Prompt + "hello\u001b[3G", 4, 0, 2),
            ("left within input", 20, Prompt + "hello\u001b[7G", 2, 0, -4),
            ("left clamps at prompt", 20, Prompt + "hello", 0, 0, -5),
            ("right clamps at end", 20, Prompt + "hello\u001b[3G", 15, 0, 5),
            ("right clamps below input", 20, Prompt + "hello\u001b[3G", 5, 2, 5),
            ("last input cell reaches empty end", 20, Prompt + "hello\u001b[7G", 15, 0, 1),
            ("already at end", 20, Prompt + "hello", 15, 0, 0),
            ("already at end below input", 20, Prompt + "hello", 5, 2, 0),
            ("same cell", 20, Prompt + "hello\u001b[5G", 4, 0, 0),
            ("empty input", 20, Prompt, 0, 0, 0),
            ("printed spaces are input", 20, Prompt + "ab  c", 4, 0, -3),
            ("right skips output cells", 20, Prompt + "h\u001b]133;C\aX\u001b]133;B\allo\u001b[3G", 6, 0, 3),
            ("left skips output cells", 20, Prompt + "h\u001b]133;C\aX\u001b]133;B\allo\u001b[7G", 2, 0, -3),
            ("left crosses soft wrap", 10, Prompt + "abcdefghij\u001b[2;2H", 2, 0, -9),
            ("right crosses soft wrap", 10, Prompt + "abcdefghij\u001b[1;3H", 1, 1, 9),
            ("right clamps after soft wrap", 10, Prompt + "abcdefghij\u001b[1;3H", 8, 1, 10),
            ("left skips continuation prompt", 8, Prompt + "abcdefg\u001b]133;P;k=c\a>>\u001b]133;B\ahi", 2, 0, -9),
            ("right stops at hard newline", 20, Prompt + "hello\r\nworld\u001b[1;3H", 2, 1, 5),
            ("left stops at hard newline", 20, Prompt + "hello\r\nworld\u001b[2;5H", 2, 0, -4),
            ("right stops before a new prompt", 10,
                Prompt + "abcdefghij\u001b[2;1H" + Prompt + "other\u001b[1;3H", 2, 1, 7),
            ("cell input remains usable after output marker", 20, Prompt + "hello\u001b]133;C\a\u001b[3G", 4, 0, 2),
            ("prompt cell without input pen", 20, Prompt + "hello\u001b]133;C\a\u001b[1G", 4, 0, 0),
            ("earlier output is excluded", 20, "out\r\n" + Prompt + "hello", 2, 0, 0),
            ("output cursor is excluded", 20, Prompt + "hello\u001b]133;C\a\r\noutput", 2, 0, 0),
            ("policy is required", 20, "\u001b]133;A\a> \u001b]133;B\ahello", 2, 0, 0),
            ("alternate screen is excluded", 20, "\u001b[?1049h" + Prompt + "hello", 2, 0, 0),
            ("negative column", 20, Prompt + "hello", -1, 0, 0),
            ("column past viewport", 20, Prompt + "hello", 20, 0, 0),
            ("negative row", 20, Prompt + "hello", 2, -1, 0),
            ("row past viewport", 20, Prompt + "hello", 2, 4, 0),
            ("wide input cells follow upstream", 20, Prompt + "a界b", 2, 0, -4),
        ];
        TheoryData<bool, bool, string, int, string, int, int, int> data = new();
        foreach (bool native in new[] { false, true })
        foreach (bool application in new[] { false, true })
        foreach ((string name, int columns, string input, int column, int row, int movement) in cases)
            data.Add(native, application, name, columns, input, column, row, movement);
        return data;
    }

    [Theory]
    [MemberData(nameof(LineClicks))]
    public void LineClickMatchesGhosttyInputCellMovement(bool native, bool application, string name,
        int columns, string input, int column, int row, int movement)
    {
        using IVtProcessor processor = CreateProcessor(native, new(columns, 4));
        processor.Process(Encoding.UTF8.GetBytes((application ? "\u001b[?1h" : "") + input));
        ITerminalPromptClickEncoderSource encoder = (ITerminalPromptClickEncoderSource)processor;
        bool handled = encoder.TryEncodePromptClick(column, row, out byte[] sequence);
        Assert.True(handled == (movement != 0), name);
        Assert.Equal(Arrows(movement, application), Encoding.ASCII.GetString(sequence));
    }

    [Theory]
    [InlineData(false, "m")]
    [InlineData(true, "m")]
    [InlineData(false, "v")]
    [InlineData(true, "v")]
    [InlineData(false, "w")]
    [InlineData(true, "w")]
    public void OtherLinePoliciesUseGhosttySingleLineFallback(bool native, string policy)
    {
        using IVtProcessor processor = CreateProcessor(native, new(20, 4));
        processor.Process(Encoding.ASCII.GetBytes($"\u001b]133;A;cl={policy}\a> \u001b]133;B\ahello"));
        Assert.True(((ITerminalPromptClickEncoderSource)processor).TryEncodePromptClick(2, 0, out byte[] sequence));
        Assert.Equal(Arrows(-5), Encoding.ASCII.GetString(sequence));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void ClickEventsTakePriorityOverLinePolicy(bool native, int clickEvents)
    {
        using IVtProcessor processor = CreateProcessor(native, new(20, 4));
        processor.Process(Encoding.ASCII.GetBytes(
            $"out\r\n\u001b]133;A;cl=line;click_events={clickEvents}\a> \u001b]133;B\ahello"));
        Assert.True(((ITerminalPromptClickEncoderSource)processor).TryEncodePromptClick(2, 1, out byte[] sequence));
        string expected = clickEvents == 0 ? Arrows(-5) : $"\u001b[<0;3;{(clickEvents == 1 ? 2 : 1)}M";
        Assert.Equal(expected, Encoding.ASCII.GetString(sequence));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    public void LineClicksUseAbsoluteRowsAcrossPagesAndScrolledViewports(bool native, int scroll)
    {
        TerminalScreen screen = new(8, 4, 1000);
        using IVtProcessor processor = CreateProcessor(native, screen);
        processor.Process(Encoding.ASCII.GetBytes(new string('\n', 100) + Prompt + new string('x', 400)));
        if (processor is ITerminalViewportScrollSource viewport)
            viewport.SetViewportOffsetRows(viewport.ViewportScrollState.MaxOffsetRows - (ulong)scroll);
        else screen.ScrollOffset = scroll;
        Assert.True(((ITerminalPromptClickEncoderSource)processor).TryEncodePromptClick(2, 1, out byte[] sequence));
        Assert.Equal(Arrows(-(2 + scroll) * 8), Encoding.ASCII.GetString(sequence));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineClicksFollowReflowedInputCells(bool native)
    {
        TerminalScreen screen = new(8, 4);
        using IVtProcessor processor = CreateProcessor(native, screen);
        processor.Process(Encoding.ASCII.GetBytes(Prompt + "abcdefghijklmno"));
        ITerminalPromptClickEncoderSource encoder = (ITerminalPromptClickEncoderSource)processor;
        foreach (int columns in new[] { 12, 6, 20 })
        {
            if (processor is BasicVtProcessor managed)
                managed.ResizeScreen(columns, 4, columns * 10, 80, reflowOnResize: true);
            else
            {
                screen.Resize(columns, 4, reflowOnResize: false);
                processor.NotifyResize(columns, 4, columns * 10, 80);
            }
            Assert.True(encoder.TryEncodePromptClick(2, 0, out byte[] sequence),
                $"Columns={columns}, cursor=({processor.CursorCol}, {processor.CursorRow})");
            Assert.Equal(Arrows(-15), Encoding.ASCII.GetString(sequence));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitMouseEventsKeepGhosttyOriginWhenReflowLosesInitialPromptMarker(bool native)
    {
        TerminalScreen screen = new(8, 4);
        using IVtProcessor processor = CreateProcessor(native, screen);
        processor.Process("\u001b]133;A;click_events=2\a> \u001b]133;B\aabcdefghijklmno"u8);
        if (processor is BasicVtProcessor managed)
            managed.ResizeScreen(12, 4, 120, 80, reflowOnResize: true);
        else
        {
            screen.Resize(12, 4, reflowOnResize: false);
            processor.NotifyResize(12, 4, 120, 80);
        }
        Assert.Equal(TerminalSemanticPrompt.PromptContinuation, screen.GetViewportRow(0).SemanticPrompt);
        ITerminalPromptClickEncoderSource encoder = (ITerminalPromptClickEncoderSource)processor;
        Assert.False(encoder.TryEncodePromptClick(2, 0, out _));
        Assert.True(encoder.TryEncodePromptClick(2, 1, out byte[] sequence));
        Assert.Equal("\u001b[<0;3;1M", Encoding.ASCII.GetString(sequence));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineClicksReachInputRetainedAfterPromptScrollsOutOfHistory(bool native)
    {
        using IVtProcessor processor = CreateProcessor(native, new(8, 4, 0));
        processor.Process(Encoding.ASCII.GetBytes(Prompt + new string('x', 47)));
        ITerminalPromptClickEncoderSource encoder = (ITerminalPromptClickEncoderSource)processor;
        Assert.True(encoder.TryEncodePromptClick(0, 0, out byte[] sequence));
        Assert.Equal(Arrows(-25), Encoding.ASCII.GetString(sequence));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineClicksUseLiveInputAndCursorKeyModeDuringSynchronizedOutput(bool native)
    {
        TerminalScreen screen = new(20, 4);
        using IVtProcessor processor = CreateProcessor(native, screen);
        processor.Process(Encoding.ASCII.GetBytes(Prompt + "abc"));
        processor.Process("\u001b[?2026hdef\u001b[?1h"u8);
        Assert.Equal(0, screen.GetViewportRow(0)[5].Codepoint);
        ITerminalPromptClickEncoderSource encoder = (ITerminalPromptClickEncoderSource)processor;
        Assert.True(encoder.TryEncodePromptClick(2, 0, out byte[] application));
        Assert.Equal(Arrows(-6, application: true), Encoding.ASCII.GetString(application));
        processor.Process("\u001b[?1l"u8);
        Assert.True(encoder.TryEncodePromptClick(2, 0, out byte[] normal));
        Assert.Equal(Arrows(-6), Encoding.ASCII.GetString(normal));
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal('d', screen.GetViewportRow(0)[5].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineClickPolicySurvivesAlternateScreenAndClearsOnReset(bool native)
    {
        using IVtProcessor processor = CreateProcessor(native, new(20, 4));
        processor.Process(Encoding.ASCII.GetBytes(Prompt + "hello"));
        ITerminalPromptClickEncoderSource encoder = (ITerminalPromptClickEncoderSource)processor;
        processor.Process("\u001b[?1049h"u8);
        Assert.False(encoder.TryEncodePromptClick(2, 0, out byte[] alternate));
        Assert.Empty(alternate);
        processor.Process("\u001b[?1049l"u8);
        Assert.True(encoder.TryEncodePromptClick(2, 0, out byte[] primary));
        Assert.Equal(Arrows(-5), Encoding.ASCII.GetString(primary));
        processor.Process("\u001bc\u001b]133;A\a> \u001b]133;B\ahello"u8);
        Assert.False(encoder.TryEncodePromptClick(2, 0, out byte[] reset));
        Assert.Empty(reset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineClickAllocatesOnlyTheResultAndNoOpAllocatesNothing(bool native)
    {
        using IVtProcessor processor = CreateProcessor(native, new(20, 4));
        processor.Process(Encoding.ASCII.GetBytes(Prompt + "hello"));
        ITerminalPromptClickEncoderSource encoder = (ITerminalPromptClickEncoderSource)processor;
        for (int index = 0; index < 128; index++)
        {
            encoder.TryEncodePromptClick(2, 0, out _);
            encoder.TryEncodePromptClick(7, 0, out _);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        byte[] expected = new byte[15];
        long arrayBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(expected);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 128; index++)
        {
            encoder.TryEncodePromptClick(2, 0, out byte[] sequence);
            GC.KeepAlive(sequence);
        }
        Assert.Equal(arrayBytes * 128, GC.GetAllocatedBytesForCurrentThread() - before);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 128; index++)
            encoder.TryEncodePromptClick(7, 0, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static IVtProcessor CreateProcessor(bool native, TerminalScreen screen)
    {
        Assert.SkipWhen(native && !GhosttyVtProcessor.IsAvailable(), "Native unavailable.");
        return native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    }

    private static string Arrows(int movement, bool application = false) =>
        string.Concat(Enumerable.Repeat(
            $"\u001b{(application ? 'O' : '[')}{(movement > 0 ? 'C' : 'D')}", Math.Abs(movement)));
}
