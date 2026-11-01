// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>A presentation instruction, never authorization to send PTY input or mutate the workspace.</summary>
public sealed record MultiplexingViewAction(string Name, Guid? BlockId, Guid? WindowId, string? Direction);

/// <summary>Consent lasts only for this invocation. Call from the UI context to receive presentation callbacks there.</summary>
public static class MultiplexingViewReceiver
{
    /// <summary>Receives only absolute window and block focus, including on a newer server.</summary>
    public static Task RunAsync(MultiplexingApiClient api, Guid sessionId, string kind, string label,
        Func<MultiplexingViewAction,CancellationToken,ValueTask<bool>> present, Action<Guid>? registered = null,
        CancellationToken cancellationToken = default) => RunCoreAsync(api, sessionId, kind, label, present, registered, false, cancellationToken);

    /// <summary>Opt in to desktop spatial/cyclic focus and tab traversal. An older broker falls back to absolute focus only.</summary>
    public static Task RunNavigatorAsync(MultiplexingApiClient api, Guid sessionId, string label,
        Func<MultiplexingViewAction,CancellationToken,ValueTask<bool>> present, Action<Guid>? registered = null,
        CancellationToken cancellationToken = default) => RunCoreAsync(api, sessionId, "native", label, present, registered, true, cancellationToken);

    private static async Task RunCoreAsync(MultiplexingApiClient api, Guid sessionId, string kind, string label,
        Func<MultiplexingViewAction,CancellationToken,ValueTask<bool>> present, Action<Guid>? registered, bool extended, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(present);
        if (sessionId == Guid.Empty || kind is not ("native" or "mobile")) throw new ArgumentException("Specify a session and native or mobile view kind.");
        var wire = ViewReceiverJson.Default;
        JsonElement response = default;
        if (extended)
        {
            var open = JsonSerializer.SerializeToElement(new OpenNavigator(sessionId, label), wire.OpenNavigator);
            try { response = await api.CallAsync("client.open_navigator", open, ct); }
            catch (MultiplexingApiException e) when (e.StatusCode == HttpStatusCode.NotFound && e.Code == "method_not_found")
            { extended = false; } // Definitive non-execution; never repeat a registration after an uncertain network result.
        }
        if (!extended)
        {
            var open = JsonSerializer.SerializeToElement(new OpenView(sessionId, kind, label), wire.OpenView);
            response = await api.CallAsync("client.open_view", open, ct);
        }
        var view = response.GetProperty("view"); var id = view.GetProperty("id").GetGuid();
        var key = response.GetProperty("readerKey").GetString();
        if (view.GetProperty("sessionId").GetGuid() != sessionId || id == Guid.Empty || key?.Length != 64)
            throw new InvalidDataException("Invalid remote-view registration.");
        long cursor = 0;
        try
        {
            if (extended && (view.GetProperty("kind").GetString() != "native" || view.GetProperty("navigationVersion").GetInt32() != 2))
                throw new InvalidDataException("Unexpected navigation profile.");
            ct.ThrowIfCancellationRequested(); registered?.Invoke(id);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var poll = JsonSerializer.SerializeToElement(new ReadView(sessionId,id,key,cursor,15000), wire.ReadView);
                var raw = await api.CallAsync("client.read_view", poll, ct);
                var page = raw.Deserialize(wire.ViewPage) ?? throw new InvalidDataException("Missing remote-view page.");
                if (page.Batches is not { Length: <= 8 } || page.Cursor != checked(cursor + page.Batches.Length))
                    throw new InvalidDataException("Invalid remote-view cursor.");
                foreach (var batch in page.Batches)
                {
                    if (batch.Sequence != checked(cursor + 1) || batch.Actions is not { Length: >= 1 and <= 16 }
                        || Array.Exists(batch.Actions, a => !Accepts(a, extended))) throw new InvalidDataException("Invalid remote-view batch or action.");
                    foreach (var action in batch.Actions)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!await present(action, ct)) break; // Local refusal consumes the batch. Never retarget or repeat it.
                    }
                    cursor = batch.Sequence;
                }
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var close = JsonSerializer.SerializeToElement(new CloseView(sessionId,id,key), wire.CloseView);
            try { await api.CallAsync("client.close_view", close, cleanup.Token); }
            catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException or ObjectDisposedException) { /* Server expiry bounds unreachable registrations. */ }
        }
    }

    private static bool Accepts(MultiplexingViewAction? a, bool extended) => a switch
    {
        { Name: "window.focus", WindowId: { } w, BlockId: null, Direction: null } => w != Guid.Empty,
        { Name: "pane.focus", BlockId: { } b, WindowId: null, Direction: null } => b != Guid.Empty,
        { Name: "pane.focus", BlockId: null, WindowId: null, Direction: "left" or "right" or "up" or "down" or "next" or "previous" } => extended,
        { Name: "client.tab.next" or "client.tab.previous", BlockId: null, WindowId: null, Direction: null } => extended,
        _ => false,
    };
}
internal sealed record OpenView(Guid SessionId,string Kind,string Label);
internal sealed record OpenNavigator(Guid SessionId,string Label);
internal sealed record ReadView(Guid SessionId,Guid ClientId,string ReaderKey,long After,int TimeoutMs);
internal sealed record CloseView(Guid SessionId,Guid ClientId,string ReaderKey);
internal sealed record ReceivedBatch(long Sequence,MultiplexingViewAction[] Actions);
internal sealed record ViewPage(long Cursor,ReceivedBatch[] Batches);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenView)),JsonSerializable(typeof(OpenNavigator)),JsonSerializable(typeof(ReadView)),JsonSerializable(typeof(CloseView)),JsonSerializable(typeof(ViewPage))]
internal partial class ViewReceiverJson:JsonSerializerContext;
