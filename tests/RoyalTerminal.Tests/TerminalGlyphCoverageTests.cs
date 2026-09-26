// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Glyphs;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalGlyphCoverageTests
{
    private const string EmptyGlyph = "AAAAAAAAAAAAAA==";
    private static string Wire(string payload) => $"\u001b_25a1;{payload}\u001b\\";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothEnginesReportAllCoverageStatesAndKeepRegistrationsIndependent(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(20, 4);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        ITerminalGlyphCoverageSink sink = Assert.IsAssignableFrom<ITerminalGlyphCoverageSink>(processor);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        TestCoverageSource coverage = new(cp => cp is 0x41 or 0xE000);
        sink.GlyphCoverageSource = coverage;
        processor.Process(Encoding.ASCII.GetBytes(Wire("q;cp=41") + Wire("q;cp=42") +
            Wire($"r;cp=e000;reply=0;{EmptyGlyph}") + Wire("q;cp=e000") +
            Wire($"r;cp=e001;reply=0;{EmptyGlyph}") + Wire("q;cp=e001")));
        Assert.Equal(new[] { Wire("q;cp=41;status=system"), Wire("q;cp=42;status="),
            Wire("q;cp=e000;status=system,glossary"), Wire("q;cp=e001;status=glossary") }, replies);
        Assert.Equal(2, screen.RegisteredGlyphCount);

        replies.Clear();
        sink.GlyphCoverageSource = null;
        processor.Process(Encoding.ASCII.GetBytes(Wire("q;cp=e000")));
        Assert.Equal(Wire("q;cp=e000;status=glossary"), Assert.Single(replies));
        sink.GlyphCoverageSource = coverage;
        replies.Clear();
        processor.Process(Encoding.ASCII.GetBytes("\u001bc" + Wire("q;cp=e000")));
        Assert.Equal(Wire("q;cp=e000;status=system"), Assert.Single(replies));
        Assert.Equal(0, screen.RegisteredGlyphCount);
        processor.Dispose();
        Assert.False(coverage.Disposed); // Source remains caller-owned.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitQueriesAndSynchronizedOutputUseWorkingGlossary(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(20, 4);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        ((ITerminalGlyphCoverageSink)processor).GlyphCoverageSource = new TestCoverageSource(_ => true);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        byte[] input = Encoding.ASCII.GetBytes("\u001b[?2026h" + Wire($"r;cp=e000;reply=0;{EmptyGlyph}") + Wire("q;cp=e000"));
        foreach (byte value in input) processor.Process(new[] { value });
        Assert.Equal(Wire("q;cp=e000;status=system,glossary"), Assert.Single(replies));
        Assert.Equal(0, screen.RegisteredGlyphCount);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal(1, screen.RegisteredGlyphCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidScalarsAndHostFailuresPreserveAReplyWithoutSystemCoverage(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new(20, 4)) : new BasicVtProcessor(new(20, 4));
        TestCoverageSource source = new(_ => throw new InvalidOperationException("Host unavailable"));
        ((ITerminalGlyphCoverageSink)processor).GlyphCoverageSource = source;
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.Process(Encoding.ASCII.GetBytes(Wire("q;cp=d800") + Wire("q;cp=110000") + Wire("q;cp=1fffff")));
        Assert.Equal(0, source.Calls);
        Assert.Equal(new[] { Wire("q;cp=d800;status="), Wire("q;cp=110000;status="), Wire("q;cp=1fffff;status=") }, replies);
        replies.Clear();
        processor.Process(Encoding.ASCII.GetBytes(Wire($"r;cp=e000;reply=0;{EmptyGlyph}") + Wire("q;cp=e000")));
        Assert.Equal(1, source.Calls);
        Assert.Equal(Wire("q;cp=e000;status=glossary"), Assert.Single(replies));
    }

    [Theory]
    [InlineData("s;fmt=glyf")]
    [InlineData("q;cp=xyz;status=")]
    [InlineData("q;cp=41;status=system")]
    [InlineData("q;cp=41;status=unknown")]
    [InlineData("q;cp=41;status=glossary;extra=true")]
    [InlineData("q;cp=41;status=glossary,system")]
    [InlineData("q;cp=100000000;status=")]
    [InlineData("q;cp=;status=")]
    public void NativeReplyAugmentationDoesNotRewriteOtherOrMalformedReplies(string payload)
    {
        TestCoverageSource source = new(_ => true);
        Assert.Null(TerminalGlyphCoverageResponse.Augment(Encoding.ASCII.GetBytes(Wire(payload)), source));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void DisabledManagedProtocolDoesNotConsultFontCoverage()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(20, 4)) { GlyphProtocolEnabled = false };
        TestCoverageSource source = new(_ => true);
        processor.GlyphCoverageSource = source;
        List<byte[]> replies = [];
        processor.ResponseCallback = replies.Add;
        processor.Process(Encoding.ASCII.GetBytes(Wire("q;cp=41") + Wire("s")));
        Assert.Empty(replies);
        Assert.Equal(0, source.Calls);
        processor.GlyphProtocolEnabled = true;
        processor.Process(Encoding.ASCII.GetBytes(Wire("q;cp=1f600")));
        Assert.Equal(Wire("q;cp=1f600;status=system"), Encoding.ASCII.GetString(Assert.Single(replies)));
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void NativeReplyAugmentationRequiresSingleCompleteBoundedReply()
    {
        TestCoverageSource source = new(_ => true);
        Assert.Null(TerminalGlyphCoverageResponse.Augment("\u001b_25a1;q;cp=41;status="u8, source));
        Assert.Null(TerminalGlyphCoverageResponse.Augment(Encoding.ASCII.GetBytes(Wire("q;cp=41;status=") + Wire("s;fmt=glyf")), source));
        Assert.Null(TerminalGlyphCoverageResponse.Augment(Encoding.ASCII.GetBytes(Wire("q;cp=" + new string('0', 100) + ";status=")), source));
        Assert.Null(TerminalGlyphCoverageResponse.Augment("normal PTY response"u8, source));
        Assert.Null(TerminalGlyphCoverageResponse.Augment(Encoding.ASCII.GetBytes(Wire("q;cp=41;status=")), null));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void SkiaCoverageIsLazyBoundedAndRejectsTofuAndInvalidScalars()
    {
        using SkiaTerminalGlyphCoverageSource source = new("Consolas", TerminalFontSource.System, null, cacheCapacity: 2);
        Assert.False(source.IsInitialized);
        Assert.False(source.HasSystemGlyph(0xD800));
        Assert.False(source.HasSystemGlyph(uint.MaxValue));
        Assert.False(source.IsInitialized);
        Assert.True(source.HasSystemGlyph('A'));
        Assert.True(source.HasSystemGlyph('B'));
        Assert.Equal(2, source.CachedCount);
        Assert.False(source.HasSystemGlyph(0x10FFFF));
        Assert.Equal(1, source.CachedCount);
        source.Dispose();
        Assert.False(source.HasSystemGlyph('A'));
        Assert.Equal(0, source.CachedCount);
    }

    [Fact]
    public void SkiaCoverageUsesConfiguredFontFileAndRegularFallback()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", "NotoEmoji-Regular.ttf");
        using GlyphCache fonts = new("Consolas", TerminalFontSource.File, path);
        using SKFont primary = fonts.CreateFont(12);
        Assert.False(primary.ContainsGlyph('A')); // Confirm this is the configured emoji font, not fallback.
        using SkiaTerminalGlyphCoverageSource source = new("Consolas", TerminalFontSource.File, path);
        Assert.True(source.HasSystemGlyph('A')); // Regular system fallback supplies text.
        Assert.True(source.HasSystemGlyph(0x1F600)); // Configured file supplies this glyph.
    }

    [Fact]
    public async Task CoverageAndDisposalAreSynchronizedAndCachedQueriesAllocateNothing()
    {
        using SkiaTerminalGlyphCoverageSource source = new();
        Assert.True(source.HasSystemGlyph('A'));
        for (int i = 0; i < 100; i++) source.HasSystemGlyph('A');
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) source.HasSystemGlyph('A');
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Task queries = Task.Run(() => { for (int i = 0; i < 1000; i++) source.HasSystemGlyph((uint)('A' + i % 26)); });
        Task dispose = Task.Run(source.Dispose);
        await Task.WhenAll(queries, dispose);
        Assert.False(source.HasSystemGlyph('A'));
    }

    [Fact]
    public void RendererOwnsItsCoverageLifetime()
    {
        using SkiaTerminalRenderer renderer = new();
        ITerminalGlyphCoverageSource source = renderer.GlyphCoverageSource;
        Assert.True(source.HasSystemGlyph('A'));
        renderer.Dispose();
        Assert.False(source.HasSystemGlyph('A'));
    }

    private sealed class TestCoverageSource(Func<uint, bool> contains) : ITerminalGlyphCoverageSource, IDisposable
    {
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public bool HasSystemGlyph(uint codepoint) { Calls++; return contains(codepoint); }
        public void Dispose() => Disposed = true;
    }
}
