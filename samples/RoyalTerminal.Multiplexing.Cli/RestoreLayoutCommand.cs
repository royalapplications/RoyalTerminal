// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class RestoreLayoutCommand
{
    internal const string Usage = "restore-layout FILE --plan | restore-layout FILE NEW-SESSION-LABEL --fresh-shells";

    internal static async Task<int> RunAsync(string[] args,string? endpoint,string? token,HttpClient http,TextWriter output,TextWriter error,CancellationToken ct)
    {
        if (args is not ([_ ,"--plan"] or [_,_,"--fresh-shells"])) { await error.WriteLineAsync(Usage);return 2; }
        Guid? created=null; bool creating=false;
        try
        {
            await using var file = new FileStream(args[0],FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            var archive=await WorkspaceArchive.ReadAsync(file,ct);
            var source=archive.Session.Deserialize(LayoutRestoreJson.Default.MultiplexerSessionDescriptor) ?? throw new InvalidDataException("Missing session descriptor.");
            Validate(source);
            if(args.Length == 2)
            {
                await output.WriteLineAsync(new JsonObject{["sourceSession"]=source.Id.ToString("D"),["windows"]=source.Windows.Count,["freshShells"]=source.Blocks.Count,["commandsReplayed"]=false}.ToJsonString());return 0;
            }
            Label(args[1]);ArgumentException.ThrowIfNullOrWhiteSpace(token);
            var origin=new Uri(endpoint??"http://127.0.0.1:7331/",UriKind.Absolute);
            if((origin.Scheme!="https" && !(origin.Scheme=="http" && origin.IsLoopback)) || origin.UserInfo.Length!=0 || origin.Query.Length!=0 || origin.Fragment.Length!=0) throw new ArgumentException("Use HTTPS, loopback or a private Unix socket.");
            var api=new MultiplexingApiClient(http,origin,token);
            var capability=await api.SendAsync(HttpMethod.Get,"api/context",cancellationToken:ct);
            if(!capability.GetProperty("administrator").GetBoolean())throw new UnauthorizedAccessException("Creating a new workspace requires an administrator.");
            creating=true;
            var session=await Call("session.create",new(){["label"]=args[1]});
            created=session.GetProperty("id").GetGuid();var sid=created.Value.ToString("D");creating=false;
            var windows=new Dictionary<Guid,Guid>();var blocks=new Dictionary<Guid,Guid>();
            for(int i=0;i<source.Windows.Count;i++)
            {
                var w=source.Windows[i]; Guid next;
                if(i==0){ next=session.GetProperty("windows")[0].GetProperty("id").GetGuid();await Call("session.set_window_label",new(){["sessionId"]=sid,["windowId"]=next.ToString("D"),["label"]=w.Label}); }
                else {var extra=await Call("session.new_window",new(){["sessionId"]=sid,["label"]=w.Label});next=extra.GetProperty("id").GetGuid();}
                windows.Add(w.Id,next);
            }
            foreach(var b in source.Blocks)
            {
                var wid=windows[b.WindowId].ToString("D");
                // Never restore command, environment, cwd or input. The new host chooses the default shell.
                var born=await Call("block.create",new(){["sessionId"]=sid,["block"]=new JsonObject{["transport"]="pty",["label"]=b.Label,["windowId"]=wid,
                    ["dimensions"]=new JsonObject{["columns"]=b.Dimensions.Columns,["rows"]=b.Dimensions.Rows,["widthPixels"]=0,["heightPixels"]=0}}});
                var id=born.GetProperty("id").GetGuid();blocks.Add(b.Id,id);
                if(b.Placement!=MultiplexerPaneKind.Tiled)
                {
                    var fields=new JsonObject{["sessionId"]=sid,["windowId"]=wid,["blockId"]=id.ToString("D"),["kind"]=b.Placement==MultiplexerPaneKind.Floating?"floating":"unplaced"};
                    if(b.FloatingBounds is {} rect) fields["bounds"]=new JsonObject{["x"]=rect.X,["y"]=rect.Y,["width"]=rect.Width,["height"]=rect.Height};
                    await Call("block.place",fields);
                }
            }
            foreach (var w in source.Windows)
            {
                var wid=windows[w.Id].ToString("D");
                if (w.Layout is {} tree) await Call("session.set_layout",new(){["sessionId"]=sid,["windowId"]=wid,["layout"]=Remap(tree,blocks)});
                var order=MultiplexerLayers.Ordered(source,w);
                var creationOrder=source.Blocks.Where(b=>b.WindowId==w.Id && b.Placement==MultiplexerPaneKind.Floating).Select(b=>b.Id);
                if (!order.SequenceEqual(creationOrder))
                {
                    var ids=new JsonArray();
                    foreach (Guid old in order) ids.Add(blocks[old].ToString("D"));
                    await Call("session.set_layer_order",new(){["sessionId"]=sid,["windowId"]=wid,["blocks"]=ids});
                }
            }
            await output.WriteLineAsync(new JsonObject{["sourceSession"]=source.Id.ToString("D"),["sessionId"]=sid,["windows"]=Map(windows),["blocks"]=Map(blocks),["commandsReplayed"]=false}.ToJsonString());
            return 0;
            async ValueTask<JsonElement> Call(string method,JsonObject fields)
            { using var doc=JsonDocument.Parse(fields.ToJsonString());return await api.CallAsync(method,doc.RootElement,ct); }
        }
        catch(Exception ex) when(ex is IOException or JsonException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException or KeyNotFoundException)
        {
            await error.WriteLineAsync(ex.Message);
            if(created.HasValue) await error.WriteLineAsync($"Partial new session: {created:D}. No automatic deletion or retry. Inspect this session before closing it.");
            else if(creating)await error.WriteLineAsync("Session creation was not confirmed. Check the host for a new session before retrying.");
            return ct.IsCancellationRequested?130:1;
        }
    }

    internal static void Validate(MultiplexerSessionDescriptor s)
    {
        if(s.Id==Guid.Empty || s.Windows is null || s.Blocks is null || s.Windows.Count is <1 or >128 || s.Blocks.Count>128)throw Bad();
        var all=new HashSet<Guid>{s.Id};var homes=new HashSet<Guid>();var blocks=new Dictionary<Guid,MultiplexerBlockDescriptor>();
        foreach(var w in s.Windows){if(w is null || w.Id==Guid.Empty || !all.Add(w.Id))throw Bad();homes.Add(w.Id);Label(w.Label);}
        foreach(var b in s.Blocks)
        {
            if(b is null || b.Id==Guid.Empty || !all.Add(b.Id) || b.SessionId!=s.Id || !homes.Contains(b.WindowId) || !Enum.IsDefined(b.Placement) || b.Dimensions.Columns is <2 or >512 || b.Dimensions.Rows is <1 or >256)throw Bad();
            if(b.Placement==MultiplexerPaneKind.Floating)
            { if(b.FloatingBounds is not {} r || r.Width is <1 or >10000 || r.Height is <1 or >10000 || r.X<0 || r.Y<0 || r.X>10000-r.Width || r.Y>10000-r.Height)throw Bad(); }
            else if(b.FloatingBounds is not null)throw Bad();
            Label(b.Label);blocks.Add(b.Id,b);
        }
        HashSet<Guid> tiled=[];
        foreach(var w in s.Windows){ _=MultiplexerLayers.Ordered(s,w); if(w.Layout is {} node) Walk(node,w.Id,0); }
        if(tiled.Count!=blocks.Values.Count(b=>b.Placement==MultiplexerPaneKind.Tiled))throw Bad();
        void Walk(MultiplexerLayoutNode node,Guid home,int depth)
        {
            if(depth>63)throw Bad();
            if(node.BlockId is Guid id){if(node.First is not null || node.Second is not null || node.Orientation is not null || !blocks.TryGetValue(id,out var b) || b.WindowId!=home || b.Placement!=MultiplexerPaneKind.Tiled || !tiled.Add(id))throw Bad();return;}
            if(node.Orientation is not (MultiplexerSplitOrientation.Horizontal or MultiplexerSplitOrientation.Vertical) || !(node.Ratio>0 && node.Ratio<1) || node.First is null || node.Second is null)throw Bad();
            Walk(node.First,home,depth+1);Walk(node.Second,home,depth+1);
        }
    }
    private static JsonObject Remap(MultiplexerLayoutNode n,Dictionary<Guid,Guid> ids) => n.BlockId is Guid b ? new(){["blockId"]=ids[b].ToString("D")} :
        new(){["orientation"]=n.Orientation.ToString(),["ratio"]=n.Ratio,["first"]=Remap(n.First!,ids),["second"]=Remap(n.Second!,ids)};
    private static JsonObject Map(Dictionary<Guid,Guid> ids){var obj=new JsonObject();foreach(var (from,to) in ids)obj.Add(from.ToString("D"),to.ToString("D"));return obj;}
    private static void Label(string? s){if(string.IsNullOrWhiteSpace(s) || s.Length>256 || s.Any(char.IsControl))throw Bad();}
    private static InvalidDataException Bad()=>new("Archive contains an invalid label, placement, geometry or split tree. No workspace has been created.");
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true)]
[JsonSerializable(typeof(MultiplexerSessionDescriptor))]
internal partial class LayoutRestoreJson:JsonSerializerContext;
