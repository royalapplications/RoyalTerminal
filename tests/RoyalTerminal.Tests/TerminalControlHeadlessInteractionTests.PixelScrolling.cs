// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalControlHeadlessInteractionTests
{
    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed, 2)]
    [InlineData(VtProcessorPreference.Managed, 3)]
    [InlineData(VtProcessorPreference.Native, 2)]
    [InlineData(VtProcessorPreference.Native, 3)]
    public async Task Headless_PixelScroll_SelectsPartialBottomRow(VtProcessorPreference preference, int clicks)
    {
        TerminalControl control = await PixelScrollControl(preference);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        try
        {
            SetPixelPosition(control, 1.5);
            Point point = await GetCellInteractionPointAsync(control, window, 2, control.Rows - 1);
            point = new(point.X, point.Y + control.Renderer!.CellHeight * 0.3);
            RaiseMousePressReleaseSequence(control, window, point, clickCount: clicks);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(control.Renderer.GetSelectionSpans().ToArray(), span => span.Row == control.Rows);
            await control.CopySelectionAsync();
            Assert.Equal($"line{control.Rows + 1:D3}", await window.Clipboard!.TryGetTextAsync());
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed, false)]
    [InlineData(VtProcessorPreference.Managed, true)]
    [InlineData(VtProcessorPreference.Native, false)]
    [InlineData(VtProcessorPreference.Native, true)]
    public async Task Headless_PixelScroll_CharacterAndRectangleCopyIncludeCapturedRow(VtProcessorPreference preference, bool rectangle)
    {
        TerminalControl control = await PixelScrollControl(preference);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        try
        {
            SetPixelPosition(control, 1.5);
            control.ClearSelection();
            control.Renderer!.SelectionStart = (0, control.Rows);
            control.Renderer.SelectionEnd = (7, control.Rows);
            control.Renderer.SelectionIsRectangle = rectangle;
            await control.CopySelectionAsync();
            Assert.Equal($"line{control.Rows + 1:D3}", await window.Clipboard!.TryGetTextAsync());
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public async Task Headless_PixelScroll_ImeRectangleTracksPhaseAndDisableSnaps(VtProcessorPreference preference)
    {
        TerminalControl control = await PixelScrollControl(preference);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        try
        {
            SetPixelPosition(control, 1);
            TextInputMethodClientRequestedEventArgs args = new() { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
            control.RaiseEvent(args);
            Assert.NotNull(args.Client);
            double before = args.Client.CursorRectangle.Y;
            SetPixelPosition(control, 1.5);
            Assert.Equal(before - control.Renderer!.CellHeight * 0.5, args.Client.CursorRectangle.Y, precision: 6);
            Assert.Equal(0.5, control.Screen!.RenderScrollFraction, precision: 6);
            control.PixelScrollingEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, control.Screen.RenderScrollFraction);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public async Task Headless_PixelScroll_HyperlinksAndFractionalWheelUsePresentedRows(VtProcessorPreference preference)
    {
        TerminalControl control = await PixelScrollControl(preference, hyperlinks: true);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        try
        {
            SetPixelPosition(control, 1.5);
            Point point = await GetCellInteractionPointAsync(control, window, 2, control.Rows - 1);
            point = new(point.X, point.Y + control.Renderer!.CellHeight * 0.3);
            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"https://example.test/{control.Rows + 1}", control.HoveredLinkUrl);
            double nextPixels = control.ScrollData!.Offset + 0.3 * control.ScrollData.CellHeight;
            TerminalViewportScrollPosition expected = TerminalViewportScrollPosition.FromPixels(
                nextPixels, control.ScrollData.MaxOffset, MaxTopRow(control));
            window.MouseWheel(point, new Vector(0, -0.1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected.FractionalRow, control.Screen!.RenderScrollFraction, precision: 6);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    private static async Task<TerminalControl> PixelScrollControl(VtProcessorPreference preference, bool hyperlinks = false)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable())
            Assert.Skip("Native unavailable; rebuild pinned native library before final validation.");
        TerminalControl control = await CreateTextSelectionControlAsync(string.Empty, preference);
        Assert.Equal(preference == VtProcessorPreference.Native, control.IsUsingNativeVtProcessor);
        Assert.False(control.PixelScrollingEnabled);
        StringBuilder text = new();
        for (int row = 0; row < control.Rows + 3; row++)
        {
            if (row != 0) text.Append("\r\n");
            if (hyperlinks) text.Append($"\u001b]8;;https://example.test/{row}\u001b\\");
            text.Append($"line{row:D3}");
            if (hyperlinks) text.Append("\u001b]8;;\u001b\\");
        }
        control.WriteOutput(Encoding.UTF8.GetBytes(text.ToString()));
        Dispatcher.UIThread.RunJobs();
        control.PixelScrollingEnabled = true;
        return control;
    }

    private static ulong MaxTopRow(TerminalControl control)
        => control.ActiveVtProcessor is ITerminalViewportScrollSource native
            ? native.ViewportScrollState.MaxOffsetRows : (ulong)control.Screen!.MaxScrollOffset;

    private static void SetPixelPosition(TerminalControl control, double rows)
    {
        ((IScrollable)control).Offset = new Vector(0, rows / MaxTopRow(control) * control.ScrollData!.MaxOffset);
        Dispatcher.UIThread.RunJobs();
    }
}
