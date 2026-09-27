// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.App.Services.Links;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class DesktopHyperlinkFileInspectorTests
{
    [Fact]
    public async Task ResolvedPathAndOriginalCopyIdentityAreKeptSeparate()
    {
        string canonical = Path.Combine(Path.GetTempPath(), "resolved.txt");
        TerminalHyperlinkRequest input = Request("alias.txt");
        Probe probe = new(path =>
        {
            Assert.True(Thread.CurrentThread.IsThreadPoolThread);
            Assert.Equal(input.Uri!.LocalPath, path);
            return new(canonical, HyperlinkFileKind.Regular, false, false);
        });
        TerminalHyperlinkRequest result = await new DesktopHyperlinkFileInspector(probe).InspectAsync(input, CancellationToken.None);
        Assert.Equal(TerminalHyperlinkDisposition.Allow, result.Disposition);
        Assert.Equal(input.Target, result.Target);
        Assert.Equal(canonical, result.DisplayText);
        Assert.Equal(canonical, result.Uri!.LocalPath);
        Assert.Equal(1, probe.Calls);
    }

    [Theory]
    [InlineData("file://remote/unsafe")]
    [InlineData("file:///tmp/note?query")]
    [InlineData("file:///tmp/note#fragment")]
    [InlineData("file:///tmp/note%00trailing")]
    public async Task InvalidFileTargetsNeverReachFilesystem(string target)
    {
        Probe probe = new(_ => throw new InvalidOperationException("Must not probe"));
        TerminalHyperlinkRequest result = await new DesktopHyperlinkFileInspector(probe).InspectAsync(
            TerminalHyperlinkSafety.Classify(target), CancellationToken.None);
        Assert.Equal(TerminalHyperlinkDisposition.Deny, result.Disposition);
        Assert.Equal(0, probe.Calls);
    }

    [Theory]
    [InlineData(0, TerminalHyperlinkDenialReason.InaccessibleFile)]
    [InlineData(1, TerminalHyperlinkDenialReason.InaccessibleFile)]
    [InlineData(2, TerminalHyperlinkDenialReason.FileInspectionUnavailable)]
    [InlineData(3, TerminalHyperlinkDenialReason.FileInspectionUnavailable)]
    public async Task ProbeFailuresFailClosed(int failure, TerminalHyperlinkDenialReason reason)
    {
        Probe probe = new(_ => throw failure switch
        {
            0 => new IOException(),
            1 => new UnauthorizedAccessException(),
            2 => new PlatformNotSupportedException(),
            _ => new DllNotFoundException(),
        });
        TerminalHyperlinkRequest result = await new DesktopHyperlinkFileInspector(probe).InspectAsync(Request("alias.txt"), CancellationToken.None);
        Assert.Equal(TerminalHyperlinkDisposition.Deny, result.Disposition);
        Assert.Equal(reason, result.DenialReason);
        Assert.Null(result.Uri);
    }

    [Fact]
    public async Task PreCancellationDoesNotScheduleFilesystemWork()
    {
        Probe probe = new(_ => throw new InvalidOperationException("Must not probe"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DesktopHyperlinkFileInspector(probe)
            .InspectAsync(Request("alias.txt"), new CancellationToken(true)).AsTask());
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task CancellationAfterNonCooperatingProbeDoesNotPublishPermission()
    {
        using CancellationTokenSource cancellation = new();
        Probe probe = new(path =>
        {
            cancellation.Cancel();
            return new(path, HyperlinkFileKind.Regular, false, false);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DesktopHyperlinkFileInspector(probe)
            .InspectAsync(Request("alias.txt"), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task LocalhostAuthorityIsConvertedToLocalPathBeforeInspection()
    {
        TerminalHyperlinkRequest input = Request("alias.txt");
        string target = new UriBuilder(input.Uri!) { Host = "localhost" }.Uri.AbsoluteUri;
        Probe probe = new(path =>
        {
            Assert.Equal(input.Uri!.LocalPath, path);
            return new(path, HyperlinkFileKind.Regular, false, false);
        });
        TerminalHyperlinkRequest result = await new DesktopHyperlinkFileInspector(probe).InspectAsync(
            TerminalHyperlinkSafety.Classify(target), CancellationToken.None);
        Assert.Equal(TerminalHyperlinkDisposition.Allow, result.Disposition);
    }

    private static TerminalHyperlinkRequest Request(string name)
        => TerminalHyperlinkSafety.Classify(new Uri(Path.Combine(Path.GetTempPath(), name)).AbsoluteUri);

    private sealed class Probe(Func<string, HyperlinkFileFacts> read) : IHyperlinkFileProbe
    {
        internal int Calls;
        public HyperlinkFileFacts Read(string path) { Calls++; return read(path); }
    }
}
