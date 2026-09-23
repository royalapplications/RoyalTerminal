// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalPromptPolicyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\u001b]133;A;redraw=last;cl=line\a")]
    [InlineData("\u001b]133;A;redraw=0;click_events=1\a\u001b]133;B\a")]
    [InlineData("\u001b]133;N;redraw=1;click_events=2;cl=w\a")]
    [InlineData("\u001b]133;A;click_events=0;cl=m\a")]
    [InlineData("\u001b]133;A;cl=v\a\u001b]133;A;cl=invalid\a")]
    [InlineData("\u001b]133;A;cl=w\a\u001b]133;P;cl=line;redraw=0\a")]
    [InlineData("\u001b]133;A;cl=line\a\u001b]133;A;click_events=bad;click_events=1\a")]
    [InlineData("\u001b]133;A;redraw=bad;redraw=last;cl=m\a")]
    [InlineData("\u001b]133;I\aabc\r\n")]
    [InlineData("\u001b]133;A;redraw=last;cl=line\a\u001b[?47h")]
    [InlineData("\u001b]133;A;cl=line\a\u001b[?47h\u001b]133;P\a\u001b[?47l")]
    [InlineData("\u001b]133;A;cl=line\a\u001b[?1049h\u001b]133;A;cl=w\a\u001b[?1049l")]
    [InlineData("\u001b]133;A;redraw=last;cl=line\a\u001bc")]
    public void LivePolicyMatchesNativeAtEverySplit(string input)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 4));
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split++)
        {
            using BasicVtProcessor managed = new(new TerminalScreen(8, 4));
            managed.Process(bytes.AsSpan(0, split));
            managed.Process(bytes.AsSpan(split));
            Assert.Equal(native.PromptState, managed.PromptState);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("last")]
    public void ResizeClearsOnlyTheShellRedrawArea(string redraw)
    {
        if (!Available()) return;
        TerminalScreen expected = new(8, 6), actual = new(8, 6);
        using GhosttyVtProcessor native = new(expected);
        using BasicVtProcessor managed = new(actual);
        byte[] bytes = Encoding.UTF8.GetBytes($"out\r\n\u001b]133;A;redraw={redraw}\aprompt\r\nnext\u001b]133;B\acmd");
        native.Process(bytes);
        managed.Process(bytes);
        expected.Resize(12, 6, reflowOnResize: false);
        native.NotifyResize(12, 6, 96, 96);
        managed.ResizeScreen(12, 6, 96, 96, reflowOnResize: true);
        Assert.Equal(native.CursorCol, managed.CursorCol);
        Assert.Equal(native.CursorRow, managed.CursorRow);
        Assert.Equal(native.PromptState, managed.PromptState);
        for (int row = 0; row < 6; row++)
        {
            TerminalRow a = actual.GetViewportRow(row), e = expected.GetViewportRow(row);
            Assert.Equal(e.SemanticPrompt, a.SemanticPrompt);
            for (int column = 0; column < 12; column++)
                Assert.Equal((e[column].Codepoint, e[column].SemanticContent), (a[column].Codepoint, a[column].SemanticContent));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingWrapPublishesNextRowContinuationWithoutNewText(bool native)
    {
        if (native && !Available()) return;
        TerminalScreen screen = new(4, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("abcde"u8);
        Assert.True(screen.GetViewportRow(1).IsWrapContinuation);
        processor.Process("\u001b[H\u001b[K"u8);
        Assert.False(screen.GetViewportRow(1).IsWrapContinuation);
        Assert.Equal('e', screen.GetViewportRow(1)[0].Codepoint);
    }

    [Fact]
    public void WrapBitsSurviveCopyOnWriteAndResetWithRecycling()
    {
        TerminalScreen screen = new(4, 3);
        using BasicVtProcessor processor = new(screen);
        processor.Process("abcde"u8);
        screen.GetRow(1).SemanticPrompt = TerminalSemanticPrompt.PromptContinuation;
        TerminalScreen copy = screen.CreateStateCopy();
        screen.GetRow(1).Clear();
        Assert.True(copy.GetRow(1).IsWrapContinuation);
        Assert.Equal(TerminalSemanticPrompt.PromptContinuation, copy.GetRow(1).SemanticPrompt);
        Assert.Equal('e', copy.GetRow(1)[0].Codepoint);
        Assert.False(screen.GetRow(1).IsWrapContinuation);
        screen.AdoptStateFrom(copy);
        Assert.True(screen.GetRow(1).IsWrapContinuation);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native prompt policy differential available: {available}");
        return available;
    }
}
