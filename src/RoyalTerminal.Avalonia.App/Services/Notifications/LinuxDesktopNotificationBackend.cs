// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

internal sealed class LinuxDesktopNotificationBackend(Func<IFreedesktopNotificationConnection> createConnection) : IDesktopNotificationBackend
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, NativeEntry> _entries = new();
    private readonly Dictionary<uint, NativeEntry> _ids = new();
    private readonly List<(uint Id, TerminalNotificationFeedback Feedback)> _early = new();
    private IFreedesktopNotificationConnection? _connection;
    private int _capabilities, _generation;
    private bool _sending, _markup, _body;
    public TerminalNotificationCapabilities Capabilities => (TerminalNotificationCapabilities)Volatile.Read(ref _capabilities);

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync().ConfigureAwait(false);
        IFreedesktopNotificationConnection connection = createConnection();
        int generation;
        lock (_sync) { generation = ++_generation; _connection = connection; }
        connection.Closed += id => Signal(generation, id, new(TerminalNotificationEvent.Closed));
        connection.ActionInvoked += (id, action) =>
        {
            if (action == "default") Signal(generation, id, new(TerminalNotificationEvent.Activated));
            else if (int.TryParse(action, NumberStyles.None, CultureInfo.InvariantCulture, out int button) && button is > 0 and <= 32)
                Signal(generation, id, new(TerminalNotificationEvent.Activated, button));
        };
        connection.Disconnected += () => Disconnected(generation);
        try
        {
            string[] names = await connection.ConnectAsync(cancellationToken).ConfigureAwait(false);
            TerminalNotificationCapabilities capabilities = TerminalNotificationCapabilities.Display | TerminalNotificationCapabilities.Close |
                TerminalNotificationCapabilities.CloseEvents | TerminalNotificationCapabilities.Alive | TerminalNotificationCapabilities.Urgency;
            _markup = System.Array.IndexOf(names, "body-markup") >= 0;
            _body = System.Array.IndexOf(names, "body") >= 0;
            if (System.Array.IndexOf(names, "actions") >= 0) capabilities |= TerminalNotificationCapabilities.Activation | TerminalNotificationCapabilities.Buttons;
            if (System.Array.IndexOf(names, "sound") >= 0) capabilities |= TerminalNotificationCapabilities.Sound;
            // Image data works where advertised. Ordered custom theme-name lookup
            // is not complete, so do not yet advertise full protocol Icons support.
            lock (_sync) if (_generation == generation) Volatile.Write(ref _capabilities, (int)capabilities);
        }
        catch { Disconnected(generation); connection.Dispose(); throw; }
    }

    public async ValueTask ShowAsync(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback, CancellationToken cancellationToken)
    {
        IFreedesktopNotificationConnection connection = _connection ?? throw new InvalidOperationException("Notification server unavailable.");
        NativeEntry? old;
        int generation;
        lock (_sync)
        {
            generation = _generation;
            old = request.ReplacesToken is Guid previous ? _entries.GetValueOrDefault(previous) : null;
            _sending = true;
        }
        uint id;
        try { id = await connection.NotifyAsync(Convert(request, old?.Id ?? 0), cancellationToken).ConfigureAwait(false); }
        catch { lock (_sync) { _sending = false; _early.Clear(); } throw; }
        if (id == 0) { lock (_sync) _sending = false; throw new InvalidOperationException("Notification server returned reserved ID zero."); }
        (uint Id, TerminalNotificationFeedback Feedback)[] early;
        lock (_sync)
        {
            _sending = false;
            if (generation != _generation) throw new InvalidOperationException("Notification server changed during delivery.");
            if (old is not null) { _entries.Remove(old.Token); _ids.Remove(old.Id); }
            NativeEntry entry = new(request.Token, id, feedback, request.Buttons.Count);
            _entries.Add(request.Token, entry);
            _ids[id] = entry;
            early = _early.ToArray();
            _early.Clear();
        }
        foreach (var item in early) Signal(generation, item.Id, item.Feedback);
    }

    public async ValueTask CloseAsync(Guid token, CancellationToken cancellationToken)
    {
        NativeEntry? entry;
        lock (_sync)
        {
            if (!_entries.Remove(token, out entry)) return;
            _ids.Remove(entry.Id);
        }
        if (_connection is { } connection) await connection.CloseAsync(entry.Id, cancellationToken).ConfigureAwait(false);
    }

    internal FreedesktopNotification Convert(TerminalNotificationRequest request, uint replaces)
    {
        string body = _body ? request.Body : string.Empty;
        string title = !_body && request.Body.Length > 0 ? request.Title + " — " + request.Body : request.Title;
        if (_markup) body = EscapeMarkup(body);
        bool actions = (Capabilities & TerminalNotificationCapabilities.Activation) != 0;
        int buttonCount = actions ? Math.Min(request.Buttons.Count, 32) : 0;
        string[] pairs = actions ? new string[(buttonCount + 1) * 2] : [];
        if (actions) { pairs[0] = "default"; pairs[1] = "Open terminal"; }
        for (int i = 0; i < buttonCount; i++) { pairs[2 + i * 2] = (i + 1).ToString(CultureInfo.InvariantCulture); pairs[3 + i * 2] = request.Buttons[i]; }
        string icon = string.Empty;
        foreach (string name in request.IconNames)
        {
            icon = name switch { "error" => "dialog-error", "warn" or "warning" => "dialog-warning", "info" => "dialog-information", "question" => "dialog-question", "help" => "help-browser", _ => string.Empty };
            if (icon.Length > 0) break;
        }
        // Client app/type names are metadata only, never desktop-entry IDs or paths.
        string category = request.Types.Count > 0 ? request.Types[0] : string.Empty;
        string sound = request.Sound switch { "error" => "dialog-error", "warn" or "warning" => "dialog-warning", "info" => "dialog-information", "question" => "dialog-question", _ => string.Empty };
        return new(request.ApplicationName ?? "RoyalTerminal", replaces, icon, title, body, pairs,
            (byte)Math.Clamp(request.Urgency, 0, 2), request.ExpireMilliseconds, category, sound,
            request.Sound == "silent", icon.Length == 0 ? NotificationImageDecoder.Decode(request.IconData) : null);
    }

    internal static string EscapeMarkup(string text)
        => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);

    private void Signal(int generation, uint id, TerminalNotificationFeedback feedback)
    {
        NativeEntry? entry;
        lock (_sync)
        {
            if (generation != _generation) return;
            if (!_ids.TryGetValue(id, out entry))
            {
                if (_sending && _early.Count < 128) _early.Add((id, feedback));
                return;
            }
            if (feedback.Button > entry.Buttons) return;
            if (feedback.Event != TerminalNotificationEvent.Activated) { _ids.Remove(id); _entries.Remove(entry.Token); }
        }
        try { entry.Callback(feedback); } catch (Exception) { }
    }

    private void Disconnected(int generation)
    {
        NativeEntry[] entries;
        lock (_sync)
        {
            if (generation != _generation) return;
            ++_generation;
            Volatile.Write(ref _capabilities, 0);
            entries = new NativeEntry[_entries.Count]; _entries.Values.CopyTo(entries, 0);
            _entries.Clear(); _ids.Clear(); _early.Clear(); _sending = false;
        }
        foreach (NativeEntry entry in entries) { try { entry.Callback(new(TerminalNotificationEvent.Failed)); } catch (Exception) { } }
    }

    public async ValueTask DisposeAsync()
    {
        IFreedesktopNotificationConnection? connection;
        uint[] ids;
        lock (_sync)
        {
            ++_generation;
            Volatile.Write(ref _capabilities, 0);
            connection = _connection; _connection = null;
            ids = new uint[_ids.Count]; _ids.Keys.CopyTo(ids, 0);
            _ids.Clear(); _entries.Clear(); _early.Clear(); _sending = false;
        }
        if (connection is null) return;
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            foreach (uint id in ids)
            {
                if (timeout.IsCancellationRequested) break;
                try { await connection.CloseAsync(id, timeout.Token).ConfigureAwait(false); } catch (Exception) { }
            }
        }
        finally { connection.Dispose(); }
    }

    private sealed record NativeEntry(Guid Token, uint Id, Action<TerminalNotificationFeedback> Callback, int Buttons);
}
