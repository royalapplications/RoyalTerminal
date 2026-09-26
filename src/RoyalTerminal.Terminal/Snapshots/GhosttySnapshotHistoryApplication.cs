// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Applies ordered, already-validated history PAGEs to the terminal captured at
/// READY. This is the lifecycle half of reconciliation; the enclosing adapter must
/// supply the current byte/line-limit decision and serialize calls with live input.
/// Framing, routing/order and resource validation remain the state reader's job.
/// </summary>
internal sealed class GhosttySnapshotHistoryApplication
{
    private readonly object _lineage;
    private readonly int _columns;
    private readonly int _declaredKeys;
    private readonly ulong _alternateGeneration;
    private int _droppedKeys;
    private bool _failed;

    internal GhosttySnapshotHistoryApplication(TerminalScreen restored)
    {
        ArgumentNullException.ThrowIfNull(restored);
        _lineage = restored.SnapshotLineage;
        _columns = restored.Columns;
        _alternateGeneration = restored.GetSnapshotGeneration(1);
        _declaredKeys = (restored.GetSnapshotRows(0) is null ? 0 : 1) |
            (restored.GetSnapshotRows(1) is null ? 0 : 2);
    }

    /// <summary>
    /// Consumes the application decision for one page, including pages that must
    /// be dropped. A gap permanently disables that key, not the other screen.
    /// Once installation fails, the caller must discard this application session.
    /// No quota decision is implied: the adapter must explicitly provide it.
    /// </summary>
    internal GhosttySnapshotHistoryProgress Apply(TerminalScreen current,
        in GhosttySnapshotHistoryPage history, bool fitsScrollbackLimits)
    {
        if (_failed) throw new InvalidOperationException("Snapshot history application failed previously.");
        try
        {
            ArgumentNullException.ThrowIfNull(current);
            if (history.Key is < 0 or > 1 || (_declaredKeys & (1 << history.Key)) == 0)
                throw new InvalidDataException("History names an undeclared snapshot screen.");
            ArgumentNullException.ThrowIfNull(history.Page);
            int mask = 1 << history.Key;
            bool compatible = (_droppedKeys & mask) == 0 &&
                ReferenceEquals(_lineage, current.SnapshotLineage) && current.Columns == _columns &&
                current.GetSnapshotRows(history.Key) is not null &&
                (history.Key == 0 || current.GetSnapshotGeneration(1) == _alternateGeneration) &&
                fitsScrollbackLimits;
            if (!compatible)
            {
                _droppedKeys |= mask;
                return new(history.Key, 0, history.Remaining, false);
            }
            int rows = current.PrependSnapshotHistory(history.Key, history.Page);
            return new(history.Key, rows, history.Remaining, ContainsPrompt(history.Page));
        }
        catch { _failed = true; throw; }
    }

    private static bool ContainsPrompt(GhosttySnapshotPage page)
    {
        foreach (byte flags in page.Grid.RowFlags)
            if ((flags & 12) != 0) return true;
        foreach (ulong cell in page.Grid.Cells)
            if (((cell >> 46) & 3) == (ulong)TerminalSemanticContent.Prompt) return true;
        return false;
    }
}

/// <summary>Progress after one consumed page; prompt state changes only for applied pages.</summary>
internal readonly record struct GhosttySnapshotHistoryProgress(int Key, int Rows, uint Remaining, bool ContainsPrompt);
