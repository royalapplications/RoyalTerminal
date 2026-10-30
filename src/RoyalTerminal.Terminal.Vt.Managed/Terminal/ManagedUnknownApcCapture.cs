// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

internal enum ManagedUnknownApcAllocation { Growth, OwnedContent }

// One processor's mutable scratch owner: do not copy an active instance.
// Unlike native malloc/free, small CLR scratch is reused across commands;
// unusually large captures are released/transferred at the command boundary.
internal struct ManagedUnknownApcCapture
{
    internal const int RetainedCapacityLimit = 4096;
    private byte[]? _buffer;
    private int _count;
    private bool _truncated;

    internal int MaximumBytes { get; private set; }
    internal int Capacity => _buffer?.Length ?? 0;
    internal bool Truncated => _truncated;
    internal Action<ManagedUnknownApcAllocation>? AllocationCheckpoint { get; set; }

    internal void Begin(int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        Reset(maximumBytes);
        MaximumBytes = maximumBytes;
    }

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        int retained = Math.Min(bytes.Length, MaximumBytes - _count);
        if (retained < bytes.Length) _truncated = true;
        int required = _count + retained;
        if (required > Capacity)
        {
            int capacity = (int)Math.Min(MaximumBytes, Math.Max(required, Math.Max((long)Capacity * 2, 1)));
            try
            {
                AllocationCheckpoint?.Invoke(ManagedUnknownApcAllocation.Growth);
                byte[] replacement = new byte[capacity];
                _buffer.AsSpan(0, _count).CopyTo(replacement);
                _buffer = replacement;
            }
            catch (OutOfMemoryException)
            {
                // Native UnknownBuilder drops this append, keeps the old
                // capture, marks it truncated and accepts later appends.
                _truncated = true;
                return;
            }
        }
        bytes[..retained].CopyTo(_buffer.AsSpan(_count, retained));
        _count = required;
    }

    internal byte[] Finish(out bool truncated)
    {
        truncated = _truncated;
        try
        {
            if (_count == 0) return [];
            if (_count == Capacity && Capacity > RetainedCapacityLimit)
            {
                // Large exact-size captures can transfer ownership without
                // another copy or keeping a large array in an idle processor.
                byte[] owned = _buffer!;
                _buffer = null;
                return owned;
            }
            AllocationCheckpoint?.Invoke(ManagedUnknownApcAllocation.OwnedContent);
            byte[] result = new byte[_count];
            _buffer.AsSpan(0, _count).CopyTo(result);
            return result;
        }
        catch (OutOfMemoryException)
        {
            truncated = true;
            return [];
        }
        finally { Reset(MaximumBytes); }
    }

    internal void Reset(int capacityLimit = RetainedCapacityLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityLimit);
        _count = 0;
        _truncated = false;
        MaximumBytes = 0;
        if (Capacity > Math.Min(capacityLimit, RetainedCapacityLimit)) _buffer = null;
    }
}
