// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty b40acce graphics_storage.zig deletes matching placements in place,
// then reaps relative descendants with the same uppercase/lowercase policy.
// It allocates no selection scratch. Follow that ordering, including unmatched
// placement IDs and initially-unplaced images. WT uses row-owned image slices
// and xterm ImageStorage uses marker/tile ownership; neither is the oracle for
// Ghostty's relative-placement or uppercase-delete rules. Dictionary.Remove on
// .NET Core 3+ preserves active enumerators; no additions occur during deletion.
public sealed class ManagedKittyDeletionTests
{
    public static IEnumerable<object[]> Selectors()
    {
        for (int selector = 0; selector < 8; selector++)
            foreach (bool upper in new[] { false, true }) yield return [selector, upper];
    }

    [Theory]
    [MemberData(nameof(Selectors))]
    public void ActualDeletionAllocatesNoSelectionScratch(int selector, bool upper)
    {
        (TerminalScreen warmScreen, ManagedKittyGraphicsStore warmStore) = Scene();
        _ = Delete(warmStore, warmScreen, selector, upper);
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool changed = Delete(store, screen, selector, upper);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(changed);
        Assert.Equal(0, allocated);
        Assert.Equal(selector == 7 ? 0 : 1, store.PlacementCount);
        Assert.Equal(selector == 7 ? 0 : 1, screen.TrackedAnchorCount);
        Assert.Equal(upper ? selector == 7 ? 1 : 2 : 5, store.ImageCount);
        Assert.Equal(store.ImageCount * 4L, store.StoredBytes);
        Assert.NotNull(store.Find(5)); // Unplaced data is not selected by these commands.
        ulong revision = store.Revision;
        Assert.False(Delete(store, screen, selector, upper));
        Assert.Equal(revision, store.Revision);
    }

    [Theory]
    [MemberData(nameof(Selectors))]
    public void ProtocolDeletionAndRedisplayMatchPinnedNative(int selector, bool upper)
    {
        if (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
            Assert.Skip("Pinned native Kitty graphics runtime unavailable.");
        }
        TerminalScreen managedScreen = new(8, 8), nativeScreen = new(8, 8);
        using IVtProcessor managed = new BasicVtProcessor(managedScreen);
        using IVtProcessor native = new GhosttyVtProcessor(nativeScreen);
        List<string> managedReplies = [], nativeReplies = [];
        managed.ResponseCallback = bytes => managedReplies.Add(Encoding.ASCII.GetString(bytes));
        native.ResponseCallback = bytes => nativeReplies.Add(Encoding.ASCII.GetString(bytes));
        foreach (IVtProcessor processor in new[] { managed, native })
        {
            processor.NotifyResize(8, 8, 80, 80);
            Send(processor, "a=T,i=1,p=1,s=1,v=1,c=1,r=1,z=11,C=1;AQIDBA==");
            Send(processor, "a=T,i=2,p=1,P=1,Q=1,H=1,V=1,s=1,v=1,c=1,r=1,z=22,C=1;AQIDBA==");
            Send(processor, "a=T,i=3,p=1,P=2,Q=1,H=1,V=1,s=1,v=1,c=1,r=1,z=33,C=1;AQIDBA==");
            processor.Process("\u001b[5;5H"u8);
            Send(processor, "a=T,i=4,p=1,s=1,v=1,c=1,r=1,z=44,C=1;AQIDBA==");
            Send(processor, "a=t,i=5,s=1,v=1;AQIDBA==");
            Send(processor, "a=T,i=6,p=1,U=1,s=1,v=1,c=1,r=1,z=11;AQIDBA==");
        }
        managedReplies.Clear();
        nativeReplies.Clear();
        string[] controls = ["i,i=1,p=1", "r,x=1,y=1", "z,z=11", "p,x=1,y=1", "q,x=1,y=1,z=11", "x,x=1", "y,y=1", "a"];
        string control = controls[selector];
        if (upper) control = char.ToUpperInvariant(control[0]) + control[1..];
        Send(managed, "a=d,d=" + control);
        Send(native, "a=d,d=" + control);
        Assert.Empty(managedReplies);
        Assert.Empty(nativeReplies);
        Assert.Equal(nativeScreen.GetKittyPlacements().ToArray().Select(p => (p.ImageId, p.ViewportColumn, p.ViewportRow)),
            managedScreen.GetKittyPlacements().ToArray().Select(p => (p.ImageId, p.ViewportColumn, p.ViewportRow)));
        for (int image = 1; image <= 6; image++)
        {
            Send(managed, $"a=p,i={image},p=99,C=1");
            Send(native, $"a=p,i={image},p=99,C=1");
        }
        Assert.Equal(nativeReplies, managedReplies);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReverseDictionaryOrderCascadesThroughTheMaximumParentDepth(bool upper)
    {
        TerminalScreen screen = new(10, 2);
        ManagedKittyGraphicsStore store = new(128);
        for (uint id = 1; id <= 9; id++) Add(store, screen, id, row: 0, column: (int)id - 1);
        // Children precede their parents in dictionary traversal, requiring
        // repeated orphan passes. Replacing keys also exercises free-slot reuse.
        for (uint id = 8; id >= 1; id--)
            Place(store, screen, id, $"a=p,i={id},p=1,P={id + 1},Q=1", 0, 0);
        Assert.True(store.DeleteById(screen, 9, 1, upper));
        Assert.Equal(0, store.PlacementCount);
        Assert.Equal(0, screen.TrackedAnchorCount);
        Assert.Equal(upper ? 0 : 9, store.ImageCount);
        Assert.Equal(upper ? 0 : 36, store.StoredBytes);
    }

    [Fact]
    public void OrphanRemovalCannotMakeAnUnmatchedIdSelectorFreeUnplacedData()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        Assert.True(store.RemovePlacement(screen, new(1, 1, false)));
        Assert.True(store.DeleteById(screen, 5, 99, deleteUnused: true)); // Reaps unrelated orphans.
        Assert.NotNull(store.Find(5)); // But p=99 did not match image 5.
        Assert.NotNull(store.Find(1)); // Already unplaced before this command.
        Assert.Null(store.Find(2));
        Assert.Null(store.Find(3));
        Assert.NotNull(store.Find(4));
    }

