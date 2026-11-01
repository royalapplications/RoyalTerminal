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
        RegisterReader("session.list_blocks","All session blocks in creation order, with home window and placement, including unplaced and exited terminals.",["sessionId:string!"],SessionToolsWire.Default.SessionBlockInventory,(a,g,_)=>
            Done(new SessionBlockInventory(_workspaces.Broker.GetSession(Session(a,g)).Blocks),SessionToolsWire.Default.SessionBlockInventory));
        RegisterReader("session.list_clients","Attachments to this one session, never all sessions of an administrator.",["sessionId:string!"],ServerJsonContext.Default.ClientList,(a,g,_)=>
        {
            Guid id=Session(a,g);_workspaces.Broker.GetSession(id);
            return Done(new ClientList(_clients.List(g).Clients.Where(c=>c.SessionId==id).OrderBy(c=>c.ConnectedUtc).ThenBy(c=>c.Id).ToArray()),ServerJsonContext.Default.ClientList);
        });
        RegisterReader("session.describe_block","Inspect placement and discover methods supported by this block type; discovery does not grant permission to invoke.",["sessionId:string!","blockId:string!"],SessionToolsWire.Default.SessionBlockDetails,(a,g,_)=>
            Done(new SessionBlockDetails(_workspaces.GetBlock(Session(a,g),a.Id("blockId")),TerminalCreator,_blockMethods.Keys.Order(StringComparer.Ordinal).Select(n=>TerminalCreator+"."+n).ToArray()),SessionToolsWire.Default.SessionBlockDetails));
        var output=ServerJsonContext.Default.MultiplexerSessionDescriptor;
        Define("session.move_block","Move beside a tiled target without restarting either process. direction is left, right, up or down; ratio is the fraction for the moving block.",["sessionId:string!","blockId:string!","targetId:string!","direction:string!","ratio:number","expectedRevision:integer"],output,(a,g,t)=>
        {
            var s=Mutable(a,g,t);var target=Block(s,a.Id("targetId"));var (axis,before)=a.String("direction") switch
            {"left"=>(MultiplexerSplitOrientation.Horizontal,true),"right"=>(MultiplexerSplitOrientation.Horizontal,false),"up"=>(MultiplexerSplitOrientation.Vertical,true),"down"=>(MultiplexerSplitOrientation.Vertical,false),_=>throw new ArgumentException("Expected left, right, up or down.")};
            double share=Fraction(a);return Done(_workspaces.Broker.PlaceBlock(s.Id,a.Id("blockId"),target.WindowId,MultiplexerPaneKind.Tiled,target.Id,axis,before?share:1-share,before,expectedRevision:s.Revision),output);
        });
        Define("session.swap_blocks","Swap the complete positions of two blocks inside one session.",["sessionId:string!","firstId:string!","secondId:string!","expectedRevision:integer"],output,(a,g,t)=>
        {var s=Mutable(a,g,t);return Done(_workspaces.Broker.SwapBlocks(s.Id,a.Id("firstId"),a.Id("secondId"),s.Revision),output);});
        Define("session.resize_pane","Move the nearest divider on the requested axis. ratio is the normalized fraction allocated to the subtree containing blockId.",["sessionId:string!","blockId:string!","orientation:string!","ratio:number!","expectedRevision:integer"],ServerJsonContext.Default.MultiplexerWindowDescriptor,(a,g,t)=>
        {
            var s=Mutable(a,g,t);var b=Block(s,a.Id("blockId"));if(b.Placement!=MultiplexerPaneKind.Tiled)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The selected block is not tiled.");
            var current=s.Windows.Single(w=>w.Id==b.WindowId);
            if(!a.Value.TryGetProperty("ratio",out _))throw new ArgumentException("ratio is required.");
            var adjusted=ResizeNearest(current.Layout!,b.Id,LayoutTemplate.ParseAxis(a.String("orientation")),Fraction(a));
            if(!adjusted.Changed)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"No matching divider for this block.");
            return Done(_workspaces.Broker.SetWindowLayout(s.Id,current.Id,adjusted.Tree,s.Revision),ServerJsonContext.Default.MultiplexerWindowDescriptor);
        });
        Define("session.resize_layer","Resize an existing floating terminal. Bounds are integer ten-thousandths of the owning window.",["sessionId:string!","blockId:string!","bounds:object!","expectedRevision:integer"],output,(a,g,t)=>
        {
            var s=Mutable(a,g,t);var b=Block(s,a.Id("blockId"));if(b.Placement!=MultiplexerPaneKind.Floating)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The selected block is not floating.");
            var r=new ApiArguments(a.Object("bounds"));r.Validate("x","y","width","height");
            return Done(_workspaces.Broker.PlaceBlock(s.Id,b.Id,b.WindowId,MultiplexerPaneKind.Floating,bounds:new(r.Integer("x",-1),r.Integer("y",-1),r.Integer("width",-1),r.Integer("height",-1)),expectedRevision:s.Revision),output);
        });
    }
    private MultiplexerSessionDescriptor Mutable(ApiArguments a,AccessGrant g,CancellationToken t)
    {
        ServerHttp.RequireAdministrator(g);t.ThrowIfCancellationRequested();var s=_workspaces.Broker.GetSession(Session(a,g));
        if(a.Revision() is long r && r!=s.Revision)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The session layout changed.");return s;
    }
    private static MultiplexerBlockDescriptor Block(MultiplexerSessionDescriptor s,Guid id)=>s.Blocks.FirstOrDefault(b=>b.Id==id)??throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Block not found in this session.");
    private static double Fraction(ApiArguments a){double n=a.Number("ratio",.5);return n is >0 and <1?n:throw new ArgumentException("ratio must be strictly between 0 and 1.");}
    private static (MultiplexerLayoutNode Tree,bool Contains,bool Changed) ResizeNearest(MultiplexerLayoutNode n,Guid id,MultiplexerSplitOrientation axis,double share)
    {
        if(n.BlockId.HasValue)return(n,n.BlockId.Value==id,false);
        var left=ResizeNearest(n.First!,id,axis,share);
        if(left.Changed)return(n with {First=left.Tree},true,true);
        if(left.Contains)return n.Orientation==axis?(n with {Ratio=share},true,true):(n,true,false);
        var right=ResizeNearest(n.Second!,id,axis,share);
        if(right.Changed)return(n with {Second=right.Tree},true,true);
        return right.Contains&&n.Orientation==axis?(n with {Ratio=1-share},true,true):(n,right.Contains,false);
    }
}
