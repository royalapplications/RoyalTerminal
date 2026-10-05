// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalControlHeadlessInteractionTests
{
    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed, false, 2)]
    [InlineData(VtProcessorPreference.Managed, true, 2)]
    [InlineData(VtProcessorPreference.Native, false, 2)]
    [InlineData(VtProcessorPreference.Native, true, 2)]
    [InlineData(VtProcessorPreference.Managed, false, 3)]
    [InlineData(VtProcessorPreference.Managed, true, 3)]
    [InlineData(VtProcessorPreference.Native, false, 3)]
    [InlineData(VtProcessorPreference.Native, true, 3)]
    public async Task Headless_ExpandedHistory_SelectsAndCopiesBeyondBothViewportEdges(
        VtProcessorPreference preference, bool atTop, int clickCount)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable())
            Assert.Skip("Native unavailable; rebuild required before final validation.");
        TerminalControl control = await CreateTextSelectionControlAsync(string.Empty, preference);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        try
        {
            Assert.Equal(preference == VtProcessorPreference.Native, control.IsUsingNativeVtProcessor);
            int rows = control.Rows;
            int columns = control.Columns;
            string word = new('w', columns * (rows + 3));
            control.WriteOutput(Encoding.ASCII.GetBytes(word));
            if (atTop) control.ScrollByRows(-rows - 3);
            else control.ScrollToBottom();
            Dispatcher.UIThread.RunJobs();
            Point point = await GetCellInteractionPointAsync(control, window, 2, 1);
            RaiseMousePressReleaseSequence(control, window, point, clickCount: clickCount);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((0, atTop ? 0 : -3), control.Renderer!.SelectionStart!.Value);
            Assert.Equal((columns, atTop ? rows + 2 : rows - 1), control.Renderer.SelectionEnd!.Value);
            Assert.Equal(rows, control.Renderer.GetSelectionSpans().Length);
            Assert.True(control.TryReadExpandedSelection(out string? selected));
            Assert.Equal(word, selected);
            await control.CopySelectionAsync();
            Assert.Equal(word, await window.Clipboard!.TryGetTextAsync());

            // Highlight spans reclip on scrolling; copy must retain the same
            // absolute word endpoints rather than exporting only visible rows.
            if (atTop) control.ScrollToBottom();
            else control.ScrollByRows(-rows - 3);
            Dispatcher.UIThread.RunJobs();
            await control.CopySelectionAsync();
            Assert.Equal(word, await window.Clipboard!.TryGetTextAsync());
            control.ClearSelection();
            Assert.False(control.TryReadExpandedSelection(out _));
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed, false)]
    [InlineData(VtProcessorPreference.Native, false)]
    [InlineData(VtProcessorPreference.Managed, true)]
    [InlineData(VtProcessorPreference.Native, true)]
    public async Task Headless_TripleClick_TrimsWhitespaceAndHonorsPromptBoundaries(
        VtProcessorPreference preference, bool semantic)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native unavailable.");
        string input = semantic ? "\u001b]133;A\a$ \u001b]133;B\acmd\u001b]133;C\a out" : "  one two  ";
        TerminalControl control = await CreateTextSelectionControlAsync(input, preference);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        try
        {
            Assert.Equal(preference == VtProcessorPreference.Native, control.IsUsingNativeVtProcessor);
            Point point = await GetCellInteractionPointAsync(control, window, 3, 0);
            RaiseMousePressReleaseSequence(control, window, point, clickCount: 3);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((2, 0), control.Renderer!.SelectionStart!.Value);
            Assert.Equal((semantic ? 5 : 9, 0), control.Renderer.SelectionEnd!.Value);
            await control.CopySelectionAsync();
            Assert.Equal(semantic ? "cmd" : "one two", await window.Clipboard!.TryGetTextAsync());
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }
}
