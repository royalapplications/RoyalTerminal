// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Cli;

internal static class RestoreLayoutCommand
{
    internal const string Usage="restore-layout FILE --plan [--identity FILE] | restore-layout FILE NEW-SESSION-LABEL --fresh-shells [--identity FILE]";
    internal static async Task<int> RunAsync(string[] args,string? endpoint,string? token,HttpClient http,TextWriter output,TextWriter error,CancellationToken ct)
    {
        Guid? created=null;bool creating=false;
        try
        {
            string? identity=ArchiveProtection.Identity(ref args);
            if(args is not ([_,"--plan"] or [_,_,"--fresh-shells"])) {await error.WriteLineAsync(Usage);return 2;}
            var archive=await ArchiveProtection.ReadAsync(args[0],identity,ct);
            var source=archive.Session.Deserialize(LayoutRestoreJson.Default.MultiplexerSessionDescriptor)??throw Bad();Validate(source);
            if(args.Length==2){await output.WriteLineAsync(new JsonObject{["sourceSession"]=source.Id.ToString("D"),["windows"]=source.Windows.Count,["freshShells"]=source.Blocks.Count,["commandsReplayed"]=false}.ToJsonString());return 0;}
            Label(args[1]);ArgumentException.ThrowIfNullOrWhiteSpace(token);var origin=new Uri(endpoint??"http://127.0.0.1:7331/",UriKind.Absolute);
            if((origin.Scheme!="https"&&!(origin.Scheme=="http"&&origin.IsLoopback))||origin.UserInfo.Length!=0||origin.Query.Length!=0||origin.Fragment.Length!=0)throw new ArgumentException("Use HTTPS, loopback or a private Unix socket.");
            var api=new MultiplexingApiClient(http,origin,token);var capability=await api.SendAsync(HttpMethod.Get,"api/context",cancellationToken:ct);
            if(!capability.GetProperty("administrator").GetBoolean())throw new UnauthorizedAccessException("Creating a new workspace requires an administrator.");
            creating=true;var session=await Call("session.create",new(){["label"]=args[1]});created=session.GetProperty("id").GetGuid();creating=false;string sid=created.Value.ToString("D");
            Dictionary<Guid,Guid> windows=[],blocks=[],frames=[];
            foreach(var w in source.Windows){Guid id;if(windows.Count==0){id=session.GetProperty("windows")[0].GetProperty("id").GetGuid();await Call("session.set_window_label",new(){["sessionId"]=sid,["windowId"]=id.ToString("D"),["label"]=w.Label});}else{id=(await Call("session.new_window",new(){["sessionId"]=sid,["label"]=w.Label})).GetProperty("id").GetGuid();}windows.Add(w.Id,id);}
            foreach(var b in source.Blocks)
            {
                string wid=windows[b.WindowId].ToString("D");var born=await Call("block.create",new(){["sessionId"]=sid,["block"]=new JsonObject{["transport"]="pty",["label"]=b.Label,["windowId"]=wid,["dimensions"]=new JsonObject{["columns"]=b.Dimensions.Columns,["rows"]=b.Dimensions.Rows,["widthPixels"]=0,["heightPixels"]=0}}});
                var id=born.GetProperty("id").GetGuid();blocks.Add(b.Id,id);
                if(b.Placement==MultiplexerPaneKind.Tiled)continue;
                bool independent=b.Placement==MultiplexerPaneKind.Floating&&b.FloatingLayerId is null;
                var fields=new JsonObject{["sessionId"]=sid,["windowId"]=wid,["blockId"]=id.ToString("D"),["kind"]=independent?"floating":"unplaced"};
                if(independent)fields["bounds"]=Bounds(b.FloatingBounds!);await Call("block.place",fields);
            }
            foreach(var w in source.Windows)
            {
                string wid=windows[w.Id].ToString("D");if(w.Layout is {} tree)await Call("session.set_layout",new(){["sessionId"]=sid,["windowId"]=wid,["layout"]=Remap(tree,blocks)});
                foreach(var layer in w.FloatingLayers??[]){var result=await Call("session.group_layer",new(){["sessionId"]=sid,["windowId"]=wid,["layout"]=Remap(layer.Layout,blocks),["bounds"]=Bounds(layer.Bounds)});frames.Add(layer.Id,result.GetProperty("id").GetGuid());}
                var order=MultiplexerLayers.Ordered(source,w);var creationOrder=source.Blocks.Where(b=>b.WindowId==w.Id&&b.Placement==MultiplexerPaneKind.Floating).Select(b=>b.Id);
                if(w.FloatingLayers is {Count:>0}||!order.SequenceEqual(creationOrder)){var ids=new JsonArray();foreach(var id in order)ids.Add(blocks[id].ToString("D"));await Call("session.set_layer_order",new(){["sessionId"]=sid,["windowId"]=wid,["blocks"]=ids});}
                if(w.ZoomedBlockId is Guid z)await Call("session.zoom_block",new(){["sessionId"]=sid,["blockId"]=blocks[z].ToString("D"),["zoom"]=true});
            }
            await output.WriteLineAsync(new JsonObject{["sourceSession"]=source.Id.ToString("D"),["sessionId"]=sid,["windows"]=Map(windows),["blocks"]=Map(blocks),["layers"]=Map(frames),["commandsReplayed"]=false}.ToJsonString());return 0;
            async ValueTask<JsonElement> Call(string method,JsonObject fields){using var doc=JsonDocument.Parse(fields.ToJsonString());return await api.CallAsync(method,doc.RootElement,ct);}
        }
        catch(Exception ex)when(ex is IOException or JsonException or InvalidDataException or ArgumentException or FormatException or InvalidOperationException or HttpRequestException or UnauthorizedAccessException or OperationCanceledException or KeyNotFoundException or TimeoutException)
        {await error.WriteLineAsync(ex.Message);if(created.HasValue)await error.WriteLineAsync($"Partial new session: {created:D}. No automatic deletion or retry. Inspect this session before closing it.");else if(creating)await error.WriteLineAsync("Session creation was not confirmed. Check the host for a new session before retrying.");return ct.IsCancellationRequested?130:1;}
    }
    internal static void Validate(MultiplexerSessionDescriptor s)
    {
        if(s.Id==Guid.Empty||s.Windows is null||s.Windows.Count is <1 or >128||s.Blocks is null||s.Blocks.Count>128)throw Bad();
        HashSet<Guid> ids=[s.Id],homes=[],tiled=[],grouped=[];Dictionary<Guid,MultiplexerBlockDescriptor> blocks=[];
        foreach(var w in s.Windows){if(w is null||w.Id==Guid.Empty||!ids.Add(w.Id))throw Bad();homes.Add(w.Id);Label(w.Label);}
        foreach(var b in s.Blocks){if(b is null||b.Id==Guid.Empty||!ids.Add(b.Id)||!homes.Contains(b.WindowId)||b.SessionId!=s.Id||!Enum.IsDefined(b.Placement)||b.Dimensions.Columns is <2 or >512||b.Dimensions.Rows is <1 or >256)throw Bad();Label(b.Label);blocks.Add(b.Id,b);
            if(b.Placement==MultiplexerPaneKind.Floating){if(b.FloatingBounds is not {} r||r.Width is <1 or >10000||r.Height is <1 or >10000||r.X<0||r.Y<0||r.X>10000-r.Width||r.Y>10000-r.Height)throw Bad();}else if(b.FloatingBounds!=null||b.FloatingLayerId.HasValue)throw Bad();}
        foreach(var w in s.Windows){var order=MultiplexerLayers.Ordered(s,w).ToList();if(w.Layout!=null)Walk(w.Layout,w.Id,0);
            if(w.ZoomedBlockId is Guid z&&(!blocks.TryGetValue(z,out var target)||target.WindowId!=w.Id||target.Placement==MultiplexerPaneKind.Unplaced))throw Bad();
            foreach(var g in w.FloatingLayers??[]){if(g==null||g.Id==Guid.Empty||!ids.Add(g.Id)||g.Layout==null||g.Bounds==null)throw Bad();IReadOnlyDictionary<Guid,MultiplexerFloatBounds> geometry;
                try{geometry=FloatTreeGeometry.Project(g.Layout,g.Bounds);}catch(ArgumentException){throw Bad();}
                int index=order.IndexOf(geometry.Keys.First());foreach(var (id,rect) in geometry){if(index<0||index>=order.Count||order[index++]!=id||!grouped.Add(id)||!blocks.TryGetValue(id,out var b)||b.WindowId!=w.Id||b.FloatingLayerId!=g.Id||b.FloatingBounds!=rect)throw Bad();}}
        }
        if(tiled.Count!=s.Blocks.Count(b=>b.Placement==MultiplexerPaneKind.Tiled)||grouped.Count!=s.Blocks.Count(b=>b.FloatingLayerId.HasValue))throw Bad();
        void Walk(MultiplexerLayoutNode n,Guid home,int depth){if(depth>63)throw Bad();if(n.BlockId is Guid id){if(n.First!=null||n.Second!=null||n.Orientation.HasValue||!blocks.TryGetValue(id,out var b)||b.WindowId!=home||b.Placement!=MultiplexerPaneKind.Tiled||!tiled.Add(id))throw Bad();return;}if(n.Orientation is not (MultiplexerSplitOrientation.Horizontal or MultiplexerSplitOrientation.Vertical)||!(n.Ratio>0&&n.Ratio<1)||n.First==null||n.Second==null)throw Bad();Walk(n.First,home,depth+1);Walk(n.Second,home,depth+1);}
    }
    private static JsonObject Bounds(MultiplexerFloatBounds r)=>new(){["x"]=r.X,["y"]=r.Y,["width"]=r.Width,["height"]=r.Height};
    private static JsonObject Remap(MultiplexerLayoutNode n,Dictionary<Guid,Guid> ids)=>n.BlockId is Guid id?new(){["blockId"]=ids[id].ToString("D")}:new(){["orientation"]=n.Orientation.ToString(),["ratio"]=n.Ratio,["first"]=Remap(n.First!,ids),["second"]=Remap(n.Second!,ids)};
    private static JsonObject Map(Dictionary<Guid,Guid> ids){var obj=new JsonObject();foreach(var (a,b) in ids)obj.Add(a.ToString("D"),b.ToString("D"));return obj;}
    private static void Label(string? s){if(string.IsNullOrWhiteSpace(s)||s.Length>256||s.Any(char.IsControl))throw Bad();}
    private static InvalidDataException Bad()=>new("Archive contains an invalid label, placement, geometry or split tree. No workspace has been created.");
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true)]
internal partial class LayoutRestoreJson:JsonSerializerContext;
