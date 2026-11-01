// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>A parser transition. Sequence belongs to the requested feed, never to the VT stream.</summary>
public sealed record TerminalMetadataEvent(long Sequence, string Name, string? Text = null, int? ExitCode = null,
    string? RecordId = null, TerminalProgramStatus? Record = null, string? Reason = null)
{
    /// <summary>Present on an aggregate session feed; remains stable if the pane moves.</summary>
    public Guid? BlockId { get; init; }
    /// <summary>Source block's metadata cursor on an aggregate feed.</summary>
    public long? BlockSequence { get; init; }
    /// <summary>Raw directory report, not a host filesystem operation.</summary>
    public string? Pwd { get; init; }
    /// <summary>Untrusted notification text, not confirmation of OS delivery.</summary>
    public TerminalDesktopNotification? Notification { get; init; }
    /// <summary>Progress with an optional percentage.</summary>
    public TerminalProgressReport? ProgressReport { get; init; }
}

/// <summary>NextSequence names the last delivered record, not an undelivered high-water mark.</summary>
public sealed record TerminalMetadataPage(long FirstSequence, long LastSequence, long NextSequence, bool Ended, IReadOnlyList<TerminalMetadataEvent> Events);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TerminalMetadataEvent))]
internal sealed partial class ActivityMetadataJson : JsonSerializerContext;

/// <summary>Bounded metadata journal. No terminal bytes, input leases or client parsers are retained here.</summary>
public sealed class TerminalMetadataFeed
{
    private const int MaxEvents = 512, MaxBytes = 512 * 1024, MaxWaiters = 32;
    private readonly object _gate = new();
    private readonly Queue<(TerminalMetadataEvent Event, int Charge)> _history = [];
    private readonly Dictionary<string, TerminalProgramStatus> _records = new(StringComparer.Ordinal);
    private int _bytes, _waiters;
    private long _last;
    private bool _ended, _admitted;
    private TaskCompletionSource? _signal;
    private TerminalMetadataFeed? _aggregate;
    private Guid _block;
    private readonly WorkspaceActivityHub? _activity;
    private readonly Guid _session;

    /// <summary>Creates a standalone terminal journal.</summary>
    public TerminalMetadataFeed() { }
    internal TerminalMetadataFeed(WorkspaceActivityHub hub, Guid session) => (_activity, _session) = (hub, session);

    internal void Observe(BasicVtProcessor processor)
    {
        processor.BellCallback = () => Add(new(0, "terminal.bell"));
        processor.TitleCallback = title => Add(new(0, "terminal.title_changed", Text: Bound(title, 2048)));
        processor.WorkingDirectoryCallback = pwd => Add(new(0, "terminal.pwd_changed") { Pwd = Bound(pwd, 4096) });
        processor.DesktopNotificationCallback = item => Add(new(0, "terminal.desktop_notification")
            { Notification = new(Bound(item.Title, 512), Bound(item.Body, 2048)) });
        processor.ProgressReportCallback = report => Add(new(0, "terminal.progress_report") { ProgressReport = report });
        processor.ProgramStatusChangedCallback = () => StatusChanged(processor.ProgramStatuses);
    }
    private static string Bound(string value, int limit) => value.Length <= limit ? value : value[..(char.IsHighSurrogate(value[limit - 1]) ? limit - 1 : limit)];

