// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text.Json;
using RoyalTerminal.Terminal;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

internal sealed class MacOsDesktopNotificationBackend(Func<IMacNotificationTransport> createTransport)
    : IDesktopNotificationBackend, IDesktopNotificationCancellation
{
    private readonly object _sync = new();
    private readonly Dictionary<long, TaskCompletionSource> _operations = new();
    private readonly Dictionary<Guid, Entry> _entries = new();
    private IMacNotificationTransport? _transport;
    private CancellationTokenSource? _pollStop, _deliveryStop;
    private SemaphoreSlim? _wake;
    private Task? _poll;
    private TaskCompletionSource _ready = NewCompletion();
    private long _sequence;
    private int _capabilities;
    public TerminalNotificationCapabilities Capabilities => (TerminalNotificationCapabilities)Volatile.Read(ref _capabilities);

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync().ConfigureAwait(false);
        _ready = NewCompletion();
        _pollStop = new(); _deliveryStop = new();
        _wake = new(0, 1);
        _transport = createTransport();
        _poll = PollAsync(_pollStop.Token);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ShowAsync(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MacNotificationCommand command = Convert(request);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deliveryStop!.Token);
        linked.Token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_entries.Count >= 128) throw new InvalidOperationException("Notification ownership limit reached.");
            _entries.Add(request.Token, new(feedback, request.Buttons.Count));
            if (request.ReplacesToken is Guid previous) _entries.Remove(previous);
        }
        try { await ExecuteAsync(command, TimeSpan.FromSeconds(30), linked.Token).ConfigureAwait(false); }
        catch
        {
            lock (_sync) _entries.Remove(request.Token);
            // Cancel an authorization-pending or in-flight native request. A late
            // add completion removes its own OS request after cancellation.
            try { _transport?.Send(JsonSerializer.SerializeToUtf8Bytes(new MacNotificationCommand { Op = "close", Token = request.Token.ToString("N") }, MacNotificationJsonContext.Default.MacNotificationCommand)); }
            catch (Exception) { }
            throw;
        }
    }

    public async ValueTask CloseAsync(Guid token, CancellationToken cancellationToken)
    {
        lock (_sync) _entries.Remove(token);
        await ExecuteAsync(new() { Op = "close", Token = token.ToString("N") }, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
    }

    public void CancelPending()
    {
        lock (_sync)
        { try { _deliveryStop?.Cancel(); } catch (ObjectDisposedException) { } }
    }

    private async Task ExecuteAsync(MacNotificationCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        TaskCompletionSource completion = NewCompletion();
        lock (_sync)
        {
            if (_transport is null) throw new InvalidOperationException("Notification center unavailable.");
            command.Sequence = ++_sequence;
            _operations.Add(command.Sequence, completion);
        }
        try
        {
            _transport.Send(JsonSerializer.SerializeToUtf8Bytes(command, MacNotificationJsonContext.Default.MacNotificationCommand));
            try { _wake?.Release(); } catch (SemaphoreFullException) { }
            await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally { lock (_sync) _operations.Remove(command.Sequence); }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_transport!.Poll() is { } bytes)
                {
                    MacNotificationState state = JsonSerializer.Deserialize(bytes, MacNotificationJsonContext.Default.MacNotificationState)
                        ?? throw new InvalidOperationException("Invalid notification state.");
                    Apply(state);
                }
                int delay;
                lock (_sync) delay = !_ready.Task.IsCompleted || _operations.Count > 0 ? 50 : _entries.Count > 0 ? 250 : 1000;
                await _wake!.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Volatile.Write(ref _capabilities, 0);
            _ready.TrySetException(exception);
            Entry[] failed;
            lock (_sync)
            {
                foreach (TaskCompletionSource operation in _operations.Values) operation.TrySetException(exception);
                failed = new Entry[_entries.Count]; _entries.Values.CopyTo(failed, 0); _entries.Clear();
            }
            foreach (Entry entry in failed) Report(entry, new(TerminalNotificationEvent.Failed));
        }
    }

    private void Apply(MacNotificationState state)
    {
        const TerminalNotificationCapabilities supported = TerminalNotificationCapabilities.Display | TerminalNotificationCapabilities.Activation |
            TerminalNotificationCapabilities.Close | TerminalNotificationCapabilities.Alive | TerminalNotificationCapabilities.Icons |
            TerminalNotificationCapabilities.Buttons | TerminalNotificationCapabilities.Sound;
        Volatile.Write(ref _capabilities, state.Ready ? state.Capabilities & (int)supported : 0);
        if (state.Ready) _ready.TrySetResult();
        foreach (MacNotificationEvent value in state.Events)
        {
            if (value.Kind == "operation")
            {
                lock (_sync)
                    if (_operations.TryGetValue(value.Sequence, out TaskCompletionSource? operation))
                    {
                        if (value.Success) operation.TrySetResult();
                        else operation.TrySetException(new InvalidOperationException("macOS notification operation failed."));
                    }
                continue;
            }
            TerminalNotificationEvent? kind = value.Kind switch
            { "activated" => TerminalNotificationEvent.Activated, "closed" => TerminalNotificationEvent.Closed, "failed" => TerminalNotificationEvent.Failed, _ => null };
            if (kind is null || !Guid.TryParseExact(value.Token, "N", out Guid token)) continue;
            Entry? entry;
            lock (_sync)
            {
                if (!_entries.TryGetValue(token, out entry) || value.Button < 0 || value.Button > entry.Buttons) continue;
                _entries.Remove(token);
            }
            Report(entry, new(kind.Value, value.Button));
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancelPending();
        if (_transport is not null)
        {
            try { await ExecuteAsync(new() { Op = "stop" }, TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { }
        }
        _pollStop?.Cancel();
        if (_poll is not null) await _poll.ConfigureAwait(false);
        _transport?.Dispose(); _transport = null;
        _pollStop?.Dispose(); _pollStop = null; _poll = null;
        _wake?.Dispose(); _wake = null;
        lock (_sync) { _deliveryStop?.Dispose(); _deliveryStop = null; _entries.Clear(); _operations.Clear(); }
        Volatile.Write(ref _capabilities, 0);
    }

    internal static MacNotificationCommand Convert(TerminalNotificationRequest request)
    {
        byte[]? png = null;
        if (NotificationImageDecoder.Decode(request.IconData) is { } decoded)
        {
            using SKBitmap bitmap = new(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            Marshal.Copy(decoded.Rgba, 0, bitmap.GetPixels(), decoded.Rgba.Length);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            png = data.ToArray();
        }
        return new()
        {
            Op = "show", Token = request.Token.ToString("N"), Replaces = request.ReplacesToken?.ToString("N"),
            Title = request.Title, Body = request.Body.Length == 0 ? " " : request.Body, Application = request.ApplicationName,
            Icons = Copy(request.IconNames), Image = png, Buttons = Copy(request.Buttons),
            Sound = request.Sound == "silent" ? "silent" : "system", Urgency = request.Urgency,
        };
    }

    private static string[] Copy(IReadOnlyList<string> values)
    { string[] result = new string[Math.Min(32, values.Count)]; for (int i = 0; i < result.Length; i++) result[i] = values[i]; return result; }
    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Report(Entry entry, TerminalNotificationFeedback feedback) { try { entry.Callback(feedback); } catch (Exception) { } }
    private sealed record Entry(Action<TerminalNotificationFeedback> Callback, int Buttons);
}
