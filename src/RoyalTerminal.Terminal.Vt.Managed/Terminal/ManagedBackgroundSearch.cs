// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Lazily owned search worker. Requests coalesce to the latest immutable capture;
/// active results precede history results, and generations guard publication.
/// The worker never acquires a terminal/UI lock, including during shutdown.
/// </summary>
internal sealed class ManagedBackgroundSearch : IDisposable
{
    private readonly object _gate = new();
    private Thread? _thread;
    private Request? _pending;
    private CancellationTokenSource? _active;
    private ManagedSearchSnapshot? _capture;
    private string _needle = string.Empty;
    private long _generation;
    private long _version;
    private long _acknowledged;
    private TerminalSearchMatch[] _results = [];
    private TerminalSearchStatus _status = TerminalSearchStatus.Complete;
    private Exception? _error;
    private bool _clearCache;
    private bool _stopping;

    private sealed record Request(ManagedSearchSnapshot Snapshot, string Needle, long Generation);

    internal bool NeedsRefresh
    {
        get { lock (_gate) return !_stopping && (_pending is not null || _active is not null || _version != _acknowledged); }
    }

    internal Exception? Error { get { lock (_gate) return _error; } }

    internal bool TakeChanged()
    {
        lock (_gate)
        {
            if (_version == _acknowledged) return false;
            _acknowledged = _version;
            return true;
        }
    }

    internal TerminalSearchStatus Populate(TerminalScreen screen, string needle, List<TerminalSearchMatch> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            destination.Clear();
            if (string.IsNullOrEmpty(needle))
            {
                CancelLocked();
                return TerminalSearchStatus.Complete;
            }
            ManagedSearchSnapshot snapshot = ManagedSearchSnapshot.Capture(screen, _capture);
            if (!ReferenceEquals(snapshot, _capture) || !string.Equals(_needle, needle, StringComparison.Ordinal))
            {
                _capture = snapshot;
                _needle = needle;
                _pending = new(snapshot, needle, ++_generation);
                _active?.Cancel();
                _results = [];
                _error = null;
                _status = TerminalSearchStatus.Pending;
                _version++;
                if (_thread is null)
                {
                    _thread = new Thread(Run) { IsBackground = true, Name = "RoyalTerminal Search" };
                    _thread.Start();
                }
                Monitor.Pulse(_gate);
            }
            destination.AddRange(_results);
            return _status;
        }
    }

    private void CancelLocked()
    {
        if (_capture is null && _needle.Length == 0) return;
        _generation++;
        _pending = null;
        _active?.Cancel();
        _capture = null;
        _needle = string.Empty;
        _results = [];
        _error = null;
        _status = TerminalSearchStatus.Complete;
        _clearCache = true;
        _version++;
        Monitor.Pulse(_gate);
    }

    private void Run()
    {
        ManagedTerminalSearch primary = new();
        ManagedTerminalSearch alternate = new();
        ManagedTerminalSearch active = new();
        List<TerminalSearchMatch> matches = [];
        while (true)
        {
            Request request;
            CancellationTokenSource cancellation;
            lock (_gate)
            {
                while (!_stopping && !_clearCache && _pending is null) Monitor.Wait(_gate);
                if (_stopping) return;
                if (_clearCache)
                {
                    primary.Reset();
                    alternate.Reset();
                    active.Reset();
                    matches.Clear();
                    _clearCache = false;
                }
                if (_pending is null) continue;
                request = _pending;
                _pending = null;
                cancellation = _active = new();
            }

            try
            {
                ManagedSearchSnapshot snapshot = request.Snapshot;
                int activeTop = Math.Max(0, snapshot.Rows.Length - snapshot.ViewportRows);
                // At least needle-length rows of overlap cover cross-row matches.
                // Extend a soft line to its beginning so blank mapping is stable.
                int start = (int)Math.Max(0L, (long)activeTop - request.Needle.Length - 1);
                while (start > 0 && snapshot.Rows[start].IsWrapContinuation) start--;
                if (start > 0)
                {
                    active.Reset();
                    active.Populate(snapshot.Slice(start), request.Needle, matches, cancellation.Token);
                    int count = 0;
                    for (int i = 0; i < matches.Count; i++)
                    {
                        TerminalSearchMatch match = matches[i];
                        if (match.EndAbsoluteRow + start < activeTop) continue;
                        matches[count++] = match with
                        {
                            AbsoluteRow = match.AbsoluteRow + start,
                            EndAbsoluteRow = match.EndAbsoluteRow + start,
                        };
                    }
                    if (count < matches.Count) matches.RemoveRange(count, matches.Count - count);
                    Publish(request, matches, TerminalSearchStatus.Pending);
                }
                ManagedTerminalSearch search = snapshot.Alternate ? alternate : primary;
                search.Populate(snapshot, request.Needle, matches, cancellation.Token);
                Publish(request, matches, TerminalSearchStatus.Complete);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                lock (_gate)
                {
                    if (!_stopping && request.Generation == _generation)
                    {
                        _results = [];
                        _error = error;
                        _status = TerminalSearchStatus.Failed;
                        _version++;
                    }
                }
            }
            finally
            {
                lock (_gate) _active = null;
                cancellation.Dispose();
            }
        }
    }

    private void Publish(Request request, List<TerminalSearchMatch> matches, TerminalSearchStatus status)
    {
        lock (_gate)
        {
            if (_stopping || request.Generation != _generation) return;
            _results = matches.ToArray();
            _status = status;
            _version++;
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
            _active?.Cancel();
            _pending = null;
            _capture = null;
            _results = [];
            thread = _thread;
            Monitor.Pulse(_gate);
        }
        // Safe while the caller holds the terminal lock: scans read only captures.
        thread?.Join();
    }
}
