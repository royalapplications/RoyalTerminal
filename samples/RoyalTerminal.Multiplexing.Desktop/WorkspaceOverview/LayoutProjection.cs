// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json.Serialization;
using Avalonia;
namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
internal readonly record struct PaneSlot(MultiplexerBlockDescriptor Block, Rect Area, int Layer);
internal sealed record LayoutProjection(IReadOnlyList<PaneSlot> Slots,IReadOnlyList<MultiplexerBlockDescriptor> Shelf)
{
    // Reserved z-order value understood by OverviewPanel. Canonical, unzoomed coordinates remain in every slot.
    internal const int SharedZoomLayer=int.MaxValue;
    internal static LayoutProjection Build(MultiplexerSessionDescriptor session,Guid windowId)
    {
        if(session.Blocks.Count>128 || session.Windows.Count is <1 or >64)throw Invalid();
        var w=session.Windows.Single(x=>x.Id==windowId);var blocks=session.Blocks.Where(x=>x.WindowId==w.Id).ToDictionary(x=>x.Id);
        var order=MultiplexerLayers.Ordered(session,w).Select((id,i)=>(id,i)).ToDictionary(x=>x.id,x=>x.i+1);
        List<PaneSlot> slots=[]; List<MultiplexerBlockDescriptor> shelf=[]; HashSet<Guid> seen=[];
        if(w.Layout is not null)Visit(w.Layout,new Rect(0,0,1,1),0);
        foreach(var b in blocks.Values)
        {
            if(b.SessionId!=session.Id || b.Id==Guid.Empty)throw Invalid();
            switch(b.Placement)
            {
                case MultiplexerPaneKind.Tiled when seen.Contains(b.Id):break;
                case MultiplexerPaneKind.Unplaced:shelf.Add(b);break;
                case MultiplexerPaneKind.Floating when b.FloatingBounds is {} r:
                    if(r.Width is <1 or >10000||r.Height is <1 or >10000||r.X<0||r.Y<0||r.X>10000-r.Width||r.Y>10000-r.Height)throw Invalid();
                    slots.Add(new(b,new(r.X/10000d,r.Y/10000d,r.Width/10000d,r.Height/10000d),order[b.Id])); break;
                default:throw Invalid();
            }
        }
        if(w.ZoomedBlockId is Guid id){int i=slots.FindIndex(x=>x.Block.Id==id);if(i<0)throw Invalid();slots[i]=slots[i] with{Layer=SharedZoomLayer};}
        return new(slots,shelf);
        void Visit(MultiplexerLayoutNode n,Rect area,int depth)
        {
            if(depth>63)throw Invalid();if(n.BlockId is Guid leaf){if(n.Orientation.HasValue||n.First!=null||n.Second!=null||!seen.Add(leaf)||!blocks.TryGetValue(leaf,out var b)||b.Placement!=MultiplexerPaneKind.Tiled)throw Invalid();slots.Add(new(b,area,0));return;}
            if(n.First is null||n.Second is null||!(n.Ratio>0 && n.Ratio<1))throw Invalid();
            if(n.Orientation==MultiplexerSplitOrientation.Horizontal){double width=area.Width*n.Ratio;Visit(n.First,new(area.X,area.Y,width,area.Height),depth+1);Visit(n.Second,new(area.X+width,area.Y,area.Width-width,area.Height),depth+1);}
            else if(n.Orientation==MultiplexerSplitOrientation.Vertical){double height=area.Height*n.Ratio;Visit(n.First,new(area.X,area.Y,area.Width,height),depth+1);Visit(n.Second,new(area.X,area.Y+height,area.Width,area.Height-height),depth+1);}else throw Invalid();
        }
    }
    private static InvalidDataException Invalid()=>new("Inconsistent workspace layout.");
}
internal sealed record RevisionPoll(Guid SessionId,long AfterRevision,int TimeoutMs);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true)]
[JsonSerializable(typeof(MultiplexerSessionDescriptor)),JsonSerializable(typeof(RevisionPoll))]
internal partial class OverviewJson:JsonSerializerContext;