    // The only nested lock order is child -> aggregate -> activity hub. No reader takes the reverse path.
    internal void AdmitTo(TerminalMetadataFeed aggregate, Guid block)
    {
        lock (_gate)
        {
            if (_admitted) throw new InvalidOperationException("This block already belongs to a session feed.");
            _admitted = true;
            lock (aggregate._gate)
            {
                if (aggregate._ended) return;
                long missing = (_history.TryPeek(out var head) ? head.Event.Sequence : _last + 1) - 1;
                if (missing != 0)
                {
                    aggregate._history.Clear(); aggregate._bytes = 0; aggregate._last = checked(aggregate._last + missing);
                    aggregate._activity?.RecordTerminalGap(aggregate._session,missing);
                    aggregate.Wake();
                }
                foreach (var (item, charge) in _history) aggregate.Store(Project(item, block), charge);
                _block = block;
                if (!_ended) _aggregate = aggregate;
            }
        }
    }
    private static TerminalMetadataEvent Project(TerminalMetadataEvent item, Guid block) => item with { BlockId = block, BlockSequence = item.Sequence };
    internal void ChildExited(int code)
    {
        lock (_gate) { if (_ended) return; AddLocked(new(0,"terminal.child_exited",ExitCode:code)); EndLocked(); }
    }
    internal void End() { lock (_gate) EndLocked(); }
    private void EndLocked() { _ended = true; _aggregate = null; Wake(); }
    private void Wake() { _signal?.TrySetResult(); _signal = null; }
    private void StatusChanged(IReadOnlyList<TerminalProgramStatus> records)
    {
        lock (_gate)
        {
            if (_ended) return;
            HashSet<string> present = new(StringComparer.Ordinal);
            foreach (var item in records)
            {
                present.Add(item.Id);
                if (_records.TryGetValue(item.Id, out var old) && old == item) continue;
                _records[item.Id] = item; AddLocked(new(0,"terminal.program_status_changed",RecordId:item.Id,Record:item,Reason:"set"));
            }
            foreach (string id in _records.Keys.Where(id => !present.Contains(id)).ToArray())
            { _records.Remove(id); AddLocked(new(0,"terminal.program_status_changed",RecordId:id,Reason:"removed")); }
        }
    }
    private void Add(TerminalMetadataEvent item) { lock (_gate) if (!_ended) AddLocked(item); }
    private void AddLocked(TerminalMetadataEvent item)
    {
        int charge = 224 + 2 * ((item.Text?.Length ?? 0) + (item.RecordId?.Length ?? 0) +
            (item.Record?.Title.Length ?? 0) + (item.Record?.Message.Length ?? 0) + (item.Record?.App.Length ?? 0) +
            (item.Pwd?.Length ?? 0) + (item.Notification?.Title.Length ?? 0) + (item.Notification?.Body.Length ?? 0));
        var owned = Store(item, charge);
        if (_aggregate is { } group) lock (group._gate) if (!group._ended) group.Store(Project(owned,_block),charge);
    }
    private TerminalMetadataEvent Store(TerminalMetadataEvent item, int charge)
    {
        item = item with { Sequence = checked(++_last) };
        _history.Enqueue((item,charge)); _bytes += charge;
        while (_history.Count > MaxEvents || _bytes > MaxBytes) _bytes -= _history.Dequeue().Charge;
        if (_activity is not null && item.BlockId is Guid block)
        {
            byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(item,ActivityMetadataJson.Default.TerminalMetadataEvent);
            using var doc = JsonDocument.Parse(encoded);
            _activity.RecordTerminal(_session,block,doc.RootElement,encoded.Length);
        }
        Wake(); return item;
    }
    /// <summary>Omit after to start now. Reads never silently skip a gap or advance over an undisclosed record.</summary>
    public async ValueTask<TerminalMetadataPage> ReadAsync(long? after = null, int limit = 64, int timeoutMs = 0, CancellationToken cancellationToken = default)
    {
        if (after < 0 || limit is < 1 or > 128 || timeoutMs is < 0 or > 25000) throw new ArgumentOutOfRangeException(nameof(after),"Invalid cursor, limit (1..128), or timeout (0..25000 ms).");
        cancellationToken.ThrowIfCancellationRequested(); Task wait;
        lock (_gate)
        {
            if (after is null) return Page(_last,limit);
            if (after != _last || _ended || timeoutMs == 0) return Page(after.Value,limit);
            if (_waiters >= MaxWaiters) throw new MultiplexerException(MultiplexerErrorCode.CapacityExceeded,"Too many metadata readers.");
            ++_waiters; wait = (_signal ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        try { await wait.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs),cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { }
        finally { lock (_gate) --_waiters; }
        cancellationToken.ThrowIfCancellationRequested(); lock (_gate) return Page(after.Value,limit);
    }
    private TerminalMetadataPage Page(long after,int limit)
    {
        long first = _history.TryPeek(out var head) ? head.Event.Sequence : _last + 1;
        if (after < first-1) throw new MultiplexerException(MultiplexerErrorCode.HistoryGap,"The metadata cursor precedes retained history.");
        if (after > _last) throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The metadata cursor is ahead of this feed.");
        var events = _history.Where(i => i.Event.Sequence > after).Take(limit).Select(i => i.Event).ToArray();
        long next=events.Length==0?after:events[^1].Sequence;
        return new(first,_last,next,_ended&&next==_last,events);
    }
}
