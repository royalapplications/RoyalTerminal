// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace RoyalTerminal.Terminal.Snapshots;

// Ghostty snapshot/kitty.zig at 4a4fc017. Unlike xterm.js VT serialization
// and Windows Terminal's SIXEL storage, these records preserve Kitty identities,
// pending pixels and animation state. Keep wire validation independent of rendering.
internal sealed record GhosttySnapshotKittyStorage(int Key, uint NextImageId, uint NextPlacementId,
    GhosttySnapshotKittyImage[] Images, GhosttySnapshotKittyPlacement[] Placements, byte[] Loading);
internal sealed record GhosttySnapshotKittyImage(uint Id, uint Number, uint Width, uint Height,
    byte Format, byte Flags, ulong Length, GhosttySnapshotKittyAnimation? Animation)
{
    internal bool Valid => Id != 0 && ExpectedLength(Width, Height, Format) == Length && Length > 0 &&
        (Animation is null || Array.TrueForAll(Animation.Frames, f => f.Length == ExpectedLength(Width, Height, 1)));
    internal static ulong? ExpectedLength(uint width, uint height, byte format)
    {
        uint channels = format switch { 0 => 3u, 1 => 4u, 3 => 2u, 4 => 1u, _ => 0u };
        ulong pixels = (ulong)width * height;
        return pixels == 0 || channels == 0 || pixels > ulong.MaxValue / channels ? null : pixels * channels;
    }
}
internal sealed record GhosttySnapshotKittyAnimation(byte State, uint MaximumLoops, uint CurrentLoop,
    uint CurrentIndex, uint RootGap, GhosttySnapshotKittyFrame[] Frames);
internal readonly record struct GhosttySnapshotKittyFrame(uint Gap, ulong Length);
internal readonly record struct GhosttySnapshotKittyKey(uint ImageId, byte Tag, uint Id)
{
    internal bool Valid => ImageId != 0 && Tag <= 1 && (Tag != 1 || Id != 0);
}
internal sealed record GhosttySnapshotKittyPlacement(GhosttySnapshotKittyKey Key, byte Location,
    ushort Page, ushort Row, ushort Column, GhosttySnapshotKittyKey Parent, int Horizontal, int Vertical,
    uint OffsetX, uint OffsetY, uint SourceX, uint SourceY, uint SourceWidth, uint SourceHeight,
    uint Columns, uint Rows, int Z);
internal sealed record GhosttySnapshotKittyPagePlacements(int Key, GhosttySnapshotKittyPlacement[] Placements);
internal sealed record GhosttySnapshotKittyPayload(int Key, uint ImageId, uint Frame, ulong DeclaredLength, ReadOnlyMemory<byte> Pixels);

internal static class GhosttySnapshotKittyCodec
{
    internal static GhosttySnapshotKittyStorage ReadStorage(ReadOnlySpan<byte> payload)
    {
        GhosttySnapshotBinaryReader reader = new(payload);
        int key = reader.ScreenKey();
        uint nextImage = reader.UInt32(), nextPlacement = reader.UInt32();
        uint imageCount = reader.UInt32(), placementCount = reader.UInt32();
        bool loading = reader.Byte() != 0;
        if ((ulong)imageCount * 27 + (ulong)placementCount * 46 > (ulong)reader.Remaining.Length)
            throw new EndOfStreamException();
        GhosttySnapshotKittyImage[] images = new GhosttySnapshotKittyImage[checked((int)imageCount)];
        for (int i = 0; i < images.Length; i++)
        {
            uint id = reader.UInt32(), number = reader.UInt32(), width = reader.UInt32(), height = reader.UInt32();
            byte format = reader.Byte(), flags = reader.Byte();
            ulong length = reader.UInt64();
            GhosttySnapshotKittyAnimation? animation = null;
            if (reader.Byte() != 0)
            {
                byte state = reader.Byte();
                uint maximum = reader.UInt32(), currentLoop = reader.UInt32(), currentIndex = reader.UInt32(), rootGap = reader.UInt32();
                uint count = reader.UInt32();
                if ((ulong)count * 12 > (ulong)reader.Remaining.Length) throw new EndOfStreamException();
                GhosttySnapshotKittyFrame[] frames = new GhosttySnapshotKittyFrame[checked((int)count)];
                for (int f = 0; f < frames.Length; f++) frames[f] = new(reader.UInt32(), reader.UInt64());
                animation = new(state <= 2 ? state : (byte)0, maximum, currentLoop,
                    currentIndex <= count ? currentIndex : 0, rootGap, frames);
            }
            images[i] = new(id, number, width, height, format, flags, length, animation);
        }
        GhosttySnapshotKittyPlacement[] placements = ReadPlacements(ref reader, placementCount, page: false);
        byte[] load = [];
        if (loading)
        {
            ReadOnlySpan<byte> start = reader.Remaining;
            SkipLoading(ref reader);
            load = start[..(start.Length - reader.Remaining.Length)].ToArray();
        }
        reader.Finish();
        return new(key, nextImage, nextPlacement, images, placements, load);
    }

