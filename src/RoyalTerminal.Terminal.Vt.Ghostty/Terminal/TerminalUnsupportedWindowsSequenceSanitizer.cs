// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Stateful sanitizer for unsupported Ghostty Windows CSI sequences.

using System.Buffers;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Strips a small set of unsupported CSI sequences from terminal output on Windows.
/// Maintains chunk boundary state so split sequences are stripped reliably.
/// </summary>
internal sealed class TerminalUnsupportedWindowsSequenceSanitizer
{
    private static readonly byte[][] UnsupportedSequences =
    [
        "\x1b[?9001h"u8.ToArray(),
        "\x1b[?9001l"u8.ToArray(),
        "\x1b[200~"u8.ToArray(),
        "\x1b[201~"u8.ToArray(),
    ];

    private byte[] _carry = [];

    public void Reset()
    {
        _carry = [];
    }

    /// <summary>
    /// Sanitizes terminal output and strips unsupported sequences.
    /// Returns <see langword="true"/> when caller should use <paramref name="sanitizedBuffer"/>.
    /// </summary>
    public bool TrySanitize(
        ReadOnlySpan<byte> data,
        out byte[]? sanitizedBuffer,
        out int sanitizedLength)
    {
        bool hadCarry = _carry.Length > 0;
        if (!hadCarry && data.IndexOf((byte)0x1B) < 0)
        {
            sanitizedBuffer = null;
            sanitizedLength = data.Length;
            return false;
        }

        int combinedLength = _carry.Length + data.Length;
        byte[] combined = ArrayPool<byte>.Shared.Rent(Math.Max(combinedLength, 1));
        int offset = 0;
        if (_carry.Length > 0)
        {
            _carry.AsSpan().CopyTo(combined);
            offset = _carry.Length;
            _carry = [];
        }

        data.CopyTo(combined.AsSpan(offset));

        byte[] output = ArrayPool<byte>.Shared.Rent(Math.Max(combinedLength, 1));
        int readIndex = 0;
        int writeIndex = 0;
        bool removedAny = false;
        ReadOnlySpan<byte> input = combined.AsSpan(0, combinedLength);

        while (readIndex < input.Length)
        {
            int matchedLength = MatchUnsupportedSequence(input.Slice(readIndex));
            if (matchedLength > 0)
            {
                removedAny = true;
                readIndex += matchedLength;
                continue;
            }

            if (input[readIndex] == 0x1B &&
                IsIncompleteUnsupportedPrefixAtBufferEnd(input.Slice(readIndex)))
            {
                _carry = input.Slice(readIndex).ToArray();
                break;
            }

            output[writeIndex++] = input[readIndex++];
        }

        ArrayPool<byte>.Shared.Return(combined);

        bool hasPendingCarry = _carry.Length > 0;
        if (!hadCarry && !removedAny && !hasPendingCarry)
        {
            ArrayPool<byte>.Shared.Return(output);
            sanitizedBuffer = null;
            sanitizedLength = data.Length;
            return false;
        }

        sanitizedBuffer = output;
        sanitizedLength = writeIndex;
        return true;
    }

    private static int MatchUnsupportedSequence(ReadOnlySpan<byte> input)
    {
        for (int i = 0; i < UnsupportedSequences.Length; i++)
        {
            ReadOnlySpan<byte> sequence = UnsupportedSequences[i];
            if (input.Length >= sequence.Length && input.StartsWith(sequence))
            {
                return sequence.Length;
            }
        }

        return 0;
    }

    private static bool IsIncompleteUnsupportedPrefixAtBufferEnd(ReadOnlySpan<byte> remaining)
    {
        for (int i = 0; i < UnsupportedSequences.Length; i++)
        {
            ReadOnlySpan<byte> sequence = UnsupportedSequences[i];
            if (remaining.Length >= sequence.Length)
            {
                continue;
            }

            if (sequence.StartsWith(remaining))
            {
                return true;
            }
        }

        return false;
    }
}
