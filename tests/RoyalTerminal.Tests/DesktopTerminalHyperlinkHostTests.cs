// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.App.Services.Links;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class DesktopTerminalHyperlinkHostTests
{
    [Theory]
    [InlineData("https://example.com", false, null, true, 0)]
    [InlineData("mailto:user@example.com", false, null, true, 0)]
    [InlineData("custom:action", true, "Example application", true, 1)]
    [InlineData("custom:action", false, "Example application", false, 1)]
    [InlineData("custom:action", true, null, false, 1)]
    [InlineData("custom:action", true, " ", false, 1)]
    [InlineData("file:///tmp/document.txt", true, "Example application", false, 1)]
    [InlineData("https://example.com/a\u202Eb", true, "Example application", false, 1)]
    [InlineData("../ambiguous", true, "Example application", false, 1)]
    public async Task OnlyAllowedOrConfirmedKnownHandlersDispatch(string target, bool accepted, string? handler,
        bool opens, int promptCount)
    {
        Probe probe = new() { Accepted = accepted, Handler = handler };
        DesktopTerminalHyperlinkHost host = new(probe, probe, probe);
        await host.HandleAsync(TerminalHyperlinkSafety.Classify(target), CancellationToken.None);
        Assert.Equal(opens ? 1 : 0, probe.Opened.Count);
        Assert.Equal(promptCount, probe.PromptCount);
        Assert.Equal(target.StartsWith("custom:", StringComparison.Ordinal) ? 1 : 0, probe.ResolveCount);
        if (opens) Assert.Equal(new Uri(target), Assert.Single(probe.Opened));
    }

    [Fact]
    public async Task ForgedClassificationCannotGrantPermissionOrSupplyDisplayText()
    {
        Probe probe = new() { Handler = "Editor", Accepted = false };
        DesktopTerminalHyperlinkHost host = new(probe, probe, probe);
        TerminalHyperlinkRequest forged = new("custom:execute", "harmless", new Uri("https://example.com"),
            TerminalHyperlinkDisposition.Allow, TerminalHyperlinkDenialReason.None);
        await host.HandleAsync(forged, CancellationToken.None);
        Assert.Equal("custom:execute", probe.LastRequest!.DisplayText);
        Assert.Equal(TerminalHyperlinkDisposition.Confirm, probe.LastRequest.Disposition);
        Assert.Empty(probe.Opened);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationAfterNonCooperatingResolverOrPromptNeverLaunches(bool duringResolve)
    {
        using CancellationTokenSource cancellation = new();
        Probe probe = new() { Handler = "Editor", Accepted = true };
        if (duringResolve) probe.OnResolve = cancellation.Cancel;
        else probe.OnPrompt = cancellation.Cancel;
        DesktopTerminalHyperlinkHost host = new(probe, probe, probe);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.HandleAsync(
            TerminalHyperlinkSafety.Classify("custom:execute"), cancellation.Token).AsTask());
        Assert.Empty(probe.Opened);
        Assert.Equal(duringResolve ? 0 : 1, probe.PromptCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolverAndPromptFailuresNeverFallBack(bool duringResolve)
    {
        Probe probe = new() { Handler = "Editor", Accepted = true };
        Action fail = () => throw new InvalidOperationException("Unavailable host");
        if (duringResolve) probe.OnResolve = fail;
        else probe.OnPrompt = fail;
        DesktopTerminalHyperlinkHost host = new(probe, probe, probe);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleAsync(
            TerminalHyperlinkSafety.Classify("custom:execute"), CancellationToken.None).AsTask());
        Assert.Empty(probe.Opened);
    }

    [Fact]
    public async Task PreCanceledRequestPerformsNoExternalWork()
    {
        Probe probe = new();
        DesktopTerminalHyperlinkHost host = new(probe, probe, probe);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.HandleAsync(
            TerminalHyperlinkSafety.Classify("https://example.com"), new CancellationToken(true)).AsTask());
        Assert.Empty(probe.Opened);
        Assert.Equal(0, probe.ResolveCount);
        Assert.Equal(0, probe.PromptCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad:scheme")]
    [InlineData("bad\0scheme")]
    [InlineData("../command")]
    public async Task InvalidAssociationSchemesNeverReachPlatformLookup(string scheme)
        => Assert.Null(await new DesktopHyperlinkHandlerResolver().ResolveAsync(scheme, CancellationToken.None));

    [Fact]
    public async Task UnregisteredSchemeHasNoHandlerOnEveryPlatform()
        => Assert.Null(await new DesktopHyperlinkHandlerResolver().ResolveAsync(
            "royalterminal-test-" + Guid.NewGuid().ToString("N"), CancellationToken.None));

    private sealed class Probe : ITerminalHyperlinkPrompt, ITerminalHyperlinkHandlerResolver, ITerminalHyperlinkLauncher
    {
        internal bool Accepted;
        internal string? Handler;
        internal int PromptCount, ResolveCount;
        internal Action? OnResolve, OnPrompt;
        internal TerminalHyperlinkRequest? LastRequest;
        internal List<Uri> Opened { get; } = [];
        public ValueTask<bool> ShowAsync(TerminalHyperlinkRequest request, string? handler, CancellationToken token)
        {
            PromptCount++;
            LastRequest = request;
            OnPrompt?.Invoke();
            return ValueTask.FromResult(Accepted);
        }
        public ValueTask<string?> ResolveAsync(string scheme, CancellationToken token)
        {
            ResolveCount++;
            OnResolve?.Invoke();
            return ValueTask.FromResult(Handler);
        }
        public ValueTask LaunchAsync(Uri uri, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Opened.Add(uri);
            return ValueTask.CompletedTask;
        }
    }
}
