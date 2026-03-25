// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Serialized transport input write pump.

namespace RoyalTerminal.Terminal;

internal sealed class TransportWritePump : IDisposable
{
    private readonly object _sync = new();
    private readonly Queue<byte[]> _priorityWrites = [];
    private readonly Queue<byte[]> _writes = [];
    private readonly Action<byte[]> _writeAction;
    private readonly Action<Exception>? _writeFaulted;
    private readonly Thread _thread;
    private bool _stopRequested;

    public TransportWritePump(
        string threadName,
        Action<byte[]> writeAction,
        Action<Exception>? writeFaulted = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadName);
        _writeAction = writeAction ?? throw new ArgumentNullException(nameof(writeAction));
        _writeFaulted = writeFaulted;
        _thread = new Thread(WriteLoop)
        {
            IsBackground = true,
            Name = threadName,
        };
        _thread.Start();
    }

    public bool TryEnqueue(ReadOnlySpan<byte> payload)
    {
        return TryEnqueue(payload.ToArray());
    }

    public bool TryEnqueue(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length == 0)
        {
            return false;
        }

        lock (_sync)
        {
            if (_stopRequested)
            {
                return false;
            }

            Queue<byte[]> queue = IsPriorityControlWrite(payload)
                ? _priorityWrites
                : _writes;
            queue.Enqueue(payload);
            Monitor.Pulse(_sync);
            return true;
        }
    }

    public void RequestStop(bool discardPendingWrites)
    {
        lock (_sync)
        {
            if (_stopRequested)
            {
                return;
            }

            _stopRequested = true;
            if (discardPendingWrites)
            {
                _priorityWrites.Clear();
                _writes.Clear();
            }

            Monitor.PulseAll(_sync);
        }
    }

    public bool Join(TimeSpan timeout)
    {
        if (Thread.CurrentThread == _thread)
        {
            return true;
        }

        return _thread.Join(timeout);
    }

    public void Dispose()
    {
        RequestStop(discardPendingWrites: true);
        _ = Join(TimeSpan.FromSeconds(5));
    }

    private void WriteLoop()
    {
        while (true)
        {
            byte[]? payload;
            lock (_sync)
            {
                while (!_stopRequested && _priorityWrites.Count == 0 && _writes.Count == 0)
                {
                    Monitor.Wait(_sync);
                }

                if (_priorityWrites.Count > 0)
                {
                    payload = _priorityWrites.Dequeue();
                }
                else if (_writes.Count > 0)
                {
                    payload = _writes.Dequeue();
                }
                else
                {
                    return;
                }
            }

            try
            {
                _writeAction(payload);
            }
            catch (Exception ex)
            {
                RequestStop(discardPendingWrites: true);
                _writeFaulted?.Invoke(ex);
                return;
            }
        }
    }

    private static bool IsPriorityControlWrite(byte[] payload)
    {
        return payload.Length == 1 && payload[0] is 0x03 or 0x1A or 0x1C;
    }
}
