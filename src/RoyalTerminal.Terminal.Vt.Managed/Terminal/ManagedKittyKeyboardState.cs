// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Ghostty's eight-slot circular flag stack, packed without heap storage.</summary>
internal struct ManagedKittyKeyboardState
{
    private ulong _bits; // Eight five-bit flags, then the three-bit current index.
    internal readonly int Index => (int)(_bits >> 40);
    internal readonly int Current => (int)((_bits >> (Index * 5)) & 31);

    internal void Set(int flags)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)flags, 31u);
        int shift = Index * 5;
        _bits = (_bits & ~(31UL << shift)) | ((ulong)flags << shift);
    }

    internal void Push(int flags)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)flags, 31u);
        SetIndex((Index + 1) & 7);
        Set(flags);
    }

    internal void Pop(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count >= 8) { _bits = 0; return; }
        while (count-- > 0)
        {
            Set(0);
            SetIndex((Index + 7) & 7);
        }
    }

    internal static ManagedKittyKeyboardState FromSnapshot(int index, ReadOnlySpan<byte> flags)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, 7u);
        if (flags.Length != 8) throw new ArgumentException("Snapshot keyboard state requires eight slots.", nameof(flags));
        ManagedKittyKeyboardState state = default;
        for (int slot = 0; slot < 8; slot++)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(flags[slot], (byte)31);
            state._bits |= (ulong)flags[slot] << (slot * 5);
        }
        state.SetIndex(index);
        return state;
    }

    internal readonly void CopyFlagsTo(Span<byte> destination)
    {
        if (destination.Length < 8) throw new ArgumentException("Eight destination bytes are required.", nameof(destination));
        for (int slot = 0; slot < 8; slot++) destination[slot] = (byte)((_bits >> (slot * 5)) & 31);
    }

    private void SetIndex(int index) => _bits = (_bits & ((1UL << 40) - 1)) | ((ulong)index << 40);
}
