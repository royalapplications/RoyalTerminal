// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Owned TERMINAL payload. Source policies are metadata, never allocation requests.
/// Strings retain arbitrary bytes; decoding does not assume UTF-8 or NUL termination.
/// </summary>
internal sealed class GhosttySnapshotTerminalState
{
    private readonly byte[] _variableState;
    private readonly int _paletteOffset;
    private readonly int _pwdOffset;
    private readonly int _pwdLength;
    private readonly int _titleOffset;

    private GhosttySnapshotTerminalState(GhosttySnapshotTerminalHeader header, byte[] variableState,
        int paletteOffset, int pwdOffset, int pwdLength, int titleOffset)
    {
        Header = header;
        _variableState = variableState;
        _paletteOffset = paletteOffset;
        _pwdOffset = pwdOffset;
        _pwdLength = pwdLength;
        _titleOffset = titleOffset;
    }

    internal GhosttySnapshotTerminalHeader Header { get; }
    internal ReadOnlySpan<byte> Pwd => _variableState.AsSpan(_pwdOffset, _pwdLength);
    internal ReadOnlySpan<byte> Title => _variableState.AsSpan(_titleOffset);

    internal bool IsTabStop(int column)
    {
        if ((uint)column >= Header.Columns) throw new ArgumentOutOfRangeException(nameof(column));
        return (_variableState[column / 8] & (1 << (column % 8))) != 0;
    }

    internal uint OriginalPaletteColor(int index)
    {
        ValidatePaletteIndex(index);
        return Rgb(_paletteOffset + index * 3);
    }

    internal bool HasPaletteOverride(int index)
    {
        ValidatePaletteIndex(index);
        return (_variableState[_paletteOffset + 768 + index / 8] & (1 << (index % 8))) != 0;
    }

    internal uint CurrentPaletteColor(int index)
    {
        if (!HasPaletteOverride(index)) return OriginalPaletteColor(index);
        ReadOnlySpan<byte> mask = _variableState.AsSpan(_paletteOffset + 768, 32);
        int rank = 0;
        for (int i = 0; i < index / 8; i++) rank += BitOperations.PopCount((uint)mask[i]);
        rank += BitOperations.PopCount((uint)(mask[index / 8] & ((1 << (index % 8)) - 1)));
        return Rgb(_paletteOffset + 800 + rank * 3);
    }

    internal static GhosttySnapshotTerminalState Read(ReadOnlySpan<byte> payload, int maximumCells,
        int maximumStringBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumStringBytes);
        GhosttySnapshotTerminalHeader header = GhosttySnapshotTerminalHeader.Read(payload, maximumCells);
        ReadOnlySpan<byte> state = payload[GhosttySnapshotTerminalHeader.Length..];
        int tabs = (header.Columns + 7) / 8;
        if (state.Length < tabs + 800) throw new EndOfStreamException();
        int overrides = 0;
        foreach (byte bits in state.Slice(tabs + 768, 32)) overrides += BitOperations.PopCount((uint)bits);
        int offset = tabs + 800 + overrides * 3;
        int pwdLength = ReadString(state, ref offset, maximumStringBytes);
        int pwdOffset = offset - pwdLength;
        int titleLength = ReadString(state, ref offset, maximumStringBytes - pwdLength);
        int titleOffset = offset - titleLength;
        if (offset != state.Length) throw new InvalidDataException("Snapshot TERMINAL has trailing payload bytes.");
        // No untrusted variable-size allocation occurs before complete bounds validation.
        byte[] owned = state.ToArray();
        if (header.Columns % 8 != 0) owned[tabs - 1] &= (byte)((1 << (header.Columns % 8)) - 1);
        return new(header, owned, tabs, pwdOffset, pwdLength, titleOffset);
    }

    internal void WritePayloadTo(Stream destination)
    {
        Header.WriteTo(destination);
        destination.Write(_variableState);
    }

    private static int ReadString(ReadOnlySpan<byte> state, ref int offset, int maximumBytes)
    {
        if (offset > state.Length - 4) throw new EndOfStreamException();
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(state[offset..]);
        offset += 4;
        if (length > maximumBytes) throw new InvalidDataException("Snapshot terminal strings exceed the configured byte limit.");
        if (length > state.Length - offset) throw new EndOfStreamException();
        offset += (int)length;
        return (int)length;
    }

    private uint Rgb(int offset) => (uint)(_variableState[offset] << 16 |
        _variableState[offset + 1] << 8 | _variableState[offset + 2]);

    private static void ValidatePaletteIndex(int index)
    {
        if ((uint)index >= 256) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
