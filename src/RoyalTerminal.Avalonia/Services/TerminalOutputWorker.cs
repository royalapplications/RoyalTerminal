// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.ExceptionServices;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Owns the dedicated, serial terminal-output processing thread for one transport session.
/// Repeated schedules are coalesced because the consumer drains the session's bounded queue.
/// </summary>
internal sealed class TerminalOutputWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly AutoResetEvent _signal = new(initialState: false);
    private readonly Action _drain;
    private readonly Thread _thread;
    private bool _scheduled;
    private bool _executing;
    private bool _stopping;
    private bool _disposed;
    private ExceptionDispatchInfo? _failure;

    public TerminalOutputWorker(Action drain)
    {
        _drain = drain ?? throw new ArgumentNullException(nameof(drain));
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "RoyalTerminal.Output",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    public void Schedule()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfFailed();
            if (_stopping || _scheduled)
            {
                return;
            }

            _scheduled = true;
            _signal.Set();
        }
    }

    public void Flush()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ThrowIfFailed();
            if (!_stopping && !_scheduled)
            {
                _scheduled = true;
                _signal.Set();
            }

            while (_scheduled || _executing)
            {
                Monitor.Wait(_sync);
                ThrowIfFailed();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _stopping = true;
            _signal.Set();
        }

        if (!ReferenceEquals(Thread.CurrentThread, _thread))
        {
            _thread.Join();
        }

        lock (_sync)
        {
            _disposed = true;
            Monitor.PulseAll(_sync);
        }

        _signal.Dispose();
    }

    private void Run()
    {
        while (true)
        {
            _signal.WaitOne();
            lock (_sync)
            {
                if (_stopping)
                {
                    _scheduled = false;
                    Monitor.PulseAll(_sync);
                    return;
                }

                if (!_scheduled)
                {
                    continue;
                }

                _scheduled = false;
                _executing = true;
            }

            bool stopAfterDrain;
            try
            {
                _drain();
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _failure = ExceptionDispatchInfo.Capture(exception);
                    _stopping = true;
                }
            }
            finally
            {
                lock (_sync)
                {
                    _executing = false;
                    Monitor.PulseAll(_sync);
                    stopAfterDrain = _stopping;
                }
            }

            if (stopAfterDrain)
            {
                return;
            }
        }
    }

    private void ThrowIfFailed()
    {
        _failure?.Throw();
    }
}
