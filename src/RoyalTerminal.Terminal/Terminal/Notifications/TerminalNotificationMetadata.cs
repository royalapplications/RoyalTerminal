// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace RoyalTerminal.Terminal;

// Matches pinned Ghostty's kitty_metadata.ValueIterator and OSC 99 option defaults:
// first lexically valid scalar wins, unknown/malformed keys are ignored, n/t repeat.
internal sealed class TerminalNotificationMetadata
{
    private readonly Dictionary<char, string> _values = new();
    internal List<string> IconNames { get; } = new();
    internal List<string> Types { get; } = new();
    internal string? this[char key] => _values.GetValueOrDefault(key);
    internal string Id => Identifier(this['i']) ?? string.Empty;
    internal bool Done => this['d'] != "0";
    internal bool Encoded => this['e'] == "1";
    internal string Payload => this['p'] ?? "title";

    internal static bool TryParse(ReadOnlySpan<byte> source, out TerminalNotificationMetadata result)
    {
        result = new();
        if (source.Length > 8192) return false;
        while (!source.IsEmpty)
        {
            int colon = source.IndexOf((byte)':');
            ReadOnlySpan<byte> part = Trim(colon < 0 ? source : source[..colon]);
            source = colon < 0 ? default : source[(colon + 1)..];
            int equals = part.IndexOf((byte)'=');
            if (equals < 0) continue;
            ReadOnlySpan<byte> key = Trim(part[..equals]);
            ReadOnlySpan<byte> value = Trim(part[(equals + 1)..]);
            if (key.Length != 1 || "acdefginopstuw"u8.IndexOf(key[0]) < 0) continue;
            bool valid = true;
            foreach (byte b in value)
                if (!IsIdentifierByte(b) && "/,(){}[]*&^%$#@!`~=?"u8.IndexOf(b) < 0) { valid = false; break; }
            if (!valid) continue;
            char name = (char)key[0];
            string text = Encoding.ASCII.GetString(value);
            if (name is 'n' or 't')
            {
                List<string> list = name == 'n' ? result.IconNames : result.Types;
                if (list.Count < 32 && DecodeText(text) is { } decoded) list.Add(decoded);
            }
            else result._values.TryAdd(name, text);
        }
        return true;
    }

    internal static string? Identifier(string? value)
    {
        if (value is null || value.Length > 256) return null;
        foreach (char c in value) if (c > 127 || !IsIdentifierByte((byte)c)) return null;
        return value;
    }

    internal static int Expiry(string value)
        => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int milliseconds) && milliseconds >= -1
            ? milliseconds : -1;

    internal static (bool Focus, bool Report) Actions(string value)
    {
        bool focus = true, report = false;
        foreach (Range range in value.AsSpan().Split(','))
        {
            ReadOnlySpan<char> part = value.AsSpan()[range].Trim();
            bool enabled = !part.StartsWith("-", StringComparison.Ordinal);
            if (!enabled) part = part[1..];
            if (part.SequenceEqual("focus")) focus = enabled;
            else if (part.SequenceEqual("report")) report = enabled;
            else return (true, false);
        }
        return (focus, report);
    }

    internal static bool IsSafeUtf8(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(bytes, out Rune rune, out int consumed) != OperationStatus.Done ||
                rune.Value < 32 || rune.Value is >= 127 and <= 159) return false;
            bytes = bytes[consumed..];
        }
        return true;
    }

    internal static byte[]? DecodeBase64(ReadOnlySpan<byte> bytes)
    {
        // Convert's whitespace tolerance is intentionally not used for protocol input.
        int remainder = bytes.Length % 4;
        if (remainder != 0)
        {
            // Kitty OSC 99 explicitly permits an unpadded final base64 chunk.
            // Reject partial padding and impossible single-character groups.
            if (remainder == 1 || bytes.Contains((byte)'=')) return null;
            int paddedLength = checked(bytes.Length + 4 - remainder);
            byte[]? rented = null;
            Span<byte> padded = paddedLength <= 256 ? stackalloc byte[paddedLength]
                : (rented = ArrayPool<byte>.Shared.Rent(paddedLength)).AsSpan(0, paddedLength);
            try
            {
                bytes.CopyTo(padded);
                padded[bytes.Length..].Fill((byte)'=');
                return DecodeBase64(padded);
            }
            finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
        }
        foreach (byte b in bytes)
            if (!(b is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or
                >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'/' or (byte)'=')) return null;
        int length = bytes.Length / 4 * 3;
        if (!bytes.IsEmpty && bytes[^1] == '=') length--;
        if (bytes.Length > 1 && bytes[^2] == '=') length--;
        byte[] decoded = new byte[length];
        if (System.Buffers.Text.Base64.DecodeFromUtf8(bytes, decoded, out int consumed, out int written) != OperationStatus.Done || consumed != bytes.Length)
            return null;
        if (written != decoded.Length) Array.Resize(ref decoded, written);
        return decoded;
    }

    internal static string? DecodeText(string base64)
        => DecodeBase64(Encoding.ASCII.GetBytes(base64)) is { } bytes ? PlainText(bytes) : null;

    internal static string? PlainText(ReadOnlySpan<byte> bytes)
    {
        // Encoded UTF-8 may contain newlines/tabs, but never pass terminal controls
        // to a desktop presenter. Preserve literal markup: the host must not parse it.
        StringBuilder text = new();
        Span<char> scalar = stackalloc char[2];
        while (!bytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(bytes, out Rune rune, out int consumed) != OperationStatus.Done) return null;
            if (rune.Value is 9 or 10 or 13 || rune.Value >= 32 && rune.Value is not (>= 127 and <= 159))
                text.Append(scalar[..rune.EncodeToUtf16(scalar)]);
            bytes = bytes[consumed..];
        }
        return text.ToString();
    }

    private static bool IsIdentifierByte(byte b) => b is >= (byte)'a' and <= (byte)'z' or
        >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'-' or (byte)'_' or (byte)'+' or (byte)'.';

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> value)
    {
        while (!value.IsEmpty && IsSpace(value[0])) value = value[1..];
        while (!value.IsEmpty && IsSpace(value[^1])) value = value[..^1];
        return value;
    }

    private static bool IsSpace(byte value) => value is 32 or 9 or 10 or 11 or 12 or 13;
}
