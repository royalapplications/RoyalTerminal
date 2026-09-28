// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotHistoryApplicationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PrimaryApplicabilityMatchesNativeAcrossResizeAndReset(int operation)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native history application differential available: {available}");
        if (!available) return;
        byte[] source = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        using GhosttySnapshotStateReader reader = new(source, new());
        TerminalScreen screen = GhosttySnapshotLiveScreen.Stage(reader.ReadReady(), TerminalTheme.Dark, 1000);
        GhosttySnapshotHistoryApplication application = new(screen);
        using GhosttySnapshotDecoder decoder = new(source);
        using GhosttyTerminal native = decoder.Ready();
        switch (operation)
        {
            case 1: screen.Resize(2, 4); native.Resize(2, 4); break;
            case 2: screen.Resize(3, 3); native.Resize(3, 3); break;
            case 3:
                screen.Resize(3, 3); screen.Resize(2, 3);
                native.Resize(3, 3); native.Resize(2, 3); break;
            case 4: screen.ClearAll(); native.Write("\u001bc"u8); break;
        }
        while (reader.ReadNextHistoryPage() is { } history)
        {
            Assert.True(decoder.Next());
            GhosttySnapshotHistoryProgress progress = application.Apply(screen, history, fitsScrollbackLimits: true);
            Assert.Equal((nuint)progress.Rows, decoder.GetProgressRows());
            Assert.Equal(progress.Remaining, decoder.GetProgressRemaining());
            Assert.Equal(history.Key, progress.Key);
            // Compatibility returning after the first drop cannot fill a gap.
            if (operation == 2) { screen.Resize(2, 3); native.Resize(2, 3); }
        }
        Assert.False(decoder.Next());
        Assert.Equal(source.Length, reader.SourceOffset);
    }

    [Theory]
    [InlineData(0)] // No removal, accepts crafted alternate history.
    [InlineData(1)] // Removed since READY.
    [InlineData(2)] // Removed and recreated since READY.
    public void AlternateGenerationMatchesNativeAndDoesNotDropPrimary(int operation)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native alternate generation differential available: {available}");
        if (!available) return;
        List<SnapshotTestRecord> original = SnapshotTestRecords.Fixture();
        byte[] header = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), 1);
        List<SnapshotTestRecord> records = [.. original.GetRange(0, 7),
            new(GhosttySnapshotRecordTag.History, header), original[8], original[7], original[8], original[9], original[11]];
        byte[] source = SnapshotTestRecords.Encode(records);
        using GhosttySnapshotStateReader reader = new(source, new());
        TerminalScreen screen = GhosttySnapshotLiveScreen.Stage(reader.ReadReady(), TerminalTheme.Dark, 1000);
        GhosttySnapshotHistoryApplication application = new(screen);
        using GhosttySnapshotDecoder decoder = new(source);
        using GhosttyTerminal native = decoder.Ready();
        if (operation > 0) { screen.ClearAll(); native.Write("\u001bc"u8); }
        if (operation == 2)
        {
            screen.SwitchToAlternateBuffer(clear: true); screen.SwitchToPrimaryBuffer();
            native.Write("\u001b[?47h\u001b[?47l"u8);
        }
        while (reader.ReadNextHistoryPage() is { } history)
        {
            Assert.True(decoder.Next());
            GhosttySnapshotHistoryProgress progress = application.Apply(screen, history, fitsScrollbackLimits: true);
            Assert.Equal((nuint)progress.Rows, decoder.GetProgressRows());
            Assert.Equal(progress.Remaining, decoder.GetProgressRemaining());
            Assert.Equal(history.Key == 0 || operation == 0, progress.Rows > 0);
        }
        Assert.False(decoder.Next());
    }

    [Fact]
    public void QuotaDropIsPermanentOnlyForItsScreen()
    {
        TerminalScreen screen = new(2, 3);
        screen.SwitchToAlternateBuffer(clear: true); screen.SwitchToPrimaryBuffer();
        GhosttySnapshotHistoryApplication application = new(screen);
        GhosttySnapshotPage page = Page(screen);
        Assert.Equal(0, application.Apply(screen, new(0, page, 2), fitsScrollbackLimits: false).Rows);
        Assert.Equal(0, application.Apply(screen, new(0, page, 1), fitsScrollbackLimits: true).Rows);
        Assert.Equal(1, application.Apply(screen, new(1, page, 0), fitsScrollbackLimits: true).Rows);
        Assert.Equal(3, screen.GetSnapshotRows(0)!.Count);
        Assert.Equal(4, screen.GetSnapshotRows(1)!.Count);
    }

    [Fact]
    public void CopyOnWritePublicationRetainsDecoderLineageButUnrelatedStateDoesNot()
    {
        TerminalScreen screen = new(2, 3);
        GhosttySnapshotHistoryApplication application = new(screen);
        GhosttySnapshotPage page = Page(screen);
        TerminalScreen copy = screen.CreateStateCopy();
        Assert.Equal(1, application.Apply(copy, new(0, page, 2), true).Rows);
        Assert.Equal(3, screen.TotalRows);
        screen.AdoptStateFrom(copy);
        Assert.Equal(1, application.Apply(screen, new(0, page, 1), true).Rows);
        Assert.Equal(5, screen.TotalRows);
        screen.AdoptStateFrom(new TerminalScreen(2, 3));
        Assert.Equal(0, application.Apply(screen, new(0, page, 0), true).Rows);
        Assert.Equal(3, screen.TotalRows);
    }

    [Fact]
    public void AlternateRemovalGenerationIsCopiedButNoOpRemovalDoesNotAdvanceIt()
    {
        TerminalScreen screen = new(2, 3);
        ulong initial = screen.GetSnapshotGeneration(1);
        screen.DiscardInactiveAlternateBuffer();
        Assert.Equal(initial, screen.GetSnapshotGeneration(1));
        screen.SwitchToAlternateBuffer(clear: true);
        screen.DiscardInactiveAlternateBuffer(); // Active screen must not be removed.
        Assert.Equal(initial, screen.GetSnapshotGeneration(1));
        screen.SwitchToPrimaryBuffer();
        TerminalScreen copy = screen.CreateStateCopy();
        copy.DiscardInactiveAlternateBuffer();
        Assert.Equal(initial, screen.GetSnapshotGeneration(1));
        Assert.Equal(initial + 1, copy.GetSnapshotGeneration(1));
        screen.AdoptStateFrom(copy);
        Assert.Equal(initial + 1, screen.GetSnapshotGeneration(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.GetSnapshotGeneration(2));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)] // Row prompt.
    [InlineData(2, true)] // Row continuation.
    [InlineData(3, false)] // Input cell only.
    [InlineData(4, true)] // Prompt cell with no row marker.
    public void PromptNotificationFollowsNativeAppliedPageScan(int kind, bool expected)
    {
        TerminalScreen screen = new(2, 3);
        TerminalRow row = new(2);
        if (kind == 1) row.SemanticPrompt = TerminalSemanticPrompt.Prompt;
        if (kind == 2) row.SemanticPrompt = TerminalSemanticPrompt.PromptContinuation;
        row[0].Codepoint = 'X';
        if (kind == 3) row[0].SemanticContent = TerminalSemanticContent.Input;
        if (kind == 4) row[0].SemanticContent = TerminalSemanticContent.Prompt;
        GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture([row], screen, 2);
        GhosttySnapshotHistoryApplication application = new(screen);
        Assert.Equal(expected, application.Apply(screen, new(0, page, 1), true).ContainsPrompt);
        GhosttySnapshotHistoryProgress dropped = application.Apply(screen, new(0, page, 0), false);
        Assert.False(dropped.ContainsPrompt);
        Assert.Equal(0, dropped.Rows);
    }

    [Fact]
    public void UndeclaredRoutingPoisonsApplicationWithoutMutatingEitherBuffer()
    {
        TerminalScreen screen = new(2, 3);
        GhosttySnapshotHistoryApplication application = new(screen);
        GhosttySnapshotPage page = Page(screen);
        screen.SwitchToAlternateBuffer(clear: true); // Not present in the READY cut.
        Assert.Throws<InvalidDataException>(() => application.Apply(screen, new(1, page, 0), true));
        Assert.Throws<InvalidOperationException>(() => application.Apply(screen, new(0, page, 0), true));
        Assert.Equal(3, screen.GetSnapshotRows(0)!.Count);
        Assert.Equal(3, screen.GetSnapshotRows(1)!.Count);
    }

    [Fact]
    public void DroppedPageDecisionsAllocateNothing()
    {
        TerminalScreen screen = new(2, 3);
        GhosttySnapshotHistoryApplication application = new(screen);
        GhosttySnapshotHistoryPage page = new(0, Page(screen), 0);
        application.Apply(screen, page, false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int rows = 0;
        for (int i = 0; i < 1000; i++) rows += application.Apply(screen, page, true).Rows;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0, rows);
    }

    private static GhosttySnapshotPage Page(TerminalScreen owner)
        => GhosttySnapshotLivePage.Capture([new TerminalRow(2)], owner, 2);
}
