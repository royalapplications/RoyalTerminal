// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Stateful sanitizer for Windows Ghostty VT input quirks.

using System.Buffers;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Strips Windows-specific sequences that are not supported by this Ghostty integration path
/// and normalizes styled ConPTY line padding before native Ghostty owns resize reflow.
/// Maintains chunk boundary state so split unsupported sequences are stripped reliably.
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

        if (!hadCarry && !RequiresSanitization(data))
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

        bool trimmedAny = TrimTrailingSpacesBeforeLineBreaks(output, writeIndex, out writeIndex);
        bool hasPendingCarry = _carry.Length > 0;
        if (!hadCarry && !removedAny && !trimmedAny && !hasPendingCarry)
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

    private static bool RequiresSanitization(ReadOnlySpan<byte> data)
    {
        return ContainsUnsupportedSequenceOrPendingPrefix(data) ||
            ContainsStyledTrailingSpacesBeforeLineBreak(data);
    }

    private static bool ContainsUnsupportedSequenceOrPendingPrefix(ReadOnlySpan<byte> data)
    {
        int searchOffset = 0;
        while (searchOffset < data.Length)
        {
            int relativeEscapeIndex = data[searchOffset..].IndexOf((byte)0x1B);
            if (relativeEscapeIndex < 0)
            {
                return false;
            }

            int escapeIndex = searchOffset + relativeEscapeIndex;
            ReadOnlySpan<byte> remaining = data[escapeIndex..];
            if (MatchUnsupportedSequence(remaining) > 0 ||
                IsIncompleteUnsupportedPrefixAtBufferEnd(remaining))
            {
                return true;
            }

            searchOffset = escapeIndex + 1;
        }

        return false;
    }

    private static bool ContainsStyledTrailingSpacesBeforeLineBreak(ReadOnlySpan<byte> data)
    {
        int lineStart = 0;
        for (int i = 0; i < data.Length; i++)
        {
            byte value = data[i];
            if (value != (byte)'\r' && value != (byte)'\n')
            {
                continue;
            }

            if (LineEndsWithStyledTrailingSpaces(data[lineStart..i]))
            {
                return true;
            }

            lineStart = i + 1;
        }

        return false;
    }

    private static bool LineEndsWithStyledTrailingSpaces(ReadOnlySpan<byte> line)
    {
        int sgrSuffixStart = FindTrailingSgrSuffixStart(line);
        if (sgrSuffixStart == line.Length)
        {
            return false;
        }

        int spaceStart = sgrSuffixStart;
        while (spaceStart > 0 && line[spaceStart - 1] == (byte)' ')
        {
            spaceStart--;
        }

        return spaceStart < sgrSuffixStart;
    }

    private static bool TrimTrailingSpacesBeforeLineBreaks(byte[] buffer, int inputLength, out int outputLength)
    {
        int writeIndex = 0;
        bool trimmedAny = false;

        for (int readIndex = 0; readIndex < inputLength; readIndex++)
        {
            byte value = buffer[readIndex];
            if (value is (byte)'\r' or (byte)'\n')
            {
                trimmedAny |= TrimCurrentLineEnd(buffer, ref writeIndex);
                buffer[writeIndex++] = value;
                continue;
            }

            buffer[writeIndex++] = value;
        }

        outputLength = writeIndex;
        return trimmedAny;
    }

    private static bool TrimCurrentLineEnd(byte[] buffer, ref int writeIndex)
    {
        int sgrSuffixStart = FindTrailingSgrSuffixStart(buffer.AsSpan(0, writeIndex));
        if (sgrSuffixStart == writeIndex)
        {
            return false;
        }

        int spaceStart = sgrSuffixStart;
        while (spaceStart > 0 && buffer[spaceStart - 1] == (byte)' ')
        {
            spaceStart--;
        }

        if (spaceStart == sgrSuffixStart)
        {
            return false;
        }

        int suffixLength = writeIndex - sgrSuffixStart;
        if (suffixLength > 0)
        {
            Buffer.BlockCopy(buffer, sgrSuffixStart, buffer, spaceStart, suffixLength);
        }

        writeIndex = spaceStart + suffixLength;
        return true;
    }

    private static int FindTrailingSgrSuffixStart(ReadOnlySpan<byte> line)
    {
        int suffixStart = line.Length;
        while (suffixStart > 0)
        {
            int escapeIndex = line[..suffixStart].LastIndexOf((byte)0x1B);
            if (escapeIndex < 0 || !IsCsiSgrSequence(line[escapeIndex..suffixStart]))
            {
                return suffixStart;
            }

            suffixStart = escapeIndex;
        }

        return suffixStart;
    }

    private static bool IsCsiSgrSequence(ReadOnlySpan<byte> sequence)
    {
        if (sequence.Length < 3 ||
            sequence[0] != 0x1B ||
            sequence[1] != (byte)'[' ||
            sequence[^1] != (byte)'m')
        {
            return false;
        }

        for (int i = 2; i < sequence.Length - 1; i++)
        {
            byte value = sequence[i];
            bool parameterByte =
                (value >= (byte)'0' && value <= (byte)'9') ||
                value == (byte)';' ||
                value == (byte)':';
            if (!parameterByte)
            {
                return false;
            }
        }

        return true;
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
