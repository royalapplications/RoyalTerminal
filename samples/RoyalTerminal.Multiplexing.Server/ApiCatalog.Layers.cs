// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal;
namespace RoyalTerminal.Multiplexing.Server;

// Top-level fields retain the original single-block HTTP response. The id is the first terminal,
// not the frame: use layerId for the stable handle even after the first terminal is moved out.
internal sealed record LayerLaunch(Guid Id,Guid SessionId,Guid WindowId,string Label,MultiplexerBlockState State,TerminalSessionDimensions Dimensions,int? ExitCode,long LastSequence,long FirstRetainedSequence,int AttachmentCount,Guid? ControllerId,MultiplexerPaneKind Placement,MultiplexerFloatBounds? FloatingBounds,Guid LayerId,Guid[] BlockIds)
{
    internal static LayerLaunch From(MultiplexerBlockDescriptor b,Guid layer,Guid[] ids)=>new(b.Id,b.SessionId,b.WindowId,b.Label,b.State,b.Dimensions,b.ExitCode,b.LastSequence,b.FirstRetainedSequence,b.AttachmentCount,b.ControllerId,b.Placement,b.FloatingBounds,layer,ids);
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true)]
[JsonSerializable(typeof(LayerLaunch)),JsonSerializable(typeof(MultiplexerLayoutNode)),JsonSerializable(typeof(MultiplexerFloatingLayer))]
internal partial class FloatWire:JsonSerializerContext;

