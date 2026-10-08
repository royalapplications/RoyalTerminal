// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Text;

namespace RoyalTerminal.Terminal;

internal interface ITerminalHistoryRowReader
{
    bool TryReadHistoryRow(int row, int maxCharacters, CancellationToken cancellationToken,
        out TerminalHistoryRow? result);
}

internal static class TerminalHistoryCapture
{
    internal static TerminalHistoryStatus Capture(ITerminalHistoryRowReader source,
        TerminalHistoryBufferInfo buffer, in TerminalHistoryCaptureRequest request,
        out TerminalHistorySnapshot? snapshot, CancellationToken cancellationToken)
    {
        snapshot = null;
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        TerminalHistoryRange available = buffer.AvailableRange;
        TerminalHistoryRange requested = request.Range ?? available;
        if (request.Buffer is { } expected)
        {
            if (expected.BufferId != buffer.BufferId) return TerminalHistoryStatus.BufferUnavailable;
            if (expected.LayoutEpoch != buffer.LayoutEpoch) return TerminalHistoryStatus.LayoutChanged;
            if (requested.Start < available.Start) return TerminalHistoryStatus.HistoryEvicted;
            if (requested.Start + requested.Count > available.Start + available.Count) return TerminalHistoryStatus.RangeUnavailable;
        }
        bool tail = request.Range is null;
        int count = Math.Min(requested.Count, request.MaxRows);
        long start = tail ? requested.Start + requested.Count - count : requested.Start;
        TerminalHistoryTruncation truncation = count < requested.Count ? TerminalHistoryTruncation.RowLimit : 0;
        // Grow only with rows actually retained; a large row budget must not preallocate all history.
        List<TerminalHistoryRow> rows = new(Math.Min(count, 16));
        int characters = 0;
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int separator = rows.Count == 0 ? 0 : 1;
            int remaining = request.MaxCharacters - characters - separator;
            long position = tail ? start + count - 1 - i : start + i;
            if (remaining < 0 || !source.TryReadHistoryRow(checked((int)(position - available.Start)),
                remaining, cancellationToken, out TerminalHistoryRow? row))
            {
                if (rows.Count == 0) return TerminalHistoryStatus.BudgetTooSmall;
                truncation |= TerminalHistoryTruncation.CharacterLimit;
                break;
            }
            rows.Add(row!);
            characters += separator + row!.Text.Length;
        }
        if (tail)
        {
            rows.Reverse();
            start += count - rows.Count;
        }
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = new(buffer, requested, new(start, rows.Count), rows.ToImmutableArray(), truncation);
        return TerminalHistoryStatus.Success;
    }
}

/// <summary>Shared plain-row semantics; storage never exceeds the requested output budget.</summary>
internal sealed class TerminalHistoryRowBuilder(int maxCharacters)
{
    private readonly StringBuilder _text = new(Math.Min(maxCharacters, 256));
    private int _erasedSpaces;
    internal int Remaining => maxCharacters - _text.Length - _erasedSpaces;

    internal void ErasedCell() => _erasedSpaces++;

    internal bool Append(ReadOnlySpan<char> text)
    {
        if (text.Length > Remaining) return false;
        int required = _text.Length + _erasedSpaces + text.Length;
        if (required > _text.Capacity)
        {
            int doubled = _text.Capacity <= int.MaxValue / 2 ? _text.Capacity * 2 : maxCharacters;
            _text.Capacity = Math.Min(maxCharacters, Math.Max(required, doubled));
        }
        _text.Append(' ', _erasedSpaces);
        _erasedSpaces = 0;
        _text.Append(text);
        return true;
    }

    internal bool Append(uint scalar)
    {
        if (!Rune.TryCreate(scalar, out Rune rune)) return Append("\uFFFD");
        Span<char> chars = stackalloc char[2];
        return Append(chars[..rune.EncodeToUtf16(chars)]);
    }

    internal TerminalHistoryRow Build(bool wrapsToNext) => new(_text.ToString(), wrapsToNext);
}
