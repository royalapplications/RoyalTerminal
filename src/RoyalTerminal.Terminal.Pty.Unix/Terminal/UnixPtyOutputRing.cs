// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Fixed storage shared by the Unix gather thread and the terminal parser.</summary>
internal sealed class UnixPtyOutputRing
{
    internal const int BufferCount = 4;
    internal const int BufferCapacity = 64 * 1024;
    private readonly object _sync = new();
    private readonly Slot[] _slots = new Slot[BufferCount];
    private readonly Action _wakeIdle;
    private int _nextSlot;
    private int _leased;
    private bool _stopped;
    private bool _bridging;

    public UnixPtyOutputRing(Action wakeIdle)
    {
        _wakeIdle = wakeIdle;
        for (int index = 0; index < _slots.Length; index++)
        {
            _slots[index] = new Slot(this);
        }
    }

    public Slot? Acquire()
    {
        lock (_sync)
        {
            while (_leased == _slots.Length && !_stopped)
            {
                Monitor.Wait(_sync);
            }

            if (_stopped)
            {
                return null;
            }

            while (_slots[_nextSlot].Leased)
            {
                _nextSlot = (_nextSlot + 1) % _slots.Length;
            }

            Slot slot = _slots[_nextSlot];
            _nextSlot = (_nextSlot + 1) % _slots.Length;
            slot.Leased = true;
            slot.Generation++;
            _leased++;
            return slot;
        }
    }

    public bool HasPendingOutput
    {
        get
        {
            lock (_sync)
            {
                // The gatherer's currently filling slot counts as one lease.
                return _leased > 1;
            }
        }
    }

    public bool TryBeginBridge()
    {
        lock (_sync)
        {
            return _bridging = !_stopped && _leased > 1;
        }
    }

    public void EndBridge()
    {
        lock (_sync)
        {
            _bridging = false;
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _stopped = true;
            _bridging = false;
            Monitor.PulseAll(_sync);
        }
    }

    private void Release(Slot slot, long generation)
    {
        bool wake;
        lock (_sync)
        {
            if (!slot.Leased || slot.Generation != generation)
            {
                return;
            }

            slot.Leased = false;
            _leased--;
            wake = !_stopped && _bridging && _leased == 1;
            Monitor.PulseAll(_sync);
        }

        if (wake)
        {
            _wakeIdle();
        }
    }

    internal sealed class Slot : ITerminalOutputLeaseOwner
    {
        private readonly UnixPtyOutputRing _owner;
        public byte[] Buffer { get; } = new byte[BufferCapacity];
        public long Generation { get; set; }
        public bool Leased { get; set; }

        public Slot(UnixPtyOutputRing owner) => _owner = owner;
        public TerminalOutputLease CreateLease(int length) => new(Buffer.AsMemory(0, length), this, Generation);
        public void Release(long generation) => _owner.Release(this, generation);
    }
}
