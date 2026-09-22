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
        if (encodedKey.IsEmpty || (encodedKey.Length & 1) != 0 || !IsHex(encodedKey))
        {
            response = [];
            return false;
        }

        string normalizedKey = encodedKey.ToString().ToUpperInvariant();
        string? encodedValue;
        if (normalizedKey == "544E") // TN
        {
            if (string.IsNullOrEmpty(terminfoName))
            {
                response = [];
                return false;
            }

            byte[] nameBytes = Encoding.UTF8.GetBytes(terminfoName);
            if (nameBytes.Length > MaxTerminfoNameBytes)
            {
                response = [];
                return false;
            }

            encodedValue = Convert.ToHexString(nameBytes);
        }
        else if (!Capabilities.TryGetValue(normalizedKey, out encodedValue))
        {
            response = [];
            return false;
        }

        string suffix = encodedValue is null ? string.Empty : $"={encodedValue}";
        response = Encoding.ASCII.GetBytes($"\x1bP1+r{normalizedKey}{suffix}\x1b\\");
        return true;
    }

    private static bool IsHex(ReadOnlySpan<char> value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f') and
                not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }
}
