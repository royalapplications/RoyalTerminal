// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedKittyImageLoader
{
    internal byte[] CaptureSnapshot(uint assignedId)
    {
        GhosttySnapshotRecordBuffer w = new(stackalloc byte[256], checked(_buffer.Data.Length + 256));
        try
        {
            ManagedKittyGraphicsCommand c = InitialCommand;
            U32(ref w, assignedId); U32(ref w, c.ImageNumber); U32(ref w, c.Get('s')); U32(ref w, c.Get('v'));
            w.WriteByte(WireFormat(c.Get('f', 32))); w.WriteByte(c.Get('o') == 'z' ? (byte)1 : (byte)0);
            w.WriteByte((byte)((c.Get('N') & 1) | (c.ImageId == 0 && c.ImageNumber == 0 ? 2u : 0u))); w.WriteByte((byte)Quiet);
            U32(ref w, c.ImageId); U32(ref w, c.ImageNumber); U32(ref w, c.PlacementId); U32(ref w, 0);
            w.WriteByte(c.Action == 'T' ? (byte)1 : (byte)0);
            if (c.Action == 'T')
            {
                foreach (char key in "iIpxywhXYcr") U32(ref w, c.Get(key));
                w.WriteByte(c.Get('C') == 1 ? (byte)1 : (byte)0); w.WriteByte(c.Get('U') != 0 ? (byte)1 : (byte)0);
                foreach (char key in "PQHVz") U32(ref w, c.Get(key));
            }
            w.WriteByte(c.Action == 'f' ? (byte)1 : (byte)0);
            if (c.Action == 'f')
            {
                w.WriteByte(WireFormat(c.Get('f', 32))); w.WriteByte(0);
                foreach (char key in "svSOiIp") U32(ref w, c.Get(key));
                w.WriteByte(c.Get('o') == 'z' ? (byte)1 : (byte)0); w.WriteByte(c.MoreChunks ? (byte)1 : (byte)0);
                foreach (char key in "Nxycrz") U32(ref w, c.Get(key));
                w.WriteByte(c.Get('X') == 1 ? (byte)1 : (byte)0); U32(ref w, c.Get('Y'));
            }
            U32(ref w, (uint)_buffer.Data.Length); w.Write(_buffer.Data.Span);
            return w.WrittenSpan.ToArray();
        }
        finally { w.Dispose(); }
    }

    internal static ManagedKittyImageLoader? RestoreSnapshot(ReadOnlySpan<byte> bytes,
        BasicVtProcessorOptions options, out uint assignedId)
    {
        GhosttySnapshotBinaryReader r = new(bytes);
        assignedId = r.UInt32();
        uint number = r.UInt32(), width = r.UInt32(), height = r.UInt32();
        byte format = r.Byte(), compression = r.Byte(), flags = r.Byte(), quiet = r.Byte();
        uint responseId = r.UInt32(), responseNumber = r.UInt32(), placement = r.UInt32();
        _ = r.UInt32(); // Frame reply is resolved only when loading completes.
        bool valid = format <= 4 && compression <= 1 && quiet <= 2;
        ManagedKittyGraphicsCommand c = new();
        c.SetValue('a', 't'); c.SetValue('i', responseId); c.SetValue('I', responseNumber);
        c.SetValue('p', placement); c.SetValue('s', width); c.SetValue('v', height);
        c.SetValue('f', ProtocolFormat(format)); c.SetValue('N', (uint)(flags & 1)); c.SetValue('q', quiet);
        if (compression == 1) c.SetValue('o', 'z');
        if (r.Byte() != 0)
        {
            c.SetValue('a', 'T');
            foreach (char key in "iIpxywhXYcr") c.SetValue(key, r.UInt32());
            byte cursor = r.Byte(); valid &= cursor <= 1;
            c.SetValue('C', cursor); c.SetValue('U', r.Byte() == 0 ? 0u : 1u);
            foreach (char key in "PQHVz") c.SetValue(key, r.UInt32());
        }
        if (r.Byte() != 0)
        {
            c.SetValue('a', 'f');
            byte frameFormat = r.Byte(), medium = r.Byte();
            valid &= frameFormat <= 4 && medium <= 3;
            c.SetValue('f', ProtocolFormat(frameFormat));
            c.SetValue('t', medium switch { 1 => 'f', 2 => 't', 3 => 's', _ => (uint)'d' });
            foreach (char key in "svSOiIp") c.SetValue(key, r.UInt32());
            byte frameCompression = r.Byte(); valid &= frameCompression <= 1;
            c.SetValue('o', frameCompression == 1 ? 'z' : 0u);
            c.SetValue('m', r.Byte() == 0 ? 0u : 1u);
            foreach (char key in "Nxycrz") c.SetValue(key, r.UInt32());
            byte composition = r.Byte(); valid &= composition <= 1;
            c.SetValue('X', composition); c.SetValue('Y', r.UInt32());
        }
        uint length = r.UInt32();
        if (length > (uint)r.Remaining.Length) throw new EndOfStreamException();
        ReadOnlySpan<byte> data = r.Bytes((int)length); r.Finish();
        if (!valid || data.Length > options.KittyGraphicsMaxImageBytes) return null;
        // The response identity belongs to the initial transmission. The load's
        // assigned implicit ID lives in the store, not in the reply command.
        c.SetValue('i', responseId); c.SetValue('I', responseNumber != 0 ? responseNumber : number);
        c.SetValue('p', placement);
        return new(c, options.KittyGraphicsPngDecoder, options.KittyGraphicsMaxImageBytes, data.ToArray(), null) { Quiet = quiet };
    }

    private static byte WireFormat(uint value) => value switch { 24 => 0, 100 => 2, 16 => 3, 8 => 4, _ => 1 };
    private static uint ProtocolFormat(byte value) => value switch { 0 => 24, 2 => 100, 3 => 16, 4 => 8, _ => 32 };
    private static void U32(ref GhosttySnapshotRecordBuffer w, uint value) => GhosttySnapshotKittyCodec.Write32(ref w, value);
}
