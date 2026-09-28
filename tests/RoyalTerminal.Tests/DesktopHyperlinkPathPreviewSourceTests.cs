// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.App.Services.Links;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class DesktopHyperlinkPathPreviewSourceTests
{
    [Fact]
    public async Task CanonicalFilePreviewRunsOffThreadAndEscapesResolvedName()
    {
        string path = Path.Combine(Path.GetTempPath(), "alias.txt");
        Probe probe = new(value =>
        {
            Assert.True(Thread.CurrentThread.IsThreadPoolThread);
            Assert.Equal(path, value);
            return Path.Combine(Path.GetTempPath(), "actual\u202E.txt");
        });
        string preview = await new DesktopHyperlinkPathPreviewSource(probe)
            .GetPathPreviewAsync(new Uri(path).AbsoluteUri, CancellationToken.None);
        Assert.Equal(TerminalHyperlinkSafety.SanitizeDisplay(Path.Combine(Path.GetTempPath(), "actual\u202E.txt")), preview);
        Assert.Equal(1, probe.Calls);
    }

    [Theory]
    [InlineData("https://example.com/a//b")]
    [InlineData("custom:action?value=a//b")]
    [InlineData("file://remote/path")]
    [InlineData("file:///tmp/path?query")]
    [InlineData("file:///tmp/a%00b")]
    [InlineData("file:///tmp/a\u202Eb")]
    public async Task NonFileAndRejectedTargetsNeverReachFilesystem(string target)
    {
        Probe probe = new(_ => throw new InvalidOperationException("Unexpected filesystem access"));
        Assert.Equal(TerminalHyperlinkSafety.SanitizeDisplay(target), await new DesktopHyperlinkPathPreviewSource(probe)
            .GetPathPreviewAsync(target, CancellationToken.None));
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task SchemeLessBlockedTargetIsStandardizedWithoutFileAccess()
    {
        const string target = "./unused/../notes.txt";
        Probe probe = new(_ => throw new InvalidOperationException("Unexpected filesystem access"));
        Assert.Equal(Path.GetFullPath(target), await new DesktopHyperlinkPathPreviewSource(probe).GetPathPreviewAsync(target, CancellationToken.None));
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task MissingPlatformResolutionKeepsNormalizedEscapedFallback()
    {
        string path = Path.Combine(Path.GetTempPath(), "notes.txt");
        Probe probe = new(_ => throw new PlatformNotSupportedException());
        Assert.Equal(path, await new DesktopHyperlinkPathPreviewSource(probe).GetPathPreviewAsync(new Uri(path).AbsoluteUri, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationDoesNotPublishACompletedFilesystemResult()
    {
        using CancellationTokenSource cancellation = new();
        Probe probe = new(path => { cancellation.Cancel(); return path; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DesktopHyperlinkPathPreviewSource(probe)
            .GetPathPreviewAsync(new Uri(Path.Combine(Path.GetTempPath(), "notes.txt")).AbsoluteUri, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task PreCancellationPerformsNoWork()
    {
        Probe probe = new(_ => throw new InvalidOperationException("Unexpected filesystem access"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DesktopHyperlinkPathPreviewSource(probe)
            .GetPathPreviewAsync("file:///tmp/notes.txt", new CancellationToken(true)).AsTask());
        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task UnixNativePreviewResolvesSymlinksWithoutContentInspection()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Directory.CreateTempSubdirectory("royalterminal-preview-").FullName;
        try
        {
            string actual = Path.Combine(root, "payload.command"), alias = Path.Combine(root, "notes.txt");
            File.WriteAllText(actual, "data");
            File.CreateSymbolicLink(alias, actual);
            string preview = await new DesktopHyperlinkPathPreviewSource(new NativeHyperlinkFileProbe())
                .GetPathPreviewAsync(new Uri(alias).AbsoluteUri, CancellationToken.None);
            Assert.EndsWith("/payload.command", preview, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class Probe(Func<string, string> resolve) : IHyperlinkPathResolver
    {
        internal int Calls;
        public string ResolvePath(string path) { Calls++; return resolve(path); }
    }
}
