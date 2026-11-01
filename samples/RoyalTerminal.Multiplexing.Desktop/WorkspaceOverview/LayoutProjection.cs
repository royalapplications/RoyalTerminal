// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json.Serialization;
using Avalonia;
namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
internal readonly record struct PaneSlot(MultiplexerBlockDescriptor Block, Rect Area, int Layer);
internal sealed record LayoutProjection(IReadOnlyList<PaneSlot> Slots, IReadOnlyList<MultiplexerBlockDescriptor> Shelf)
{
    internal static LayoutProjection Build(MultiplexerSessionDescriptor session, Guid windowId)
    {
        if (session.Blocks.Count > 128 || session.Windows.Count is < 1 or > 64) throw new InvalidDataException("The session exceeds the overview limits.");
        var window = session.Windows.Single(w => w.Id == windowId);
        var blocks = session.Blocks.Where(b => b.WindowId == windowId).ToDictionary(b => b.Id);
        var layers = MultiplexerLayers.Ordered(session,window).Select((id,index)=>(id,index)).ToDictionary(x=>x.id,x=>x.index+1);
        var slots = new List<PaneSlot>(); var shelf = new List<MultiplexerBlockDescriptor>(); var seen = new HashSet<Guid>();
        if (window.Layout is not null) Visit(window.Layout,new Rect(0,0,1,1),0);
        foreach (var block in blocks.Values)
        {
            if (block.SessionId != session.Id || block.Id == Guid.Empty) throw Invalid();
            switch(block.Placement)
            {
                case MultiplexerPaneKind.Tiled when seen.Contains(block.Id): break;
                case MultiplexerPaneKind.Unplaced: shelf.Add(block); break;
                case MultiplexerPaneKind.Floating when block.FloatingBounds is {} r:
                    if(r.Width is <1 or >10000 || r.Height is <1 or >10000 || r.X<0 || r.Y<0 || r.X>10000-r.Width || r.Y>10000-r.Height)throw Invalid();
                    slots.Add(new(block,new Rect(r.X/10000d,r.Y/10000d,r.Width/10000d,r.Height/10000d),layers[block.Id]));break;
                default: throw Invalid();
            }
        }
        return new(slots,shelf);
        void Visit(MultiplexerLayoutNode node,Rect area,int depth)
        {
            if(depth>63)throw Invalid();
            if(node.BlockId is Guid id){if(node.Orientation is not null||node.First is not null||node.Second is not null||!seen.Add(id)||!blocks.TryGetValue(id,out var block)||block.Placement!=MultiplexerPaneKind.Tiled)throw Invalid();slots.Add(new(block,area,0));return;}
            if(node.First is null||node.Second is null||!(node.Ratio>0&&node.Ratio<1))throw Invalid();
            if(node.Orientation==MultiplexerSplitOrientation.Horizontal){double w=area.Width*node.Ratio;Visit(node.First,new(area.X,area.Y,w,area.Height),depth+1);Visit(node.Second,new(area.X+w,area.Y,area.Width-w,area.Height),depth+1);}
            else if(node.Orientation==MultiplexerSplitOrientation.Vertical){double h=area.Height*node.Ratio;Visit(node.First,new(area.X,area.Y,area.Width,h),depth+1);Visit(node.Second,new(area.X,area.Y+h,area.Width,area.Height-h),depth+1);}else throw Invalid();
        }
    }
    static InvalidDataException Invalid()=>new("The server returned an inconsistent workspace layout.");
}
internal sealed record RevisionPoll(Guid SessionId,long AfterRevision,int TimeoutMs);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true)]
[JsonSerializable(typeof(MultiplexerSessionDescriptor)),JsonSerializable(typeof(RevisionPoll))]
internal partial class OverviewJson:JsonSerializerContext;
