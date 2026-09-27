// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Text;
using RoyalTerminal.Terminal.Glyphs;

namespace RoyalTerminal.Terminal;

internal static class ManagedGlyphProtocol
{
    internal readonly record struct Result(byte ResponseKind, bool Mutated = false, uint Codepoint = 0,
        string? Error = null, bool Glossary = false);

    // Execute without host callbacks so the caller can publish the glyph dirty
    // revision before formatting/delivering effects, even on a rejected mutation.
    internal static Result Execute(ReadOnlySpan<byte> command, ref TerminalGlyphGlossary? glossary,
        Action<TerminalGlyphAllocation>? allocationCheckpoint = null)
    {
        if (command.IsEmpty || command.Length > 1 && command[1] != ';') return default;
        ReadOnlySpan<byte> options = command.Length > 1 ? command[2..] : [];
        switch (command[0])
        {
            case (byte)'s':
                return new((byte)'s');
            case (byte)'q':
                if (Option(options, "cp"u8, out ReadOnlySpan<byte> cpText) && Unsigned(cpText, 16, 0x1FFFFF, out uint cp))
                    return new((byte)'q', Codepoint: cp, Glossary: glossary?.TryGet(cp, out _) == true);
                return default;
            case (byte)'c':
                string? clearError = null;
                if (!Option(options, "cp"u8, out cpText)) glossary?.Clear();
                else if (!Unsigned(cpText, 16, 0x1FFFFF, out cp)) clearError = "malformed_payload";
                else if (!TerminalGlyphGlossary.IsPrivateUse(cp)) clearError = "out_of_namespace";
                else glossary?.Delete(cp);
                return new((byte)'c', Mutated: true, Error: clearError);
            case (byte)'r':
                int separator = options.LastIndexOf((byte)';');
                if (separator < 0) return default; // Request classification fails before execution.
                ReadOnlySpan<byte> payload = options[(separator + 1)..];
                options = options[..separator];
                byte verbosity = 1;
                if (Option(options, "reply"u8, out ReadOnlySpan<byte> value) && value.Length == 1 && value[0] is >= (byte)'0' and <= (byte)'2')
                    verbosity = (byte)(value[0] - '0');
                cp = 0;
                bool validCp = Option(options, "cp"u8, out cpText) && Unsigned(cpText, 16, 0x1FFFFF, out cp);
                if (!validCp) cp = 0;
                string? error = validCp ? Register(options, payload, cp, ref glossary, allocationCheckpoint) : "malformed_payload";
                bool respond = verbosity != 0 && (verbosity == 1 || error is not null);
                return new(respond ? (byte)'r' : (byte)0, Mutated: true, Codepoint: cp, Error: error);
            default:
                return default;
        }
    }

    internal static void Send(in Result result, Action<byte[]>? reply, ITerminalGlyphCoverageSource? coverageSource)
    {
        if (reply is null || result.ResponseKind == 0) return;
        byte[] bytes = result.ResponseKind switch
        {
            (byte)'s' => "\u001b_25a1;s;fmt=glyf\u001b\\"u8.ToArray(),
            (byte)'q' => TerminalGlyphCoverageResponse.Format(result.Codepoint, result.Glossary, coverageSource),
            _ => TerminalGlyphResponseFormatter.Status(result.ResponseKind, result.Codepoint, result.Error),
        };
        reply(bytes);
    }

    private static string? Register(ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload, uint cp,
        ref TerminalGlyphGlossary? glossary, Action<TerminalGlyphAllocation>? allocationCheckpoint)
    {
        try { return RegisterCore(options, payload, cp, ref glossary, allocationCheckpoint); }
        catch (OutOfMemoryException) { return "out_of_memory"; }
    }

