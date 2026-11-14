// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;

public sealed partial class MultiplexerBroker
{
    /// <summary>Publish an entire validated tree of shelved processes as one floating frame.</summary>
    public MultiplexerFloatingLayer GroupFloating(Guid sessionId, Guid windowId, MultiplexerLayoutNode layout, MultiplexerFloatBounds bounds, long? expectedRevision=null)
    {
        var rectangles=FloatTreeGeometry.Project(layout,bounds);
        lock(_gate)
        {
            var s=FindSession(sessionId); CheckRevision(s,expectedRevision); var w=FindWindow(s,windowId);
            foreach(var id in rectangles.Keys)
                if(GetPlacement(s,id) is not {Kind: MultiplexerPaneKind.Unplaced} p || p.Window!=windowId)
                    throw Conflict("Every new floating leaf must be shelved in the destination window.");
            var group=new MultiplexerFloatingLayer(Guid.NewGuid(),bounds,layout);
            w.Floats.AddRange(rectangles.Keys); Install(s,w,group,rectangles); s.Changed(); return group;
        }
    }
    /// <summary>Replace a compound frame's geometry or divider tree without changing membership, processes or stacking position.</summary>
    public MultiplexerSessionDescriptor EditFloating(Guid sessionId, Guid layerId, MultiplexerLayoutNode? layout=null, MultiplexerFloatBounds? bounds=null, long? expectedRevision=null)
    {
        lock(_gate)
        {
            var s=FindSession(sessionId); CheckRevision(s,expectedRevision);
            var (w,old)=Group(s,layerId); var next=old with {Bounds=bounds??old.Bounds, Layout=layout??old.Layout};
            var positions=FloatTreeGeometry.Project(next.Layout,next.Bounds);
            var owned=w.Floats.Where(id=>s.Placements[id].Layer==old.Id).ToArray();
            if(!owned.ToHashSet().SetEquals(positions.Keys))throw new ArgumentException("A frame edit cannot replace its terminals.");
            if(next==old)return Describe(s);
            int offset=w.Floats.IndexOf(owned[0]); w.Floats.RemoveRange(offset,owned.Length); w.Floats.InsertRange(offset,positions.Keys);
            Install(s,w,next,positions); s.Changed(); return Describe(s);
        }
    }
    /// <summary>Sets (or, when omitted, toggles) shared zoom for a visible block in its owning window. A stale unzoom for A never clears B.</summary>
    public MultiplexerWindowDescriptor ZoomBlock(Guid sessionId,Guid blockId,bool? zoom=null,long? expectedRevision=null)
    {
        lock(_gate)
        {
            var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var p=GetPlacement(s,blockId);
            if(p.Kind==MultiplexerPaneKind.Unplaced)throw Conflict("Cannot zoom a shelved block.");
            var w=FindWindow(s,p.Window); bool set=zoom??(w.Zoom!=blockId);
            Guid? next=set?blockId:w.Zoom==blockId?null:w.Zoom;
            if(next!=w.Zoom){w.Zoom=next;s.Changed();}return Describe(w);
        }
    }
    private static (Window Window,MultiplexerFloatingLayer Layer) Group(Session s,Guid key)
    {
        if(s.Placements.TryGetValue(key,out var p)&&p.Layer is Guid id)key=id;
        foreach(var w in s.Windows)if(w.Groups.TryGetValue(key,out var layer))return (w,layer);
        throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Floating frame not found.");
    }
    private static void Install(Session s,Window w,MultiplexerFloatingLayer layer,IReadOnlyDictionary<Guid,MultiplexerFloatBounds> positions)
    {
        w.Groups[layer.Id]=layer;
        foreach(var (id,bounds) in positions)s.Placements[id]=new(w.Id,MultiplexerPaneKind.Floating,bounds,layer.Id);
    }
    private static void Unlink(Session s,Window w,Guid id,Placement p)
    {
        if(w.Zoom==id)w.Zoom=null;
        if(p.Kind==MultiplexerPaneKind.Tiled){w.Layout=RemoveLeaf(w.Layout,id);return;}
        if(p.Kind!=MultiplexerPaneKind.Floating)return;
        w.Floats.Remove(id);
        if(p.Layer is not Guid key)return;
        var old=w.Groups[key];var tree=RemoveLeaf(old.Layout,id);
        if(tree is null)w.Groups.Remove(key);
        else{var next=old with{Layout=tree};Install(s,w,next,FloatTreeGeometry.Project(tree,next.Bounds));}
    }
    /// <summary>Detach an entire frame atomically, then drain all independent process transports outside the broker lock.</summary>
    public async ValueTask CloseFrameAsync(Guid sessionId,Guid key,long revision)
    {
        MultiplexerBlock[] victims;
        lock(_gate)
        {
            var s=FindSession(sessionId);CheckRevision(s,revision);
            if(s.Placements.TryGetValue(key,out var p)&&p.Layer is null)
            {
                if(p.Kind!=MultiplexerPaneKind.Floating)throw Conflict("The block is not floating.");
                victims=[FindBlock(s,key)];
            }
            else
            {
                var (w,g)=Group(s,key);
                victims=w.Floats.Where(id=>s.Placements[id].Layer==g.Id).Select(id=>FindBlock(s,id)).ToArray();
            }
            foreach(var b in victims){RemoveBlock(s,b);_retiring.Add(b);}
        }
        try{await CloseBlocksAsync(victims).ConfigureAwait(false);}
        finally{lock(_gate)foreach(var b in victims)_retiring.Remove(b);}
    }
}
