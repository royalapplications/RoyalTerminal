// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    /// <summary>
    /// Captures the live terminal and unfinished parser input in Ghostty's version-1 binary
    /// format. The caller owns the result and must serialize this call with all terminal access.
    /// Includes both screens and history; upstream v1 omits images, glyph registrations and
    /// host selection. During synchronized output this captures live, not frozen render state.
    /// Continuation retention must be enabled. Throws before returning an incomplete snapshot.
    /// </summary>
    public byte[] GetBinarySnapshot(GhosttySnapshotDecodeLimits? limits = null)
    {
        using MemoryStream output = new();
        WriteBinarySnapshotTo(output, limits);
        return output.ToArray();
    }

    /// <summary>
    /// Streams a Ghostty v1 snapshot without buffering the entire history. The caller serializes
    /// all terminal access for the duration; the destination is left open. Only row references,
    /// page ranges and one encoded page are staged. Bounds apply to the same logical records as
    /// decode. On failure the destination may contain an incomplete prefix without FINISH;
    /// continuation failures emit no bytes. No VT replay or buffer switching is performed.
    /// </summary>
    public void WriteBinarySnapshotTo(Stream destination, GhosttySnapshotDecodeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) throw new ArgumentException("Snapshot destination is not writable.", nameof(destination));
        limits ??= new(); limits.Validate();
        GhosttySnapshotSavedCursor.ValidateExtent(_screen.Columns, nameof(_screen.Columns));
        GhosttySnapshotSavedCursor.ValidateExtent(_screen.ViewportRows, nameof(_screen.ViewportRows));
        ReadOnlySpan<byte> continuation = _continuation.GetBytes();
        if (continuation.Length > limits.MaximumContinuationBytes) throw new InvalidDataException("Snapshot continuation exceeds the byte limit.");
        GhosttySnapshotContinuation.Validate(continuation);
        if ((long)_title.Bytes.Length + _workingDirectory.Bytes.Length > limits.MaximumStringBytesPerRecord)
            throw new InvalidDataException("Snapshot metadata exceeds the string limit.");
        bool alternateExists = _screen.GetSnapshotRows(1) is not null;
        long cells = 0;
        int pages = 0;
        GhosttySnapshotPagePlan primary = new(_screen, 0, limits, ref cells, ref pages);
        GhosttySnapshotPagePlan? alternate = alternateExists ? new(_screen, 1, limits, ref cells, ref pages) : null;
        using MemoryStream payload = new();
        long totalPayloadBytes = 0;
        GhosttySnapshotFraming.WriteEnvelope(destination);
        CaptureSnapshotTerminal(payload, alternateExists); Emit(GhosttySnapshotRecordTag.Terminal);
        WriteScreen(0, primary);
        if (alternate is not null) WriteScreen(1, alternate);
        payload.Write(continuation); Emit(GhosttySnapshotRecordTag.Continuation);
        Emit(GhosttySnapshotRecordTag.Ready);
        WriteHistory(0, primary);
        if (alternate is not null) WriteHistory(1, alternate);
        Emit(GhosttySnapshotRecordTag.Finish);

        void WriteScreen(int key, GhosttySnapshotPagePlan plan)
        {
            CaptureSnapshotScreen(payload, key, plan.Resident.Count, plan.HistoryRows, limits.MaximumStringBytesPerRecord);
            // Cursor links have their own per-record string bound.
            _ = GhosttySnapshotScreenState.Read(payload.GetBuffer().AsSpan(0, (int)payload.Length), limits.MaximumPages, limits.MaximumStringBytesPerRecord);
            Emit(GhosttySnapshotRecordTag.Screen);
            foreach (GhosttySnapshotPagePlan.Range range in plan.Resident) WritePage(plan, range);
        }

        void WriteHistory(int key, GhosttySnapshotPagePlan plan)
        {
            Span<byte> header = stackalloc byte[6];
            BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)key);
            BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)plan.History.Count);
            payload.Write(header); Emit(GhosttySnapshotRecordTag.History);
            for (int i = plan.History.Count - 1; i >= 0; i--) WritePage(plan, plan.History[i]);
        }

        void WritePage(GhosttySnapshotPagePlan plan, GhosttySnapshotPagePlan.Range range)
        {
            GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture(plan.Rows.AsSpan(range.Start, range.Count), _screen, limits.MaximumCells);
            page.WritePayloadTo(payload);
            Emit(GhosttySnapshotRecordTag.Page);
        }

        void Emit(GhosttySnapshotRecordTag tag)
        {
            if (payload.Length > limits.MaximumPayloadBytes || payload.Length > limits.MaximumTotalPayloadBytes - totalPayloadBytes)
                throw new InvalidDataException("Snapshot exceeds the payload byte limit.");
            totalPayloadBytes += payload.Length;
            GhosttySnapshotFraming.WriteRecord(destination, tag, payload.GetBuffer().AsSpan(0, checked((int)payload.Length)));
            payload.SetLength(0); payload.Position = 0;
        }
    }
}
