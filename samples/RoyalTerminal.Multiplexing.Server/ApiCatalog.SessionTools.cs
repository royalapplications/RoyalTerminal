// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RoyalTerminal.Multiplexing.Server;

internal sealed record SessionBlockInventory(IReadOnlyList<MultiplexerBlockDescriptor> Blocks);
internal sealed record SessionBlockDetails(MultiplexerBlockDescriptor Block, string Creator, IReadOnlyList<string> Methods);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true)]
[JsonSerializable(typeof(SessionBlockInventory)),JsonSerializable(typeof(SessionBlockDetails))]
internal partial class SessionToolsWire : JsonSerializerContext;

internal sealed partial class ApiCatalog
{
    private void RegisterSessionTools()
    {
        RegisterReader("session.help","Discover this host's registered session methods, with input and result schemas.",["sessionId:string!"],ServerJsonContext.Default.MethodList,(a,g,t)=>
        {
            _=_workspaces.Broker.GetSession(Session(a,g));t.ThrowIfCancellationRequested();
            return Done(new MethodList(Describe().Methods.Where(m=>m.Name.StartsWith("session.",StringComparison.Ordinal)).OrderBy(m=>m.Name,StringComparer.Ordinal).ToArray()),ServerJsonContext.Default.MethodList);
        });
        RegisterReader("session.list_blocks","All session blocks in creation order, including unplaced and exited terminals.",["sessionId:string!"],SessionToolsWire.Default.SessionBlockInventory,(a,g,_)=>Done(new SessionBlockInventory(_workspaces.Broker.GetSession(Session(a,g)).Blocks),SessionToolsWire.Default.SessionBlockInventory));
        RegisterReader("session.list_clients","Attachments to this one session.",["sessionId:string!"],ServerJsonContext.Default.ClientList,(a,g,_)=>
        {Guid id=Session(a,g);_workspaces.Broker.GetSession(id);return Done(new ClientList(_clients.List(g).Clients.Where(c=>c.SessionId==id).OrderBy(c=>c.ConnectedUtc).ThenBy(c=>c.Id).ToArray()),ServerJsonContext.Default.ClientList);});
        RegisterReader("session.describe_block","Inspect placement and supported methods.",["sessionId:string!","blockId:string!"],SessionToolsWire.Default.SessionBlockDetails,(a,g,_)=>Done(new SessionBlockDetails(_workspaces.GetBlock(Session(a,g),a.Id("blockId")),TerminalCreator,_blockMethods.Keys.Order(StringComparer.Ordinal).Select(n=>TerminalCreator+"."+n).ToArray()),SessionToolsWire.Default.SessionBlockDetails));
        var output=ServerJsonContext.Default.MultiplexerSessionDescriptor;
        Define("session.move_block","Move beside a tiled target without restarting either process.",["sessionId:string!","blockId:string!","targetId:string!","direction:string!","ratio:number","expectedRevision:integer"],output,(a,g,t)=>
        {
            var s=Mutable(a,g,t);var target=Block(s,a.Id("targetId"));var (axis,before)=a.String("direction") switch
            {"left"=>(MultiplexerSplitOrientation.Horizontal,true),"right"=>(MultiplexerSplitOrientation.Horizontal,false),"up"=>(MultiplexerSplitOrientation.Vertical,true),"down"=>(MultiplexerSplitOrientation.Vertical,false),_=>throw new ArgumentException("Expected left, right, up or down.")};
            double share=Fraction(a);return Done(_workspaces.Broker.PlaceBlock(s.Id,a.Id("blockId"),target.WindowId,MultiplexerPaneKind.Tiled,target.Id,axis,before?share:1-share,before,expectedRevision:s.Revision),output);
        });
        Define("session.swap_blocks","Swap the complete positions of two blocks inside one session.",["sessionId:string!","firstId:string!","secondId:string!","expectedRevision:integer"],output,(a,g,t)=>
        {var s=Mutable(a,g,t);return Done(_workspaces.Broker.SwapBlocks(s.Id,a.Id("firstId"),a.Id("secondId"),s.Revision),output);});
        Define("session.resize_pane","Move the nearest divider on the requested axis in the selected tile or floating frame.",["sessionId:string!","blockId:string!","orientation:string!","ratio:number!","expectedRevision:integer"],ServerJsonContext.Default.MultiplexerWindowDescriptor,(a,g,t)=>
        {
            var s=Mutable(a,g,t);var b=Block(s,a.Id("blockId"));var current=s.Windows.Single(w=>w.Id==b.WindowId);
            var tree=b.FloatingLayerId is Guid key?current.FloatingLayers!.Single(l=>l.Id==key).Layout:b.Placement==MultiplexerPaneKind.Tiled?current.Layout:null;
            if(tree is null)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The selected block has no divider.");
            if(!a.Value.TryGetProperty("ratio",out _))throw new ArgumentException("ratio is required.");
            var adjusted=ResizeNearest(tree,b.Id,LayoutTemplate.ParseAxis(a.String("orientation")),Fraction(a));
            if(!adjusted.Changed)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"No matching divider for this block.");
            var result=b.FloatingLayerId is Guid layer?_workspaces.Broker.EditFloating(s.Id,layer,adjusted.Tree,expectedRevision:s.Revision).Windows.Single(w=>w.Id==current.Id):_workspaces.Broker.SetWindowLayout(s.Id,current.Id,adjusted.Tree,s.Revision);
            return Done(result,ServerJsonContext.Default.MultiplexerWindowDescriptor);
        });
        Define("session.resize_layer","Set the outer bounds of an entire floating frame.",["sessionId:string!","blockId:string!","bounds:object!","expectedRevision:integer"],output,(a,g,t)=>
        {
            var s=Mutable(a,g,t);var key=a.Id("blockId");var r=FrameBounds(a);var b=s.Blocks.FirstOrDefault(b=>b.Id==key);
            if(b is null || b.FloatingLayerId.HasValue)return Done(_workspaces.Broker.EditFloating(s.Id,key,bounds:r,expectedRevision:s.Revision),output);
            if(b.Placement!=MultiplexerPaneKind.Floating)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The selected block is not floating.");
            return Done(_workspaces.Broker.PlaceBlock(s.Id,b.Id,b.WindowId,MultiplexerPaneKind.Floating,bounds:r,expectedRevision:s.Revision),output);
        });
    }
    private MultiplexerSessionDescriptor Mutable(ApiArguments a,AccessGrant g,CancellationToken t)
    {ServerHttp.RequireAdministrator(g);t.ThrowIfCancellationRequested();var s=_workspaces.Broker.GetSession(Session(a,g));if(a.Revision() is long r&&r!=s.Revision)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The session layout changed.");return s;}
    private static MultiplexerBlockDescriptor Block(MultiplexerSessionDescriptor s,Guid id)=>s.Blocks.FirstOrDefault(b=>b.Id==id)??throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Block not found in this session.");
    private static double Fraction(ApiArguments a){double n=a.Number("ratio",.5);return n is >0 and <1?n:throw new ArgumentException("ratio must be strictly between 0 and 1.");}
    private static (MultiplexerLayoutNode Tree,bool Contains,bool Changed) ResizeNearest(MultiplexerLayoutNode n,Guid id,MultiplexerSplitOrientation axis,double share)
    {
        if(n.BlockId.HasValue)return(n,n.BlockId.Value==id,false);var left=ResizeNearest(n.First!,id,axis,share);
        if(left.Changed)return(n with{First=left.Tree},true,true);
        if(left.Contains)return n.Orientation==axis?(n with{Ratio=share},true,true):(n,true,false);
        var right=ResizeNearest(n.Second!,id,axis,share);if(right.Changed)return(n with{Second=right.Tree},true,true);
        return right.Contains&&n.Orientation==axis?(n with{Ratio=1-share},true,true):(n,right.Contains,false);
    }
}
