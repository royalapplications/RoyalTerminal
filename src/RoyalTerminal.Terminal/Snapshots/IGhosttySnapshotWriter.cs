// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

// A constrained generic sink keeps the existing streaming codecs usable with
// stack-owned scratch, without boxing ref structs or retaining stack pointers.
internal interface IGhosttySnapshotWriter
{
    void Write(scoped ReadOnlySpan<byte> bytes);
    void WriteByte(byte value);
}

internal readonly struct GhosttySnapshotStreamWriter(Stream stream) : IGhosttySnapshotWriter
{
    public void Write(scoped ReadOnlySpan<byte> bytes) => stream.Write(bytes);
    public void WriteByte(byte value) => stream.WriteByte(value);
}
