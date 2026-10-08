// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace RoyalTerminal.Terminal;

/// <summary>Outcome of a bounded history operation. Failures never return partial snapshots.</summary>
public enum TerminalHistoryStatus
{
    /// <summary>The operation completed.</summary>
    Success,
    /// <summary>The processor or native library lacks this capability.</summary>
    Unsupported,
    /// <summary>The requested buffer is not active or is unavailable.</summary>
    BufferUnavailable,
    /// <summary>The range extends beyond available rows.</summary>
    RangeUnavailable,
    /// <summary>The requested rows have left retained history.</summary>
    HistoryEvicted,
    /// <summary>The supplied layout epoch is obsolete.</summary>
    LayoutChanged,
    /// <summary>The next complete row cannot fit the character budget.</summary>
    BudgetTooSmall,
    /// <summary>The processor has been disposed.</summary>
    Disposed,
}

/// <summary>Reasons that content was omitted from a successful capture.</summary>
[Flags]
public enum TerminalHistoryTruncation
{
    /// <summary>No rows were omitted.</summary>
    None = 0,
    /// <summary>The row budget omitted rows.</summary>
    RowLimit = 1,
    /// <summary>The character budget omitted complete rows.</summary>
    CharacterLimit = 2,
}

/// <summary>A half-open physical row interval within one buffer layout epoch.</summary>
/// <param name="Start">Monotonic row origin, independent of viewport scrolling.</param>
/// <param name="Count">Number of physical rows.</param>
public readonly record struct TerminalHistoryRange(long Start, int Count);

/// <summary>Metadata for the authoritative active buffer, including history and live rows.</summary>
/// <param name="BufferId">Instance-specific buffer identity.</param>
/// <param name="LayoutEpoch">Opaque row-layout identity; changes invalidate live range requests.</param>
/// <param name="AlternateBuffer">Whether this is the alternate buffer.</param>
/// <param name="Columns">Current cell width.</param>
/// <param name="Rows">Live-screen height.</param>
/// <param name="AvailableRange">Currently accessible physical rows.</param>
public sealed record TerminalHistoryBufferInfo(Guid BufferId, Guid LayoutEpoch, bool AlternateBuffer,
    int Columns, int Rows, TerminalHistoryRange AvailableRange);

/// <summary>A bounded request. A null buffer/range captures the current buffer's recent tail.</summary>
/// <param name="MaxRows">Positive row budget.</param>
/// <param name="MaxCharacters">Positive UTF-16 budget, including LF separators.</param>
/// <param name="Buffer">Metadata obtained for an explicit range.</param>
/// <param name="Range">Explicit interval; requires Buffer. Null selects the entire available tail.</param>
public readonly record struct TerminalHistoryCaptureRequest(int MaxRows, int MaxCharacters,
    TerminalHistoryBufferInfo? Buffer = null, TerminalHistoryRange? Range = null)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxRows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxCharacters, 1);
        if ((Buffer is null) != (Range is null)) throw new ArgumentException("Buffer and Range must be supplied together.");
        if (Range is { } range)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(range.Start);
            ArgumentOutOfRangeException.ThrowIfNegative(range.Count);
            if (range.Start > long.MaxValue - range.Count)
                throw new ArgumentOutOfRangeException(nameof(Range), "The range end exceeds supported row coordinates.");
        }
    }
}

/// <summary>An owned physical row. Empty rows are retained; wraps are never joined.</summary>
/// <param name="Text">Plain text with trailing erased cells omitted.</param>
/// <param name="WrapsToNext">Whether this physical row wraps into the following row.</param>
public sealed record TerminalHistoryRow(string Text, bool WrapsToNext);

/// <summary>An immutable page, whose offset is relative to the captured snapshot.</summary>
/// <param name="Status">Page outcome.</param>
/// <param name="SnapshotId">Identity of the owning snapshot.</param>
/// <param name="Offset">Requested row offset.</param>
/// <param name="Rows">Complete owned rows.</param>
/// <param name="NextOffset">Continuation within the same snapshot, or null at its end.</param>
public sealed record TerminalHistoryPage(TerminalHistoryStatus Status, Guid SnapshotId, int Offset,
    ImmutableArray<TerminalHistoryRow> Rows, int? NextOffset);

/// <summary>Owned capture, independent of the source terminal and its lifetime. No expiration is imposed.</summary>
public sealed class TerminalHistorySnapshot
{
    internal TerminalHistorySnapshot(TerminalHistoryBufferInfo buffer, TerminalHistoryRange requested,
        TerminalHistoryRange captured, ImmutableArray<TerminalHistoryRow> rows, TerminalHistoryTruncation truncation)
    {
        Buffer = buffer;
        RequestedRange = requested;
        CapturedRange = captured;
        Rows = rows;
        Truncation = truncation;
    }

    /// <summary>Unique capture identity; not a revision of the live terminal.</summary>
    public Guid SnapshotId { get; } = Guid.NewGuid();
    /// <summary>Metadata at capture time.</summary>
    public TerminalHistoryBufferInfo Buffer { get; }
    /// <summary>Range before applying budgets.</summary>
    public TerminalHistoryRange RequestedRange { get; }
    /// <summary>Contiguous range actually retained.</summary>
    public TerminalHistoryRange CapturedRange { get; }
    /// <summary>Complete immutable physical rows in oldest-to-newest order.</summary>
    public ImmutableArray<TerminalHistoryRow> Rows { get; }
    /// <summary>Reasons that requested rows were omitted.</summary>
    public TerminalHistoryTruncation Truncation { get; }
    /// <summary>Whether budgets omitted requested rows.</summary>
    public bool Truncated => Truncation != TerminalHistoryTruncation.None;

    /// <summary>Reads complete rows without consulting or retaining the live terminal.</summary>
    /// <remarks>Budgets count UTF-16 code units and LF separators. Concurrent reads are safe.</remarks>
    public TerminalHistoryPage ReadPage(int offset, int maxRows, int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Rows.Length);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 1);
        cancellationToken.ThrowIfCancellationRequested();
        int count = 0, characters = 0;
        while (count < maxRows && offset + count < Rows.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string text = Rows[offset + count].Text;
            int separator = count == 0 ? 0 : 1;
            if (separator > maxCharacters - characters || text.Length > maxCharacters - characters - separator) break;
            characters += separator + text.Length;
            count++;
        }
        if (count == 0 && offset < Rows.Length)
            return new(TerminalHistoryStatus.BudgetTooSmall, SnapshotId, offset, [], null);
        return new(TerminalHistoryStatus.Success, SnapshotId, offset, Rows.Slice(offset, count),
            offset + count < Rows.Length ? offset + count : null);
    }
}

/// <summary>Optional bounded, non-destructive observation of authoritative terminal history.</summary>
/// <remarks>Serialize calls with input, resize, reset, history edits and disposal using the owning
/// screen's lock. Returned metadata and snapshots contain no live owner or native references.</remarks>
public interface ITerminalHistorySnapshotSource
{
    /// <summary>Obtains metadata for the active buffer without moving its viewport.</summary>
    TerminalHistoryStatus GetHistoryBufferInfo(out TerminalHistoryBufferInfo? buffer);
    /// <summary>Captures a bounded range or tail. Cancellation throws; failed captures return null.</summary>
    TerminalHistoryStatus CaptureHistory(in TerminalHistoryCaptureRequest request,
        out TerminalHistorySnapshot? snapshot, CancellationToken cancellationToken = default);
}
