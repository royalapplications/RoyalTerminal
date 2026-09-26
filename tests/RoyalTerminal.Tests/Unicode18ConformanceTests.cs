// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Unicode;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class Unicode18ConformanceTests
{
    [Fact]
    public void GraphemeSegmentationPassesEveryPinnedUnicode18ConformanceCase()
    {
        int cases = 0;
        foreach (string line in File.ReadLines(Fixture("GraphemeBreakTest.txt")))
        {
            string data = line.Split('#')[0].Trim();
            if (data.Length == 0) continue;
            List<int> expected = [];
            StringBuilder text = new();
            foreach (string token in data.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "÷") expected.Add(text.Length);
                else if (token != "×") text.Append(char.ConvertFromUtf32(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
            }
            List<int> actual = [0];
            GraphemeEnumerator enumerator = new(text.ToString());
            while (enumerator.MoveNext(out Grapheme grapheme)) actual.Add(grapheme.Offset + grapheme.Length);
            Assert.True(expected.SequenceEqual(actual), $"Unicode18 case failed: {data}; got {string.Join(',', actual)}");
            cases++;
        }
        Assert.True(cases > 700);
    }

    [Fact]
    public void GraphemeAndIndicPropertiesMatchUcdForEveryCodepoint()
    {
        GraphemeBreakClass[] expected = new GraphemeBreakClass[0x110000];
        foreach ((int start, int end, string[] fields) in ReadRecords("GraphemeBreakProperty.txt"))
        {
            GraphemeBreakClass value = fields[0] switch
            {
                "Other" => GraphemeBreakClass.Other, "Control" => GraphemeBreakClass.Control,
                "CR" => GraphemeBreakClass.CR, "LF" => GraphemeBreakClass.LF,
                "Extend" => GraphemeBreakClass.Extend, "L" => GraphemeBreakClass.L,
                "V" => GraphemeBreakClass.V, "T" => GraphemeBreakClass.T,
                "LV" => GraphemeBreakClass.LV, "LVT" => GraphemeBreakClass.LVT,
                "SpacingMark" => GraphemeBreakClass.SpacingMark, "Prepend" => GraphemeBreakClass.Prepend,
                "Regional_Indicator" => GraphemeBreakClass.RegionalIndicator, "ZWJ" => GraphemeBreakClass.ZWJ,
                _ => throw new InvalidDataException(fields[0]),
            };
            Array.Fill(expected, value, start, end - start);
        }
        foreach ((int start, int end, string[] fields) in ReadRecords("emoji-data.txt"))
        {
            if (fields[0] == "Extended_Pictographic") Array.Fill(expected, GraphemeBreakClass.ExtendedPictographic, start, end - start);
        }
        IndicConjunctBreakClass[] indic = new IndicConjunctBreakClass[0x110000];
        bool[] modifierBases = new bool[0x110000];
        foreach ((int start, int end, string[] fields) in ReadRecords("emoji-data.txt"))
            if (fields[0] == "Emoji_Modifier_Base") Array.Fill(modifierBases, true, start, end - start);
        foreach ((int start, int end, string[] fields) in ReadRecords("IndicConjunctBreak.txt"))
        {
            IndicConjunctBreakClass value = fields[1] switch
            {
                "Consonant" => IndicConjunctBreakClass.Consonant, "Linker" => IndicConjunctBreakClass.Linker,
                "Extend" => IndicConjunctBreakClass.Extend, "None" => IndicConjunctBreakClass.None,
                _ => throw new InvalidDataException(fields[1]),
            };
            Array.Fill(indic, value, start, end - start);
        }
        for (uint cp = 0; cp < expected.Length; cp++)
        {
            Codepoint value = new(cp);
            if (expected[cp] != value.GraphemeBreakClass) Assert.Fail($"GCB U+{cp:X}");
            if (indic[cp] != value.IndicConjunctBreakClass) Assert.Fail($"InCB U+{cp:X}");
            if (modifierBases[cp] != value.IsEmojiModifierBase) Assert.Fail($"Emoji_Modifier_Base U+{cp:X}");
        }
    }

    [Fact]
    public void EastAsianWidthMatchesUcdForEveryCodepoint()
    {
        EastAsianWidthClass[] expected = new EastAsianWidthClass[0x110000];
        Array.Fill(expected, EastAsianWidthClass.Neutral);
        // UCD default-wide ranges include unassigned CJK codepoints.
        foreach ((int start, int end) in new[] { (0x3400, 0x4DC0), (0x4E00, 0xA000), (0xF900, 0xFB00), (0x20000, 0x2FFFE), (0x30000, 0x3FFFE) })
            Array.Fill(expected, EastAsianWidthClass.Wide, start, end - start);
        foreach ((int start, int end, string[] fields) in ReadRecords("DerivedEastAsianWidth.txt"))
        {
            EastAsianWidthClass value = fields[0] switch
            {
                "N" => EastAsianWidthClass.Neutral, "Na" => EastAsianWidthClass.Narrow,
                "W" => EastAsianWidthClass.Wide, "F" => EastAsianWidthClass.Fullwidth,
                "H" => EastAsianWidthClass.Halfwidth, "A" => EastAsianWidthClass.Ambiguous,
                _ => throw new InvalidDataException(fields[0]),
            };
            Array.Fill(expected, value, start, end - start);
        }
        for (uint cp = 0; cp < expected.Length; cp++)
            if (expected[cp] != new Codepoint(cp).EastAsianWidthClass) Assert.Fail($"EAW U+{cp:X}");
    }

    [Fact]
    public void TerminalGraphemeKernelMatchesPinnedGhosttyAcrossAllRepresentativeTriples()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        // Includes every boundary class, both Unicode18 linker categories,
        // independent GB9c/GB11 state, invalid VS and surrogate scalar input.
        uint[] representatives = [0, 0x0A, 0x0D, 0x41, 0x0300, 0x0600, 0x0915, 0x094D,
            0x0903, 0x1100, 0x1161, 0x11A8, 0xAC00, 0xAC01, 0x1CF5, 0x200C,
            0x200D, 0x231A, 0xFE0E, 0xFE0F, 0x1F1E6, 0x1F44D, 0x1F3FB, 0x1F600, 0xD800];
        Span<uint> input = stackalloc uint[3];
        foreach (uint a in representatives)
        foreach (uint b in representatives)
        foreach (uint c in representatives)
        {
            input[0] = a; input[1] = b; input[2] = c;
            int consumed = TerminalCellWidthCalculator.GetFirstGraphemeWidth(input, out int width);
            nuint nativeConsumed = GhosttyUnicode.GetGraphemeWidth(input, out byte nativeWidth);
            if ((nuint)consumed != nativeConsumed || width != nativeWidth)
                Assert.Fail($"U+{a:X} U+{b:X} U+{c:X}: managed {consumed}/{width}, Ghostty {nativeConsumed}/{nativeWidth}");
        }
    }

    [Theory]
    [InlineData("?\u094D\u0924", 3, 2)]
    [InlineData("\u1CF5\u0995", 2, 2)]
    [InlineData("\U0001F600\u094D\u0300\u200D\U0001F600", 5, 2)]
    [InlineData("\U0001F600\u094D\u0300\u0915", 4, 2)]
    [InlineData("A\U0001F3FB", 1, 1)]
    [InlineData("\U0001F1E6\uFE0F\U0001F1E7", 3, 2)]
    public void TerminalTailoringPreservesUnicode18AndIgnoredSelectorState(string text, int consumed, int width)
    {
        uint[] codepoints = text.EnumerateRunes().Select(rune => (uint)rune.Value).ToArray();
        Assert.Equal(consumed, TerminalCellWidthCalculator.GetFirstGraphemeWidth(codepoints, out int actualWidth));
        Assert.Equal(width, actualWidth);
    }

    [Theory]
    [InlineData("A", 0xFE0F, true, true, 1)]
    [InlineData("A", 0x1F3FB, false, false, 1)]
    [InlineData("\U0001F44D", 0x1F3FB, true, false, 2)]
    [InlineData("?\u094D", 0x0924, true, false, 2)]
    public void AppendedGraphemeWidthUsesTerminalTailoring(string prefix, int codepoint, bool append, bool ignored, int width)
    {
        Assert.Equal(append, TerminalCellWidthCalculator.TryGetAppendedGraphemeWidth(prefix, codepoint, out int actualWidth, out bool actualIgnored));
        Assert.Equal(ignored, actualIgnored);
        Assert.Equal(width, actualWidth);
    }

    [Fact]
    public void StreamingPrinterDiscardsInvalidSelectorsWithoutBreakingRegionalIndicatorPair()
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[?2027h\U0001F1E6\uFE0F\U0001F1E7"));
        Assert.Equal("\U0001F1E6\U0001F1E7", screen.GetViewportRow(0)[0].Grapheme);
        Assert.Equal(2, processor.CursorCol);
    }

    [Theory]
    [InlineData(0x05C8)]
    [InlineData(0x05C9)]
    [InlineData(0x1AEC)]
    [InlineData(0x1AF0)]
    [InlineData(0x10EFA)]
    [InlineData(0x1E6F5)]
    public void StreamingPrinterAttachesNewCombiningMarks(int mark)
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        string text = "A" + char.ConvertFromUtf32(mark);
        foreach (byte value in Encoding.UTF8.GetBytes(text)) processor.Process([value]);
        Assert.Equal(text, screen.GetViewportRow(0)[0].Grapheme);
        Assert.Equal(1, processor.CursorCol);
    }

    [Theory]
    [InlineData("\u1100\u1161\u11A8")]
    [InlineData("\u0915\u094D\u0937")]
    [InlineData("?\u094D\u0924")]
    [InlineData("\U0001F600\u094D\u0300\u0915")]
    public void StreamingPrinterUsesFullGraphemeProperties(string text)
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[?2027h"u8);
        processor.Process(Encoding.UTF8.GetBytes(text));
        Assert.Equal(text, screen.GetViewportRow(0)[0].Grapheme);
        Assert.Equal(TerminalCellWidthCalculator.GetCellWidth(text), processor.CursorCol);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Unicode18", name);

    private static IEnumerable<(int Start, int End, string[] Fields)> ReadRecords(string name)
    {
        foreach (string line in File.ReadLines(Fixture(name)))
        {
            string data = line.Split('#')[0].Trim();
            if (data.Length == 0) continue;
            string[] fields = data.Split(';', StringSplitOptions.TrimEntries);
            string[] range = fields[0].Split("..");
            yield return (int.Parse(range[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(range[^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture) + 1, fields[1..]);
        }
    }
}
