// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalHyperlinkIdentityTests(ITestOutputHelper output)
{
    [Fact]
    public void RegistryKeepsByteIdentityAndCopiesOwnedValues()
    {
        TerminalScreen screen = new(8, 2);
        byte[] uri = [255, 128, 65], id = [254, 66];
        int explicitToken = screen.RegisterHyperlink(uri, id, 0);
        int implicitToken = screen.RegisterHyperlink(uri, [], 0);
        Assert.NotEqual(explicitToken, implicitToken);
        Assert.NotEqual(implicitToken, screen.RegisterHyperlink(uri, [], 1));
        Assert.Equal(explicitToken, screen.RegisterHyperlink(uri, id, 999));
        TerminalScreen copy = screen.CreateStateCopy();
        uri[0] = id[0] = 0;
        screen.ClearAll();
        Assert.True(copy.TryGetHyperlink(explicitToken, out TerminalHyperlink? link));
        Assert.Equal(new byte[] { 255, 128, 65 }, link!.UriBytes.ToArray());
        Assert.Equal(new byte[] { 254, 66 }, link.ExplicitId.ToArray());
        screen.AdoptStateFrom(copy);
        Assert.True(screen.TryGetHyperlinkUrl(explicitToken, out string? url));
        Assert.Equal(link.Uri, url);
    }

    [Fact]
    public void RepeatedByteRegistryLookupsDoNotAllocate()
    {
        TerminalScreen screen = new(8, 2);
        int token = screen.RegisterHyperlink("https://example.com"u8, "one"u8, 0);
        for (int i = 0; i < 1000; i++) screen.RegisterHyperlink("https://example.com"u8, "one"u8, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int last = 0;
        for (int i = 0; i < 1000; i++) last = screen.RegisterHyperlink("https://example.com"u8, "one"u8, 0);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(token, last);
    }

    [Theory]
    [InlineData("\u001b]8;;url\aA\u001b]8;;\a\u001b]8;;url\aB")]
    [InlineData("\u001b]8;id=one;url\aA\u001b]8;id=two;url\aB\u001b]8;id=one;url\aC")]
    [InlineData("\u001b]8;id=one:id=two;url\aA\u001b]8;id=two:id=;url\aB")]
    [InlineData("\u001b]8;id=one;url\aA\u001b]8;id=invalid;\aB\u001b]8;;\aC")]
    [InlineData("\u001b]8;bad:id=ignored;url\aA\u001b]8;id=;url\aB")]
    [InlineData("\u001b]8;;url\aA\u001b[?47hB\u001b]8;;url\aC\u001b[?47lD\u001b]8;;url\aE")]
    [InlineData("\u001b]8;;url\aA\u001b[?1049hB\u001b]8;;url\aC\u001b[?1049lD\u001b]8;;url\aE")]
    [InlineData("\u001b[?2027h\u001b]8;id=one;first\a❤\u001b]8;id=two;second\a\ufe0f")]
    [InlineData("\u001b[?2027h1234567\u001b]8;id=one;first\a❤\u001b]8;id=two;second\a\ufe0f")]
    public void OriginalIdentitiesMatchNativeAtEverySplit(string input)
        => CompareEverySplit(Encoding.UTF8.GetBytes(input));

    [Fact]
    public void InvalidUtf8InUriAndIdSurvivesLiveProcessing()
    {
        byte[] bytes = [27, 93, 56, 59, 105, 100, 61, 255, 59, 117, 114, 108, 254, 7, 65];
        CompareEverySplit(bytes);
    }

    [Fact]
    public void StyledExportRetainsExplicitIdentityAndImplicitBoundaries()
    {
        TerminalScreen source = new(16, 2), target = new(16, 2);
        using BasicVtProcessor writer = new(source), reader = new(target);
        writer.Process("\u001b]8;id=first;url\aA\u001b]8;id=second;url\aB\u001b]8;;url\aC\u001b]8;;url\aD"u8);
        Assert.True(writer.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(Extras: new(IncludeHyperlinks: true)), out string snapshot));
        reader.Process(Encoding.UTF8.GetBytes(snapshot));
        Assert.True(target.TryGetHyperlink(target.GetRow(0)[0].HyperlinkId, out var first));
        Assert.True(target.TryGetHyperlink(target.GetRow(0)[1].HyperlinkId, out var second));
        Assert.Equal("first"u8.ToArray(), first!.ExplicitId.ToArray());
        Assert.Equal("second"u8.ToArray(), second!.ExplicitId.ToArray());
        Assert.NotEqual(target.GetRow(0)[2].HyperlinkId, target.GetRow(0)[3].HyperlinkId);
    }

    [Fact]
    public unsafe void NativeExtensionLayoutsAndSmallBufferContract()
    {
        Assert.Equal(IntPtr.Size + 24, Unsafe.SizeOf<GhosttyVtNative.RoyalPromptState>());
        Assert.Equal(IntPtr.Size == 8 ? 32 : 16, Unsafe.SizeOf<GhosttyVtNative.RoyalHyperlinkMetadata>());
        if (!Available()) return;
        using GhosttyTerminal terminal = new(8, 2);
        terminal.Write("\u001b]8;id=abc;url\aA"u8);
        Assert.True(terminal.TryGetGridReference(GhosttyVtNative.GhosttyPoint.Active(0, 0), out var reference));
        byte[] uri = [77, 77, 77], id = [77, 77];
        Assert.False(terminal.TryReadHyperlink(in reference, uri, id, out int uriLength, out int idLength, out _));
        Assert.Equal((3, 3), (uriLength, idLength));
        Assert.Equal(new byte[] { 77, 77, 77 }, uri);
        Assert.Equal(new byte[] { 77, 77 }, id);
        id = new byte[idLength];
        Assert.True(terminal.TryReadHyperlink(in reference, uri, id, out _, out _, out _));
        Assert.Equal("url"u8.ToArray(), uri);
        Assert.Equal("abc"u8.ToArray(), id);
        var state = GhosttyVtNative.RoyalPromptState.CreateSized();
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.PromptState(0, &state));
    }

    private void CompareEverySplit(byte[] bytes)
    {
        if (!Available()) return;
        TerminalScreen expected = new(8, 3);
        using GhosttyVtProcessor native = new(expected);
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen actual = new(8, 3);
            using BasicVtProcessor managed = new(actual);
            managed.Process(bytes.AsSpan(0, split));
            managed.Process(bytes.AsSpan(split));
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 8; column++)
            {
                bool hasExpected = expected.TryGetHyperlink(expected.GetViewportRow(row)[column].HyperlinkId, out var e);
                bool hasActual = actual.TryGetHyperlink(actual.GetViewportRow(row)[column].HyperlinkId, out var a);
                Assert.True(hasExpected == hasActual, $"split {split} link presence {row},{column}");
                if (!hasExpected) continue;
                Assert.Equal(e!.UriBytes.ToArray(), a!.UriBytes.ToArray());
                Assert.Equal(e.ExplicitId.ToArray(), a.ExplicitId.ToArray());
                Assert.Equal(e.ImplicitId, a.ImplicitId);
            }
        }
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native hyperlink identity differential available: {available}");
        return available;
    }
}
