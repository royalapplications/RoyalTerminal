// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Managed paste encoder that mirrors Ghostty's paste framing and sanitization rules.
/// </summary>
public static class TerminalPasteEncoder
{
    private static readonly byte[] s_bracketedStart = "\x1b[200~"u8.ToArray();
    private static readonly byte[] s_bracketedEnd = "\x1b[201~"u8.ToArray();

    /// <summary>
    /// Returns whether the supplied text is considered safe to paste.
    /// </summary>
    public static bool IsSafe(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.IndexOf('\n') < 0 && !text.Contains("\x1b[201~", StringComparison.Ordinal);
    }

    /// <summary>
    /// Encodes the supplied text using Ghostty-compatible paste rules.
    /// </summary>
    public static byte[] Encode(string text, bool bracketedPaste)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] payload = Encoding.UTF8.GetBytes(text);
        SanitizeInPlace(payload);

        if (!bracketedPaste)
        {
            ReplaceNewlinesWithCarriageReturn(payload);
            return payload;
        }

        byte[] result = new byte[s_bracketedStart.Length + payload.Length + s_bracketedEnd.Length];
        Buffer.BlockCopy(s_bracketedStart, 0, result, 0, s_bracketedStart.Length);
        Buffer.BlockCopy(payload, 0, result, s_bracketedStart.Length, payload.Length);
        Buffer.BlockCopy(s_bracketedEnd, 0, result, s_bracketedStart.Length + payload.Length, s_bracketedEnd.Length);
        return result;
    }

    private static void ReplaceNewlinesWithCarriageReturn(byte[] payload)
    {
        for (int i = 0; i < payload.Length; i++)
        {
            if (payload[i] == (byte)'\n')
            {
                payload[i] = (byte)'\r';
            }
        }
    }

    private static void SanitizeInPlace(byte[] payload)
    {
        for (int i = 0; i < payload.Length; i++)
        {
            switch (payload[i])
            {
                case 0x00:
                case 0x03:
                case 0x04:
                case 0x05:
                case 0x08:
                case 0x0F:
                case 0x11:
                case 0x12:
                case 0x13:
                case 0x15:
                case 0x16:
                case 0x17:
                case 0x1A:
                case 0x1B:
                case 0x1C:
                case 0x7F:
                    payload[i] = (byte)' ';
                    break;
            }
        }
    }
}
