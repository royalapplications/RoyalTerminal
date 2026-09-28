// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
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
    public async Task Headless_CanonicalPreviewUpdatesCompiledTooltipWithoutChangingLaunchIdentity(VtProcessorPreference preference)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable()) return;
        await WithHyperlinkControl(preference, async (control, window) =>
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://RoyalTerminal.Avalonia.App/"))
                { Source = new Uri("avares://RoyalTerminal.Avalonia.App/Styles/Hyperlinks.axaml") });
            using ControlledPathPreview source = new();
            HyperlinkHostProbe host = new();
            control.HyperlinkPathPreviewSource = source;
            control.HyperlinkHost = host;
            const string target = "file:///alias/notes.txt";
            Point point = await PutOscLink(control, window, target);
            Assert.Single(source.Requests);
            source.Complete(0, "/resolved/a\u202E.txt");
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            Assert.Equal(target, control.HoveredLinkUrl);
            Assert.Equal("/resolved/a\\u{202E}.txt", control.HoveredLinkDisplayText);
            Assert.Equal(control.HoveredLinkDisplayText, ToolTip.GetTip(control));
            RaiseModifiedLeftClick(control, window, point, KeyModifiers.Control);
            Assert.Equal(target, Assert.Single(host.Requests).Target);
            Assert.Empty(control.ActivatedLinks);
        });
    }

    [AvaloniaTheory]
    [InlineData(VtProcessorPreference.Managed)]
    [InlineData(VtProcessorPreference.Native)]
    public async Task Headless_OutputUnderStationaryPointerInvalidatesPreview(VtProcessorPreference preference)
    {
        if (preference == VtProcessorPreference.Native && !GhosttyVtProcessor.IsAvailable()) return;
        await WithHyperlinkControl(preference, async (control, window) =>
        {
            using ControlledPathPreview source = new();
            control.HyperlinkPathPreviewSource = source;
            await PutOscLink(control, window, "file:///first.txt");
            control.WriteOutput(Encoding.UTF8.GetBytes("\r\u001b]8;;file:///second.txt\u001b\\label\u001b]8;;\u001b\\"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(source.Requests[0].Token.IsCancellationRequested);
            Assert.Single(source.Requests);
            source.Complete(0, "/stale");
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            Assert.Equal("file:///second.txt", source.Requests[1].Target);
            Assert.NotEqual("/stale", control.HoveredLinkDisplayText);
            source.Complete(1, "/second.txt");
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            Assert.Equal("/second.txt", control.HoveredLinkDisplayText);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Headless_StopOrDetachClearsPreviewAndRejectsLateCompletion(bool detach)
    {
        await WithHyperlinkControl(VtProcessorPreference.Managed, async (control, window) =>
        {
            using ControlledPathPreview source = new();
            control.HyperlinkPathPreviewSource = source;
            Point point = await PutOscLink(control, window, "file:///alias.txt");
            if (detach) window.Content = null;
            else control.StopPty();
            Assert.True(source.Requests[0].Token.IsCancellationRequested);
            Assert.Null(control.HoveredLinkDisplayText);
            source.Complete(0, "/stale");
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            Assert.Null(control.HoveredLinkDisplayText);
            if (detach) { window.Content = control; await StabilizeWindowAsync(window, control); }
            RaisePointerMove(control, window, point);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, source.Requests.Count);
            source.Complete(1, "/current.txt");
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            Assert.Equal("/current.txt", control.HoveredLinkDisplayText);
        });
    }

    [AvaloniaFact]
    public async Task Headless_DisablingOsc8CancelsPreviewAndClearsBindableText()
    {
        await WithHyperlinkControl(VtProcessorPreference.Managed, async (control, window) =>
        {
            using ControlledPathPreview source = new();
            control.HyperlinkPathPreviewSource = source;
            await PutOscLink(control, window, "file:///alias.txt");
            control.EnableOsc8Hyperlinks = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(source.Requests[0].Token.IsCancellationRequested);
            Assert.Null(control.HoveredLinkDisplayText);
            source.Complete(0, "/stale");
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            Assert.Null(control.HoveredLinkDisplayText);
        });
    }

    private sealed class ControlledPathPreview : ITerminalHyperlinkPathPreviewSource, IDisposable
    {
        internal List<PathPreviewRequest> Requests { get; } = [];
        public ValueTask<string> GetPathPreviewAsync(string target, CancellationToken token)
        {
            PathPreviewRequest request = new(target, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
            Requests.Add(request);
            return new(request.Completion.Task);
        }
        internal void Complete(int index, string display) => Requests[index].Completion.SetResult(display);
        public void Dispose()
        {
            foreach (PathPreviewRequest request in Requests) request.Completion.TrySetCanceled();
        }
    }

    private sealed record PathPreviewRequest(string Target, CancellationToken Token, TaskCompletionSource<string> Completion);

    [AvaloniaFact]
    public async Task Headless_CoalescedReturnToSameTargetDoesNotReviveOldPreview()
    {
        await WithHyperlinkControl(VtProcessorPreference.Managed, async (control, window) =>
        {
            using ControlledPathPreview source = new();
            control.HyperlinkPathPreviewSource = source;
            await PutOscLink(control, window, "file:///first.txt");
            const string second = "\r\u001b]8;;file:///second.txt\u001b\\label\u001b]8;;\u001b\\";
            const string first = "\r\u001b]8;;file:///first.txt\u001b\\label\u001b]8;;\u001b\\";
            control.WriteOutput(Encoding.UTF8.GetBytes(second));
            control.WriteOutput(Encoding.UTF8.GetBytes(first));
            Dispatcher.UIThread.RunJobs();
            Assert.True(source.Requests[0].Token.IsCancellationRequested);
            source.Complete(0, "/stale");
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            Assert.Equal(2, source.Requests.Count);
            Assert.NotEqual("/stale", control.HoveredLinkDisplayText);
            source.Complete(1, "/current");
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            Assert.Equal("/current", control.HoveredLinkDisplayText);
        });
    }
}
