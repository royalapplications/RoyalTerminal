// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalHyperlinkPreviewCoordinatorTests
{
    [AvaloniaFact]
    public async Task RapidTargetChangesKeepOneActiveAndOnlyTheLatestPendingTarget()
    {
        List<string?> displays = [];
        TerminalHyperlinkPreviewCoordinator coordinator = new((_, text) => displays.Add(text));
        Source source = new();
        coordinator.Update("file:///a", source);
        coordinator.Update("file:///b", source);
        coordinator.Update("file:///c", source);
        Assert.Single(source.Requests);
        Assert.True(source.Requests[0].Token.IsCancellationRequested);
        source.Complete(0, "/stale/a");
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
        Assert.Equal(2, source.Requests.Count);
        Assert.Equal("file:///c", source.Requests[1].Target);
        Assert.DoesNotContain("/stale/a", displays);
        source.Complete(1, "/resolved/c\n");
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
        Assert.Equal("/resolved/c\\u{A}", displays[^1]);
        coordinator.Cancel();
    }

    [AvaloniaFact]
    public async Task SourceReplacementCannotPublishOldResultOrRunConcurrently()
    {
        string? display = null;
        TerminalHyperlinkPreviewCoordinator coordinator = new((_, text) => display = text);
        Source first = new(), second = new();
        coordinator.Update("file:///alias", first);
        coordinator.Update("file:///alias", second);
        Assert.Empty(second.Requests);
        first.Complete(0, "/old");
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
        Assert.Equal("file:///alias", display);
        Assert.Single(second.Requests);
        second.Complete(0, "/new");
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
        Assert.Equal("/new", display);
        coordinator.Cancel();
    }

    [AvaloniaFact]
    public async Task CancellationAndClearingRejectLateNonCooperatingResults()
    {
        string? display = null;
        TerminalHyperlinkPreviewCoordinator coordinator = new((_, text) => display = text);
        Source source = new();
        coordinator.Update("file:///alias", source);
        coordinator.Update("file:///queued", source);
        coordinator.Cancel();
        Assert.Null(display);
        source.Complete(0, "/stale");
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
        Assert.Null(display);
        Assert.Single(source.Requests);
    }

    [AvaloniaTheory]
    [InlineData("https://example.com/a//b", "https://example.com/a//b")]
    [InlineData("https://example.com/a\u202Eb", "https://example.com/a\\u{202E}b")]
    [InlineData("custom:action", "custom:action")]
    [InlineData("mailto:user@example.com", "mailto:user@example.com")]
    public void NonPathTargetsNeverStartFilesystemPreview(string target, string expected)
    {
        string? display = null;
        TerminalHyperlinkPreviewCoordinator coordinator = new((_, text) => display = text);
        Source source = new();
        coordinator.Update(target, source);
        Assert.Equal(expected, display);
        Assert.Empty(source.Requests);
    }

    [AvaloniaFact]
    public void UnchangedTargetsReusePublicationWithoutAllocating()
    {
        int publications = 0;
        TerminalHyperlinkPreviewCoordinator coordinator = new((_, _) => publications++);
        Source source = new();
        coordinator.Update("https://example.com", source);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) coordinator.Update("https://example.com", source);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(1, publications);
    }

    [AvaloniaFact]
    public async Task ThrowingHostRetainsEscapedFallbackAndReleasesSlot()
    {
        string? display = null;
        TerminalHyperlinkPreviewCoordinator coordinator = new((_, text) => display = text);
        Source source = new();
        coordinator.Update("file:///alias", source);
        source.Requests[0].Completion.SetException(new IOException("Unavailable path"));
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
        Assert.Equal("file:///alias", display);
        coordinator.Update("file:///next", source);
        Assert.Equal(2, source.Requests.Count);
        source.Complete(1, "/next");
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
        Assert.Equal("/next", display);
        coordinator.Cancel();
    }

    private sealed class Source : ITerminalHyperlinkPathPreviewSource
    {
        internal List<Request> Requests { get; } = [];
        public ValueTask<string> GetPathPreviewAsync(string target, CancellationToken token)
        {
            Request request = new(target, token, new(TaskCreationOptions.RunContinuationsAsynchronously));
            Requests.Add(request);
            return new(request.Completion.Task); // Intentionally ignores cancellation.
        }
        internal void Complete(int index, string result) => Requests[index].Completion.SetResult(result);
    }

    private sealed record Request(string Target, CancellationToken Token, TaskCompletionSource<string> Completion);
}
