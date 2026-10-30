// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalLifecycleEffectSource
{
    /// <inheritdoc />
    public Action<TerminalSemanticPromptReport>? SemanticPromptCallback { get; set; }

    /// <inheritdoc />
    public Action? ResetCallback { get; set; }

    private void PublishSemanticPrompt(ReadOnlySpan<byte> payload)
    {
        if (SemanticPromptCallback is not { } callback || payload.IsEmpty ||
            (payload.Length > 1 && payload[1] != ';')) return;
        TerminalSemanticPromptKind kind = payload[0] switch
        {
            (byte)'A' or (byte)'N' or (byte)'P' => TerminalSemanticPromptKind.PromptStart,
            (byte)'B' or (byte)'I' => TerminalSemanticPromptKind.InputStart,
            (byte)'C' => TerminalSemanticPromptKind.OutputStart,
            (byte)'D' => TerminalSemanticPromptKind.CommandEnd,
            _ => 0,
        };
        if (kind == 0) return;
        ReadOnlySpan<byte> options = payload.Length > 2 ? payload[2..] : [];
        TerminalSemanticPromptRole role = TerminalSemanticPromptRole.Primary;
        int? exitCode = null;
        byte[] command = [], error = [];
        if (kind == TerminalSemanticPromptKind.PromptStart && TrySemanticOption(options, "k"u8, out ReadOnlySpan<byte> value))
            role = value switch { [(byte)'r'] => TerminalSemanticPromptRole.Right,
                [(byte)'c'] => TerminalSemanticPromptRole.Continuation, [(byte)'s'] => TerminalSemanticPromptRole.Secondary, _ => role };
        if (kind == TerminalSemanticPromptKind.CommandEnd)
        {
            int separator = options.IndexOf((byte)';');
            if (int.TryParse(separator < 0 ? options : options[..separator], NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out int code)) exitCode = code;
            if (TrySemanticOption(options, "err"u8, out value)) error = value.ToArray();
        }
        if (kind == TerminalSemanticPromptKind.OutputStart)
        {
            if (TrySemanticOption(options, "cmdline"u8, out value)) command = DecodeSemanticCommand(value, percent: false);
            else if (TrySemanticOption(options, "cmdline_url"u8, out value)) command = DecodeSemanticCommand(value, percent: true);
        }
        callback(new(kind, role, exitCode, command, error));
    }

    private static bool TrySemanticOption(ReadOnlySpan<byte> options, ReadOnlySpan<byte> key, out ReadOnlySpan<byte> value)
    {
        while (!options.IsEmpty)
        {
            int separator = options.IndexOf((byte)';');
            ReadOnlySpan<byte> entry = separator < 0 ? options : options[..separator];
            int equals = entry.IndexOf((byte)'=');
            if (equals >= 0 && entry[..equals].SequenceEqual(key)) { value = entry[(equals + 1)..]; return true; }
            if (separator < 0) break;
            options = options[(separator + 1)..];
        }
        value = [];
        return false;
    }

    private static byte[] DecodeSemanticCommand(ReadOnlySpan<byte> text, bool percent)
    {
        if (!percent)
        {
            int prefix = text.StartsWith("$'"u8) ? 2 : text.StartsWith("'"u8) ? 1 : 0;
            if (prefix != 0)
            {
                if (text.Length <= prefix || text[^1] != '\'') return [];
                text = text[prefix..^1];
            }
        }
        // OSC 133 uses Ghostty's fixed 2048-byte capture. Decode to bounded
        // stack scratch so only the final owned event payload is allocated.
        Span<byte> decoded = stackalloc byte[2048];
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            byte value = text[i];
            if (percent && value == '%')
            {
                if (text.Length - i < 3) return [];
                int hi = Hex(text[i + 1]), lo = Hex(text[i + 2]);
                if (hi < 0 || lo < 0) return [];
                value = (byte)((hi << 4) | lo);
                i += 2;
            }
            else if (!percent && value == '\\')
            {
                if (++i == text.Length) return [];
                int escaped = text[i] switch
                {
                    (byte)' ' or (byte)'\\' or (byte)'"' or (byte)'\'' or (byte)'$' => text[i],
                    (byte)'e' => 27, (byte)'n' => 10, (byte)'r' => 13, (byte)'t' => 9, (byte)'v' => 11, _ => -1,
                };
                if (escaped < 0) return [];
                value = (byte)escaped;
            }
            if (count == decoded.Length) return [];
            decoded[count++] = value;
        }
        return decoded[..count].ToArray();

        static int Hex(byte value) => value switch
        {
            >= (byte)'0' and <= (byte)'9' => value - '0',
            >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => value - 'A' + 10, _ => -1,
        };
    }
}
