// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedCsiParserParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\u001b[31\u0007", "\u001b[31", "mX")]
    [InlineData("\u001b\u0007[31", "\u001b[31", "mX")]
    [InlineData("\u001b[31\u001b[32", "\u001b[32", "mX")]
    [InlineData("\u001b[1?999", "\u001b[1?999", "mX")]
    [InlineData("\u001b[1 99", "\u001b[1 99", "mX")]
    [InlineData("\u001b[:123", "\u001b[:123", "mX")]
    [InlineData("\u001b[38:2::1:2:3", "\u001b[38:2::1:2:3", "mX")]
    public void SplitContinuationsAreMinimalAndDoNotRepeatImmediateControls(string prefix, string expected, string tail)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(prefix);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen screen = new(12, 3), replayScreen = new(12, 3);
            using BasicVtProcessor managed = new(screen), replay = new(replayScreen);
            int bells = 0, replayBells = 0;
            managed.BellCallback = () => bells++;
            replay.BellCallback = () => replayBells++;
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            Assert.False(managed.IsParserGround);
            Assert.Equal(Encoding.UTF8.GetBytes(expected), managed.GetContinuation());
            Assert.True(GhosttySnapshotContinuation.IsValid(managed.GetContinuation()));
            replay.Process(managed.GetContinuation());
            Assert.Equal(0, replayBells);
            Assert.Equal(prefix.Contains('\a') ? 1 : 0, bells);
            managed.Process(Encoding.UTF8.GetBytes(tail)); replay.Process(Encoding.UTF8.GetBytes(tail));
            Assert.True(managed.IsParserGround);
            Assert.Equal(screen.GetViewportRow(0)[0], replayScreen.GetViewportRow(0)[0]);
        }
    }

    [Fact]
    public void C0StormDoesNotConsumeContinuationBudgetOrAllocateAfterWarmup()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(12, 3), new() { ContinuationMaxBytes = 8 });
        byte[] bells = new byte[100_000]; Array.Fill(bells, (byte)7);
        processor.Process("\u001b[31"u8);
        processor.Process(bells);
        Assert.Equal("\u001b[31"u8.ToArray(), processor.GetContinuation());
        long before = GC.GetAllocatedBytesForCurrentThread();
        processor.Process(bells);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(processor.ProcessUntilGround("mX"u8, out int consumed));
        Assert.Equal(1, consumed);
    }

    [Theory]
    [InlineData("\u001b[31\u0007mX")]
    [InlineData("\u001b[2\u0009CX")]
    [InlineData("AB\u001b[2\u000dCX")]
    [InlineData("\u001b[31\u001b[32mX")]
    [InlineData("\u001b[1?999mX")]
    [InlineData("\u001b[1 99mX")]
    [InlineData("\u001b[:123mX")]
    [InlineData("\u001b[31\u007fmX")]
    [InlineData("\u001b[4294967296CX")]
    [InlineData("\u001b[9999999999999999999999999999999999999CX")]
    [InlineData("\u001b[2$CX")]
    [InlineData("\u001b[31$$mX")]
    [InlineData("\u001b[1:2HX")]
    [InlineData("\u001b[?25$hX")]
    [InlineData("\u001b[31\u009b32mX")]
    [InlineData("\u001b[31\u0085X")]
    public void CsiStateTransitionsMatchNativeAtEverySplit(string sequence) => CompareEverySplit(sequence);

    [Theory]
    [InlineData("4:0")]
    [InlineData("4:1")]
    [InlineData("4:2")]
    [InlineData("4:3")]
    [InlineData("4:4")]
    [InlineData("4:5")]
    [InlineData("4:999")]
    [InlineData("4:")]
    [InlineData("4::;31")]
    [InlineData("4:3:9;31")]
    [InlineData("1:2;31")]
    [InlineData("38:2:1:2:3")]
    [InlineData("38:2::1:2:3")]
    [InlineData("48:2:123:4:5:6")]
    [InlineData("58:2::7:8:9;4:3")]
    [InlineData("38:5:257;48:5:258;58:5:259")]
    [InlineData("38;5;999;48;5;1000")]
    [InlineData("38;2;256;257;258;48;2;511;512;513")]
    [InlineData("38:2:1:2;31")]
    [InlineData("38:2:1:2:3:4:5;31")]
    [InlineData("58:4:")]
    [InlineData("38:5:31:4:3")]
    public void ColonSgrAndOutOfRangeComponentsMatchNative(string parameters)
        => CompareEverySplit($"\u001b[{parameters}mX");

    [Fact]
    public void ParameterLimitMatchesNativeWithoutUnboundedGrowth()
    {
        foreach (int count in new[] { 23, 24, 25, 1000 })
        {
            string parameters = string.Concat(Enumerable.Repeat("0;", count - 1)) + "31";
            CompareEverySplit($"\u001b[{parameters}mX", allSplits: count < 1000);
        }
        CompareEverySplit("\u001b[" + new string(';', 24) + "mX");
    }

    [Fact]
    public void UntilGroundStopsAtC1TerminationBeforeFollowingText()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(12, 3));
        processor.Process("\u001b[31"u8);
        Assert.True(processor.ProcessUntilGround(new byte[] { 0x9C, (byte)'X' }, out int consumed));
        Assert.Equal(1, consumed);
        Assert.Equal(0, processor.CursorCol);
        Assert.Empty(processor.GetContinuation());
    }

    private void CompareEverySplit(string sequence, bool allSplits = true)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native CSI differential available: {available}");
        if (!available) return;
        byte[] bytes = Encoding.Latin1.GetBytes(sequence);
        TerminalScreen nativeScreen = new(12, 3);
        using GhosttyVtProcessor native = new(nativeScreen);
        native.Process(bytes);
        for (int split = 0; split <= (allSplits ? bytes.Length : 0); split++)
        {
            TerminalScreen managedScreen = new(12, 3);
            using BasicVtProcessor managed = new(managedScreen);
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            Assert.True(managed.IsParserGround);
            Assert.Equal((native.CursorRow, native.CursorCol), (managed.CursorRow, managed.CursorCol));
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 12; column++)
                {
                    TerminalCell expected = nativeScreen.GetViewportRow(row)[column];
                    TerminalCell actual = managedScreen.GetViewportRow(row)[column];
                    Assert.Equal(expected.Codepoint, actual.Codepoint);
                    if (expected.Codepoint == 0) continue;
                    Assert.Equal(expected.ForegroundIdentity, actual.ForegroundIdentity);
                    Assert.Equal(expected.BackgroundIdentity, actual.BackgroundIdentity);
                    Assert.Equal(expected.UnderlineIdentity, actual.UnderlineIdentity);
                    Assert.Equal(expected.Attributes, actual.Attributes);
                    Assert.Equal(expected.UnderlineStyle, actual.UnderlineStyle);
                    Assert.Equal(expected.Decorations, actual.Decorations);
                }
        }
    }
}
