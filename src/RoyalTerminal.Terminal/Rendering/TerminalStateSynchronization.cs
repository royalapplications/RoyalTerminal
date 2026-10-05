// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Coordinates access to terminal state and lets a busy parser hand the lock to a waiting renderer.
/// </summary>
/// <remarks>
/// Normal state access continues to lock <see cref="SyncRoot"/>. Rendering uses
/// <see cref="AcquireDemand"/> and parsing calls <see cref="YieldToDemand"/> between
/// batches, following Ghostty's renderer state demand handoff. This prevents an
/// unfair monitor from letting sustained output repeatedly overtake a waiting frame.
/// </remarks>
public sealed class TerminalStateSynchronization
{
    private readonly object _handoffSync = new();
    private int _pendingDemand;

    /// <summary>Gets the monitor that protects the terminal state.</summary>
    public object SyncRoot { get; } = new();

    internal int PendingDemand => Volatile.Read(ref _pendingDemand);

    /// <summary>
    /// Acquires the state monitor while advertising render demand to the output parser.
    /// The returned scope must be disposed once on the acquiring thread.
    /// </summary>
    public DemandScope AcquireDemand()
    {
        Interlocked.Increment(ref _pendingDemand);
        try
        {
            Monitor.Enter(SyncRoot);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingDemand);
        }

        return new DemandScope(this);
    }

    /// <summary>
    /// Gives a waiting renderer an opportunity to acquire the state monitor.
    /// Call only outside the state monitor, between parsing batches.
    /// </summary>
    /// <remarks>
    /// Waiting is bounded to one millisecond, as in Ghostty, so a descheduled renderer
    /// cannot indefinitely stall output. When there is no demand this performs one
    /// volatile read and does not acquire another monitor or allocate.
    /// </remarks>
    public void YieldToDemand()
    {
        if (Volatile.Read(ref _pendingDemand) == 0)
        {
            return;
        }

        lock (_handoffSync)
        {
            if (Volatile.Read(ref _pendingDemand) != 0)
            {
                Monitor.Wait(_handoffSync, millisecondsTimeout: 1);
            }
        }
    }

    private void ReleaseDemand()
    {
        Monitor.Exit(SyncRoot);
        lock (_handoffSync)
        {
            Monitor.PulseAll(_handoffSync);
        }
    }

    /// <summary>An allocation-free scope for a demanding terminal state acquisition.</summary>
    public readonly struct DemandScope : IDisposable
    {
        private readonly TerminalStateSynchronization? _owner;

        internal DemandScope(TerminalStateSynchronization owner)
        {
            _owner = owner;
        }

        /// <summary>Releases the state monitor and wakes a parser yielding to this frame.</summary>
        public void Dispose() => _owner?.ReleaseDemand();
    }
}
