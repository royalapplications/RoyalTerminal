// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Validates replay-safe snapshot continuations without executing terminal input.</summary>
internal static class GhosttySnapshotContinuation
{
    // Ghostty parse_table.zig / stream_continuation.zig, snapshot v1. A valid
    // VT tail starts at its last ESC. Once that constraint is established,
    // any transition to ground or handler-visible action makes it invalid;
    // there is no need to build OSC/DCS/APC payloads or invoke their handlers.
    internal static bool IsValid(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return true;
        if (bytes[0] != 0x1B)
            return Rune.DecodeFromUtf8(bytes, out _, out _) == OperationStatus.NeedMoreData;
        if (bytes[1..].Contains((byte)0x1B)) return false;
        State state = State.Escape;
        foreach (byte value in bytes[1..])
        {
            if (value is 0x18 or 0x1A) return false;
            // OSC and DCS payloads override the anywhere C1 transitions.
            if (state is State.Osc or State.DcsData or State.DcsIgnore)
            {
                if (state == State.Osc && value == 7) return false;
                continue;
            }
            if (value is >= 0x80 and <= 0x9F)
            {
                State next = value switch
                {
                    0x90 => State.DcsEntry,
                    0x98 or 0x9E or 0x9F => State.Apc,
                    0x9B => State.CsiEntry,
                    0x9D => State.Osc,
                    _ => State.Invalid,
                };
                // Leaving an APC commits apc_end even if a new builder begins.
                if (next == State.Invalid || (state == State.Apc && next != State.Apc)) return false;
                state = next;
                continue;
            }
            if (state == State.Apc || value >= 0x7F) continue;
            if (value < 0x20)
            {
                // DCS header controls are ignored. ESC/CSI controls execute.
                if (state is State.DcsEntry or State.DcsParam or State.DcsIntermediate) continue;
                return false;
            }
            if (state == State.Escape)
            {
                state = value switch
                {
                    >= 0x20 and <= 0x2F => State.EscapeIntermediate,
                    (byte)'P' => State.DcsEntry,
                    (byte)'[' => State.CsiEntry,
                    (byte)']' => State.Osc,
                    (byte)'X' or (byte)'^' or (byte)'_' => State.Apc,
                    _ => State.Invalid,
                };
            }
            else if (state == State.EscapeIntermediate)
            {
                if (value >= 0x30) return false;
            }
            else if (state is State.CsiEntry or State.CsiParam or State.CsiIntermediate or State.CsiIgnore)
            {
                if (value >= 0x40) return false;
                if (state == State.CsiIgnore) continue;
                if (value < 0x30) state = State.CsiIntermediate;
                else if (state == State.CsiIntermediate ||
                    (state == State.CsiEntry && value == ':') ||
                    (state == State.CsiParam && value >= 0x3C)) state = State.CsiIgnore;
                else state = State.CsiParam;
            }
            else // DCS entry/parameters/intermediates
            {
                if (value >= 0x40) state = State.DcsData;
                else if (value < 0x30) state = State.DcsIntermediate;
                else if (state == State.DcsIntermediate || value == ':' ||
                    (state == State.DcsParam && value >= 0x3C)) state = State.DcsIgnore;
                else state = State.DcsParam;
            }
            if (state == State.Invalid) return false;
        }
        return true;
    }

    internal static void Validate(ReadOnlySpan<byte> bytes)
    {
        if (!IsValid(bytes)) throw new InvalidDataException("Snapshot continuation must be a minimal, unfinished, side-effect-free parser tail.");
    }

    private enum State : byte
    {
        Invalid, Escape, EscapeIntermediate, CsiEntry, CsiParam, CsiIntermediate,
        CsiIgnore, DcsEntry, DcsParam, DcsIntermediate, DcsData, DcsIgnore, Osc, Apc,
    }
}