internal sealed partial class ApiCatalog
{
    private void RegisterLayerTools()
    {
        foreach (var raise in new[] { true,false })
            Define(raise?"session.raise_layer":"session.lower_layer","Move a whole floating frame one level or to the edge.", ["sessionId:string!","blockId:string!","toEdge:boolean","expectedRevision:integer"],ServerJsonContext.Default.MultiplexerSessionDescriptor,(a,g,t)=>
            {
                var s=Mutable(a,g,t);return Done(_workspaces.Broker.StackLayer(s.Id,a.Id("blockId"),raise,Flag(a,"toEdge")??false,s.Revision),ServerJsonContext.Default.MultiplexerSessionDescriptor);
            });
        Define("session.set_layer_order","Atomically reorder complete floating frames.",["sessionId:string!","windowId:string!","blocks:array!","expectedRevision:integer"],ServerJsonContext.Default.MultiplexerSessionDescriptor,(a,g,t)=>
        {
            var s=Mutable(a,g,t);if(!a.Value.TryGetProperty("blocks",out var p)||p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()>s.Blocks.Count)throw new ArgumentException("Expected floating IDs.");
            var ids=p.EnumerateArray().Select(x=>x.ValueKind==JsonValueKind.String&&x.TryGetGuid(out var id)?id:throw new ArgumentException("Expected a UUID.")).ToArray();
            return Done(_workspaces.Broker.SetLayerOrder(s.Id,a.Id("windowId"),ids,s.Revision),ServerJsonContext.Default.MultiplexerSessionDescriptor);
        });
        Define("session.zoom_block","Set shared window zoom to true or false; omit zoom to toggle. Does not resize a PTY or acquire control.",["sessionId:string!","blockId:string!","zoom:boolean","expectedRevision:integer"],ServerJsonContext.Default.MultiplexerWindowDescriptor,(a,g,t)=>
        {
            var s=Mutable(a,g,t);return Done(_workspaces.Broker.ZoomBlock(s.Id,a.Id("blockId"),Flag(a,"zoom"),s.Revision),ServerJsonContext.Default.MultiplexerWindowDescriptor);
        });
        Define("session.new_block","Launch an unplaced terminal leaf.",["sessionId:string!","windowId:string","layout:object!","expectedRevision:integer"],ServerJsonContext.Default.MultiplexerBlockDescriptor,async(a,g,t)=>
        {
            var s=Mutable(a,g,t);if(LayoutTemplate.Parse(s.Id,a.Object("layout")) is not LayoutTemplate.Leaf leaf)throw new ArgumentException("Expected one unplaced leaf.");
            var b=await _workspaces.CreateBlockAsync(s.Id,leaf.Block with{WindowId=a.OptionalId("windowId"),ExpectedRevision=s.Revision},t,MultiplexerPaneKind.Unplaced);
            try{SendInitial(s.Id,b.Id,leaf,t);}catch{await _workspaces.CloseBlockAsync(s.Id,b.Id);throw;}return Element(b,ServerJsonContext.Default.MultiplexerBlockDescriptor);
        });
        Define("session.new_layer","Launch a shell leaf or nested binary layout in one floating frame.",["sessionId:string!","windowId:string!","layout:object!","bounds:object","expectedRevision:integer"],FloatWire.Default.LayerLaunch,LaunchFrameAsync);
        Define("session.close_layer","Stop all members of a floating frame. A detached or retiled terminal is not part of its old frame.",["sessionId:string!","blockId:string!","expectedRevision:integer"],ServerJsonContext.Default.ApiSuccess,async(a,g,t)=>
        {var s=Mutable(a,g,t);await _workspaces.CloseFloatingLayerAsync(s.Id,a.Id("blockId"),s.Revision);return Success();});
        Define("session.group_layer","Publish existing shelved terminals of one window as one frame (without restarting them).",["sessionId:string!","windowId:string!","layout:object!","bounds:object!","expectedRevision:integer"],FloatWire.Default.MultiplexerFloatingLayer,(a,g,t)=>
        {var s=Mutable(a,g,t);return Done(_workspaces.Broker.GroupFloating(s.Id,a.Id("windowId"),ReadTree(a),FrameBounds(a)),FloatWire.Default.MultiplexerFloatingLayer);});
        Define("session.set_layer_layout","Replace the divider tree of a frame, preserving all members.",["sessionId:string!","layerId:string!","layout:object!","expectedRevision:integer"],ServerJsonContext.Default.MultiplexerSessionDescriptor,(a,g,t)=>
        {var s=Mutable(a,g,t);return Done(_workspaces.Broker.EditFloating(s.Id,a.Id("layerId"),ReadTree(a),expectedRevision:s.Revision),ServerJsonContext.Default.MultiplexerSessionDescriptor);});
    }
    private static bool? Flag(ApiArguments a,string key)=>!a.Value.TryGetProperty(key,out var value)?null:value.ValueKind switch{JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw new ArgumentException($"{key} must be boolean.")};
    private static MultiplexerLayoutNode ReadTree(ApiArguments a)=>a.Object("layout").Deserialize(FloatWire.Default.MultiplexerLayoutNode)??throw new ArgumentException("Missing layout.");
    private static MultiplexerFloatBounds FrameBounds(ApiArguments a)
    {
        if(!a.Value.TryGetProperty("bounds",out _))return MultiplexerFloatBounds.Default;
        var b=new ApiArguments(a.Object("bounds"));b.Validate("x","y","width","height");return new(b.Integer("x",-1),b.Integer("y",-1),b.Integer("width",-1),b.Integer("height",-1));
    }
    private void SendInitial(Guid sid,Guid id,LayoutTemplate.Leaf leaf,CancellationToken ct)
    {if(!string.IsNullOrEmpty(leaf.InitialInput)){ct.ThrowIfCancellationRequested();_workspaces.Input(sid,id,Convert.ToBase64String(Encoding.UTF8.GetBytes(leaf.InitialInput)));}}
    private async ValueTask<JsonElement> LaunchFrameAsync(ApiArguments a,AccessGrant grant,CancellationToken ct)
    {
        var s=Mutable(a,grant,ct);Guid window=a.Id("windowId");var plan=LayoutTemplate.Parse(s.Id,a.Object("layout"));var bounds=FrameBounds(a);
        List<LayoutTemplate.Leaf> leaves=[];
        MultiplexerLayoutNode Map(LayoutTemplate node,Func<LayoutTemplate.Leaf,Guid> lookup)=>node switch{
            LayoutTemplate.Leaf l=>MultiplexerLayoutNode.Leaf(lookup(l)),LayoutTemplate.Split d=>MultiplexerLayoutNode.Split(d.Orientation,Map(d.First,lookup),Map(d.Second,lookup),d.Ratio),_=>throw new InvalidOperationException()};
        // Both template validation and integer geometry validation precede any command execution.
        FloatTreeGeometry.Project(Map(plan,l=>{leaves.Add(l);return Guid.NewGuid();}),bounds);
        List<Guid> owned=[];long revision=s.Revision;
        try
        {
            bool single=leaves.Count==1;
            foreach(var l in leaves){ct.ThrowIfCancellationRequested();var b=await _workspaces.CreateBlockAsync(s.Id,l.Block with{WindowId=window,ExpectedRevision=revision++},ct,single?MultiplexerPaneKind.Floating:MultiplexerPaneKind.Unplaced,single?bounds:null);owned.Add(b.Id);}
            int index=0;ct.ThrowIfCancellationRequested();Guid layer=single?owned[0]:_workspaces.Broker.GroupFloating(s.Id,window,Map(plan,_=>owned[index++]),bounds,revision).Id;
            for(int i=0;i<leaves.Count;i++)SendInitial(s.Id,owned[i],leaves[i],ct);
            return Element(LayerLaunch.From(_workspaces.GetBlock(s.Id,owned[0]),layer,[..owned]),FloatWire.Default.LayerLaunch);
        }
        catch(Exception error) when(error is not OutOfMemoryException)
        {
            List<Exception> failures=[];foreach(var id in owned){try{await _workspaces.CloseBlockAsync(s.Id,id);}catch(MultiplexerException ex)when(ex.Code==MultiplexerErrorCode.NotFound){}catch(Exception ex){failures.Add(ex);}}
            if(failures.Count!=0)throw new AggregateException("Failed to launch and fully clean up a floating layout.",new[]{error}.Concat(failures));throw;
        }
    }
}
