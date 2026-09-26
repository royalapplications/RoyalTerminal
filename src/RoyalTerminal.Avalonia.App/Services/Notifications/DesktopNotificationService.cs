// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Threading.Channels;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

// A bounded, owned async worker. Close requests are state, not queue items: even
// a saturated producer cannot drop cleanup or retain a cancelled in-flight show.
internal sealed class DesktopNotificationService : IDisposable
{
    private readonly IDesktopNotificationBackend _backend;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _bytes, _capabilities;
    private long _sequence;
    private bool _disposed;
    internal Task Completion { get; }
    internal Task Ready => _ready.Task;
    internal TerminalNotificationCapabilities Capabilities => (TerminalNotificationCapabilities)Volatile.Read(ref _capabilities) & _backend.Capabilities;

    internal DesktopNotificationService(IDesktopNotificationBackend backend)
    {
        _backend = backend;
        Completion = Task.Run(RunAsync);
    }

    internal bool Show(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback)
    {
        int size = Size(request);
        lock (_sync)
        {
            if (_disposed || (Capabilities & TerminalNotificationCapabilities.Display) == 0 ||
                _entries.Count >= 128 || size > 8 * 1024 * 1024 - _bytes || _entries.ContainsKey(request.Token)) return false;
            _entries.Add(request.Token, new(request, feedback, size, ++_sequence));
            _bytes += size;
        }
        _wake.Writer.TryWrite(true);
        return true;
    }

    internal void Close(Guid token)
    {
        lock (_sync) if (_entries.TryGetValue(token, out Entry? entry)) entry.Close = true;
        _wake.Writer.TryWrite(true);
    }

    internal bool IsAlive(Guid token)
    {
        lock (_sync) return !_disposed && _entries.TryGetValue(token, out Entry? entry) && !entry.Close && !entry.Completed;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            Volatile.Write(ref _capabilities, 0);
        }
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        _wake.Writer.TryComplete();
        // Completion owns backend cleanup; disposing a window never waits on DBus.
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if ((_backend.Capabilities & TerminalNotificationCapabilities.Display) == 0)
                {
                    Volatile.Write(ref _capabilities, 0);
                    try { await _backend.InitializeAsync(_stop.Token).ConfigureAwait(false); }
                    catch (Exception) when (!_stop.IsCancellationRequested) { }
                    if ((_backend.Capabilities & TerminalNotificationCapabilities.Display) == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), _stop.Token).ConfigureAwait(false);
                        continue;
                    }
                }
                Volatile.Write(ref _capabilities, (int)_backend.Capabilities);
                _ready.TrySetResult();
                Entry[] entries;
                lock (_sync) { entries = new Entry[_entries.Count]; _entries.Values.CopyTo(entries, 0); }
                System.Array.Sort(entries, static (left, right) => left.Sequence.CompareTo(right.Sequence));
                foreach (Entry entry in entries)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    bool close, completed;
                    lock (_sync)
                    {
                        if (!_entries.ContainsKey(entry.Request.Token)) continue;
                        close = entry.Close; completed = entry.Completed;
                    }
                    if (completed) { Remove(entry); continue; }
                    if (close)
                    {
                        if (entry.Shown) await TryCloseAsync(entry.Request.Token).ConfigureAwait(false);
                        Remove(entry);
                        continue;
                    }
                    if (entry.Shown) continue;
                    try
                    {
                        // Finish an in-flight (backend-bounded) show during shutdown:
                        // only its reply gives us the OS ID needed for cleanup.
                        await _backend.ShowAsync(entry.Request, feedback => Feedback(entry, feedback), CancellationToken.None).ConfigureAwait(false);
                        entry.Shown = true;
                        // Only a successful replacement retires the old backend revision.
                        if (entry.Request.ReplacesToken is Guid old)
                        {
                            lock (_sync) if (_entries.Remove(old, out Entry? replaced)) _bytes -= replaced.Size;
                        }
                    }
                    catch (Exception) when (!_stop.IsCancellationRequested)
                    {
                        if (entry.Request.ReplacesToken is Guid old)
                        {
                            await TryCloseAsync(old).ConfigureAwait(false);
                            lock (_sync) if (_entries.Remove(old, out Entry? replaced)) _bytes -= replaced.Size;
                        }
                        Feedback(entry, new(TerminalNotificationEvent.Failed));
                    }
                }
                // Periodic wake also notices a disconnected backend with no active requests.
                using CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(1));
                try { await _wake.Reader.ReadAsync(idle.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ChannelClosedException) { }
        catch (Exception) { }
        finally
        {
            Volatile.Write(ref _capabilities, 0);
            _ready.TrySetCanceled();
            try { await _backend.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            lock (_sync) { _disposed = true; _entries.Clear(); _bytes = 0; }
            _stop.Dispose();
        }
    }

    private void Feedback(Entry entry, TerminalNotificationFeedback feedback)
    {
        lock (_sync)
        {
            if (_disposed || !_entries.TryGetValue(entry.Request.Token, out Entry? current) || !ReferenceEquals(current, entry) || entry.FeedbackDelivered) return;
            entry.FeedbackDelivered = true;
        }
        // The protocol callback only records an atomic event. No terminal/OS locks
        // are held while notifying an external owner.
        try { entry.Callback(feedback); } catch (Exception) { }
        // Publish protocol feedback before changing the alive cache. Otherwise a
        // concurrent VT refresh could interpret an activation as an ordinary close.
        lock (_sync)
        {
            if (feedback.Event == TerminalNotificationEvent.Activated) entry.Close = true;
            else entry.Completed = true;
        }
        _wake.Writer.TryWrite(true);
    }

    private async ValueTask TryCloseAsync(Guid token)
    {
        try { await _backend.CloseAsync(token, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception) when (!_stop.IsCancellationRequested) { }
    }

    private void Remove(Entry entry)
    {
        lock (_sync) if (_entries.Remove(entry.Request.Token)) _bytes -= entry.Size;
    }

    private static int Size(TerminalNotificationRequest request)
    {
        long length = request.Title.Length + request.Body.Length + (long)(request.ApplicationName?.Length ?? 0) + request.Sound.Length;
        foreach (string text in request.Buttons) length += text.Length;
        foreach (string text in request.Types) length += text.Length;
        foreach (string text in request.IconNames) length += text.Length;
        return (int)Math.Min(int.MaxValue, length * sizeof(char) + request.IconData.Length);
    }

    private sealed class Entry(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> callback, int size, long sequence)
    {
        internal readonly TerminalNotificationRequest Request = request;
        internal readonly Action<TerminalNotificationFeedback> Callback = callback;
        internal readonly int Size = size;
        internal readonly long Sequence = sequence;
        internal bool Close, Completed, Shown, FeedbackDelivered;
    }
}