    private static string? RegisterCore(ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload, uint cp,
        ref TerminalGlyphGlossary? glossary, Action<TerminalGlyphAllocation>? allocationCheckpoint)
    {
        if (Option(options, "fmt"u8, out ReadOnlySpan<byte> format) && !format.SequenceEqual("glyf"u8)) return "malformed_payload";
        if (!Metric(options, "upm"u8, 1000, out uint upm) ||
            !Metric(options, "aw"u8, upm, out uint advance) ||
            !Metric(options, "lh"u8, upm, out uint height)) return "malformed_payload";
        byte width = 1;
        if (Option(options, "width"u8, out ReadOnlySpan<byte> rawWidth))
        {
            if (rawWidth.Length != 1 || rawWidth[0] is not ((byte)'1' or (byte)'2')) return "malformed_payload";
            width = (byte)(rawWidth[0] - '0');
        }
        if (!Layout(options, out TerminalGlyphLayout layout)) return "malformed_payload";

        // Match Zig's length/padding validation before checking the decoded limit.
        if (payload.Length % 4 != 0) return "malformed_payload";
        int padding = payload.Length > 0 && payload[^1] == '=' ? 1 : 0;
        if (payload.Length > 1 && payload[^2] == '=') padding++;
        int size = payload.Length / 4 * 3 - padding;
        if (size > TerminalGlyphDecoder.MaxPayloadBytes) return "payload_too_large";
        for (int i = 0; i < payload.Length - padding; i++)
            if (payload[i] is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or
                >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'/')) return "malformed_payload";
        if (padding == 2 && payload[^1] != '=') return "malformed_payload";

