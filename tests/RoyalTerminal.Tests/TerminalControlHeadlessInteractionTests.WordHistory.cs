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
    [InlineData(VtProcessorPreference.Managed, false)]
    [InlineData(VtProcessorPreference.Managed, true)]
    [InlineData(VtProcessorPreference.Native, false)]
    [InlineData(VtProcessorPreference.Native, true)]
    public async Task Headless_WordHistory_SelectsAndCopiesBeyondBothViewportEdges(
        VtProcessorPreference preference, bool atTop)
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
            RaiseMousePressReleaseSequence(control, window, point, clickCount: 2);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((0, atTop ? 0 : -3), control.Renderer!.SelectionStart!.Value);
            Assert.Equal((columns, atTop ? rows + 2 : rows - 1), control.Renderer.SelectionEnd!.Value);
            Assert.Equal(rows, control.Renderer.GetSelectionSpans().Length);
            Assert.True(control.TryReadExpandedWordSelection(out string? selected));
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
            Assert.False(control.TryReadExpandedWordSelection(out _));
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }
}
