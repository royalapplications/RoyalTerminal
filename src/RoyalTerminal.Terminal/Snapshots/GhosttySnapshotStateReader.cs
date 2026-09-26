// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Validates ordered snapshot records transactionally through READY, then yields
/// history one page at a time. Never executes continuation or mutates a terminal.
/// Caller streams stay open; bytes after FINISH remain unread.
/// </summary>
internal sealed class GhosttySnapshotStateReader : IDisposable
{
    private readonly GhosttySnapshotRecordReader _reader;
    private readonly GhosttySnapshotDecodeLimits _limits;
    private Phase _phase;
    private int _remainingCells;
    private int _remainingPages;
    private long _remainingPayloadBytes;
    private int _screenCount;
    private int _pendingHistories;
    private int _historyKeys;
    private int _currentHistoryKey;
    private uint _currentHistoryPages;

    internal GhosttySnapshotStateReader(ReadOnlyMemory<byte> source, GhosttySnapshotDecodeLimits limits)
        : this(CreateReader(source, limits), limits) { }

    internal GhosttySnapshotStateReader(Stream source, GhosttySnapshotDecodeLimits limits)
        : this(CreateReader(source, limits), limits) { }

    private GhosttySnapshotStateReader(GhosttySnapshotRecordReader reader, GhosttySnapshotDecodeLimits limits)
    {
        _reader = reader;
        _limits = limits;
        _remainingCells = limits.MaximumCells;
        _remainingPages = limits.MaximumPages;
        _remainingPayloadBytes = limits.MaximumTotalPayloadBytes;
    }

    internal long SourceOffset => _reader.SourceOffset;

    internal GhosttySnapshotReadyState ReadReady()
    {
        EnsureUsable();
        if (_phase != Phase.Start) throw new InvalidOperationException("Snapshot READY was already consumed.");
        try
        {
            _reader.ReadEnvelope();
            GhosttySnapshotTerminalState terminal = GhosttySnapshotTerminalState.Read(
                ReadPayload(GhosttySnapshotRecordTag.Terminal), _remainingCells, _limits.MaximumStringBytesPerRecord);
            _screenCount = terminal.Header.ScreenCount;
            GhosttySnapshotScreen[] screens = new GhosttySnapshotScreen[_screenCount];
            int keys = 0;
            for (int index = 0; index < _screenCount; index++)
            {
                GhosttySnapshotScreenState state = GhosttySnapshotScreenState.Read(
                    ReadPayload(GhosttySnapshotRecordTag.Screen), _remainingPages, _limits.MaximumStringBytesPerRecord);
                ValidateKey(state.Key, ref keys);
                _remainingPages -= state.PageCount;
                // Every page must contain at least one cell. Check before allocating its reference array.
                if (state.PageCount > _remainingCells) throw new InvalidDataException("Snapshot pages exceed the remaining cell budget.");
                GhosttySnapshotPage[] pages = new GhosttySnapshotPage[state.PageCount];
                long rows = 0;
                for (int pageIndex = 0; pageIndex < pages.Length; pageIndex++)
                {
                    GhosttySnapshotPage page = ReadPage();
                    pages[pageIndex] = page;
                    rows += page.Grid.Rows;
                }
                if (rows < terminal.Header.Rows) throw new InvalidDataException("Snapshot screen pages do not cover the active area.");
                screens[state.Key] = new(state, pages);
            }
            ReadOnlySpan<byte> continuation = ReadPayload(GhosttySnapshotRecordTag.Continuation);
            if (continuation.Length > _limits.MaximumContinuationBytes)
                throw new InvalidDataException("Snapshot continuation exceeds the configured byte limit.");
            GhosttySnapshotContinuation.Validate(continuation);
            byte[] ownedContinuation = continuation.ToArray();
            ReadPayload(GhosttySnapshotRecordTag.Ready);
            _pendingHistories = _screenCount;
            _phase = Phase.History;
            return new(terminal, screens, ownedContinuation);
        }
        catch { _phase = Phase.Failed; throw; }
    }

    internal GhosttySnapshotHistoryPage? ReadNextHistoryPage()
    {
        EnsureUsable();
        if (_phase == Phase.Start) throw new InvalidOperationException("Read snapshot READY before history.");
        if (_phase == Phase.Finished) return null;
        try
        {
            while (true)
            {
                if (_currentHistoryPages > 0)
                {
                    GhosttySnapshotPage page = ReadPage();
                    return new(_currentHistoryKey, page, --_currentHistoryPages);
                }
                if (_pendingHistories == 0)
                {
                    ReadPayload(GhosttySnapshotRecordTag.Finish);
                    _phase = Phase.Finished;
                    return null;
                }
                GhosttySnapshotHistoryHeader header = GhosttySnapshotHistoryHeader.Read(
                    ReadPayload(GhosttySnapshotRecordTag.History), _remainingPages);
                ValidateKey(header.Key, ref _historyKeys);
                _pendingHistories--;
                _remainingPages -= (int)header.PageCount;
                _currentHistoryKey = header.Key;
                _currentHistoryPages = header.PageCount;
            }
        }
        catch { _phase = Phase.Failed; throw; }
    }

    public void Dispose()
    {
        if (_phase == Phase.Disposed) return;
        _reader.Dispose();
        _phase = Phase.Disposed;
    }

    private GhosttySnapshotPage ReadPage()
    {
        GhosttySnapshotPage page = GhosttySnapshotPage.Read(ReadPayload(GhosttySnapshotRecordTag.Page),
            _remainingCells, _limits.MaximumSuffixCodepointsPerPage, _limits.MaximumStringBytesPerRecord);
        _remainingCells -= checked(page.Grid.Columns * page.Grid.Rows);
        return page;
    }

    private ReadOnlySpan<byte> ReadPayload(GhosttySnapshotRecordTag expected)
    {
        GhosttySnapshotRecordTag actual = _reader.ReadRecord(out ReadOnlySpan<byte> payload);
        if (actual != expected) throw new InvalidDataException($"Expected snapshot {expected}, got {actual}.");
        if (payload.Length > _remainingPayloadBytes) throw new InvalidDataException("Snapshot exceeds the total payload byte limit.");
        _remainingPayloadBytes -= payload.Length;
        return payload;
    }

    private void ValidateKey(int key, ref int keys)
    {
        if (key >= _screenCount || (keys & (1 << key)) != 0)
            throw new InvalidDataException("Snapshot screen routing is undeclared or duplicated.");
        keys |= 1 << key;
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_phase == Phase.Disposed, this);
        if (_phase == Phase.Failed) throw new InvalidOperationException("Snapshot decoder is invalid after an earlier failure.");
    }

    private static GhosttySnapshotRecordReader CreateReader(ReadOnlyMemory<byte> source, GhosttySnapshotDecodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits); limits.Validate();
        return new(source, (int)Math.Min(limits.MaximumPayloadBytes, limits.MaximumTotalPayloadBytes));
    }

    private static GhosttySnapshotRecordReader CreateReader(Stream source, GhosttySnapshotDecodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits); limits.Validate();
        return new(source, (int)Math.Min(limits.MaximumPayloadBytes, limits.MaximumTotalPayloadBytes));
    }

    private enum Phase : byte { Start, History, Finished, Failed, Disposed }
}
