// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Follow Ghostty ImageStorage.addImage/addPlacement reserve-before-mutation and
// graphics_exec.display's pin cleanup. Managed image/frame-list owners also
// allocate, so prepare them before eviction. xterm.js ImageStorage and Windows
// Terminal ImageSlice do not supply this Kitty quota/replacement contract.
public sealed class ManagedKittyStoreFailureTests
{
    [Theory]
    [InlineData((int)ManagedKittyStoreAllocation.ImageCapacity)]
    [InlineData((int)ManagedKittyStoreAllocation.Image)]
    public void ImagePreparationFailureDoesNotEvictExistingImages(int checkpoint)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        ManagedKittyGraphicsStore.Image[] originals = [store.Find(1)!, store.Find(2)!, store.Find(3)!];
        ulong revision = store.Revision;
        Assert.False(store.TryAddImage(screen, 4, 0, Pixels(), false, out string error,
            stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); }));
        Assert.Equal("ENOMEM: out of memory", error);
        Assert.Equal(revision, store.Revision);
        Assert.Equal(12, store.StoredBytes);
        Assert.Equal(3, store.ImageCount);
        Assert.Equal(3, store.PlacementCount);
        Assert.Equal(3, screen.TrackedAnchorCount);
        for (uint id = 1; id <= 3; id++) Assert.Same(originals[id - 1], store.Find(id));
        Assert.True(store.TryAddImage(screen, 4, 0, Pixels(), false, out error), error);
        Assert.Null(store.Find(1));
        Assert.Equal(12, store.StoredBytes);
        Assert.Equal(2, screen.TrackedAnchorCount);
        Assert.NotNull(store.Find(4));
    }

    [Fact]
    public void ReplacementPreparationFailureRetainsOldImageAndAnchors()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        ManagedKittyGraphicsStore.Image original = store.Find(1)!;
        ulong revision = store.Revision;
        ManagedKittyImagePixels larger = new(new(2, 1, [9, 8, 7, 6, 5, 4, 3, 2]));
        Assert.False(store.TryAddImage(screen, 1, 0, larger, false, out _,
            stage => { if (stage == ManagedKittyStoreAllocation.Image) throw new OutOfMemoryException(); }));
        Assert.Same(original, store.Find(1));
        Assert.Equal(revision, store.Revision);
        Assert.Equal(12, store.StoredBytes);
        Assert.Equal(3, store.PlacementCount);
        Assert.Equal(3, screen.TrackedAnchorCount);
        Assert.True(store.TryAddImage(screen, 1, 0, larger, false, out _));
        Assert.NotSame(original, store.Find(1));
        Assert.Null(store.Find(2));
        Assert.Equal(12, store.StoredBytes);
        Assert.Equal(1, screen.TrackedAnchorCount);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, original.Animation.CurrentPixels.Data.ToArray());
    }

    [Fact]
    public void ExistingImageAndPlacementKeysDoNotRequestExtraDictionaryCapacity()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        Action<ManagedKittyStoreAllocation> checkpoint = stage =>
        {
            if (stage is ManagedKittyStoreAllocation.ImageCapacity or ManagedKittyStoreAllocation.PlacementCapacity)
                throw new InvalidOperationException("Replacement needs no dictionary growth");
        };
        Assert.True(store.TryAddPlacement(screen, store.Find(2)!, Command("a=p,i=2,p=1"), 0, 1, 8, 16,
            out _, out _, checkpoint));
        Assert.True(store.TryAddImage(screen, 2, 0, Pixels(), false, out _, checkpoint));
        Assert.Equal(12, store.StoredBytes);
        Assert.Equal(2, screen.TrackedAnchorCount);
    }

    public static IEnumerable<object[]> PlacementFailures()
    {
        foreach (string kind in new[] { "", ",U=1", ",P=1,Q=1" })
        {
            yield return [kind, false, (int)ManagedKittyStoreAllocation.PlacementCapacity];
            foreach (bool replace in new[] { false, true })
            {
                yield return [kind, replace, (int)ManagedKittyStoreAllocation.Placement];
                if (kind.Length == 0) yield return [kind, replace, (int)ManagedKittyStoreAllocation.Anchor];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PlacementFailures))]
    public void PlacementPreparationFailurePreservesPreviousOwnership(string kind, bool replace, int checkpoint)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        ManagedKittyGraphicsStore.Image image = store.Find(2)!;
        ManagedKittyGraphicsStore.Placement original = store.Placements.Single(p => p.Key.ImageId == 2).Value;
        TerminalScreen held = screen.CreateStateCopy();
        ulong revision = store.Revision;
        ManagedKittyGraphicsCommand command = Command($"a=p,i=2,p={(replace ? 1 : 2)}{kind}");
        Assert.False(store.TryAddPlacement(screen, image, command, 0, 2, 8, 16, out var placement, out string error,
            stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); }));
        Assert.Null(placement);
        Assert.Equal(checkpoint == (int)ManagedKittyStoreAllocation.Anchor
            ? "EINVAL: failed to prepare terminal state" : "ENOMEM: out of memory", error);
        Assert.Equal(revision, store.Revision);
        Assert.Equal(3, store.PlacementCount);
        Assert.Equal(1, image.PlacementCount);
        Assert.Equal(3, screen.TrackedAnchorCount);
        Assert.Same(original, store.Placements.Single(p => p.Key.ImageId == 2).Value);
        Assert.True(screen.TryResolveAnchor(original.Anchor!, out _));
        Assert.True(store.TryAddPlacement(screen, image, command, 0, 2, 8, 16, out placement, out error), error);
        Assert.NotNull(placement);
        Assert.Equal(replace ? 3 : 4, store.PlacementCount);
        Assert.Equal(replace ? 1 : 2, image.PlacementCount);
        Assert.Equal(3 + (kind.Length == 0 ? 1 : 0) - (replace ? 1 : 0), screen.TrackedAnchorCount);
        Assert.True(held.TryResolveAnchor(original.Anchor!, out _));
        store.Clear(screen);
        Assert.Equal(0, screen.TrackedAnchorCount);
        Assert.Equal(3, held.TrackedAnchorCount);
    }

    [Theory]
    [InlineData((int)ManagedKittyStoreAllocation.ImageCapacity)]
    [InlineData((int)ManagedKittyStoreAllocation.Image)]
    [InlineData((int)ManagedKittyStoreAllocation.PlacementCapacity)]
    [InlineData((int)ManagedKittyStoreAllocation.Anchor)]
    [InlineData((int)ManagedKittyStoreAllocation.Placement)]
    public void NonAllocationFailuresPropagateWithoutLosingOwnership(int checkpoint)
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        InvalidOperationException failure = new("Not an allocation failure");
        ulong revision = store.Revision;
        Action<ManagedKittyStoreAllocation> allocation = stage => { if ((int)stage == checkpoint) throw failure; };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
        {
            if (checkpoint <= (int)ManagedKittyStoreAllocation.Image)
                store.TryAddImage(screen, 4, 0, Pixels(), false, out _, allocation);
            else store.TryAddPlacement(screen, store.Find(2)!, Command("a=p,i=2,p=2"), 0, 2, 8, 16, out _, out _, allocation);
        }));
        Assert.Equal(revision, store.Revision);
        Assert.Equal(12, store.StoredBytes);
        Assert.Equal(3, store.ImageCount);
        Assert.Equal(3, store.PlacementCount);
        Assert.Equal(3, screen.TrackedAnchorCount);
    }

    [Fact]
    public void AdmissionAndParentErrorsPrecedeAllocation()
    {
        (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene();
        Action<ManagedKittyStoreAllocation> unexpected = _ => throw new InvalidOperationException("No allocation expected");
        Assert.False(store.TryAddImage(screen, 4, 0, new(new(4, 1, new byte[16])), false, out string error, unexpected));
        Assert.Equal("ENOMEM: out of memory", error);
        Assert.False(store.TryAddPlacement(screen, store.Find(2)!, Command("a=p,i=2,p=2,U=1,P=1"),
            0, 2, 8, 16, out _, out error, unexpected));
        Assert.Equal("EINVAL: virtual placement cannot refer to a parent", error);
        Assert.False(store.TryAddPlacement(screen, store.Find(2)!, Command("a=p,i=2,p=2,P=99"),
            0, 2, 8, 16, out _, out error, unexpected));
        Assert.Equal("ENOPARENT: parent image not found", error);
    }

    [Theory]
    [InlineData((int)ManagedKittyStoreAllocation.ImageCapacity)]
    [InlineData((int)ManagedKittyStoreAllocation.Image)]
    [InlineData((int)ManagedKittyStoreAllocation.PlacementCapacity)]
    [InlineData((int)ManagedKittyStoreAllocation.Anchor)]
    [InlineData((int)ManagedKittyStoreAllocation.Placement)]
    public void ProcessorReportsStoreFailureAndCanRetry(int checkpoint)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0, KittyGraphicsStorageLimitBytes = 16 });
        for (int id = 1; id <= 3; id++) Send(processor, $"a=T,i={id},p=1,s=1,v=1,C=1;AQIDBA==");
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.KittyStoreAllocationCheckpoint = stage => { if ((int)stage == checkpoint) throw new OutOfMemoryException(); };
        Send(processor, "a=T,i=4,p=1,s=1,v=1;AQIDBA==");
        string expected = checkpoint == (int)ManagedKittyStoreAllocation.Anchor
            ? "EINVAL: failed to prepare terminal state" : "ENOMEM: out of memory";
        Assert.Equal($"\u001b_Gi=4,p=1;{expected}\u001b\\", Assert.Single(replies));
        Assert.Equal(3, screen.GetKittyPlacements().Length);
        Assert.Equal(3, screen.TrackedAnchorCount);
        Assert.False(screen.SnapshotMutationFailed);
        Assert.True(processor.IsParserGround);
        replies.Clear();
        processor.KittyStoreAllocationCheckpoint = null;
        // A failed display retains the already admitted image. A failed image
        // admission requires a fresh transmission, not a stale pending loader.
        Send(processor, checkpoint <= (int)ManagedKittyStoreAllocation.Image
            ? "a=T,i=4,p=1,s=1,v=1,C=1;AQIDBA==" : "a=p,i=4,p=1,C=1");
        Assert.Equal("\u001b_Gi=4,p=1;OK\u001b\\", Assert.Single(replies));
        Assert.Equal(4, screen.GetKittyPlacements().Length);
        Assert.Equal(4, screen.TrackedAnchorCount);
    }

    private static (TerminalScreen, ManagedKittyGraphicsStore) Scene()
    {
        TerminalScreen screen = new(8, 2);
        ManagedKittyGraphicsStore store = new(12);
        for (uint id = 1; id <= 3; id++)
        {
            Assert.True(store.TryAddImage(screen, id, 0, Pixels(), false, out _));
            Assert.True(store.TryAddPlacement(screen, store.Find(id)!, Command($"a=p,i={id},p=1"),
                0, (int)id - 1, 8, 16, out _, out _));
        }
        return (screen, store);
    }

    private static ManagedKittyImagePixels Pixels() => new(new(1, 1, [1, 2, 3, 4]));

    private static ManagedKittyGraphicsCommand Command(string control)
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(control), 0, out var command));
        return command;
    }

    private static void Send(BasicVtProcessor processor, string command)
        => processor.Process(Encoding.ASCII.GetBytes("\u001b_G" + command + "\u001b\\"));
}