        allocationCheckpoint?.Invoke(TerminalGlyphAllocation.DecodeBuffer);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(size, 1));
        try
        {
            if (Base64.DecodeFromUtf8(payload, buffer, out int consumed, out int written) != OperationStatus.Done || consumed != payload.Length)
                return "malformed_payload";
            allocationCheckpoint?.Invoke(TerminalGlyphAllocation.Outline);
            if (!TerminalGlyphDecoder.TryDecode(buffer.AsSpan(0, written), out TerminalGlyphOutline? outline, out TerminalGlyphDecodeError error))
                return error switch
                {
                    TerminalGlyphDecodeError.CompositeUnsupported => "composite_unsupported",
                    TerminalGlyphDecodeError.HintingUnsupported => "hinting_unsupported",
                    TerminalGlyphDecodeError.PayloadTooLarge => "payload_too_large",
                    _ => "malformed_payload",
                };
            // Ghostty validates options/payload before namespace or FIFO mutation.
            if (!TerminalGlyphGlossary.IsPrivateUse(cp)) return "out_of_namespace";
            allocationCheckpoint?.Invoke(TerminalGlyphAllocation.Registration);
            TerminalGlyphRegistration registration = new(outline, upm, advance, height, width, layout);
            if (glossary is null)
            {
                allocationCheckpoint?.Invoke(TerminalGlyphAllocation.Glossary);
                glossary = new();
            }
            glossary.Register(cp, registration, allocationCheckpoint);
            return null;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static bool Metric(ReadOnlySpan<byte> options, ReadOnlySpan<byte> key, uint fallback, out uint value)
    {
        value = fallback;
        return !Option(options, key, out ReadOnlySpan<byte> raw) || Unsigned(raw, 10, uint.MaxValue, out value) && value > 0;
    }

    private static bool Layout(ReadOnlySpan<byte> options, out TerminalGlyphLayout layout)
    {
        layout = default;
        TerminalGlyphSize size = TerminalGlyphSize.Height;
        if (Option(options, "size"u8, out ReadOnlySpan<byte> raw))
        {
            if (raw.SequenceEqual("height"u8)) size = TerminalGlyphSize.Height;
            else if (raw.SequenceEqual("advance"u8)) size = TerminalGlyphSize.Advance;
            else if (raw.SequenceEqual("contain"u8)) size = TerminalGlyphSize.Contain;
            else if (raw.SequenceEqual("cover"u8)) size = TerminalGlyphSize.Cover;
            else if (raw.SequenceEqual("stretch"u8)) size = TerminalGlyphSize.Stretch;
            else return false;
        }
        TerminalGlyphAlignment horizontal = TerminalGlyphAlignment.Center, vertical = TerminalGlyphAlignment.Center;
        if (Option(options, "align"u8, out raw))
        {
            int comma = raw.IndexOf((byte)',');
            if (comma < 0 || !Alignment(raw[..comma], out horizontal) || horizontal == TerminalGlyphAlignment.Baseline ||
                !Alignment(raw[(comma + 1)..], out vertical)) return false;
        }
        Span<double> pad = stackalloc double[4];
        pad.Clear();
        if (Option(options, "pad"u8, out raw))
        {
            for (int i = 0; i < 4; i++)
            {
                int comma = raw.IndexOf((byte)',');
                if ((i < 3) != (comma >= 0)) return false;
                if (!Fraction(comma < 0 ? raw : raw[..comma], out pad[i])) return false;
                if (comma >= 0) raw = raw[(comma + 1)..];
            }
            if (pad[0] + pad[2] >= 1 || pad[1] + pad[3] >= 1) pad.Clear();
        }
        layout = new(size, horizontal, vertical, pad[0], pad[1], pad[2], pad[3]);
        return true;
    }

    private static bool Alignment(ReadOnlySpan<byte> raw, out TerminalGlyphAlignment result)
    {
        result = TerminalGlyphAlignment.Center;
        if (raw.SequenceEqual("center"u8)) return true;
        if (raw.SequenceEqual("start"u8)) { result = TerminalGlyphAlignment.Start; return true; }
        if (raw.SequenceEqual("end"u8)) { result = TerminalGlyphAlignment.End; return true; }
        if (raw.SequenceEqual("baseline"u8)) { result = TerminalGlyphAlignment.Baseline; return true; }
        return false;
    }

    private static bool Fraction(ReadOnlySpan<byte> raw, out double result)
    {
        result = 0;
        bool negative = !raw.IsEmpty && raw[0] == '-';
        if (!raw.IsEmpty && raw[0] is (byte)'+' or (byte)'-') raw = raw[1..];
        int index = 0, digits = 0;
        while (index < raw.Length && raw[index] != '.')
        {
            byte digit = raw[index++];
            if (digit is < (byte)'0' or > (byte)'9') return false;
            result = result * 10 + digit - '0';
            digits++;
        }
        ulong fraction = 0, scale = 1;
        if (index < raw.Length)
        {
            index++;
            while (index < raw.Length)
            {
                byte digit = raw[index++];
                if (digit is < (byte)'0' or > (byte)'9') return false;
                if (scale < 1_000_000_000_000_000) { fraction = fraction * 10 + digit - '0'; scale *= 10; }
                digits++;
            }
        }
        result += (double)fraction / scale;
        if (negative) result = -result;
        return digits > 0 && result >= 0 && result <= 1;
    }

    private static bool Unsigned(ReadOnlySpan<byte> raw, uint radix, uint maximum, out uint result)
    {
        result = 0;
        bool negative = !raw.IsEmpty && raw[0] == '-';
        if (!raw.IsEmpty && raw[0] is (byte)'+' or (byte)'-') raw = raw[1..];
        if (raw.IsEmpty || raw[0] == '_' || raw[^1] == '_') return false;
        foreach (byte value in raw)
        {
            if (value == '_') continue;
            uint digit = value is >= (byte)'0' and <= (byte)'9' ? (uint)(value - '0') :
                value is >= (byte)'a' and <= (byte)'f' ? (uint)(value - 'a' + 10) :
                value is >= (byte)'A' and <= (byte)'F' ? (uint)(value - 'A' + 10) : uint.MaxValue;
            if (digit >= radix || digit > maximum || result > (maximum - digit) / radix || negative && digit != 0)
            { result = 0; return false; }
            result = result * radix + digit;
        }
        return true;
    }

    private static bool Option(ReadOnlySpan<byte> options, ReadOnlySpan<byte> key, out ReadOnlySpan<byte> value)
    {
        value = default;
        bool found = false;
        while (!options.IsEmpty)
        {
            int separator = options.IndexOf((byte)';');
            ReadOnlySpan<byte> option = separator < 0 ? options : options[..separator];
            int equals = option.IndexOf((byte)'=');
            if (equals >= 0 && option[..equals].SequenceEqual(key)) { value = option[(equals + 1)..]; found = true; }
            if (separator < 0) break;
            options = options[(separator + 1)..];
        }
        return found;
    }

}
