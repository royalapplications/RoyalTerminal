// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RoyalTerminal.Multiplexing.Server;

internal static class ViewActions
{
    internal static bool Supported(string kind,ViewAction a) => kind == "browser" || a.Name is "window.focus" or "pane.focus" && a.Direction is null;
    internal static ViewAction[] Parse(JsonElement args, MultiplexerSessionDescriptor session)
    {
        if (!args.TryGetProperty("actions",out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 16) throw new ArgumentException("Supply 1–16 client actions.");
        var commands = new List<ViewAction>();
        foreach (var json in array.EnumerateArray())
        {
            var a = new ApiArguments(json); a.Validate("name","blockId","windowId","direction");
            string name = a.String("name"); var block = a.OptionalId("blockId"); var window = a.OptionalId("windowId");
            string? direction = json.TryGetProperty("direction",out _) ? a.String("direction") : null;
            bool valid = name switch
            {
                "pane.focus" => window is null && ((block.HasValue && direction is null) || (!block.HasValue && direction is "left" or "right" or "up" or "down" or "next" or "previous")),
                "window.focus" => window.HasValue && !block.HasValue && direction is null,
                "pane.zoom" or "client.tab.next" or "client.tab.previous" or "client.palette" or "client.sidebar.toggle" => !block.HasValue && !window.HasValue && direction is null,
                _ => false
            };
            if (!valid) throw new ArgumentException("Unknown client action or invalid action arguments.");
            if (block.HasValue && !session.Blocks.Any(b => b.Id == block && b.Placement != MultiplexerPaneKind.Unplaced)) throw new ApiException(409,"view_target","Terminal is missing or unplaced.");
            if (window.HasValue && !session.Windows.Any(w => w.Id == window)) throw new ApiException(409,"view_target","Window is missing.");
            commands.Add(new(name,block,window,direction));
        }
        return [..commands];
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ViewLease)), JsonSerializable(typeof(ViewIdentity[])), JsonSerializable(typeof(ViewDelivery)), JsonSerializable(typeof(ViewReceipt))]
internal partial class RemoteViewJson : JsonSerializerContext;

internal sealed partial class ApiCatalog
{
    private void RegisterRemoteViews()
    {
        var hub = new ViewMailbox(TimeProvider.System,_tokens.IsActive); var wire = RemoteViewJson.Default;
        Define("client.open_view","Opt in one user interface for a single session. Keep the reader key in memory only.",["sessionId:string!","label:string!","kind:string!"],wire.ViewLease,(a,g,_) => {
            var s = _workspaces.Broker.GetSession(Session(a,g)); return Done(hub.Open(s.Id,a.String("label"),a.String("kind"),g),wire.ViewLease);
        },"Session-scoped receiver; distinct from a terminal control lease.");
        Define("client.views","List remote-control-enabled interfaces. Does not expose receiver secrets.",["sessionId:string!"],wire.ViewIdentityArray,(a,g,_) => { ServerHttp.RequireAdministrator(g); return Done(hub.List(Session(a,g)),wire.ViewIdentityArray); });
        Define("client.queue","Validate and enqueue one ordered presentation-only batch for an explicit client.",["sessionId:string!","clientId:string!","actions:array!"],wire.ViewReceipt,(a,g,t) => {
            ServerHttp.RequireAdministrator(g); t.ThrowIfCancellationRequested(); var s = _workspaces.Broker.GetSession(Session(a,g)); return Done(hub.Publish(s.Id,a.Id("clientId"),ViewActions.Parse(a.Value,s)),wire.ViewReceipt);
        });
        Define("client.read_view","Read up to eight batches; after acknowledges the last handled sequence.",["sessionId:string!","clientId:string!","readerKey:string!","after:integer!","timeoutMs:integer"],wire.ViewDelivery,async(a,g,t) => {
            var sid=Session(a,g); if (!a.Value.TryGetProperty("after",out var n)||n.ValueKind!=JsonValueKind.Number||!n.TryGetInt64(out var after)) throw new ArgumentException("Supply an integral cursor.");
            var page=await hub.ReadAsync(sid,a.Id("clientId"),a.String("readerKey"),g,after,a.Integer("timeoutMs",15000),t);
            if (!_tokens.IsActive(g)) throw new ApiException(403,"expired","Receiver capability revoked.");
            _ = _workspaces.Broker.GetSession(sid); return Element(page,wire.ViewDelivery);
        },"Requires the same active capability and private reader key; one outstanding poll per receiver.");
        Define("client.close_view","Withdraw consent and discard undelivered actions.",["sessionId:string!","clientId:string!","readerKey:string!"],ServerJsonContext.Default.ApiSuccess,(a,g,_) => {hub.Close(Session(a,g),a.Id("clientId"),a.String("readerKey"),g);return new(Success());},"Requires the receiver proof.");
    }
}
