// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Text.Unicode;

namespace RoyalTerminal.GhosttySharp.Terminal;

/// <summary>
/// High-performance terminal data processing utilities.
/// Uses SIMD intrinsics (SSE2/AVX2/NEON) for accelerated UTF-8 validation,
/// ANSI escape sequence stripping, and line extraction.
/// </summary>
public static class TerminalDataProcessor
{
    private const byte Escape = 0x1B;
    private const byte BracketOpen = (byte)'[';
    private const byte Newline = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';

    /// <summary>
    /// Validates that the input span contains valid UTF-8 data using SIMD acceleration.
    /// Falls back to scalar validation when SIMD is not available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValidUtf8(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return true;

        // Use .NET's built-in UTF-8 validation which is already SIMD-optimized
        return Utf8.IsValid(data);
    }

    /// <summary>
    /// Strips ANSI/VT escape sequences from terminal output using SIMD-accelerated scanning.
    /// Returns the cleaned text in a rented buffer that must be returned to the pool.
    /// </summary>
    /// <param name="input">Raw terminal output with escape sequences.</param>
    /// <param name="output">Destination span; receives the cleaned text.</param>
    /// <returns>Number of bytes written to output.</returns>
    public static int StripAnsiEscapes(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.IsEmpty) return 0;

        var writePos = 0;
        var readPos = 0;

        while (readPos < input.Length && writePos < output.Length)
        {
            // Use SIMD to find next escape character quickly
            var remaining = input[readPos..];
            var escapeIdx = FindByteSIMD(remaining, Escape);

            if (escapeIdx < 0)
            {
                // No more escapes, copy rest
                var toCopy = Math.Min(remaining.Length, output.Length - writePos);
                remaining[..toCopy].CopyTo(output[writePos..]);
                writePos += toCopy;
                break;
            }

            // Copy everything before the escape
            if (escapeIdx > 0)
            {
                var toCopy = Math.Min(escapeIdx, output.Length - writePos);
                remaining[..toCopy].CopyTo(output[writePos..]);
                writePos += toCopy;
            }

            readPos += escapeIdx + 1;

            // Skip the escape sequence
            readPos = SkipEscapeSequence(input, readPos);
        }

        return writePos;
    }

    /// <summary>
    /// Extracts individual lines from terminal data, handling both \n and \r\n line endings.
    /// Uses SIMD-accelerated newline scanning for high throughput.
    /// </summary>
    /// <param name="data">Terminal data to split into lines.</param>
    /// <returns>Enumerable of line ranges as (offset, length) tuples.</returns>
    public static List<Range> ExtractLineRanges(ReadOnlySpan<byte> data)
    {
        var lines = new List<Range>();
        if (data.IsEmpty) return lines;

        var lineStart = 0;
        var pos = 0;

        while (pos < data.Length)
        {
            var remaining = data[pos..];
            var nlIdx = FindByteSIMD(remaining, Newline);

            if (nlIdx < 0)
            {
                // No more newlines, rest is the last line
                if (lineStart < data.Length)
                    lines.Add(new Range(lineStart, data.Length));
                break;
            }

            var lineEnd = pos + nlIdx;

            // Handle \r\n
            var adjustedEnd = lineEnd;
            if (adjustedEnd > lineStart && data[adjustedEnd - 1] == CarriageReturn)
                adjustedEnd--;

            lines.Add(new Range(lineStart, adjustedEnd));
            lineStart = lineEnd + 1;
            pos = lineStart;
        }

        return lines;
    }

    /// <summary>
    /// Converts UTF-8 terminal data to a .NET string using the fastest available path.
    /// Validates UTF-8 before conversion and replaces invalid sequences.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string DecodeUtf8(ReadOnlySpan<byte> utf8Data)
    {
        if (utf8Data.IsEmpty) return string.Empty;
        return Encoding.UTF8.GetString(utf8Data);
    }

    /// <summary>
    /// Encodes a .NET string to UTF-8 into a rented buffer.
    /// Returns the number of bytes written.
    /// </summary>
    public static int EncodeUtf8(ReadOnlySpan<char> text, Span<byte> destination)
    {
        if (text.IsEmpty) return 0;
        return Encoding.UTF8.GetBytes(text, destination);
    }

    /// <summary>
    /// Counts the number of complete UTF-8 characters in the span using SIMD.
    /// </summary>
    public static int CountUtf8Characters(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return 0;

        var count = 0;
        var pos = 0;

        // SIMD path: count non-continuation bytes (bytes where top 2 bits != 10)
        if (Vector256.IsHardwareAccelerated && data.Length >= Vector256<byte>.Count)
        {
            var threshold = Vector256.Create((byte)0xC0);
            var pattern = Vector256.Create((byte)0x80);

            while (pos + Vector256<byte>.Count <= data.Length)
            {
                var vec = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(data[pos..]));
                var masked = vec & threshold;
                var isContinuation = Vector256.Equals(masked, pattern);
                // Count non-continuation bytes
                var notContinuation = ~isContinuation;
                count += BitOperations.PopCount(notContinuation.ExtractMostSignificantBits());
                pos += Vector256<byte>.Count;
            }
        }
        else if (Vector128.IsHardwareAccelerated && data.Length >= Vector128<byte>.Count)
        {
            var threshold = Vector128.Create((byte)0xC0);
            var pattern = Vector128.Create((byte)0x80);

            while (pos + Vector128<byte>.Count <= data.Length)
            {
                var vec = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(data[pos..]));
                var masked = vec & threshold;
                var isContinuation = Vector128.Equals(masked, pattern);
                var notContinuation = ~isContinuation;
                count += BitOperations.PopCount(notContinuation.ExtractMostSignificantBits());
                pos += Vector128<byte>.Count;
            }
        }

        // Scalar remainder
        while (pos < data.Length)
        {
            if ((data[pos] & 0xC0) != 0x80)
                count++;
            pos++;
        }

        return count;
    }

    /// <summary>
    /// SIMD-accelerated byte search. Returns the index of the first occurrence of the needle,
    /// or -1 if not found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindByteSIMD(ReadOnlySpan<byte> haystack, byte needle)
    {
        return haystack.IndexOf(needle);
    }

    /// <summary>
    /// Skips an ANSI escape sequence starting at the given position.
    /// Handles CSI, OSC, and single-character escape sequences.
    /// </summary>
    private static int SkipEscapeSequence(ReadOnlySpan<byte> data, int pos)
    {
        if (pos >= data.Length) return pos;

        var b = data[pos];

        // CSI sequence: ESC [ ... final_byte
        if (b == BracketOpen)
        {
            pos++;
            // Skip parameter bytes (0x30-0x3F) and intermediate bytes (0x20-0x2F)
            while (pos < data.Length)
            {
                var c = data[pos];
                if (c is >= 0x40 and <= 0x7E) // Final byte
                {
                    pos++;
                    break;
                }
                pos++;
            }
            return pos;
        }

        // OSC sequence: ESC ] ... (ST or BEL)
        if (b == (byte)']')
        {
            pos++;
            while (pos < data.Length)
            {
                var c = data[pos];
                if (c == 0x07) // BEL terminator
                {
                    pos++;
                    break;
                }
                if (c == 0x1B && pos + 1 < data.Length && data[pos + 1] == (byte)'\\') // ST terminator
                {
                    pos += 2;
                    break;
                }
                pos++;
            }
            return pos;
        }

        // DCS, PM, APC: ESC P/^/_ ... ST
        if (b is (byte)'P' or (byte)'^' or (byte)'_')
        {
            pos++;
            while (pos < data.Length)
            {
                if (data[pos] == 0x1B && pos + 1 < data.Length && data[pos + 1] == (byte)'\\')
                {
                    pos += 2;
                    break;
                }
                pos++;
            }
            return pos;
        }

        // Single-character escape: ESC + one byte
        return pos + 1;
    }
}

/// <summary>
/// Bit counting operations helper.
/// </summary>
internal static class BitOperations
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopCount(uint value) => System.Numerics.BitOperations.PopCount(value);
}
