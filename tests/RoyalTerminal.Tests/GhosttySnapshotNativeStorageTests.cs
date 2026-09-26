// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotNativeStorageTests
{
    // Zig 0.16 std/hash/wyhash.zig vectors (wyhash upstream test_vector.cpp).
    [Theory]
    [InlineData(0UL, "", 0x0409638ee2bde459UL)]
    [InlineData(1UL, "a", 0xa8412d091b5fe0a9UL)]
    [InlineData(2UL, "abc", 0x32dd92e4b2915153UL)]
    [InlineData(3UL, "message digest", 0x8619124089a3a16bUL)]
    [InlineData(4UL, "abcdefghijklmnopqrstuvwxyz", 0x7a43afb61d7f5f40UL)]
    [InlineData(5UL, "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 0xff42329b90e50d58UL)]
    [InlineData(6UL, "12345678901234567890123456789012345678901234567890123456789012345678901234567890", 0xc39cab13b115aad3UL)]
    public void WyhashMatchesNativeVectorsForEveryFragmentSize(ulong seed, string input, ulong expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        Span<byte> scratch = stackalloc byte[48];
        for (int size = 1; size <= bytes.Length + 1; size++)
        {
            GhosttySnapshotWyhash hash = new(scratch, seed);
            for (int offset = 0; offset < bytes.Length; offset += size)
                hash.Update(bytes.AsSpan(offset, Math.Min(size, bytes.Length - offset)));
            Assert.Equal(expected, hash.Finish());
            Assert.Equal(expected, hash.Finish());
        }
    }

    [Fact]
    public void WyhashMaintainsThePrecedingTailAcrossFortyEightByteBoundaries()
    {
        byte[] bytes = Encoding.ASCII.GetBytes(new string('Z', 48) + "01234567890abcdefg");
        Span<byte> first = stackalloc byte[48], second = stackalloc byte[48];
        for (int length = 49; length <= bytes.Length; length++)
        {
            GhosttySnapshotWyhash whole = new(first);
            whole.Update(bytes.AsSpan(0, length));
            for (int split = 0; split <= length; split++)
            {
                GhosttySnapshotWyhash divided = new(second);
                divided.Update(bytes.AsSpan(0, split));
                divided.Update(bytes.AsSpan(split, length - split));
                Assert.Equal(whole.Finish(), divided.Finish());
            }
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void BitmapSmallSpansCannotCombineAdjacentWordHoles(int chunk)
    {
        GhosttySnapshotBitmap bitmap = new((uint)(128 * chunk), chunk);
        Assert.True(bitmap.TryAllocate(60 * chunk, out var left));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(0, 60), left);
        Assert.True(bitmap.TryAllocate(4 * chunk, out var end));
        Assert.True(bitmap.TryAllocate(4 * chunk, out var start));
        Assert.True(bitmap.TryAllocate(60 * chunk, out var right));
        bitmap.Free(end); bitmap.Free(start);
        Assert.False(bitmap.TryAllocate(8 * chunk, out _)); // Eight contiguous bits, but across a word.
        Assert.True(bitmap.TryAllocate(4 * chunk, out var reused));
        Assert.Equal(end, reused);
        bitmap.Free(left); bitmap.Free(right); bitmap.Free(reused);
        Assert.True(bitmap.TryAllocate(128 * chunk, out var all));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(0, 128), all);
        Assert.False(bitmap.TryAllocate(1, out _));
    }

    [Fact]
    public void LargeBitmapSpansCrossWordsAndFailSafelyAtTheLastWord()
    {
        GhosttySnapshotBitmap bitmap = new(192 * 32, 32);
        Assert.True(bitmap.TryAllocate(60 * 32, out var prefix));
        Assert.True(bitmap.TryAllocate(65 * 32, out var large));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(60, 65), large);
        Assert.True(bitmap.TryAllocate(67 * 32, out var tail));
        Assert.Equal(new GhosttySnapshotBitmap.Slice(125, 67), tail);
        bitmap.Free(large);
        Assert.True(bitmap.TryAllocate(65 * 32, out var replacement));
        Assert.Equal(large, replacement);
        bitmap.Free(prefix);
        Assert.False(bitmap.TryAllocate(66 * 32, out _));
        GhosttySnapshotBitmap tooSmall = new(32, 32); // rounds to one word
        Assert.False(tooSmall.TryAllocate(65 * 32, out _));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 63)]
    [InlineData(3, 0)]
    [InlineData(3, 63)]
    [InlineData(3, 64)]
    [InlineData(3, 127)]
    public void LargeSpanPastLastWordLeavesAllAvailableChunksUntouched(int words, int prefixChunks)
    {
        foreach (int chunkBytes in new[] { 16, 32 })
        {
            GhosttySnapshotBitmap bitmap = new((uint)(words * 64 * chunkBytes), chunkBytes);
            if (prefixChunks != 0) Assert.True(bitmap.TryAllocate(prefixChunks * chunkBytes, out _));
            int remaining = words * 64 - prefixChunks;
            Assert.False(bitmap.TryAllocate((remaining + 1) * chunkBytes, out _));
            Assert.True(bitmap.TryAllocate(remaining * chunkBytes, out var tail));
            Assert.Equal(new GhosttySnapshotBitmap.Slice(prefixChunks, remaining), tail);
            Assert.False(bitmap.TryAllocate(1, out _));
        }
    }

    [Fact]
    public void DeadSetItemsRemainUntilTrimOrProbeReclaimsThem()
    {
        Context context = new();
        GhosttySnapshotRefCountedSet<Value> set = new(4, context); // two usable IDs
        Value a = new(1, 0), b = new(2, 1), c = new(3, 2);
        int first = set.Add(a), second = set.Add(b);
        Assert.Equal(1, first); Assert.Equal(2, second);
        set.Release(first);
        Assert.Empty(context.DeletedValues);
        Assert.Equal(0, set.Add(c)); // dead non-tail ID still requires native rehash
        set.Release(second);
        Assert.Equal(1, set.Add(c));
        Assert.Equal(new[] { 2, 1 }, context.DeletedValues);
    }

    [Fact]
    public void InsertReusesDeadProbeIdAndLookupDeduplicatesEvenWhenFull()
    {
        Context context = new();
        GhosttySnapshotRefCountedSet<Value> set = new(8, context);
        Value a = new(1, 0), b = new(2, 1), c = new(3, 0);
        int first = set.Add(a), second = set.Add(b);
        set.Release(first);
        Assert.Equal(first, set.Add(c));
        Assert.Equal(second, set.Add(b));
        Assert.Equal(new[] { 1, 2 }, context.DeletedValues); // old A and temporary duplicate B
        Assert.Equal(3, set.Add(new(4, 4)));
    }

    [Fact]
    public void ReapingATailIdBackshiftsDisplacedLiveEntries()
    {
        Context context = new();
        GhosttySnapshotRefCountedSet<Value> set = new(8, context);
        Assert.Equal(1, set.Add(new(1, 0)));
        Assert.Equal(2, set.Add(new(2, 0)));
        Value displaced = new(3, 1);
        Assert.Equal(3, set.Add(displaced));
        int tail = set.Add(new(4, 0));
        set.Release(tail);
        Assert.Equal(3, set.Add(displaced));
        Assert.Equal(new[] { 4, 3 }, context.DeletedValues);
    }

    [Fact]
    public void ProbeLimitRejectsNewValuesBeforeNominalCapacityButStillAllowsDuplicates()
    {
        Context context = new();
        GhosttySnapshotRefCountedSet<Value> set = new(128, context);
        for (int i = 0; i < 32; i++) Assert.Equal(i + 1, set.Add(new(i, 127)));
        Assert.Equal(0, set.Add(new(32, 127)));
        Assert.Equal(1, set.Add(new(0, 127)));
        set.Release(32);
        Assert.Equal(32, set.Add(new(32, 80)));
    }

    [Fact]
    public void LiveReferencesDistinguishRehashFromCapacityExhaustion()
    {
        GhosttySnapshotRefCountedSet<Value> set = new(4, new Context());
        int first = set.Add(new(1, 0)), last = set.Add(new(2, 1));
        Assert.Equal(2, set.Count);
        set.Use(first);
        Assert.Equal(2, set.ReferenceCount(first));
        set.Release(first);
        Assert.Equal(2, set.Count);
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, set.TryAdd(new(3, 2), out int id));
        Assert.Equal(0, id);
        set.Release(first);
        Assert.Equal(1, set.Count);
        Assert.Equal(GhosttySnapshotSetAddResult.NeedsRehash, set.TryAdd(new(3, 2), out id));
        Assert.Equal(0, id);
        // Lookup still precedes pressure checks, and a released tail is trimmed.
        Assert.Equal(GhosttySnapshotSetAddResult.Success, set.TryAdd(new(2, 1), out id));
        Assert.Equal(last, id);
        set.Release(last); set.Release(last);
        Assert.Equal(0, set.Count);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, set.TryAdd(new(3, 2), out id));
        Assert.Equal(1, id);
    }

    [Fact]
    public void RehashThresholdUsesNativeTruncationAndProbeFailureTakesPrecedence()
    {
        GhosttySnapshotRefCountedSet<Value> set = new(8, new Context()); // cap=6, threshold=5
        for (int i = 1; i <= 5; i++) Assert.Equal(i, set.Add(new(i, (ulong)i)));
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, set.TryAdd(new(6, 7), out _));
        set.Release(1);
        Assert.Equal(GhosttySnapshotSetAddResult.NeedsRehash, set.TryAdd(new(6, 7), out _));

        GhosttySnapshotRefCountedSet<Value> probes = new(128, new Context());
        for (int i = 1; i <= 32; i++) probes.Add(new(i, 0));
        probes.Release(1);
        Assert.Equal(GhosttySnapshotSetAddResult.OutOfMemory, probes.TryAdd(new(33, 63), out _));
    }

    [Fact]
    public void CopiesPreserveDeadSlotsButDoNotShareReferenceOrDeletionState()
    {
        Context sourceContext = new(), copyContext = new();
        GhosttySnapshotRefCountedSet<Value> source = new(4, sourceContext);
        int first = source.Add(new(1, 0)), last = source.Add(new(2, 1));
        source.Release(first);
        GhosttySnapshotRefCountedSet<Value> copy = source.Copy(copyContext);
        Assert.Equal(GhosttySnapshotSetAddResult.NeedsRehash, copy.TryAdd(new(3, 2), out _));
        copy.Release(last);
        Assert.Equal(1, copy.Add(new(3, 2)));
        Assert.Equal(new[] { 2, 1 }, copyContext.DeletedValues);
        Assert.Empty(sourceContext.DeletedValues);
        Assert.Equal(1, source.Count);
        Assert.Equal(new Value(2, 1), source.Get(last));
        Assert.Equal(GhosttySnapshotSetAddResult.NeedsRehash, source.TryAdd(new(3, 2), out _));
    }

    [Fact]
    public void PreferredIdsCanReviveAnInteriorHoleEvenWhenTheNextIdIsExhausted()
    {
        Context context = new();
        GhosttySnapshotRefCountedSet<Value> set = new(4, context);
        int first = set.Add(new(1, 0)), last = set.Add(new(2, 1));
        set.Release(first);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, set.TryAddWithId(new(3, 2), first, out int id));
        Assert.Equal(first, id);
        Assert.Equal(2, set.Count);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, set.TryAddWithId(new(3, 2), last, out id));
        Assert.Equal(first, id); // The preferred ID is occupied by another value.
        Assert.Equal(2, set.ReferenceCount(first));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, set.TryAddWithId(new(2, 1), last, out id));
        Assert.Equal(last, id);
        Assert.Equal(2, set.ReferenceCount(last));
        Assert.Equal(new[] { 1, 3, 2 }, context.DeletedValues);
    }

    [Fact]
    public void PreferredDeadIdDoesNotDisplaceAnAlreadyLivingEqualValue()
    {
        GhosttySnapshotRefCountedSet<Value> set = new(8, new Context());
        int first = set.Add(new(1, 0)), second = set.Add(new(2, 1));
        set.Release(first);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, set.TryAddWithId(new(2, 1), first, out int id));
        Assert.Equal(second, id);
        Assert.Equal(1, set.Count);
        Assert.Equal(2, set.ReferenceCount(second));
        Assert.Throws<InvalidOperationException>(() => set.Use(first));
        Assert.Throws<InvalidOperationException>(() => set.Release(first));
    }

    [Fact]
    public void GroupedReleaseKeepsLiveCountsAndRejectsUnderflowWithoutMutation()
    {
        GhosttySnapshotRefCountedSet<Value> set = new(4, new Context());
        int id = set.Add(new(1, 0));
        set.Use(id); set.Use(id);
        set.ReleaseMultiple(id, 2);
        Assert.Equal(1, set.Count);
        Assert.Equal(1, set.ReferenceCount(id));
        Assert.Throws<InvalidOperationException>(() => set.ReleaseMultiple(id, 2));
        Assert.Equal(1, set.ReferenceCount(id));
        set.ReleaseMultiple(id, 1);
        Assert.Equal(0, set.Count);
        set.ReleaseMultiple(0, 20);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, set.TryAdd(new(2, 1), out _));
    }

    [Fact]
    public void LargeCapacityHintsDoNotAllocateDenseStorage()
    {
        Context context = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        GhosttySnapshotBitmap bitmap = new(uint.MaxValue, 32);
        GhosttySnapshotRefCountedSet<Value> set = new(ushort.MaxValue, context);
        Assert.True(bitmap.TryAllocate(1, out _));
        Assert.Equal(1, set.Add(new(1, 12345)));
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(bytes, 1, 64 * 1024);
    }

    [Fact]
    public void GroupedUseChecksOverflowAndPreservesOneLiveEntry()
    {
        GhosttySnapshotRefCountedSet<Value> set = new(4, new Context());
        int id = set.Add(new(1, 0));
        set.UseMultiple(id, 100);
        Assert.Equal(101, set.ReferenceCount(id));
        Assert.Equal(1, set.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => set.UseMultiple(id, -1));
        set.UseMultiple(id, int.MaxValue - 101);
        Assert.Throws<OverflowException>(() => set.UseMultiple(id, 1));
        Assert.Equal(int.MaxValue, set.ReferenceCount(id));
        set.ReleaseMultiple(id, int.MaxValue);
        Assert.Equal(0, set.Count);
        set.UseMultiple(0, 20);
    }

    private readonly record struct Value(int Id, ulong Hash);
    private sealed class Context : IGhosttySnapshotSetContext<Value>
    {
        internal List<int> DeletedValues { get; } = [];
        public ulong Hash(Value value) => value.Hash;
        public bool Equal(Value left, Value right) => left == right;
        public void Deleted(Value value) => DeletedValues.Add(value.Id);
    }
}
