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
        CancellationToken cancellationToken = default) => RunCoreAsync(api, sessionId, kind, label, present, registered, 1, cancellationToken);

    /// <summary>Receives absolute, spatial and cyclic focus plus tab traversal.</summary>
    public static Task RunNavigatorAsync(MultiplexingApiClient api, Guid sessionId, string label,
        Func<MultiplexingViewAction,CancellationToken,ValueTask<bool>> present, Action<Guid>? registered = null,
        CancellationToken cancellationToken = default) => RunCoreAsync(api, sessionId, "native", label, present, registered, 2, cancellationToken);

    /// <summary>Receives desktop navigator commands and presentation-only pane zoom, negotiating down on older brokers.</summary>
    public static Task RunOverviewAsync(MultiplexingApiClient api, Guid sessionId, string label,
        Func<MultiplexingViewAction,CancellationToken,ValueTask<bool>> present, Action<Guid>? registered = null,
        CancellationToken cancellationToken = default) => RunCoreAsync(api, sessionId, "native", label, present, registered, 3, cancellationToken);

    private static async Task<(JsonElement Lease,int Offer,bool Negotiated)> RegisterAsync(MultiplexingApiClient api, Guid session, string kind, string label, int offer, CancellationToken ct)
    {
        var wire=ViewReceiverJson.Default;
        // A definitive missing method has no side effects. Never retry after denial, timeout or network failure.
        static bool Absent(MultiplexingApiException e) => e.StatusCode==HttpStatusCode.NotFound && e.Code=="method_not_found";
        if(offer>=3)
        {
            var args=JsonSerializer.SerializeToElement(new NegotiateView(session,kind,label,offer),wire.NegotiateView);
            try { return (await api.CallAsync("client.negotiate_view",args,ct),offer,true); }
            catch(MultiplexingApiException e) when(Absent(e)) { }
        }
        if(offer>=2)
        {
            var args=JsonSerializer.SerializeToElement(new OpenNavigator(session,label),wire.OpenNavigator);
            try { return (await api.CallAsync("client.open_navigator",args,ct),2,false); }
            catch(MultiplexingApiException e) when(Absent(e)) { }
        }
        var basic=JsonSerializer.SerializeToElement(new OpenView(session,kind,label),wire.OpenView);
        return (await api.CallAsync("client.open_view",basic,ct),1,false);
    }

    private static async Task RunCoreAsync(MultiplexingApiClient api, Guid sessionId, string kind, string label,
        Func<MultiplexingViewAction,CancellationToken,ValueTask<bool>> present, Action<Guid>? registered, int offer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(present);
        if (sessionId == Guid.Empty || kind is not ("native" or "mobile")) throw new ArgumentException("Specify a session and native or mobile view kind.");
        var wire = ViewReceiverJson.Default;
        var (response,maxVersion,negotiated)=await RegisterAsync(api,sessionId,kind,label,offer,ct);
        var view = response.GetProperty("view"); var id = view.GetProperty("id").GetGuid();
        var key = response.GetProperty("readerKey").GetString();
        if (view.GetProperty("sessionId").GetGuid() != sessionId || id == Guid.Empty || key?.Length != 64)
            throw new InvalidDataException("Invalid remote-view registration.");
        long cursor = 0;
        try
        {
            // Version one predates the field. Do not accept a higher version or a changed kind as the result of negotiation.
            int version = view.TryGetProperty("navigationVersion",out var v) ? v.GetInt32() : 1;
            if (version < 1 || version > maxVersion || !negotiated && version!=maxVersion || view.GetProperty("kind").GetString()!=kind)
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
                    if (batch is null || batch.Sequence != checked(cursor + 1) || batch.Actions is not { Length: >= 1 and <= 16 }
                        || Array.Exists(batch.Actions, a => !Accepts(a, version))) throw new InvalidDataException("Invalid remote-view batch or action.");
                    foreach (var action in batch.Actions)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!await present(action, ct)) break; // A refusal consumes the batch; do not retry or retarget it.
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
            catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
    private static bool Accepts(MultiplexingViewAction? a, int version) => a switch
    {
        { Name: "window.focus", WindowId: { } w, BlockId: null, Direction: null } => w != Guid.Empty,
        { Name: "pane.focus", BlockId: { } b, WindowId: null, Direction: null } => b != Guid.Empty,
        { Name: "pane.focus", BlockId: null, WindowId: null, Direction: "left" or "right" or "up" or "down" or "next" or "previous" } => version>=2,
        { Name: "client.tab.next" or "client.tab.previous", BlockId: null, WindowId: null, Direction: null } => version>=2,
        { Name: "pane.zoom", BlockId: null, WindowId: null, Direction: null } => version>=3,
        _ => false,
    };
}
internal sealed record OpenView(Guid SessionId,string Kind,string Label);
internal sealed record OpenNavigator(Guid SessionId,string Label);
internal sealed record NegotiateView(Guid SessionId,string Kind,string Label,int MaxVersion);
internal sealed record ReadView(Guid SessionId,Guid ClientId,string ReaderKey,long After,int TimeoutMs);
internal sealed record CloseView(Guid SessionId,Guid ClientId,string ReaderKey);
internal sealed record ReceivedBatch(long Sequence,MultiplexingViewAction[] Actions);
internal sealed record ViewPage(long Cursor,ReceivedBatch[] Batches);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenView)),JsonSerializable(typeof(OpenNavigator)),JsonSerializable(typeof(NegotiateView)),JsonSerializable(typeof(ReadView)),JsonSerializable(typeof(CloseView)),JsonSerializable(typeof(ViewPage))]
internal partial class ViewReceiverJson:JsonSerializerContext;
