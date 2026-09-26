// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace RoyalTerminal.Terminal;

// Shared by both VT adapters. All operations are serialized with processor access;
// only Active.Report runs on backend threads, and it never touches the processor.
internal sealed class TerminalNotificationProtocol(ITerminalNotificationHost host, TimeProvider clock, Action<byte[]> send) : IDisposable
{
    private const int MaxNotifications = 64, MaxRetainedBytes = 4 * 1024 * 1024;
    private readonly Dictionary<string, TerminalNotificationAssembly> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Active> _active = new();
    private readonly TerminalNotificationIconCache _icons = new();
    private int _pendingBytes, _activeBytes;
    private bool _disposed;

    internal TimeSpan? NextRefreshDelay
    {
        get
        {
            if (_active.Count == 0) return null;
            TimeSpan delay = TimeSpan.FromMilliseconds(100);
            foreach (Active item in _active.Values)
            {
                if (Volatile.Read(ref item.Feedback) != 0) return TimeSpan.Zero;
                if (item.Request.ExpireMilliseconds <= 0 || !Has(TerminalNotificationCapabilities.Close)) continue;
                TimeSpan remaining = TimeSpan.FromMilliseconds(item.Request.ExpireMilliseconds) - clock.GetElapsedTime(item.Started);
                if (remaining < delay) delay = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
            return delay;
        }
    }

    internal void Handle(ReadOnlySpan<byte> metadataBytes, ReadOnlySpan<byte> payload, bool bell)
    {
        if (_disposed || !Has(TerminalNotificationCapabilities.Display) ||
            !TerminalNotificationMetadata.TryParse(metadataBytes, out var metadata) ||
            payload.Length > (metadata.Encoded ? 4096 : 2048) || !TerminalNotificationMetadata.IsSafeUtf8(payload)) return;
        string id = metadata.Id;
        switch (metadata.Payload)
        {
            case "?": Reply(id, "?", Capabilities(), bell); return;
            case "alive":
                if (Has(TerminalNotificationCapabilities.Alive))
                {
                    Refresh();
                    StringBuilder alive = new();
                    foreach (Active item in _active.Values)
                    {
                        if (item.Id.Length == 0 || !SafeAlive(item.Request.Token)) continue;
                        if (alive.Length > 0) alive.Append(',');
                        alive.Append(item.Id);
                    }
                    Reply(id, "alive", alive.ToString(), bell);
                }
                return;
            case "close":
                if (id.Length > 0 && Has(TerminalNotificationCapabilities.Close))
                {
                    RemovePending(id);
                    if (Find(id) is { } previous) Remove(previous, close: true, report: true);
                }
                return;
            case "title": case "body": case "buttons": break;
            case "icon": if (!metadata.Encoded) return; break;
            default: return; // Unknown payloads must not complete an existing assembly.
        }
        byte[]? decoded = metadata.Encoded ? TerminalNotificationMetadata.DecodeBase64(payload) : null;
        if (metadata.Encoded && decoded is null) return;
        ReadOnlySpan<byte> data = decoded is null ? payload : decoded;
        if (!_pending.TryGetValue(id, out var assembly))
        {
            if (_pending.Count >= MaxNotifications) return;
            _pending.Add(id, assembly = new());
            _pendingBytes += assembly.RetainedBytes;
        }
        int before = assembly.RetainedBytes;
        bool appended = assembly.Append(metadata, data, bell);
        _pendingBytes += assembly.RetainedBytes - before;
        if (!appended || _pendingBytes > MaxRetainedBytes) { RemovePending(id); return; }
        if (!metadata.Done) return;
        RemovePending(id);
        byte[] iconBytes = assembly.CopyIcon();
        if (iconBytes.Length > 0 && assembly.IconId is { Length: > 0 } iconId) _icons.Put(iconId, iconBytes);
        ReadOnlyMemory<byte> icon = iconBytes.Length > 0 ? iconBytes : _icons.Get(assembly.IconId ?? string.Empty);
        if (!ShouldShow(assembly.Occasion)) return;
        Active? old = id.Length > 0 ? Find(id) : null;
        TerminalNotificationRequest? request = assembly.CreateRequest(old?.Request.Token, icon);
        if (request is null || old is null && _active.Count >= MaxNotifications) return;
        int size = RequestBytes(request);
        if (_activeBytes - (old?.Size ?? 0) + size > MaxRetainedBytes) return;
        Active active = new(request, id, assembly, clock.GetTimestamp(), size);
        bool accepted;
        try { accepted = host.Show(request, active.Report); }
        catch (Exception) { accepted = false; }
        if (!accepted) return;
        if (_disposed) { SafeClose(request.Token); return; }
        if (old is not null) Remove(old, close: false, report: false);
        _active.Add(request.Token, active);
        _activeBytes += size;
        if (active.ReportClose && !Has(TerminalNotificationCapabilities.CloseEvents))
        {
            active.ReportClose = false;
            Reply(id, "close", "untracked", active.Bell);
        }
    }

    internal void Refresh()
    {
        if (_disposed || _active.Count == 0) return;
        // Replies may reenter/reset the processor. Never enumerate live state while
        // invoking external code. The pool keeps the ordinary timer path allocation-free.
        Active[] snapshot = ArrayPool<Active>.Shared.Rent(_active.Count);
        int count = _active.Count;
        _active.Values.CopyTo(snapshot, 0);
        try
        {
            for (int i = 0; i < count; i++)
            {
                Active item = snapshot[i];
                if (!_active.ContainsKey(item.Request.Token)) continue;
                int feedback = Interlocked.Exchange(ref item.Feedback, 0);
                if (feedback >= 3)
                {
                    Remove(item, close: true, report: false);
                    int button = feedback - 3;
                    if (item.ReportActivation && Has(TerminalNotificationCapabilities.Activation))
                        Reply(item.Id, null, button == 0 ? string.Empty : button.ToString(CultureInfo.InvariantCulture), item.Bell);
                    if (_disposed) return;
                    if (item.Focus && Has(TerminalNotificationCapabilities.Focus)) { try { host.Focus(); } catch (Exception) { } }
                    if (item.ReportClose) Reply(item.Id, "close", string.Empty, item.Bell);
                }
                else if (feedback is 1 or 2) Remove(item, close: false, report: true);
                else if (item.Request.ExpireMilliseconds > 0 && Has(TerminalNotificationCapabilities.Close) &&
                    clock.GetElapsedTime(item.Started).TotalMilliseconds >= item.Request.ExpireMilliseconds)
                    Remove(item, close: true, report: true);
                else if (Has(TerminalNotificationCapabilities.Alive) && !SafeAlive(item.Request.Token))
                    Remove(item, close: false, report: true);
                if (_disposed) return;
            }
        }
        finally { ArrayPool<Active>.Shared.Return(snapshot, clearArray: true); }
    }

    internal void ResetParser() { _pending.Clear(); _pendingBytes = 0; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetParser();
        _icons.Clear();
        foreach (Guid token in _active.Keys) SafeClose(token);
        _active.Clear();
        _activeBytes = 0;
    }

    private bool Has(TerminalNotificationCapabilities flag)
    {
        try { return (host.Capabilities & flag) == flag; }
        catch (Exception) { return false; }
    }

    private bool ShouldShow(string occasion)
    {
        try { return occasion == "always" || !host.IsFocused && (occasion != "invisible" || !host.IsVisible); }
        catch (Exception) { return false; }
    }

    private bool SafeAlive(Guid token) { try { return host.IsAlive(token); } catch (Exception) { return true; } }
    private void SafeClose(Guid token) { try { host.Close(token); } catch (Exception) { } }

    private Active? Find(string id)
    {
        foreach (Active item in _active.Values) if (item.Id == id) return item;
        return null;
    }

    private void RemovePending(string id)
    {
        if (_pending.Remove(id, out var pending)) _pendingBytes -= pending.RetainedBytes;
    }

    private static int RequestBytes(TerminalNotificationRequest request)
    {
        int characters = request.Title.Length + request.Body.Length + (request.ApplicationName?.Length ?? 0) + request.Sound.Length;
        foreach (string type in request.Types) characters += type.Length;
        foreach (string name in request.IconNames) characters += name.Length;
        foreach (string button in request.Buttons) characters += button.Length;
        return characters * sizeof(char) + request.IconData.Length;
    }

    private void Remove(Active item, bool close, bool report)
    {
        if (!_active.Remove(item.Request.Token)) return;
        _activeBytes -= item.Size;
        if (close) SafeClose(item.Request.Token);
        if (report && item.ReportClose) Reply(item.Id, "close", string.Empty, item.Bell);
    }

    private void Reply(string id, string? payload, string content, bool bell)
    {
        if (_disposed) return;
        send(Encoding.UTF8.GetBytes($"\u001b]99;i={(id.Length == 0 ? "0" : id)}{(payload is null ? string.Empty : ":p=" + payload)};{content}{(bell ? "\a" : "\u001b\\")}"));
    }

    private string Capabilities()
    {
        StringBuilder result = new("p=title,body");
        if (Has(TerminalNotificationCapabilities.Close)) result.Append(",close");
        if (Has(TerminalNotificationCapabilities.Alive)) result.Append(",alive");
        if (Has(TerminalNotificationCapabilities.Icons)) result.Append(",icon");
        if (Has(TerminalNotificationCapabilities.Buttons | TerminalNotificationCapabilities.Activation)) result.Append(",buttons");
        bool focus = Has(TerminalNotificationCapabilities.Focus), report = Has(TerminalNotificationCapabilities.Activation);
        if (focus || report) result.Append(":a=").Append(focus ? report ? "focus,report" : "focus" : "report");
        if (Has(TerminalNotificationCapabilities.CloseEvents)) result.Append(":c=1");
        result.Append(":o=always,invisible,unfocused");
        if (Has(TerminalNotificationCapabilities.NamedSounds)) result.Append(":s=system,silent,error,warn,warning,info,question");
        else if (Has(TerminalNotificationCapabilities.Sound)) result.Append(":s=system,silent");
        if (Has(TerminalNotificationCapabilities.Urgency)) result.Append(":u=0,1,2");
        if (Has(TerminalNotificationCapabilities.Close)) result.Append(":w=1");
        return result.ToString();
    }

    private sealed class Active(TerminalNotificationRequest request, string id, TerminalNotificationAssembly assembly, long started, int size)
    {
        internal readonly TerminalNotificationRequest Request = request;
        internal readonly string Id = id;
        internal readonly bool Bell = assembly.Bell, Focus = assembly.Focus, ReportActivation = assembly.Report;
        internal bool ReportClose = assembly.ReportClose;
        internal readonly long Started = started;
        internal readonly int Size = size;
        internal int Feedback;

        internal void Report(TerminalNotificationFeedback feedback)
        {
            int value = feedback.Event switch
            {
                TerminalNotificationEvent.Closed => 1,
                TerminalNotificationEvent.Failed => 2,
                TerminalNotificationEvent.Activated when feedback.Button >= 0 && feedback.Button <= Request.Buttons.Count => 3 + feedback.Button,
                _ => 0,
            };
            if (value != 0) Interlocked.CompareExchange(ref Feedback, value, 0);
        }
    }
}
