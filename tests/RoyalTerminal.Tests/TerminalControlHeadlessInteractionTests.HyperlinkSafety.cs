// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalControlHeadlessInteractionTests
{
    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed, "https://example.com/allowed", TerminalHyperlinkDisposition.Allow)]
    [InlineData(VtProcessorPreference.Managed, "vscode://file/tmp/example.cs", TerminalHyperlinkDisposition.Confirm)]
    [InlineData(VtProcessorPreference.Managed, "file:///tmp/payload.command", TerminalHyperlinkDisposition.InspectFile)]
    [InlineData(VtProcessorPreference.Managed, "https://example.com/a\u202Eb", TerminalHyperlinkDisposition.Deny)]
    [InlineData(VtProcessorPreference.Native, "https://example.com/allowed", TerminalHyperlinkDisposition.Allow)]
    [InlineData(VtProcessorPreference.Native, "vscode://file/tmp/example.cs", TerminalHyperlinkDisposition.Confirm)]
    [InlineData(VtProcessorPreference.Native, "file:///tmp/payload.command", TerminalHyperlinkDisposition.InspectFile)]
    [InlineData(VtProcessorPreference.Native, "https://example.com/a\u202Eb", TerminalHyperlinkDisposition.Deny)]
    public async Task Headless_Osc8TargetsPassThroughSharedSafetyBeforeAnyLauncher(VtProcessorPreference preference,
        string target, TerminalHyperlinkDisposition disposition)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable()) return;
        await WithHyperlinkControl(preference, async (control, window) =>
        {
            HyperlinkHostProbe host = new();
            control.HyperlinkHost = host;
            Point point = await PutOscLink(control, window, target);
            Assert.Equal(target, control.HoveredLinkUrl);
            Assert.Equal(TerminalHyperlinkSafety.SanitizeDisplay(target), control.HoveredLinkDisplayText);
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            if (disposition == TerminalHyperlinkDisposition.Allow)
            {
                Assert.Single(control.ActivatedLinks);
                Assert.Empty(host.Requests);
            }
            else
            {
                Assert.Empty(control.ActivatedLinks);
                TerminalHyperlinkRequest request = Assert.Single(host.Requests);
                Assert.Equal(disposition, request.Disposition);
                Assert.Equal(target, request.Target);
            }
        });
    }

    [AvaloniaFact]
    public async Task Headless_MissingOrThrowingHyperlinkHostNeverFallsBackToGenericLaunch()
    {
        await WithHyperlinkControl(VtProcessorPreference.Managed, async (control, window) =>
        {
            Point point = await PutOscLink(control, window, "custom:execute");
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Assert.Empty(control.ActivatedLinks);
            HyperlinkHostProbe host = new() { Throw = true };
            control.HyperlinkHost = host;
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(host.Requests);
            Assert.Empty(control.ActivatedLinks);
        });
    }

    [AvaloniaFact]
    public async Task Headless_Osc8CanBeDisabledWithoutDisablingPlainTextLinks()
    {
        await WithHyperlinkControl(VtProcessorPreference.Managed, async (control, window) =>
        {
            Point point = await PutOscLink(control, window, "https://example.com/hidden");
            Assert.True(control.EnableOsc8Hyperlinks);
            control.EnableOsc8Hyperlinks = false;
            Assert.Null(control.HoveredLinkUrl);
            Assert.Null(control.HoveredLinkDisplayText);
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Assert.Empty(control.ActivatedLinks);
            control.ClearValue(TerminalControl.EnableOsc8HyperlinksProperty);
            Assert.True(control.EnableOsc8Hyperlinks);
            RaisePointerMove(control, window, point);
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Assert.Single(control.ActivatedLinks);
            control.ActivatedLinks.Clear();
            control.EnableOsc8Hyperlinks = false;
            control.WriteOutput("\r\nhttps://example.com/plain"u8.ToArray());
            Dispatcher.UIThread.RunJobs();
            Point plain = await GetCellInteractionPointAsync(control, window, 5, 1);
            RaisePointerMove(control, window, plain);
            RaiseModifiedLeftClick(control, window, plain, KeyModifiers.Control);
            Assert.Equal("https://example.com/plain", Assert.Single(control.ActivatedLinks).AbsoluteUri);
        });
    }

    [AvaloniaFact]
    public async Task Headless_HyperlinkRequestsAreBoundedAndCanceledOnHostReplacementDetachAndStop()
    {
        await WithHyperlinkControl(VtProcessorPreference.Managed, async (control, window) =>
        {
            HyperlinkHostProbe first = new() { Wait = true }, second = new() { Wait = true };
            control.HyperlinkHost = first;
            Point point = await PutOscLink(control, window, "custom:confirm");
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Assert.Single(first.Requests);
            control.HyperlinkHost = second;
            Assert.True(first.Token.IsCancellationRequested);
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Assert.Single(second.Requests);
            window.Content = null;
            Assert.True(second.Token.IsCancellationRequested);
            Dispatcher.UIThread.RunJobs();
            window.Content = control;
            await StabilizeWindowAsync(window, control);
            point = await GetCellInteractionPointAsync(control, window, 1, 0);
            RaisePointerMove(control, window, point);
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Assert.Equal(2, second.Requests.Count);
            control.StopPty();
            Assert.True(second.Token.IsCancellationRequested);
            Assert.Empty(control.ActivatedLinks);
        });
    }

    private async Task WithHyperlinkControl(VtProcessorPreference preference,
        Func<LinkActivationProbeTerminalControl, Window, Task> action)
    {
        LinkActivationProbeTerminalControl control = new() { VtProcessorPreference = preference, Width = 640, Height = 400 };
        Window window = new() { Width = 640, Height = 400, Content = control };
        window.Show();
        try { await StabilizeWindowAsync(window, control); await action(control, window); }
        finally { await CleanupWindowAsync(window, control.StopPty); }
    }

    private async Task<Point> PutOscLink(TerminalControl control, Window window, string target)
    {
        control.WriteOutput(Encoding.UTF8.GetBytes($"\u001b]8;;{target}\u001b\\label\u001b]8;;\u001b\\"));
        Dispatcher.UIThread.RunJobs();
        Point point = await GetCellInteractionPointAsync(control, window, 1, 0);
        RaisePointerMove(control, window, point);
        return point;
    }

    private sealed class HyperlinkHostProbe : ITerminalHyperlinkHost
    {
        private readonly TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<TerminalHyperlinkRequest> Requests { get; } = [];
        internal bool Throw { get; init; }
        internal bool Wait { get; init; }
        internal CancellationToken Token { get; private set; }

        public ValueTask HandleAsync(TerminalHyperlinkRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Token = cancellationToken;
            if (Throw) throw new InvalidOperationException("Host failed before showing UI");
            return Wait ? new(_pending.Task.WaitAsync(cancellationToken)) : ValueTask.CompletedTask;
        }
    }
}
