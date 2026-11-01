// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>A presentation instruction. It is not authorization to send PTY input or mutate the workspace.</summary>
public sealed record MultiplexingViewAction(string Name, Guid? BlockId, Guid? WindowId, string? Direction);

/// <summary>Consent lasts only for the lifetime of this invocation. Call from the UI context to receive callbacks there.</summary>
public static class MultiplexingViewReceiver
{
    /// <summary>Registers an individual view, applies ordered batches and acknowledges only handled sequences. No automatic replay of an uncertain UI operation.</summary>
    public static async Task RunAsync(MultiplexingApiClient api, Guid sessionId, string kind, string label,
        Func<MultiplexingViewAction,CancellationToken,ValueTask<bool>> present,
        Action<Guid>? registered = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(present);
        if (sessionId == Guid.Empty || kind is not ("native" or "mobile")) throw new ArgumentException("Specify a session and native or mobile view kind.");
        var wire=ViewReceiverJson.Default;
        var request=JsonSerializer.SerializeToElement(new OpenView(sessionId,kind,label),wire.OpenView);
        var response=await api.CallAsync("client.open_view",request,cancellationToken);
        var view=response.GetProperty("view"); var id=view.GetProperty("id").GetGuid();
        var key=response.GetProperty("readerKey").GetString();
        if(view.GetProperty("sessionId").GetGuid()!=sessionId || id==Guid.Empty || key?.Length!=64)throw new InvalidDataException("Invalid remote-view registration.");
        long cursor=0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); registered?.Invoke(id);
            while(true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var poll=JsonSerializer.SerializeToElement(new ReadView(sessionId,id,key,cursor,15000),wire.ReadView);
                var raw=await api.CallAsync("client.read_view",poll,cancellationToken);
                var delivery=raw.Deserialize(wire.ViewPage)??throw new InvalidDataException("Missing remote-view page.");
                if(delivery.Batches is not {Length:<=8} || delivery.Cursor!=checked(cursor+delivery.Batches.Length))throw new InvalidDataException("Invalid remote-view cursor.");
                foreach(var batch in delivery.Batches)
                {
                    if(batch.Sequence!=checked(cursor+1) || batch.Actions is not {Length:>=1 and <=16})throw new InvalidDataException("Invalid remote-view batch.");
                    foreach(var action in batch.Actions)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if(action is not ({Name:"window.focus",WindowId:not null,BlockId:null,Direction:null} or {Name:"pane.focus",BlockId:not null,WindowId:null,Direction:null}))throw new InvalidDataException("Unexpected remote-view action.");
                        if(!await present(action,cancellationToken))break; // The target disappeared or local policy rejected it. Never reinterpret an ID.
                    }
                    cursor=batch.Sequence;
                }
            }
        }
        finally
        {
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var close=JsonSerializer.SerializeToElement(new CloseView(sessionId,id,key),wire.CloseView);
            try{await api.CallAsync("client.close_view",close,cleanup.Token);}catch(Exception ex) when(ex is IOException or HttpRequestException or OperationCanceledException or ObjectDisposedException){/* Server TTL bounds an unreachable receiver. */}
        }
    }
}
internal sealed record OpenView(Guid SessionId,string Kind,string Label);
internal sealed record ReadView(Guid SessionId,Guid ClientId,string ReaderKey,long After,int TimeoutMs);
internal sealed record CloseView(Guid SessionId,Guid ClientId,string ReaderKey);
internal sealed record ReceivedBatch(long Sequence,MultiplexingViewAction[] Actions);
internal sealed record ViewPage(long Cursor,ReceivedBatch[] Batches);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OpenView)),JsonSerializable(typeof(ReadView)),JsonSerializable(typeof(CloseView)),JsonSerializable(typeof(ViewPage))]
internal partial class ViewReceiverJson:JsonSerializerContext;
