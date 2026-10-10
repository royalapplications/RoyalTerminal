// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Text;
using System.Text;

namespace RoyalTerminal.Terminal;

// Mirrors Ghostty osc/parsers/program_status.zig. No allocations until the whole
// report validates; all decoded scratch is bounded by the protocol's limits.
internal static class ProgramStatusParser
{
    internal const int MaxBodyBytes = 4087; // Includes room for ESC ] 7501 ; and ESC \.

    internal static bool TryParse(ReadOnlySpan<byte> body, out TerminalProgramStatus? report)
    {
        report = null;
        if (body.Length > MaxBodyBytes) return false;
        ReadOnlySpan<byte> state = [], id = [], app = [], kind = [], progress = [], title = [], message = [];
        foreach (Range range in body.Split((byte)':'))
        {
            ReadOnlySpan<byte> pair = body[range];
            int equals = pair.IndexOf((byte)'=');
            if (equals < 0) continue;
            ReadOnlySpan<byte> key = Trim(pair[..equals]);
            if (key.Length > 16) return false;
            ReadOnlySpan<byte> value = Trim(pair[(equals + 1)..]);
            if (key.IsEmpty || !IsValue(value)) continue;
            if (key.SequenceEqual("state"u8)) state = value;
            else if (key.SequenceEqual("id"u8))
            {
                if (!IsId(value)) return false;
                id = value;
            }
            else if (key.SequenceEqual("app"u8))
            {
                if (value.Length > 32) return false;
                app = value;
            }
            else if (key.SequenceEqual("kind"u8)) kind = value;
            else if (key.SequenceEqual("progress"u8)) progress = value;
            else if (key.SequenceEqual("title"u8))
            {
                if (value.Length > 256) return false;
                title = value;
            }
            else if (key.SequenceEqual("msg"u8))
            {
                if (value.Length > 2732) return false;
                message = value;
            }
        }
        TerminalProgramStatusState parsedState;
        if (state.SequenceEqual("idle"u8)) parsedState = TerminalProgramStatusState.Idle;
        else if (state.SequenceEqual("working"u8)) parsedState = TerminalProgramStatusState.Working;
        else if (state.SequenceEqual("done"u8)) parsedState = TerminalProgramStatusState.Done;
        else if (state.SequenceEqual("blocked"u8)) parsedState = TerminalProgramStatusState.Blocked;
        else if (state.SequenceEqual("error"u8)) parsedState = TerminalProgramStatusState.Error;
        else if (state.SequenceEqual("clear"u8)) parsedState = TerminalProgramStatusState.Clear;
        else return false;

        Span<byte> titleBytes = stackalloc byte[192];
        Span<byte> messageBytes = stackalloc byte[2048];
        if (!DecodeText(title, titleBytes, out int titleLength) ||
            !DecodeText(message, messageBytes, out int messageLength)) return false;
        TerminalProgramStatusKind parsedKind = TerminalProgramStatusKind.None;
        if (parsedState == TerminalProgramStatusState.Blocked)
        {
            if (kind.SequenceEqual("permission"u8)) parsedKind = TerminalProgramStatusKind.Permission;
            else if (kind.SequenceEqual("question"u8)) parsedKind = TerminalProgramStatusKind.Question;
            else if (kind.SequenceEqual("auth"u8)) parsedKind = TerminalProgramStatusKind.Auth;
        }
        byte? parsedProgress = parsedState is TerminalProgramStatusState.Working or TerminalProgramStatusState.Blocked
            ? ParseProgress(progress) : null;
        report = new(parsedState, Encoding.ASCII.GetString(id), parsedKind, parsedProgress,
            IsName(app) ? Encoding.ASCII.GetString(app) : string.Empty,
            Encoding.UTF8.GetString(titleBytes[..titleLength]), Encoding.UTF8.GetString(messageBytes[..messageLength]));
        return true;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
    {
        int start = 0, end = value.Length;
        while (start < end && IsWhitespace(value[start])) start++;
        while (end > start && IsWhitespace(value[end - 1])) end--;
        return value[start..end];
    }

    private static bool IsWhitespace(byte value) => value is (byte)' ' or >= 9 and <= 13;

    private static bool IsNameByte(byte value)
        => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or
            >= (byte)'0' and <= (byte)'9' or (byte)'_' or (byte)'.' or (byte)'+' or (byte)'-';

    private static bool IsName(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) return false;
        foreach (byte b in value) if (!IsNameByte(b)) return false;
        return true;
    }

    private static bool IsValue(ReadOnlySpan<byte> value)
    {
        foreach (byte b in value) if (!IsNameByte(b) && b is not ((byte)',' or (byte)'/' or (byte)'=')) return false;
        return true;
    }

    private static bool IsId(ReadOnlySpan<byte> value)
    {
        if (value.Length > 128) return false;
        int depth = 0;
        foreach (Range range in value.Split((byte)'/'))
        {
            ReadOnlySpan<byte> segment = value[range];
            if (++depth > 8 || segment.Length > 32 || !IsName(segment)) return false;
        }
        return true;
    }

    private static byte? ParseProgress(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) return null;
        int number = 0;
        foreach (byte b in value)
        {
            if (b is < (byte)'0' or > (byte)'9') return null;
            number = number * 10 + b - '0';
            if (number > 100) return null;
        }
        return (byte)number;
    }

    private static bool DecodeText(scoped ReadOnlySpan<byte> encoded, Span<byte> decoded, out int length)
    {
        length = 0;
        if (encoded.IsEmpty) return true;
        Span<byte> padded = stackalloc byte[2732];
        if (encoded[^1] != '=' && encoded.Length % 4 != 0)
        {
            if (encoded.Length % 4 == 1) return false;
            int paddedLength = (encoded.Length + 3) & ~3;
            encoded.CopyTo(padded);
            padded[encoded.Length..paddedLength].Fill((byte)'=');
            encoded = padded[..paddedLength];
        }
        if (Base64.DecodeFromUtf8(encoded, decoded, out _, out length) != OperationStatus.Done) return false;
        ReadOnlySpan<byte> remaining = decoded[..length];
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(remaining, out Rune rune, out int consumed) != OperationStatus.Done ||
                rune.Value <= 0x1f || rune.Value is >= 0x7f and <= 0x9f) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }
}
