// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty RefCountedSet reclaims tail IDs, preferred holes and dead buckets in
// existing page storage. Reusing a CLR wrapper must not change that ordering,
// deletion callbacks, collision probes, reference counts or COW ownership.
public sealed class GhosttySnapshotSetReuseTests
{
    [Theory]
    [InlineData(0)] // Tail trim.
    [InlineData(1)] // Preferred interior ID.
    [InlineData(2)] // Dead probe bucket.
    public void WarmReclamationReusesOneClearedWrapper(int mode)
    {
        Context context = new();
        GhosttySnapshotRefCountedSet<Value> set = new(16, context);
        int id = set.Add(new(0, 0));
        int anchor = mode == 0 ? 0 : set.Add(new(-1, 7));
        for (int i = 1; i <= 128; i++) id = Replace(set, id, i, mode);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 129; i <= 256; i++) id = Replace(set, id, i, mode);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(1, id);
        Assert.Equal(new(256, 0), set.Get(id));
        Assert.Equal(1, set.ReferenceCount(id));
        Assert.Equal(mode == 0 ? 1 : 2, set.Count);
        Assert.Equal(256, context.Deletions);
        if (anchor != 0) Assert.Equal(new(-1, 7), set.Get(anchor));
    }

    [Fact]
    public void CopiedSetsDoNotShareReusedWrapperOrDeletionContext()
    {
        Context originalContext = new(), copyContext = new();
        GhosttySnapshotRefCountedSet<Value> original = new(16, originalContext);
        int first = original.Add(new(1, 0)), anchor = original.Add(new(2, 7));
        original.Release(first);
        Assert.Equal(first, original.Add(new(3, 0))); // Leaves a dead-bucket wrapper in scratch.
        GhosttySnapshotRefCountedSet<Value> copy = original.Copy(copyContext);
        original.Release(first);
        Assert.Equal(first, original.Add(new(4, 0)));
        copy.Release(first);
        Assert.Equal(first, copy.Add(new(5, 0)));

        Assert.Equal(new(4, 0), original.Get(first));
        Assert.Equal(new(5, 0), copy.Get(first));
        Assert.Equal(new(2, 7), original.Get(anchor));
        Assert.Equal(new(2, 7), copy.Get(anchor));
        Assert.Equal(2, originalContext.Deletions);
        Assert.Equal(1, copyContext.Deletions);
    }

    [Fact]
    public void SpareWrapperDoesNotKeepDeletedReferencePayloadAlive()
    {
        (GhosttySnapshotRefCountedSet<Payload> set, WeakReference<Payload> deleted) = CreateSpareWithDeletedPayload();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        Assert.False(deleted.TryGetTarget(out _));
        Assert.Equal(2, set.Count);
        GC.KeepAlive(set);
    }

    [Fact]
    public void RecycledCollisionStateDoesNotChangeLookupOrReferenceCounts()
    {
        GhosttySnapshotRefCountedSet<Value> set = new(128, new Context());
        for (int id = 1; id <= 16; id++) Assert.Equal(id, set.Add(new(id, 127)));
        // Preferred holes force backshifts through a dense wrapped probe chain.
        for (int round = 0; round < 8; round++)
        {
            for (int id = 1; id <= 16; id += 2)
            {
                set.Release(id);
                Assert.Equal(GhosttySnapshotSetAddResult.Success, set.TryAddWithId(new(100 + round * 16 + id, 127), id, out int added));
                Assert.Equal(id, added);
            }
            for (int id = 1; id <= 16; id++)
            {
                Value value = new((id & 1) == 0 ? id : 100 + round * 16 + id, 127);
                Assert.Equal(id, set.Lookup(value));
                Assert.Equal(value, set.Get(id));
                Assert.Equal(1, set.ReferenceCount(id));
            }
        }
        Assert.Equal(16, set.Count);
    }

    private static int Replace(GhosttySnapshotRefCountedSet<Value> set, int id, int value, int mode)
    {
        set.Release(id);
        if (mode != 1) return set.Add(new(value, 0));
        _ = set.TryAddWithId(new(value, 0), id, out int added);
        return added;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (GhosttySnapshotRefCountedSet<Payload>, WeakReference<Payload>) CreateSpareWithDeletedPayload()
    {
        GhosttySnapshotRefCountedSet<Payload> set = new(16, new PayloadContext());
        Payload old = new(1, 0);
        int first = set.Add(old);
        _ = set.Add(new(2, 7));
        set.Release(first);
        Assert.Equal(first, set.Add(new(3, 0)));
        return (set, new(old));
    }

    private readonly record struct Value(int Id, ulong Hash);
    private sealed class Context : IGhosttySnapshotSetContext<Value>
    {
        internal int Deletions;
        public ulong Hash(Value value) => value.Hash;
        public bool Equal(Value left, Value right) => left == right;
        public void Deleted(Value value) => Deletions++;
    }

    private sealed record Payload(int Id, ulong Hash);
    private sealed class PayloadContext : IGhosttySnapshotSetContext<Payload>
    {
        public ulong Hash(Payload value) => value.Hash;
        public bool Equal(Payload left, Payload right) => left.Id == right.Id;
        public void Deleted(Payload value) { }
    }
}