    [Fact]
    public void DeletingAStoreCopyPreservesOriginalAnchorsAndPublishedPixels()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        store.Publish(screen, 10, 10);
        TerminalScreen copyScreen = screen.CreateStateCopy();
        ManagedKittyGraphicsStore copy = store.CreateStateCopy();
        Assert.True(copy.DeleteByRange(copyScreen, 1, 5, deleteUnused: true));
        copy.Publish(copyScreen, 10, 10);
        Assert.Equal(5, store.ImageCount);
        Assert.Equal(4, store.PlacementCount);
        Assert.Equal(2, screen.TrackedAnchorCount);
        Assert.Equal(4, screen.GetKittyPlacements().Length);
        Assert.Empty(copyScreen.GetKittyPlacements().ToArray());
        Assert.Equal(0, copy.StoredBytes);
        Assert.Equal(0, copyScreen.TrackedAnchorCount);
    }

    [Fact]
    public void ScreenClearAlsoFreesInitiallyUnplacedImagesWithoutAllocating()
    {
        (TerminalScreen warmScreen, ManagedKittyGraphicsStore warmStore) = Scene();
        warmStore.ClearScreen(warmScreen, 10, 10);
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool changed = store.ClearScreen(screen, 10, 10);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(changed);
        Assert.Equal(0, allocated);
        Assert.Equal(0, store.ImageCount);
        Assert.Equal(0, store.PlacementCount);
        Assert.Equal(0, store.StoredBytes);
    }

    private static bool Delete(ManagedKittyGraphicsStore store, TerminalScreen screen, int selector, bool upper) => selector switch
    {
        0 => store.DeleteById(screen, 1, 1, upper),
        1 => store.DeleteByRange(screen, 1, 1, upper),
        2 => store.DeleteByZ(screen, 11, upper),
        3 => store.DeleteAtCell(screen, 0, 0, null, 10, 10, upper),
        4 => store.DeleteAtCell(screen, 0, 0, 11, 10, 10, upper),
        5 => store.DeleteByColumn(screen, 0, 10, 10, upper),
        6 => store.DeleteByRow(screen, 0, 10, 10, upper),
        _ => store.DeleteVisible(screen, 10, 10, upper),
    };

    private static (TerminalScreen, ManagedKittyGraphicsStore) Scene()
    {
        TerminalScreen screen = new(8, 8);
        ManagedKittyGraphicsStore store = new(128);
        Add(store, screen, 1, 0, 0, "z=11");
        Add(store, screen, 2, 0, 0, "P=1,Q=1,H=1,V=1,z=22");
        Add(store, screen, 3, 0, 0, "P=2,Q=1,H=1,V=1,z=33");
        Add(store, screen, 4, 4, 4, "z=44");
        Assert.True(store.TryAddImage(screen, 5, 0, new(new(1, 1, [1, 2, 3, 4])), false, out _));
        return (screen, store);
    }

    private static void Add(ManagedKittyGraphicsStore store, TerminalScreen screen, uint id, int row, int column, string options = "")
    {
        Assert.True(store.TryAddImage(screen, id, 0, new(new(1, 1, [1, 2, 3, 4])), false, out _));
        Place(store, screen, id, $"a=p,i={id},p=1,c=1,r=1" + (options.Length == 0 ? "" : "," + options), row, column);
    }
    private static void Place(ManagedKittyGraphicsStore store, TerminalScreen screen, uint id, string control, int row, int column)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(control), 0, out var command));
        Assert.True(store.TryAddPlacement(screen, store.Find(id)!, command, row, column, 10, 10, out _, out string error), error);
    }
    private static void Send(IVtProcessor processor, string command)
        => processor.Process(Encoding.ASCII.GetBytes("\u001b_G" + command + "\u001b\\"));
}
