// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.App.Services;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalControlHeadlessInteractionTests
{
    [AvaloniaFact]
    public async Task Headless_WindowResize_RequiresOptInAndCoalescesOnUiThread()
    {
        TerminalControl control = await CreateTextSelectionControlAsync(string.Empty);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        ResizeRecordingHost host = new();
        try
        {
            control.WindowResizeHost = host;
            Assert.False(control.AllowVtWindowResize);
            control.WriteOutput("\u001b[8;24;80t"u8);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Requests);
            control.AllowVtWindowResize = true;
            control.WriteOutput("\u001b[8;24;80t\u001b[8;30;0t\u001b[8;0;100t"u8);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new TerminalWindowResizeRequest(100, 30), Assert.Single(host.Requests));
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaFact]
    public async Task Headless_WindowResize_RevocationReplacementAndSessionStopDropQueuedWork()
    {
        TerminalControl control = await CreateTextSelectionControlAsync(string.Empty);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        ResizeRecordingHost first = new(), second = new();
        try
        {
            control.WindowResizeHost = first;
            control.AllowVtWindowResize = true;
            control.WriteOutput("\u001b[8;24;80t"u8);
            control.AllowVtWindowResize = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(first.Requests);
            control.AllowVtWindowResize = true;
            control.WriteOutput("\u001b[8;24;80t"u8);
            control.WindowResizeHost = second;
            control.WriteOutput("\u001b[8;30;0t"u8);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(first.Requests);
            Assert.Equal(new TerminalWindowResizeRequest(0, 30), Assert.Single(second.Requests));
            control.WriteOutput("\u001b[8;50;120t"u8);
            control.StopPty();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(second.Requests);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaFact]
    public async Task Headless_WindowResize_DetachDropsQueuedRequests()
    {
        TerminalControl control = await CreateTextSelectionControlAsync(string.Empty);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        ResizeRecordingHost host = new();
        try
        {
            control.WindowResizeHost = host; control.AllowVtWindowResize = true;
            control.WriteOutput("\u001b[8;30;100t"u8);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Requests);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaFact]
    public async Task Headless_DesktopWindowResize_RejectsLayoutsAndHonorsZeroDimension()
    {
        TerminalControl control = await CreateTextSelectionControlAsync(string.Empty);
        Window window = Assert.IsType<Window>(TopLevel.GetTopLevel(control));
        bool eligible = false;
        DesktopTerminalWindowResizeHost host = new(window, control, () => eligible);
        try
        {
            double oldWidth = window.Width, oldHeight = window.Height;
            host.RequestResize(new(100, 40));
            Assert.Equal(oldWidth, window.Width);
            control.AllowVtWindowResize = true;
            host.RequestResize(new(100, 40));
            Assert.Equal(oldWidth, window.Width); // Multi-tab/pane policy refuses it.
            eligible = true; window.CanResize = false;
            host.RequestResize(new(100, 40));
            Assert.Equal(oldWidth, window.Width);
            window.CanResize = true; window.WindowState = WindowState.Maximized;
            host.RequestResize(new(100, 40));
            Assert.Equal(oldWidth, window.Width);
            window.WindowState = WindowState.Normal;
            host.RequestResize(new(100, 0));
            Assert.Equal(oldHeight, window.Height);
            Assert.True(window.Width >= 100 * control.Renderer!.CellWidth);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    private sealed class ResizeRecordingHost : ITerminalWindowResizeHost
    {
        public List<TerminalWindowResizeRequest> Requests { get; } = [];
        public void RequestResize(TerminalWindowResizeRequest request)
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            Requests.Add(request);
        }
    }
}
