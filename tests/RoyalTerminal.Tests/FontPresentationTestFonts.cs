// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using SkiaSharp;

namespace RoyalTerminal.Tests;

/// <summary>
/// Builds a portable COLRv0 fixture in memory from the pinned OFL Noto Emoji
/// outlines. Its name is changed; copyright/license records remain intact.
/// The bitmap-only upstream Noto Color Emoji cannot be loaded by CoreText.
/// Layouts: Microsoft OpenType COLR, CPAL and sfnt specifications.
/// </summary>
internal static class FontPresentationTestFonts
{
    internal static SKTypeface Load(string name)
    {
        if (name != "NotoColorEmoji.ttf")
            return SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts", name))
                   ?? throw new InvalidOperationException(name);

        using SKTypeface source = Load("NotoEmoji-Regular.ttf");
        SortedDictionary<uint, byte[]> tables = new();
        foreach (uint tag in source.GetTableTags())
            if (tag != 0x44534947) tables.Add(tag, source.GetTableData(tag)); // Strip invalidated DSIG.
        ushort space = source.GetGlyph(' ');
        List<ushort> glyphs = [];
        for (int glyph = 1; glyph < source.GlyphCount; glyph++)
            if (glyph != space) glyphs.Add(checked((ushort)glyph));

        byte[] colr = new byte[14 + glyphs.Count * 10];
        Put16(colr, 2, glyphs.Count);
        Put32(colr, 4, 14);
        Put32(colr, 8, (uint)(14 + glyphs.Count * 6));
        Put16(colr, 12, glyphs.Count);
        for (int i = 0; i < glyphs.Count; i++)
        {
            int record = 14 + i * 6;
            Put16(colr, record, glyphs[i]);
            Put16(colr, record + 2, i);
            Put16(colr, record + 4, 1);
            // A COLRv0 layer references the ordinary outline of this glyph,
            // not its COLR entry, so this does not form a recursive paint graph.
            Put16(colr, 14 + glyphs.Count * 6 + i * 4, glyphs[i]);
        }
        tables[0x434F4C52] = colr;
        byte[] cpal = new byte[18];
        Put16(cpal, 2, 1); Put16(cpal, 4, 1); Put16(cpal, 6, 1);
        Put32(cpal, 8, 14);
        cpal[14] = 0x40; cpal[15] = 0x70; cpal[16] = 0xF0; cpal[17] = 0xFF;
        tables[0x4350414C] = cpal;
        tables[0x6E616D65] = Rename(tables[0x6E616D65]);
        Put32(tables[0x68656164], 8, 0);

        int size = 12 + tables.Count * 16;
        foreach (byte[] table in tables.Values) size += Align(table.Length);
        byte[] bytes = new byte[size];
        Put32(bytes, 0, 0x00010000);
        Put16(bytes, 4, tables.Count);
        int selector = BitOperations.Log2((uint)tables.Count);
        int searchRange = (1 << selector) * 16;
        Put16(bytes, 6, searchRange); Put16(bytes, 8, selector);
        Put16(bytes, 10, tables.Count * 16 - searchRange);
        int directory = 12, offset = 12 + tables.Count * 16, head = 0;
        foreach ((uint tag, byte[] table) in tables)
        {
            Put32(bytes, directory, tag);
            Put32(bytes, directory + 4, Checksum(table));
            Put32(bytes, directory + 8, (uint)offset);
            Put32(bytes, directory + 12, (uint)table.Length);
            table.CopyTo(bytes, offset);
            if (tag == 0x68656164) head = offset;
            directory += 16;
            offset += Align(table.Length);
        }
        Put32(bytes, head + 8, unchecked(0xB1B0AFBA - Checksum(bytes)));
        using SKData data = SKData.CreateCopy(bytes);
        return SKTypeface.FromData(data) ?? throw new InvalidOperationException("Portable COLR fixture rejected.");
    }

    private static byte[] Rename(byte[] table)
    {
        int count = Read16(table, 2), storage = Read16(table, 4);
        List<byte[]> strings = [];
        int size = 6 + count * 12;
        for (int i = 0; i < count; i++)
        {
            int record = 6 + i * 12, name = Read16(table, record + 6);
            byte[] value;
            if (name is 1 or 3 or 4 or 6 or 16)
            {
                string renamed = name == 6 ? "RoyalTerminalColorFixture" : "RoyalTerminal Color Fixture";
                int platform = Read16(table, record);
                value = (platform is 0 or 3 ? Encoding.BigEndianUnicode : Encoding.ASCII).GetBytes(renamed);
            }
            else value = table.AsSpan(storage + Read16(table, record + 10), Read16(table, record + 8)).ToArray();
            strings.Add(value);
            size += value.Length;
        }
        byte[] result = new byte[size];
        int newStorage = 6 + count * 12, offset = newStorage;
        Put16(result, 2, count); Put16(result, 4, newStorage);
        for (int i = 0; i < count; i++)
        {
            int record = 6 + i * 12;
            table.AsSpan(record, 8).CopyTo(result.AsSpan(record));
            Put16(result, record + 8, strings[i].Length);
            Put16(result, record + 10, offset - newStorage);
            strings[i].CopyTo(result, offset);
            offset += strings[i].Length;
        }
        return result;
    }

    private static int Align(int value) => (value + 3) & ~3;
    private static int Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    private static void Put16(byte[] data, int offset, int value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), checked((ushort)value));
    private static void Put32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i++) sum = unchecked(sum + ((uint)data[i] << (24 - ((i & 3) * 8))));
        return sum;
    }
}
