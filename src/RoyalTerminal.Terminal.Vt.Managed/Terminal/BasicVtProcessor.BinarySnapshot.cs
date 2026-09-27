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
    /// page ranges and one encoded page are staged. Record scratch starts on the stack and
    /// rents cleared-on-return storage for larger records. Bounds apply before scratch growth to
    /// the same logical records as
    /// decode. On failure the destination may contain an incomplete prefix without FINISH;
    /// continuation failures emit no bytes. No VT replay or buffer switching is performed.
    /// </summary>
    public void WriteBinarySnapshotTo(Stream destination, GhosttySnapshotDecodeLimits? limits = null)
    {
        _screen.ThrowIfSnapshotMutationFailed();
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
        long totalPayloadBytes = 0;
        GhosttySnapshotRecordBuffer payload = new(stackalloc byte[512], RecordLimit());
        try
        {
            GhosttySnapshotFraming.WriteEnvelope(destination);
            CaptureSnapshotTerminal(ref payload, alternateExists); Emit(ref payload, GhosttySnapshotRecordTag.Terminal);
            WriteScreen(ref payload, 0, primary);
            if (alternate is not null) WriteScreen(ref payload, 1, alternate);
            payload.Write(continuation); Emit(ref payload, GhosttySnapshotRecordTag.Continuation);
            Emit(ref payload, GhosttySnapshotRecordTag.Ready);
            WriteHistory(ref payload, 0, primary);
            if (alternate is not null) WriteHistory(ref payload, 1, alternate);
            Emit(ref payload, GhosttySnapshotRecordTag.Finish);
        }
        finally { payload.Dispose(); }

        void WriteScreen(ref GhosttySnapshotRecordBuffer payload, int key, GhosttySnapshotPagePlan plan)
        {
            CaptureSnapshotScreen(ref payload, key, plan.Resident.Count, plan.HistoryRows, limits.MaximumStringBytesPerRecord);
            // Cursor links have their own per-record string bound.
            _ = GhosttySnapshotScreenState.Read(payload.WrittenSpan, limits.MaximumPages, limits.MaximumStringBytesPerRecord);
            Emit(ref payload, GhosttySnapshotRecordTag.Screen);
            foreach (GhosttySnapshotPagePlan.Range range in plan.Resident) WritePage(ref payload, plan, range);
        }

        void WriteHistory(ref GhosttySnapshotRecordBuffer payload, int key, GhosttySnapshotPagePlan plan)
        {
            Span<byte> header = stackalloc byte[6];
            BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)key);
            BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)plan.History.Count);
            payload.Write(header); Emit(ref payload, GhosttySnapshotRecordTag.History);
            for (int i = plan.History.Count - 1; i >= 0; i--) WritePage(ref payload, plan, plan.History[i]);
        }

        void WritePage(ref GhosttySnapshotRecordBuffer payload, GhosttySnapshotPagePlan plan, GhosttySnapshotPagePlan.Range range)
        {
            GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture(plan.Rows.AsSpan(range.Start, range.Count), _screen, limits.MaximumCells);
            page.WritePayloadTo(ref payload);
            Emit(ref payload, GhosttySnapshotRecordTag.Page);
        }

        int RecordLimit() => (int)Math.Min(limits.MaximumPayloadBytes, limits.MaximumTotalPayloadBytes - totalPayloadBytes);

        void Emit(ref GhosttySnapshotRecordBuffer payload, GhosttySnapshotRecordTag tag)
        {
            totalPayloadBytes += payload.WrittenSpan.Length;
            GhosttySnapshotFraming.WriteRecord(destination, tag, payload.WrittenSpan);
            payload.Reset(RecordLimit());
        }
    }
}
