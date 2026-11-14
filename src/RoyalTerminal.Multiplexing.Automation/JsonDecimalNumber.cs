// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace RoyalTerminal.Multiplexing.Automation;

// Exact decimal comparison avoids rounding large integer IDs or very small numbers through double.
// Exponents are compared symbolically; no huge power of ten or expanded decimal is allocated.
internal readonly record struct JsonDecimalNumber(bool Negative, string Digits, BigInteger Exponent)
{
    internal static JsonDecimalNumber Parse(JsonElement value)
    {
        string raw = value.GetRawText();
        bool negative = raw[0] == '-';
        int start = negative ? 1 : 0;
        int exponentOffset = raw.IndexOfAny(['e', 'E']);
        string mantissa = raw[start..(exponentOffset < 0 ? raw.Length : exponentOffset)];
        int dot = mantissa.IndexOf('.');
        int fractionDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
        string digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return new(false, "0", BigInteger.Zero);
        int trailing = 0;
        while (digits[digits.Length - trailing - 1] == '0') trailing++;
        if (trailing > 0) digits = digits[..^trailing];
        BigInteger exponent = exponentOffset < 0 ? BigInteger.Zero : BigInteger.Parse(raw.AsSpan(exponentOffset + 1),
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return new(negative, digits, exponent - fractionDigits + trailing);
    }

    internal int CompareTo(JsonDecimalNumber other)
    {
        if (Digits == "0") return other.Digits == "0" ? 0 : other.Negative ? 1 : -1;
        if (other.Digits == "0") return Negative ? -1 : 1;
        if (Negative != other.Negative) return Negative ? -1 : 1;
        int magnitude = (Exponent + Digits.Length).CompareTo(other.Exponent + other.Digits.Length);
        if (magnitude == 0)
        {
            int count = Math.Max(Digits.Length, other.Digits.Length);
            for (int index = 0; index < count; index++)
            {
                char first = index < Digits.Length ? Digits[index] : '0';
                char second = index < other.Digits.Length ? other.Digits[index] : '0';
                magnitude = first.CompareTo(second);
                if (magnitude != 0) break;
            }
        }
        return Negative ? -magnitude : magnitude;
    }
}