    internal static GhosttySnapshotKittyPagePlacements ReadPagePlacements(ReadOnlySpan<byte> payload)
    {
        GhosttySnapshotBinaryReader reader = new(payload);
        int key = reader.ScreenKey();
        GhosttySnapshotKittyPlacement[] placements = ReadPlacements(ref reader, reader.UInt32(), page: true);
        reader.Finish();
        return new(key, placements);
    }

    internal static GhosttySnapshotKittyPayload ReadPayload(ReadOnlyMemory<byte> payload)
    {
        GhosttySnapshotBinaryReader reader = new(payload.Span);
        int key = reader.ScreenKey();
        uint image = reader.UInt32(), frame = reader.UInt32();
        ulong length = reader.UInt64();
        // Matching live targets validate the declared length before copying.
        // Dropped targets still validate framing/CRC but ignore stale pixels.
        return new(key, image, frame, length, payload[18..]);
    }

    private static GhosttySnapshotKittyPlacement[] ReadPlacements(ref GhosttySnapshotBinaryReader r, uint count, bool page)
    {
        if ((ulong)count * 46 > (ulong)r.Remaining.Length) throw new EndOfStreamException();
        GhosttySnapshotKittyPlacement[] result = new GhosttySnapshotKittyPlacement[checked((int)count)];
        for (int i = 0; i < result.Length; i++)
        {
            GhosttySnapshotKittyKey key = ReadKey(ref r), parent = default;
            byte location = r.Byte();
            ushort pageIndex = 0, row = 0, column = 0;
            int horizontal = 0, vertical = 0;
            switch (location)
            {
                case 0:
                    pageIndex = page ? (ushort)0 : r.UInt16(); row = r.UInt16(); column = r.UInt16(); break;
                case 1: if (page) location = byte.MaxValue; break;
                case 2: parent = ReadKey(ref r); horizontal = r.Int32(); vertical = r.Int32(); break;
                default: location = byte.MaxValue; break;
            }
            result[i] = new(key, location, pageIndex, row, column, parent, horizontal, vertical,
                r.UInt32(), r.UInt32(), r.UInt32(), r.UInt32(), r.UInt32(), r.UInt32(), r.UInt32(), r.UInt32(), r.Int32());
        }
        return result;
    }

    internal static GhosttySnapshotKittyKey ReadKey(ref GhosttySnapshotBinaryReader reader)
        => new(reader.UInt32(), reader.Byte(), reader.UInt32());

    // Load payload is retained verbatim until its managed adapter validates the
    // command semantics. Validate every declared byte here before READY commits.
    private static void SkipLoading(ref GhosttySnapshotBinaryReader reader)
    {
        reader.Bytes(36);
        if (reader.Byte() != 0) reader.Bytes(66);
        if (reader.Byte() != 0) reader.Bytes(61);
        uint count = reader.UInt32();
        if (count > (uint)reader.Remaining.Length) throw new EndOfStreamException();
        reader.Bytes((int)count);
    }

