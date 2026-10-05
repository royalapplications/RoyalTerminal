// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedColorParserTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Specifications()
    {
        string[] values = ["", " ", "#abc", "abc", "abcdef", "#123456789", "#123456789abc", "#12345678", "#1234",
            "#0_00_00_0", "#_aa000000", "rgb:a/b/c", "rgb:f/00/abcd", "rgb:1/111/ffff", "rgb:1_2/0/f",
            "rgb:_1/0/0", "rgb:1_/0/0", "rgb:+1/0/0", "rgb:00000/0/0", "rgb:0//0", "rgb:0/0/0/0",
            "RGB:ff/ff/ff", "rgbi:.5/+1./-0", "rgbi:.3/.1/.7", "rgbi:1.0000000000000001/0/0",
            "rgbi:1.000000000000001/0/0", "rgbi:0.33333333333333399999/0/0", "rgbi:0e0/0/0", "rgbi:-.1/0/0",
            "rgbi:NaN/0/0", "rgbi:inf/0/0", "rgbi:./0/0", "rgbi:+/0/0", "rgbi:0 /0/0", "rgbi:0\t/0/0",
            "rgbi:00.000/0/0", "rgbi:1/1/1", "rgbi:2/0/0", "rgbi:0x0.8/0/0", "rgbi:0..5/0/0",
            "0xff0000", "123", "123456", "65535", "aliceblue", "ALIceBLUE", "light slate gray", "LightSlateGray",
            "light  slate gray", "DarkGrey", "gray100", "gray101", "\t #123 \t", "\n#123", "#123\r", "\u00a0red",
            "ſnow", "blacK", "\0red", "#０00", "red\0", "rgbi:" + new string('1', 400) + "/0/0",
            "rgbi:0." + new string('3', 400) + "/0/0"];
        foreach (string value in values) yield return [value];
    }

    [Theory]
    [MemberData(nameof(Specifications))]
    public void ColorSyntaxAndNormalizationMatchNative(string value)
    {
        if (!Available()) return;
        Compare(value);
    }

    [Fact]
    public void EveryUpstreamX11NameAndAsciiCaseVariantMatchesNative()
    {
        if (!Available()) return;
        GhosttyX11Color[] entries = GhosttyColorUtilities.GetX11Colors();
        Assert.True(entries.Length > 700);
        foreach (GhosttyX11Color entry in entries)
        {
            Assert.True(ManagedColorParser.TryParse(entry.Name, out uint actual));
            Assert.Equal(Pack(entry.Color), actual);
            Compare(entry.Name.ToUpperInvariant());
            Compare(" \t" + entry.Name + "\t ");
        }
        output.WriteLine($"Compared {entries.Length} upstream names, uppercase variants and padded variants.");
    }

    [Fact]
    public void AllSixteenBitChannelValuesUseNativeTruncation()
    {
        if (!Available()) return;
        for (int i = 0; i <= 65535; i++) Compare($"rgb:{i:x4}/7ff/1");
        for (int i = 0; i < 4096; i++) Compare($"#{i:x3}123abc");
    }

    [Fact]
    public void WarmParserIsAllocationFreeAndMeasuresLegacyNumericSubset()
    {
        string[] formats = ["#abcdef", "rgb:12/34/56", "\t#abcdef\t", "aliceblue", "rgbi:.25/.5/.75"];
        for (int i = 0; i < 10000; i++) ManagedColorParser.TryParse(formats[i % formats.Length], out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        uint checksum = 0;
        for (int i = 0; i < 10000; i++) { ManagedColorParser.TryParse(formats[i % formats.Length], out uint color); checksum ^= color; }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(0u, checksum);
        // Identical supported inputs; no assertions on noisy wall-clock timings.
        string[] subset = ["#abcdef", "rgb:12/34/56", "\t#abcdef\t"];
        for (int i = 0; i < 10000; i++) TerminalThemeParser.TryParseColor(subset[i % 3], out _);
        foreach (bool legacy in new[] { true, false })
        {
            double[] samples = new double[7];
            long allocation = 0;
            for (int sample = 0; sample < samples.Length; sample++)
            {
                before = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100000; i++)
                {
                    if (legacy) TerminalThemeParser.TryParseColor(subset[i % 3], out _);
                    else ManagedColorParser.TryParse(subset[i % 3], out _);
                }
                samples[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocation = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(samples);
            output.WriteLine($"{(legacy ? "Legacy theme parser" : "Managed VT parser")}: median {samples[3]:F3} ms / {allocation} bytes per 100,000 parses.");
        }
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native color parser available: {available}");
        return available;
    }

    private static void Compare(string value)
    {
        bool expected = GhosttyColorUtilities.TryParse(value, out GhosttyVtNative.GhosttyColorRgb color);
        Assert.Equal(expected, ManagedColorParser.TryParse(value, out uint actual));
        if (expected) Assert.Equal(Pack(color), actual);
    }

    private static uint Pack(GhosttyVtNative.GhosttyColorRgb color) => 0xFF000000u | (uint)(color.R << 16 | color.G << 8 | color.B);
}
