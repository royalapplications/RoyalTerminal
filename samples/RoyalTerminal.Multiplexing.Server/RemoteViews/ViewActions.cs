// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RoyalTerminal.Multiplexing.Server;

internal static class ViewActions
{
    internal static ViewAction[] Parse(JsonElement args, MultiplexerSessionDescriptor session)
    {
        if (!args.TryGetProperty("actions",out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 16) throw new ArgumentException("Supply 1–16 client actions.");
        var commands = new List<ViewAction>(array.GetArrayLength());
        foreach (var json in array.EnumerateArray())
        {
            var a = new ApiArguments(json); a.Validate("name","blockId","windowId","direction");
            var block = a.OptionalId("blockId"); var window = a.OptionalId("windowId");
            var command = new ViewAction(a.String("name"), block, window, json.TryGetProperty("direction",out _) ? a.String("direction") : null);
            if (!ViewCapabilities.Allows("browser", 1, command)) throw new ArgumentException("Unknown client action or invalid action arguments.");
            if (block.HasValue && !session.Blocks.Any(b => b.Id == block && b.Placement != MultiplexerPaneKind.Unplaced)) throw new ApiException(409,"view_target","Terminal is missing or unplaced.");
            if (window.HasValue && !session.Windows.Any(w => w.Id == window)) throw new ApiException(409,"view_target","Window is missing.");
            commands.Add(command);
        }
        return [..commands];
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ViewLease)), JsonSerializable(typeof(ViewIdentity[])), JsonSerializable(typeof(ViewDelivery)), JsonSerializable(typeof(ViewReceipt)), JsonSerializable(typeof(ViewActionCatalog)), JsonSerializable(typeof(ViewOutcome))]
internal partial class RemoteViewJson : JsonSerializerContext;

internal sealed partial class ApiCatalog
{
    private void RegisterRemoteViews()
    {
        var hub = new ViewMailbox(TimeProvider.System,_tokens.IsActive); var wire = RemoteViewJson.Default;
        Define("client.open_view","Opt in one UI using its original presentation profile.",["sessionId:string!","label:string!","kind:string!"],wire.ViewLease,(a,g,_) => {
            var s = _workspaces.Broker.GetSession(Session(a,g)); return Done(hub.Open(s.Id,a.String("label"),a.String("kind"),g),wire.ViewLease);
        },"Session-scoped receiver; distinct from a terminal control lease.");
        Define("client.open_navigator","Register a legacy desktop navigator.",["sessionId:string!","label:string!"],wire.ViewLease,(a,g,_) => {
            var s = _workspaces.Broker.GetSession(Session(a,g)); return Done(hub.Open(s.Id,a.String("label"),"native",g,2),wire.ViewLease);
        },"Desktop-only fallback; handheld clients must not claim this identity.");
        Define("client.negotiate_view","Register a consenting view at the highest mutually supported presentation version.",["sessionId:string!","label:string!","kind:string!","maxVersion:integer!"],wire.ViewLease,(a,g,_) => {
            var s = _workspaces.Broker.GetSession(Session(a,g)); var kind=a.String("kind");
            return Done(hub.Open(s.Id,a.String("label"),kind,g,ViewCapabilities.Negotiate(kind,a.Integer("maxVersion",0))),wire.ViewLease);
        },"Negotiation grants neither input nor mutation rights.");
        Define("client.views","List opted-in interfaces without receiver secrets.",["sessionId:string!"],wire.ViewIdentityArray,(a,g,_) => { ServerHttp.RequireAdministrator(g); return Done(hub.List(Session(a,g)),wire.ViewIdentityArray); });
        Define("client.actions","Describe action forms for one active view.",["sessionId:string!","clientId:string!"],wire.ViewActionCatalog,(a,g,_) => {
            ServerHttp.RequireAdministrator(g); var session = _workspaces.Broker.GetSession(Session(a,g)); return Done(hub.Describe(session.Id,a.Id("clientId")),wire.ViewActionCatalog);
        },"Reading the catalog does not acknowledge, queue, or extend a receiver lease.");
        Define("client.queue","Validate and enqueue one presentation-only batch. Admission is not application.",["sessionId:string!","clientId:string!","actions:array!"],wire.ViewReceipt,(a,g,t) => {
            ServerHttp.RequireAdministrator(g); t.ThrowIfCancellationRequested(); var s = _workspaces.Broker.GetSession(Session(a,g)); return Done(hub.Publish(s.Id,a.Id("clientId"),ViewActions.Parse(a.Value,s)),wire.ViewReceipt);
        });
        Define("client.read_view","Read up to eight batches; after acknowledges delivery, not success.",["sessionId:string!","clientId:string!","readerKey:string!","after:integer!","timeoutMs:integer"],wire.ViewDelivery,async(a,g,t) => {
            var sid=Session(a,g);
            var page=await hub.ReadAsync(sid,a.Id("clientId"),a.String("readerKey"),g,Cursor(a,"after"),a.Integer("timeoutMs",15000),t);
            EnsureLive(sid,g); return Element(page,wire.ViewDelivery);
        },"Requires the original capability and private reader key.");
        Define("client.finish_view","Report how many consecutive actions the receiver applied. A short prefix means it declined the next action.",["sessionId:string!","clientId:string!","readerKey:string!","sequence:integer!","applied:integer!"],wire.ViewOutcome,(a,g,t)=>{
            t.ThrowIfCancellationRequested();var sid=Session(a,g);EnsureLive(sid,g);
            return Done(hub.Finish(sid,a.Id("clientId"),a.String("readerKey"),g,Cursor(a,"sequence"),a.Integer("applied",-1)),wire.ViewOutcome);
        },"Only this receiver can report; duplicate identical reports are safe.");
        Define("client.view_result","Inspect or wait for the disposition of a queued presentation batch.",["sessionId:string!","clientId:string!","sequence:integer!","timeoutMs:integer"],wire.ViewOutcome,async(a,g,t)=>{
            ServerHttp.RequireAdministrator(g);var sid=Session(a,g);EnsureLive(sid,g);
            var outcome=await hub.ResultAsync(sid,a.Id("clientId"),Cursor(a,"sequence"),a.Integer("timeoutMs",0),t);EnsureLive(sid,g);return Element(outcome,wire.ViewOutcome);
        },"A timeout returns current state. A legacy delivery acknowledgement is unreported, not applied.");
        Define("client.close_view","Withdraw consent and discard undelivered actions.",["sessionId:string!","clientId:string!","readerKey:string!"],ServerJsonContext.Default.ApiSuccess,(a,g,_) => {hub.Close(Session(a,g),a.Id("clientId"),a.String("readerKey"),g);return new(Success());},"Requires the receiver proof.");

        static long Cursor(ApiArguments a,string name)=>a.Value.TryGetProperty(name,out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetInt64(out long value)?value:throw new ArgumentException($"Supply an integral {name}.");
        void EnsureLive(Guid session,AccessGrant grant){if(!_tokens.IsActive(grant))throw new ApiException(403,"expired","Receiver capability revoked.");_= _workspaces.Broker.GetSession(session);}
    }
}