    internal static void WriteStorage(ref GhosttySnapshotRecordBuffer writer, GhosttySnapshotKittyStorage value)
    {
        Write16(ref writer, checked((ushort)value.Key)); Write32(ref writer, value.NextImageId); Write32(ref writer, value.NextPlacementId);
        Write32(ref writer, (uint)value.Images.Length); Write32(ref writer, (uint)value.Placements.Length);
        writer.WriteByte(value.Loading.Length == 0 ? (byte)0 : (byte)1);
        foreach (GhosttySnapshotKittyImage image in value.Images)
        {
            Write32(ref writer, image.Id); Write32(ref writer, image.Number); Write32(ref writer, image.Width); Write32(ref writer, image.Height);
            writer.WriteByte(image.Format); writer.WriteByte(image.Flags); Write64(ref writer, image.Length);
            writer.WriteByte(image.Animation is null ? (byte)0 : (byte)1);
            if (image.Animation is { } animation)
            {
                writer.WriteByte(animation.State); Write32(ref writer, animation.MaximumLoops); Write32(ref writer, animation.CurrentLoop);
                Write32(ref writer, animation.CurrentIndex); Write32(ref writer, animation.RootGap); Write32(ref writer, (uint)animation.Frames.Length);
                foreach (GhosttySnapshotKittyFrame frame in animation.Frames) { Write32(ref writer, frame.Gap); Write64(ref writer, frame.Length); }
            }
        }
        foreach (GhosttySnapshotKittyPlacement placement in value.Placements) WritePlacement(ref writer, placement, page: false);
        writer.Write(value.Loading);
    }

    internal static void WritePagePlacements(ref GhosttySnapshotRecordBuffer writer, GhosttySnapshotKittyPagePlacements value)
    {
        Write16(ref writer, checked((ushort)value.Key)); Write32(ref writer, (uint)value.Placements.Length);
        foreach (GhosttySnapshotKittyPlacement placement in value.Placements) WritePlacement(ref writer, placement, page: true);
    }

    internal static void WritePayload(ref GhosttySnapshotRecordBuffer writer, int key, uint imageId, uint frame, ReadOnlySpan<byte> bytes)
    {
        Write16(ref writer, checked((ushort)key)); Write32(ref writer, imageId); Write32(ref writer, frame);
        Write64(ref writer, (ulong)bytes.Length); writer.Write(bytes);
    }

    private static void WritePlacement(ref GhosttySnapshotRecordBuffer w, GhosttySnapshotKittyPlacement p, bool page)
    {
        WriteKey(ref w, p.Key); w.WriteByte(p.Location);
        switch (p.Location)
        {
            case 0: if (!page) Write16(ref w, p.Page); Write16(ref w, p.Row); Write16(ref w, p.Column); break;
            case 2: WriteKey(ref w, p.Parent); Write32(ref w, unchecked((uint)p.Horizontal)); Write32(ref w, unchecked((uint)p.Vertical)); break;
        }
        Write32(ref w, p.OffsetX); Write32(ref w, p.OffsetY); Write32(ref w, p.SourceX); Write32(ref w, p.SourceY);
        Write32(ref w, p.SourceWidth); Write32(ref w, p.SourceHeight); Write32(ref w, p.Columns); Write32(ref w, p.Rows); Write32(ref w, unchecked((uint)p.Z));
    }
    private static void WriteKey(ref GhosttySnapshotRecordBuffer w, GhosttySnapshotKittyKey key)
    { Write32(ref w, key.ImageId); w.WriteByte(key.Tag); Write32(ref w, key.Id); }
    internal static void Write16(ref GhosttySnapshotRecordBuffer w, ushort value)
    { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); w.Write(bytes); }
    internal static void Write32(ref GhosttySnapshotRecordBuffer w, uint value)
    { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); w.Write(bytes); }
    internal static void Write64(ref GhosttySnapshotRecordBuffer w, ulong value)
    { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, value); w.Write(bytes); }
}

internal ref struct GhosttySnapshotBinaryReader(ReadOnlySpan<byte> bytes)
{
    internal ReadOnlySpan<byte> Remaining { get; private set; } = bytes;
    internal ReadOnlySpan<byte> Bytes(int count)
    {
        if (count < 0 || count > Remaining.Length) throw new EndOfStreamException();
        ReadOnlySpan<byte> result = Remaining[..count]; Remaining = Remaining[count..]; return result;
    }
    internal byte Byte() => Bytes(1)[0];
    internal ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
    internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
    internal int Int32() => unchecked((int)UInt32());
    internal ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8));
    internal int ScreenKey() { int key = UInt16(); return key <= 1 ? key : throw new InvalidDataException("Invalid Kitty snapshot screen key."); }
    internal void Finish() { if (!Remaining.IsEmpty) throw new InvalidDataException("Trailing Kitty snapshot record bytes."); }
}
