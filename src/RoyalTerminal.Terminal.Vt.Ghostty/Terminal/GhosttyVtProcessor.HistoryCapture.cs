// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor : ITerminalHistorySnapshotSource, ITerminalHistoryRowReader
{
    private sealed class HistoryIdentity
    {
        internal readonly Guid BufferId = Guid.NewGuid();
        internal Guid Epoch = Guid.NewGuid();
        internal ulong NativeEpoch;
        internal ulong Generation;
        internal int HostLimit = -1;
    }

    private readonly HistoryIdentity _historyPrimary = new();
    private readonly HistoryIdentity _historyAlternate = new();

    /// <inheritdoc />
    public TerminalHistoryStatus GetHistoryBufferInfo(out TerminalHistoryBufferInfo? buffer)
    {
        buffer = null;
        if (_disposed) return TerminalHistoryStatus.Disposed;
        if (!_terminal.TryGetHistoryInfo(out GhosttyVtNative.RoyalHistoryInfo native)) return TerminalHistoryStatus.Unsupported;
        bool alternate = native.Alternate != 0;
        HistoryIdentity identity = alternate ? _historyAlternate : _historyPrimary;
        if (identity.NativeEpoch != native.Epoch || identity.Generation != native.ScreenGeneration ||
            identity.HostLimit != _screen.ScrollbackLimit)
        {
            identity.Epoch = Guid.NewGuid();
            identity.NativeEpoch = native.Epoch;
            identity.Generation = native.ScreenGeneration;
            identity.HostLimit = _screen.ScrollbackLimit;
        }
        ulong history = native.TotalRows > native.Rows ? native.TotalRows - native.Rows : 0;
        ulong accessibleHistory = alternate ? 0 : Math.Min(history, (ulong)Math.Max(0, _screen.ScrollbackLimit));
        ulong hidden = history - accessibleHistory;
        buffer = new(identity.BufferId, identity.Epoch, alternate, native.Columns, native.Rows,
            new(checked((long)(native.Origin + hidden)), checked((int)(native.TotalRows - hidden))));
        return TerminalHistoryStatus.Success;
    }

    /// <inheritdoc />
    public TerminalHistoryStatus CaptureHistory(in TerminalHistoryCaptureRequest request,
        out TerminalHistorySnapshot? snapshot, CancellationToken cancellationToken = default)
    {
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = null;
        TerminalHistoryStatus status = GetHistoryBufferInfo(out TerminalHistoryBufferInfo? buffer);
        return status == TerminalHistoryStatus.Success
            ? TerminalHistoryCapture.Capture(this, buffer!, request, out snapshot, cancellationToken) : status;
    }

    unsafe bool ITerminalHistoryRowReader.TryReadHistoryRow(int row, int maxCharacters,
        CancellationToken cancellationToken, out TerminalHistoryRow? result)
    {
        // Query authoritative geometry, not the last published scrollbar/render mirror.
        _terminal.TryGetHistoryInfo(out GhosttyVtNative.RoyalHistoryInfo native);
        ulong history = native.TotalRows > native.Rows ? native.TotalRows - native.Rows : 0;
        ulong accessible = native.Alternate != 0 ? 0 : Math.Min(history, (ulong)Math.Max(0, _screen.ScrollbackLimit));
        ulong absoluteRow = checked(history - accessible + (ulong)row);
        TerminalHistoryRowBuilder builder = new(maxCharacters);
        bool wraps = false;
        result = null;
        Span<uint> small = stackalloc uint[16];
        var reference = GhosttyVtNative.GhosttyGridRef.CreateSized();
        Require(GhosttyVtNative.RoyalHistoryRowRef(_terminal.Handle, absoluteRow, ref reference));
        Require(GhosttyVtNative.GridRefRow(in reference, out ulong rawRow));
        Require(GhosttyVtNative.RowGet(rawRow, GhosttyVtNative.GhosttyRowData.Wrap, &wraps));
        for (ushort column = 0; column < native.Columns; column++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reference.X = column;
            Require(GhosttyVtNative.GridRefCell(in reference, out ulong cell));
            GhosttyVtNative.GhosttyCellWide wide = default;
            Require(GhosttyVtNative.CellGet(cell, GhosttyVtNative.GhosttyCellData.Wide, &wide));
            if (wide is GhosttyVtNative.GhosttyCellWide.SpacerHead or GhosttyVtNative.GhosttyCellWide.SpacerTail) continue;
            bool hasText = false;
            Require(GhosttyVtNative.CellGet(cell, GhosttyVtNative.GhosttyCellData.HasText, &hasText));
            if (!hasText) { builder.ErasedCell(); continue; }
            fixed (uint* buffer = small)
            {
                GhosttyVtNative.GhosttyResult status = GhosttyVtNative.GridRefGraphemes(in reference, buffer, 16, out nuint needed);
                if (needed > (nuint)Math.Max(0, builder.Remaining)) return false;
                if (status == GhosttyVtNative.GhosttyResult.Success)
                {
                    if (!AppendGrapheme(builder, small[..checked((int)needed)], cancellationToken)) return false;
                }
                else if (status == GhosttyVtNative.GhosttyResult.OutOfSpace)
                {
                    GhosttyVtNative.GhosttyResult fit = GhosttyVtNative.RoyalHistoryGraphemeFits(in reference,
                        (nuint)Math.Max(0, builder.Remaining));
                    if (fit == GhosttyVtNative.GhosttyResult.OutOfSpace) return false;
                    Require(fit);
                    // Exact sizing avoids pool bucket slack exceeding the remaining budget.
                    uint[] scratch = GC.AllocateUninitializedArray<uint>(checked((int)needed));
                    fixed (uint* large = scratch)
                        Require(GhosttyVtNative.GridRefGraphemes(in reference, large, needed, out needed));
                    if (!AppendGrapheme(builder, scratch, cancellationToken)) return false;
                }
                else Require(status);
            }
        }
        result = builder.Build(wraps);
        return true;
    }

    private static bool AppendGrapheme(TerminalHistoryRowBuilder builder, ReadOnlySpan<uint> scalars,
        CancellationToken cancellationToken)
    {
        foreach (uint scalar in scalars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!builder.Append(scalar)) return false;
        }
        return true;
    }

    private static void Require(GhosttyVtNative.GhosttyResult status)
    {
        if (status != GhosttyVtNative.GhosttyResult.Success)
            throw new InvalidOperationException($"Native history read failed: {status}.");
    }
}
