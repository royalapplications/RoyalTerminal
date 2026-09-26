// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedUtf8ParserParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("C080")]
    [InlineData("C1BF")]
    [InlineData("E08080")]
    [InlineData("E09FBF")]
    [InlineData("EDA080")]
    [InlineData("EDBFBF")]
    [InlineData("F0808080")]
    [InlineData("F08FBFBF")]
    [InlineData("F4908080")]
    [InlineData("F5808080")]
    [InlineData("FEFF")]
    [InlineData("80A0BF")]
    [InlineData("C220")]
    [InlineData("E1A020")]
    [InlineData("F09F2041")]
    [InlineData("F09FC2A0")]
    [InlineData("F09FF09F9880")]
    [InlineData("E11B5B33316D41")]
    [InlineData("E11841")]
    [InlineData("E11A41")]
    [InlineData("E10741")]
    [InlineData("E10A41")]
    [InlineData("E17F41")]
    [InlineData("C280C29F")]
    [InlineData("C2A0E0A080ED9FBFF0908080F48FBFBF")]
    public void InvalidAndBoundaryScalarsMatchNativeAcrossEverySplit(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex + "58");
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native UTF-8 differential available: {available}");
        if (!available) return;
        TerminalScreen nativeScreen = new(40, 3);
        using GhosttyVtProcessor native = new(nativeScreen);
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen screen = new(40, 3);
            using BasicVtProcessor managed = new(screen);
            managed.Process(bytes.AsSpan(0, split));
            managed.Process(bytes.AsSpan(split));
            Assert.True(managed.IsParserGround);
            Assert.Empty(managed.GetContinuation());
            Assert.Equal((native.CursorRow, native.CursorCol), (managed.CursorRow, managed.CursorCol));
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 40; col++)
                {
                    TerminalCell expected = nativeScreen.GetViewportRow(row)[col];
                    TerminalCell actual = screen.GetViewportRow(row)[col];
                    Assert.Equal(expected.Codepoint, actual.Codepoint);
                    if (expected.Codepoint != 0) Assert.Equal(expected.ForegroundIdentity, actual.ForegroundIdentity);
                }
        }
    }

    [Theory]
    [InlineData(0x18)]
    [InlineData(0x1A)]
    [InlineData(0x07)]
    public void InterruptedScalarIsReplacedBeforeControlAndGroundBoundary(int control)
    {
        TerminalScreen screen = new(12, 2);
        using BasicVtProcessor managed = new(screen);
        int bells = 0;
        managed.BellCallback = () => bells++;
        managed.Process(new byte[] { 0xF0, 0x9F });
        Assert.True(managed.ProcessUntilGround(new byte[] { (byte)control, (byte)'X' }, out int consumed));
        Assert.Equal(1, consumed);
        Assert.Equal(0xFFFD, screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal(1, managed.CursorCol);
        Assert.Equal(control == 7 ? 1 : 0, bells);
        Assert.Empty(managed.GetContinuation());
    }

    [Fact]
    public void RejectedPrefixIsCommittedBeforeNewPartialScalarReplay()
    {
        TerminalScreen screen = new(12, 2), replayScreen = new(12, 2);
        using BasicVtProcessor managed = new(screen), replay = new(replayScreen);
        managed.Process(new byte[] { 0xF0, 0x9F, 0xED });
        byte[] continuation = managed.GetContinuation();
        Assert.Equal(new byte[] { 0xED }, continuation);
        Assert.True(GhosttySnapshotContinuation.IsValid(continuation));
        Assert.Equal(0xFFFD, screen.GetViewportRow(0)[0].Codepoint);
        replay.Process(continuation);
        managed.Process(new byte[] { 0x9F, 0xBF });
        replay.Process(new byte[] { 0x9F, 0xBF });
        Assert.Equal(0xD7FF, screen.GetViewportRow(0)[1].Codepoint);
        Assert.Equal(0xD7FF, replayScreen.GetViewportRow(0)[0].Codepoint);
        Assert.Empty(managed.GetContinuation());
    }

    [Fact]
    public void EverySecondByteAtEachUtf8BoundaryClassMatchesNative()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native UTF-8 boundary matrix available: {available}");
        if (!available) return;
        TerminalScreen screen = new(12, 3), nativeScreen = new(12, 3);
        using BasicVtProcessor managed = new(screen);
        using GhosttyVtProcessor native = new(nativeScreen);
        byte[] bytes = new byte[] { 0, 0, 0x18, (byte)'X' };
        foreach (byte lead in new byte[] { 0xC2, 0xDF, 0xE0, 0xE1, 0xED, 0xEF, 0xF0, 0xF1, 0xF4 })
            for (int second = 0; second <= byte.MaxValue; second++)
            {
                bytes[0] = lead;
                bytes[1] = (byte)second;
                managed.Reset(); native.Reset();
                managed.Process(bytes.AsSpan(0, 1)); managed.Process(bytes.AsSpan(1));
                native.Process(bytes);
                Assert.True(managed.IsParserGround);
                Assert.True((native.CursorRow, native.CursorCol) == (managed.CursorRow, managed.CursorCol),
                    $"Cursor mismatch for {lead:X2} {second:X2}");
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 12; col++)
                        Assert.True(nativeScreen.GetViewportRow(row)[col].Codepoint == screen.GetViewportRow(row)[col].Codepoint,
                            $"Cell mismatch for {lead:X2} {second:X2} at ({row},{col})");
            }
    }

    [Fact]
    public void WarmErrorReplacementDoesNotAllocate()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(12, 3));
        byte[] bytes = new byte[] { 0xE0, 0x80, 0xBF, 0x0D };
        for (int i = 0; i < 100; i++) managed.Process(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) managed.Process(bytes);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
