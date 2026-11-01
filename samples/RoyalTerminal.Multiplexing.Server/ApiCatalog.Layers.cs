// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text;
using System.Text.Json;
namespace RoyalTerminal.Multiplexing.Server;
internal sealed partial class ApiCatalog
{
    private void RegisterLayerTools()
    {
        foreach (var raise in new[] { true, false })
            Define(raise ? "session.raise_layer" : "session.lower_layer", "Move a floating terminal one level, or to the edge with toEdge. Does not affect focus or keyboard ownership.",
                ["sessionId:string!", "blockId:string!", "toEdge:boolean", "expectedRevision:integer"], ServerJsonContext.Default.MultiplexerSessionDescriptor, (a,g,ct) =>
                {
                    var s = Mutable(a,g,ct);
                    var edge = a.Value.TryGetProperty("toEdge",out var p) && (p.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new ArgumentException("toEdge must be boolean.") });
                    return Done(_workspaces.Broker.StackLayer(s.Id,a.Id("blockId"),raise,edge,s.Revision),ServerJsonContext.Default.MultiplexerSessionDescriptor);
                });
        Define("session.set_layer_order", "Atomically replace a window's complete bottom-to-top floating order.",
            ["sessionId:string!", "windowId:string!", "blocks:array!", "expectedRevision:integer"], ServerJsonContext.Default.MultiplexerSessionDescriptor, (a,g,ct) =>
            {
                var s=Mutable(a,g,ct);
                if(!a.Value.TryGetProperty("blocks",out var p)||p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()>s.Blocks.Count)throw new ArgumentException("Expected floating block IDs.");
                var ids=p.EnumerateArray().Select(x=>x.ValueKind==JsonValueKind.String&&x.TryGetGuid(out var id)?id:throw new ArgumentException("Expected a block UUID.")).ToArray();
                return Done(_workspaces.Broker.SetLayerOrder(s.Id,a.Id("windowId"),ids,s.Revision),ServerJsonContext.Default.MultiplexerSessionDescriptor);
            });
        Define("session.new_block", "Launch one unplaced terminal. The tiled tree never includes it, even while starting.",
            ["sessionId:string!","windowId:string","layout:object!","expectedRevision:integer"], ServerJsonContext.Default.MultiplexerBlockDescriptor,
            (a,g,t) => LaunchPlacedAsync(a,g,t,MultiplexerPaneKind.Unplaced));
        Define("session.new_layer", "Launch one floating terminal above the existing layers, without disturbing the tiled tree.",
            ["sessionId:string!","windowId:string!","layout:object!","bounds:object","expectedRevision:integer"], ServerJsonContext.Default.MultiplexerBlockDescriptor,
            (a,g,t) => LaunchPlacedAsync(a,g,t,MultiplexerPaneKind.Floating));
        Define("session.close_layer", "Stop and remove the selected single-terminal floating layer. Tiled and shelved terminals cannot be closed by this operation.",
            ["sessionId:string!","blockId:string!","expectedRevision:integer"],ServerJsonContext.Default.ApiSuccess,async(a,g,t)=>
            {
                var session=Mutable(a,g,t);
                await _workspaces.CloseFloatingLayerAsync(session.Id,a.Id("blockId"),session.Revision);
                return Success();
            });
    }
    private async ValueTask<JsonElement> LaunchPlacedAsync(ApiArguments a,AccessGrant grant,CancellationToken ct,MultiplexerPaneKind kind)
    {
        var s=Mutable(a,grant,ct);
        if(LayoutTemplate.Parse(s.Id,a.Object("layout")) is not LayoutTemplate.Leaf leaf)
            throw new ArgumentException("This operation accepts a single terminal leaf, not a split tree.");
        Guid? window=kind==MultiplexerPaneKind.Floating?a.Id("windowId"):a.OptionalId("windowId");
        MultiplexerFloatBounds? bounds=null;
        if(a.Value.TryGetProperty("bounds",out _))
        {
            var r=new ApiArguments(a.Object("bounds"));r.Validate("x","y","width","height");
            bounds=new(r.Integer("x",-1),r.Integer("y",-1),r.Integer("width",-1),r.Integer("height",-1));
        }
        var b=await _workspaces.CreateBlockAsync(s.Id,leaf.Block with { WindowId=window,ExpectedRevision=s.Revision },ct,kind,bounds);
        if(!string.IsNullOrEmpty(leaf.InitialInput))
        {
            try{ct.ThrowIfCancellationRequested();_workspaces.Input(s.Id,b.Id,Convert.ToBase64String(Encoding.UTF8.GetBytes(leaf.InitialInput)));}
            catch{try{await _workspaces.CloseBlockAsync(s.Id,b.Id);}catch(MultiplexerException e)when(e.Code==MultiplexerErrorCode.NotFound){}throw;}
        }
        return Element(b,ServerJsonContext.Default.MultiplexerBlockDescriptor);
    }
}
