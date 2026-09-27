// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.Terminal;

/// <summary>Encodes Ghostty-compatible XTGETTCAP responses.</summary>
internal static partial class GhosttyXtgettcap
{
    private const int MaxTerminfoNameBytes = 128;

    public static bool TryCreateResponse(
        ReadOnlySpan<char> encodedKey,
        string? terminfoName,
        out byte[] response)
    {
        response = [];
        if (encodedKey.IsEmpty || (encodedKey.Length & 1) != 0 || encodedKey.Length > MaximumEncodedKeyLength)
            return false;
        Span<char> normalized = stackalloc char[MaximumEncodedKeyLength];
        for (int i = 0; i < encodedKey.Length; i++)
        {
            if (!Normalize(encodedKey[i], out normalized[i])) return false;
        }
        return CreateResponse(normalized[..encodedKey.Length], terminfoName, out response);
    }

    internal static bool TryCreateResponse(ReadOnlySpan<byte> encodedKey, string? terminfoName, out byte[] response)
    {
        response = [];
        if (encodedKey.IsEmpty || (encodedKey.Length & 1) != 0 || encodedKey.Length > MaximumEncodedKeyLength)
            return false;
        Span<char> normalized = stackalloc char[MaximumEncodedKeyLength];
        for (int i = 0; i < encodedKey.Length; i++)
        {
            if (!Normalize((char)encodedKey[i], out normalized[i])) return false;
        }
        return CreateResponse(normalized[..encodedKey.Length], terminfoName, out response);
    }

    private static bool CreateResponse(ReadOnlySpan<char> normalizedKey, string? terminfoName, out byte[] response)
    {
        response = [];
        if (normalizedKey.SequenceEqual("544E")) // TN is configured per processor, not a global cached response.
        {
            if (string.IsNullOrEmpty(terminfoName) || Encoding.UTF8.GetByteCount(terminfoName) > MaxTerminfoNameBytes)
                return false;
            Span<byte> name = stackalloc byte[MaxTerminfoNameBytes];
            int written = Encoding.UTF8.GetBytes(terminfoName.AsSpan(), name);
            response = AllocateResponse(normalizedKey, written * 2, hasValue: true);
            Span<byte> value = response.AsSpan(6 + normalizedKey.Length, written * 2);
            ReadOnlySpan<byte> hex = "0123456789ABCDEF"u8;
            for (int i = 0; i < written; i++)
            {
                value[i * 2] = hex[name[i] >> 4];
                value[i * 2 + 1] = hex[name[i] & 15];
            }
            return true;
        }
        if (!Capabilities.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(normalizedKey, out string? encodedValue))
            return false;
        response = AllocateResponse(normalizedKey, encodedValue?.Length ?? 0, encodedValue is not null);
        if (encodedValue is not null)
            Encoding.ASCII.GetBytes(encodedValue.AsSpan(), response.AsSpan(6 + normalizedKey.Length));
        return true;
    }

    private static byte[] AllocateResponse(ReadOnlySpan<char> key, int valueLength, bool hasValue)
    {
        byte[] result = new byte[7 + key.Length + (hasValue ? 1 + valueLength : 0)];
        "\u001bP1+r"u8.CopyTo(result);
        Encoding.ASCII.GetBytes(key, result.AsSpan(5));
        if (hasValue) result[5 + key.Length] = (byte)'=';
        result[^2] = 0x1B;
        result[^1] = (byte)'\\';
        return result;
    }

    private static bool Normalize(char value, out char normalized)
    {
        normalized = value is >= 'a' and <= 'f' ? (char)(value - ('a' - 'A')) : value;
        return normalized is >= '0' and <= '9' or >= 'A' and <= 'F';
    }
}
