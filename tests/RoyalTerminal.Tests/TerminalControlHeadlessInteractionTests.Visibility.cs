// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalControlHeadlessInteractionTests
{
    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public async Task Headless_VisibilityReportsTrackHostNotFocusAndReleaseOldAncestors(VtProcessorPreference preference)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native unavailable.");
        RecordingTransport transport = new();
        PasswordStateProcessorFactory factory = new();
        TerminalControl control = CreateControlWithTransport(transport, factory, preference);
        Border original = new() { Child = control };
        Border replacement = new();
        TextBox sibling = new();
        Window window = new() { Width = 640, Height = 400,
            Content = new StackPanel { Children = { original, replacement, sibling } } };
        control.WriteOutput("unattached"u8);
        Assert.False(((ITerminalVisibilityState)factory.Processor!).PotentiallyVisible);
        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\u001b[?2033h"u8);
            Expect(true);
            control.Focus();
            sibling.Focus();
            Assert.Equal(0, transport.GetInputCount());
            original.IsVisible = false;
            Expect(false);
            window.WindowState = WindowState.Minimized;
            original.IsVisible = true;
            Assert.Equal(0, transport.GetInputCount());
            window.WindowState = WindowState.Normal;
            Expect(true);
            original.Opacity = 0;
            Expect(false);
            original.Opacity = 0.5;
            Expect(true);
            window.Hide();
            Expect(false);
            window.Show();
            Expect(true);
            original.Child = null;
            Expect(false);
            original.IsVisible = false;
            Assert.Equal(0, transport.GetInputCount());
            replacement.Child = control;
            Expect(true);
            control.IsVisible = false;
            Expect(false);
            control.WriteOutput("\u001bc\u001b[?998n\u001b[?2033h"u8);
            Assert.Equal(2, transport.GetInputCount());
            Assert.All(transport.Inputs, bytes => Assert.Equal("\u001b[?999;2n", Encoding.ASCII.GetString(bytes)));
            transport.ClearInputs();
            control.StopPty();
            control.IsVisible = true;
            control.IsVisible = false;
            Assert.Equal(0, transport.GetInputCount());
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\u001b[?2033h"u8);
            Expect(false);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }

        void Expect(bool visible)
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(visible, ((ITerminalVisibilityState)factory.Processor!).PotentiallyVisible);
            Assert.Equal(visible ? "\u001b[?999;1n" : "\u001b[?999;2n", Encoding.ASCII.GetString(Assert.Single(transport.Inputs)));
            transport.ClearInputs();
        }
    }

    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public async Task Headless_HiddenStartupAndProcessorReplacementUseCurrentVisibility(VtProcessorPreference preference)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable()) Assert.Skip("Native unavailable.");
        RecordingTransport transport = new();
        PasswordStateProcessorFactory factory = new();
        TerminalControl control = CreateControlWithTransport(transport, factory, preference);
        Border parent = new() { Child = control, IsVisible = false };
        Window window = new() { Width = 640, Height = 400, Content = parent };
        window.Show();
        try
        {
            control.WriteOutput("initialize"u8);
            IVtProcessor first = factory.Processor!;
            Assert.False(((ITerminalVisibilityState)first).PotentiallyVisible);
            control.ScrollbackLimit++;
            Assert.NotSame(first, factory.Processor);
            Assert.False(((ITerminalVisibilityState)factory.Processor!).PotentiallyVisible);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\u001b[?2033h"u8);
            Assert.Equal("\u001b[?999;2n", Encoding.ASCII.GetString(Assert.Single(transport.Inputs)));
            transport.ClearInputs();
            parent.IsVisible = true;
            Assert.Equal("\u001b[?999;1n", Encoding.ASCII.GetString(Assert.Single(transport.Inputs)));
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    [AvaloniaFact]
    public async Task Headless_OptionalEndpointVisibilityIsInitializedAndDetachedWithoutFocusInference()
    {
        TerminalControl control = CreateControlWithTransport(new RecordingTransport());
        VisibilityEndpoint first = new(), second = new();
        control.AttachEndpoint(first);
        Assert.Equal([false], first.Visibility);
        Window window = new() { Width = 640, Height = 400, Content = control };
        window.Show();
        try
        {
            await StabilizeWindowAsync(window, control);
            Assert.Equal([false, true], first.Visibility);
            control.AttachEndpoint(second);
            Assert.Equal([false, true, false], first.Visibility);
            Assert.Equal([true], second.Visibility);
            control.IsVisible = false;
            Assert.Equal([true, false], second.Visibility);
            control.DetachEndpoint();
            int count = second.Visibility.Count;
            control.IsVisible = true;
            Assert.Equal(count, second.Visibility.Count);
        }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    private sealed class VisibilityEndpoint : ITerminalEndpoint, ITerminalVisibilitySink
    {
        public List<bool> Visibility { get; } = [];
        public void SetVisibility(bool potentiallyVisible) => Visibility.Add(potentiallyVisible);
        public void SendText(ReadOnlySpan<byte> utf8) { }
        public void SetFocus(bool focused) { }
        public void SetSize(int widthPx, int heightPx) { }
    }
}
