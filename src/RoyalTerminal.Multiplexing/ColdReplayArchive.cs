// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace RoyalTerminal.Multiplexing;

/// <summary>Single-writer archive under the block publication gate. Immutable sealed pages can be read
/// outside that gate. No terminal-state interpretation, text decoding, file I/O or unbounded worker queue.</summary>
internal sealed class ColdReplayArchive(int budget)
{
    // A page can never require more than 64 KiB of decompression scratch or 256 event headers.
    private const int PageBytes = 64 * 1024, PageEvents = 256, MetadataCharge = 112;
    private readonly Queue<Page> _pages = [];
    private readonly List<MultiplexerEvent> _tail = [];
    private int _tailPayload, _tailCharge;
    private long _stored, _logical;
    private long LogicalLimit => Math.Min(128L * 1024 * 1024, 8L * budget);
    public long? FirstSequence => _pages.TryPeek(out Page? head) ? head.First : _tail.Count > 0 ? _tail[0].Sequence : null;

    internal void Append(MultiplexerEvent value)
    {
        int bytes = value.Data?.Length ?? 0, meta = MetadataCharge + (value.Message?.Length ?? 0) * sizeof(char);
        // A larger custom hot event cannot be copied without violating the fixed scratch bound. Represent
        // its absence as an explicit history gap, never as a recoverable stream with a hole.
        if (bytes > PageBytes || bytes + meta > LogicalLimit) { Clear(); return; }
        if (_tail.Count != 0 && (_tail.Count == PageEvents || _tailPayload + bytes > PageBytes)) Seal();
        _tail.Add(value); _tailPayload += bytes; _tailCharge += meta + bytes;
        if (_tail.Count == PageEvents || _tailPayload == PageBytes || _tailCharge + _stored > budget) Seal();
        while (_pages.TryPeek(out Page? first) && (_stored + _tailCharge > budget || _logical + _tailCharge > LogicalLimit))
        { _pages.Dequeue(); _stored -= first.Charge; _logical -= first.Logical; }
    }

    private void Seal()
    {
        if (_tail.Count == 0) return;
        Page page = new(_tail, _tailPayload, _tailCharge);
        _pages.Enqueue(page); _stored += page.Charge; _logical += page.Logical;
        _tail.Clear(); _tailPayload = _tailCharge = 0;
    }

    internal void Clear() { _pages.Clear(); _tail.Clear(); _stored = _logical = _tailPayload = _tailCharge = 0; }

    // Called under the same gate as attachment creation. The hot journal starts immediately after
    // the last cold event. Only references/at most 256 tail entries are copied while subscribing.
    internal Cursor? SnapshotAfter(long cursor, long firstHot)
    {
        if (cursor >= firstHot - 1) return null;
        if (FirstSequence is not long earliest || cursor < earliest - 1)
            throw new MultiplexerException(MultiplexerErrorCode.HistoryGap, "Required terminal history is older than both replay tiers.");
        Page[] pages = _pages.Where(p => p.Last > cursor).ToArray();
        return new Cursor(pages, _tail.Where(e => e.Sequence > cursor).ToArray(), cursor + 1);
    }

    internal sealed class Page
    {
        internal readonly MultiplexerEvent[] Headers;
        private readonly byte[] _payload;
        private readonly int[] _offsets;
        private readonly int _rawSize;
        private readonly bool _compressed;
        internal int Charge { get; }
        internal int Logical { get; }
        internal long First => Headers[0].Sequence;
        internal long Last => Headers[^1].Sequence;

        internal Page(List<MultiplexerEvent> values, int bytes, int logical)
        {
            Logical = logical; _rawSize = bytes; Headers = new MultiplexerEvent[values.Count];
            _offsets = new int[values.Count + 1]; byte[] raw = new byte[bytes];
            for (int i = 0; i < values.Count; ++i)
            {
                MultiplexerEvent ev = values[i]; Headers[i] = ev.Data is null ? ev : ev with { Data = null };
                int size = ev.Data?.Length ?? 0; ev.Data?.CopyTo(raw, _offsets[i]); _offsets[i + 1] = _offsets[i] + size;
            }
            // Fast, fixed-size compression, with incompressible bytes retained verbatim. Limiting output
            // to raw size avoids expanding noisy/cryptographic terminal output.
            _payload = raw;
            if (bytes >= 512)
            {
                byte[] compressed = new byte[bytes];
                if (BrotliEncoder.TryCompress(raw, compressed, out int written, quality: 1, window: 16) && written + 32 < bytes)
                { _payload = compressed[..written]; _compressed = true; }
            }
            Charge = checked(logical - bytes + _payload.Length + 96);
        }

        internal byte[] Open()
        {
            if (!_compressed) return _payload;
            byte[] raw = new byte[_rawSize];
            if (!BrotliDecoder.TryDecompress(_payload, raw, out int written) || written != raw.Length)
                throw new InvalidDataException("Corrupt in-memory replay archive.");
            return raw;
        }
        internal MultiplexerEvent Copy(int i, byte[] raw) => Headers[i].Kind == MultiplexerEventKind.Output
            ? Headers[i] with { Data = raw.AsSpan(_offsets[i], _offsets[i + 1] - _offsets[i]).ToArray() } : Headers[i];
    }

    internal sealed class Cursor(Page[] pages, MultiplexerEvent[] tail, long next)
    {
        private int _page, _index, _tailIndex;
        private byte[]? _decoded;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Completion => _done.Task;
        internal bool Available { get { lock (_done) return _page < pages.Length || _tailIndex < tail.Length; } }
        internal bool TryRead([NotNullWhen(true)] out MultiplexerEvent? value)
        {
            lock (_done)
            {
                if (_page < pages.Length)
                {
                    Page page = pages[_page];
                    if (_decoded is null) { _index = checked((int)Math.Max(0L, next - page.First)); _decoded = page.Open(); }
                    value = page.Copy(_index++, _decoded);
                    if (_index == page.Headers.Length) { pages[_page++] = null!; _decoded = null; }
                }
                else if (_tailIndex < tail.Length)
                {
                    MultiplexerEvent item = tail[_tailIndex]; tail[_tailIndex++] = null!;
                    value = item.Data is { } bytes ? item with { Data = (byte[])bytes.Clone() } : item;
                }
                else { value = null; return false; }
                if (value.Sequence != next++) throw new InvalidDataException("Noncontiguous cold replay sequence.");
                if (_page == pages.Length && _tailIndex == tail.Length) _done.TrySetResult();
                return true;
            }
        }
        internal void Discard()
        {
            lock (_done) { Array.Clear(pages); Array.Clear(tail); _page = pages.Length; _tailIndex = tail.Length; _decoded = null; _done.TrySetResult(); }
        }
    }
}
