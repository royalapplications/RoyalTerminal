// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty 90bce0d2d scrollMarginsBegin keeps clipping/removal and falls back to
// generic pin tracking for each failed restore append. WT ImageSlice moves row
// pixel slices and xterm Buffer tracks line markers; neither defines this Kitty
// pin-restore failure contract. Royal follows Ghostty for margins and uses the
// same safe degradation for its extra marginless/history compatibility scratch.
public sealed class ManagedKittyScrollFailureTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void OneFailedGrowthKeepsOtherRestoresAndAllowsRetry(int kind, bool growExisting)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(kind == 2);
        int count = growExisting ? 6 : 3;
        for (uint id = 1; id <= (growExisting ? 4 : count); id++) Add(screen, store, id);
        if (growExisting)
        {
            Begin(kind, screen, store);
            store.EndMarginScroll(screen);
            MoveAll(screen, store, 3);
            Add(screen, store, 5);
            Add(screen, store, 6);
        }
        TerminalScreen held = screen.CreateStateCopy();
        int attempts = 0;
        Begin(kind, screen, store, stage =>
        {
            Assert.Equal(ManagedKittyStoreAllocation.ScrollRestoreCapacity, stage);
            if (++attempts == 1) throw new OutOfMemoryException();
        });
        screen.ShiftAnchorsInViewportRows(0, 6, 1);
        store.EndMarginScroll(screen);
        Assert.Equal(2, attempts); // A later placement retries growth successfully.
        int restored = 0, generic = 0;
        foreach (var entry in store.Placements)
        {
            TerminalScreenAnchor anchor = entry.Value.Anchor!;
            Assert.True(screen.TryResolveAnchor(anchor, out var current));
            int row = current.Row - screen.GetAbsoluteRowForViewportRow(0);
            if (row == (kind == 0 ? 2 : 3)) restored++;
            if (row == 4) generic++;
            Assert.True(held.TryResolveAnchor(anchor, out var original));
            Assert.Equal(held.GetAbsoluteRowForViewportRow(3), original.Row);
        }
        Assert.Equal(count - 1, restored);
        Assert.Equal(1, generic);
        Assert.Equal(count, screen.TrackedAnchorCount);
        Assert.False(screen.SnapshotMutationFailed);
        MoveAll(screen, store, 3);
        Begin(kind, screen, store, _ => throw new InvalidOperationException("Retained capacity must be reused"));
        screen.ShiftAnchorsInViewportRows(0, 6, 1);
        store.EndMarginScroll(screen);
        foreach (var entry in store.Placements)
        {
            Assert.True(screen.TryResolveAnchor(entry.Value.Anchor!, out var current));
            Assert.Equal(screen.GetAbsoluteRowForViewportRow(kind == 0 ? 2 : 3), current.Row);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WarmRestoreScratchDoesNotAllocateOrInvokeCapacityCheckpoint(int kind)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(kind == 2);
        for (uint id = 1; id <= 6; id++) Add(screen, store, id);
        Action<ManagedKittyStoreAllocation> unexpected = _ => throw new InvalidOperationException();
        // A zero delta keeps every placement eligible through repeated cycles.
        void Cycle(Action<ManagedKittyStoreAllocation>? checkpoint)
        {
            if (kind == 0) store.BeginMarginScroll(screen, 1, 5, 0, 10, 10, allocationCheckpoint: checkpoint);
            else if (kind == 1) store.BeginPinScroll(screen, 0, false, checkpoint);
            else Assert.True(store.BeginHistoryErase(screen, checkpoint));
            store.EndMarginScroll(screen);
        }
        Cycle(null);
        for (int i = 0; i < 100; i++) Cycle(unexpected);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Cycle(unexpected);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FailedRestoreAppendDoesNotSkipClippingOrOrphanCleanup()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(false);
        Add(screen, store, 1, row: 1);
        Add(screen, store, 2, options: ",P=1,Q=1");
        Add(screen, store, 3, row: 1, options: ",r=4");
        ManagedKittyGraphicsStore.Placement clipped = store.Placements.Single(p => p.Key.ImageId == 3).Value;
        store.Publish(screen, 10, 10);
        TerminalScreen held = screen.CreateStateCopy();
        ulong revision = store.Revision;
        int failures = 0;
        store.BeginMarginScroll(screen, 1, 5, -1, 10, 10, allocationCheckpoint: _ =>
        {
            failures++;
            throw new OutOfMemoryException();
        });
        Assert.Equal(1, failures);
        Assert.Equal(1, store.PlacementCount); // Fully clipped parent and relative child retired.
        Assert.Equal(3, store.ImageCount); // Clipping never deletes reusable image data.
        Assert.Equal(2u, clipped.Options.SourceY);
        Assert.Equal(6u, clipped.Options.SourceHeight);
        Assert.True(store.Revision > revision);
        screen.ShiftAnchorsInViewportRows(1, 5, -1);
        store.EndMarginScroll(screen);
        store.Publish(screen, 10, 10); // Generic tracking pruned the failed root.
        Assert.Equal(0, store.PlacementCount);
        Assert.Equal(0, screen.TrackedAnchorCount);
        Assert.Equal(3, store.ImageCount);
        Assert.Equal(3, held.GetKittyPlacements().Length);
        foreach (TerminalKittyImagePlacement placement in held.GetKittyPlacements()) Assert.Equal(8, placement.SourceHeight);
        Assert.False(screen.SnapshotMutationFailed);
    }

    [Theory]
    [InlineData("margin-up", "\u001b[2;6r\u001b[S", 1, 'C')]
    [InlineData("margin-down", "\u001b[2;6r\u001b[T", 2, 'B')]
    [InlineData("delete-lines", "\u001b[3;1H\u001b[M", 2, 'D')]
    [InlineData("insert-lines", "\u001b[3;1H\u001b[L", 3, 'C')]
    [InlineData("index", "\u001b[2;6r\u001b[6;1H\u001bD", 1, 'C')]
    [InlineData("pin-down", "\u001b[T", 1, 'A')]
    [InlineData("history", "\u001b[3J", 0, 'A')]
    public void ProcessorFinishesTextOperationWhenEveryRestoreGrowthFails(string kind, string operation, int row, char expected)
    {
        TerminalScreen screen = new(8, 7, 20);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 7, 80, 70);
        if (kind == "history") Write(processor, "\u001b[7;1H\r\n");
        Write(processor, "\u001b[1;1HA\u001b[2;1HB\u001b[3;1HC\u001b[4;1HD\u001b[5;1HE\u001b[6;1HF\u001b[7;1HG" +
            "\u001b[3;2H\u001b_Ga=T,i=1,p=1,s=1,v=1,c=1,r=1,C=1;AQIDBA==\u001b\\");
        int failures = 0;
        processor.KittyStoreAllocationCheckpoint = stage =>
        {
            if (stage != ManagedKittyStoreAllocation.ScrollRestoreCapacity) return;
            failures++;
            throw new OutOfMemoryException();
        };
        Write(processor, operation);
        Assert.Equal(1, failures);
        Assert.Equal(expected, screen.GetViewportRow(row).ReadOnlyCells[0].Codepoint);
        Assert.False(screen.SnapshotMutationFailed);
        Assert.True(processor.IsParserGround);
        if (kind == "history") Assert.Equal(screen.ViewportRows, screen.TotalRows);
        processor.KittyStoreAllocationCheckpoint = null;
        Write(processor, "\u001b[r\u001b[1;1HZ");
        Assert.Equal('Z', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void EmptyOrIneligibleScenesDoNotTryToGrowRestoreScratch()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(false);
        Action<ManagedKittyStoreAllocation> unexpected = _ => throw new InvalidOperationException();
        store.BeginPinScroll(screen, 0, false, unexpected);
        Assert.False(store.BeginHistoryErase(screen, unexpected));
        Add(screen, store, 1, row: 0);
        Add(screen, store, 2, options: ",U=1");
        store.BeginMarginScroll(screen, 1, 5, -1, 10, 10, allocationCheckpoint: unexpected);
        store.EndMarginScroll(screen);
    }

    [Fact]
    public void NonAllocationFailureIsNotSilenced()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(false);
        Add(screen, store, 1);
        InvalidOperationException failure = new("Not allocation failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            store.BeginPinScroll(screen, 0, false, _ => throw failure)));
        Assert.Equal(1, screen.TrackedAnchorCount);
    }

    private static (TerminalScreen, ManagedKittyGraphicsStore) Scene(bool history)
    {
        TerminalScreen screen = new(8, 7, 20);
        if (history) { screen.AddRow(); screen.AddRow(); }
        return (screen, new(4096));
    }

    private static void Add(TerminalScreen screen, ManagedKittyGraphicsStore store, uint id,
        int row = 3, string options = "")
    {
        ManagedKittyImagePixels pixels = new(new(1, 8, new byte[32]));
        Assert.True(store.TryAddImage(screen, id, 0, pixels, false, out _));
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes($"a=p,i={id},p=1,c=1{(options.Contains("r=") ? "" : ",r=1")}{options}"), 0, out var command));
        Assert.True(store.TryAddPlacement(screen, store.Find(id)!, command,
            screen.GetAbsoluteRowForViewportRow(row), 0, 10, 10, out _, out _));
    }

    private static void MoveAll(TerminalScreen screen, ManagedKittyGraphicsStore store, int row)
    {
        foreach (var entry in store.Placements)
            Assert.True(screen.MoveAnchor(entry.Value.Anchor!, screen.GetAbsoluteRowForViewportRow(row), 0));
    }

    private static void Begin(int kind, TerminalScreen screen, ManagedKittyGraphicsStore store,
        Action<ManagedKittyStoreAllocation>? checkpoint = null)
    {
        if (kind == 0) store.BeginMarginScroll(screen, 1, 5, -1, 10, 10, allocationCheckpoint: checkpoint);
        else if (kind == 1) store.BeginPinScroll(screen, 0, false, checkpoint);
        else Assert.True(store.BeginHistoryErase(screen, checkpoint));
    }

    private static void Write(BasicVtProcessor processor, string text) => processor.Process(Encoding.ASCII.GetBytes(text));
}
