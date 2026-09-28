// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

// At most one UI action is queued, independent of how many CSI requests arrive.
// Zero dimensions preserve a previously queued nonzero dimension, just as they
// would after applying the earlier request. Reset invalidates undelivered work.
internal sealed class TerminalWindowResizeQueue
{
    private readonly object _sync = new();
    private readonly Action<Action> _schedule;
    private readonly Action<TerminalWindowResizeRequest> _apply;
    private readonly Action _drain;
    private TerminalWindowResizeRequest _pending;
    private bool _scheduled;

    internal TerminalWindowResizeQueue(Action<Action> schedule, Action<TerminalWindowResizeRequest> apply)
    {
        _schedule = schedule;
        _apply = apply;
        _drain = Drain;
    }

    internal void Enqueue(TerminalWindowResizeRequest request)
    {
        if (request is { Columns: 0, Rows: 0 }) return;
        lock (_sync)
        {
            _pending = new(request.Columns == 0 ? _pending.Columns : request.Columns,
                request.Rows == 0 ? _pending.Rows : request.Rows);
            if (_scheduled) return;
            _scheduled = true;
        }
        try { _schedule(_drain); }
        catch
        {
            lock (_sync) { _scheduled = false; _pending = default; }
            throw;
        }
    }

    // The UI thread calls Reset, like Drain/host lifetime changes. Keep the
    // already-posted action so repeated reset/rebind cannot flood the dispatcher.
    internal void Reset()
    {
        lock (_sync) _pending = default;
    }

    private void Drain()
    {
        TerminalWindowResizeRequest request;
        lock (_sync)
        {
            request = _pending;
            _pending = default;
            _scheduled = false;
        }
        if (request is not { Columns: 0, Rows: 0 }) _apply(request);
    }
}
