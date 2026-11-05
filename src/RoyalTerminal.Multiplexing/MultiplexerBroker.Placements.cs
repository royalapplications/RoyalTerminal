// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;
/// <summary>How a terminal is placed inside its home window.</summary>
public enum MultiplexerPaneKind
{
    /// <summary>A leaf in the split tree.</summary>
    Tiled,
    /// <summary>A rectangle over the tiled content.</summary>
    Floating,
    /// <summary>No visible rectangle; process and attachments are unchanged.</summary>
    Unplaced,
}
/// <summary>Coordinates in ten-thousandths of the window, independent of client DPI.</summary>
public sealed record MultiplexerFloatBounds(int X,int Y,int Width,int Height)
{
    /// <summary>Default rectangle for a newly floated pane.</summary>
    public static MultiplexerFloatBounds Default {get;}=new(1500,1500,7000,7000);
    internal void Validate(){if(Width is <1 or >10000||Height is <1 or >10000||X<0||Y<0||X>10000-Width||Y>10000-Height)throw new ArgumentOutOfRangeException(nameof(MultiplexerFloatBounds),"The rectangle must fit within [0,10000] on both axes.");}
}
public sealed partial class MultiplexerBroker
{
    private readonly record struct Placement(Guid Window,MultiplexerPaneKind Kind,MultiplexerFloatBounds? Bounds=null,Guid? Layer=null);
    private static Placement GetPlacement(Session s,Guid id)=>s.Placements.TryGetValue(id,out var p)?p:throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Block not found.");
    /// <summary>Move a process without replacing its stream. Moving a member out of a compound frame collapses its former split.</summary>
    public MultiplexerSessionDescriptor PlaceBlock(Guid sessionId,Guid blockId,Guid windowId,MultiplexerPaneKind kind,Guid? relativeTo=null,MultiplexerSplitOrientation orientation=MultiplexerSplitOrientation.Horizontal,double ratio=.5,bool before=false,MultiplexerFloatBounds? bounds=null,long? expectedRevision=null)
    {
        if(!Enum.IsDefined(kind))throw new ArgumentOutOfRangeException(nameof(kind));ValidateSplit(orientation,ratio);
        if(kind!=MultiplexerPaneKind.Tiled&&(relativeTo.HasValue||before))throw new ArgumentException("Only a tile can have a split target or ordering.");
        if(kind!=MultiplexerPaneKind.Floating&&bounds is not null)throw new ArgumentException("Only a floating block accepts bounds.");bounds?.Validate();
        lock(_gate)
        {
            var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var old=GetPlacement(s,blockId);
            var source=FindWindow(s,old.Window);var dest=FindWindow(s,windowId);
            if(relativeTo is {} anchor){if(anchor==blockId)throw new ArgumentException("Cannot split a block against itself.");var target=GetPlacement(s,anchor);if(target.Window!=dest.Id||target.Kind!=MultiplexerPaneKind.Tiled)throw Conflict("Split target is not tiled in the destination window.");}
            if(old.Kind==MultiplexerPaneKind.Floating&&kind==old.Kind&&source==dest)
            {
                if(bounds is null)return Describe(s);
                if(old.Layer is Guid key)return EditFloating(sessionId,key,bounds:bounds,expectedRevision:expectedRevision);
                if(bounds!=old.Bounds){s.Placements[blockId]=old with{Bounds=bounds};s.Changed();}return Describe(s);
            }
            var next=new Placement(windowId,kind,kind==MultiplexerPaneKind.Floating?bounds??old.Bounds??MultiplexerFloatBounds.Default:null);
            if(next==old&&kind!=MultiplexerPaneKind.Tiled)return Describe(s);
            bool keepZoom=source==dest&&source.Zoom==blockId&&kind!=MultiplexerPaneKind.Unplaced;
            Unlink(s,source,blockId,old);
            if(kind==MultiplexerPaneKind.Tiled)
            {
                var leaf=MultiplexerLayoutNode.Leaf(blockId);
                MultiplexerLayoutNode Join(MultiplexerLayoutNode other)=>before?MultiplexerLayoutNode.Split(orientation,leaf,other,ratio):MultiplexerLayoutNode.Split(orientation,other,leaf,ratio);
                dest.Layout=relativeTo is {} id?Rewrite(dest.Layout,n=>n.BlockId==id?Join(n):n):dest.Layout is null?leaf:Join(dest.Layout);
            }
            else if(kind==MultiplexerPaneKind.Floating)dest.Floats.Add(blockId);
            s.Placements[blockId]=next;if(keepZoom)dest.Zoom=blockId;s.Changed();return Describe(s);
        }
    }
    /// <summary>Raise/lower a frame as a unit. Either a member or the stable compound frame identity is accepted.</summary>
    public MultiplexerSessionDescriptor StackLayer(Guid sessionId,Guid blockId,bool raise,bool toEdge=false,long? expectedRevision=null)
    {
        lock(_gate)
        {
            var s=FindSession(sessionId);CheckRevision(s,expectedRevision); Window w;Guid key;
            if(s.Placements.TryGetValue(blockId,out var p)){if(p.Kind!=MultiplexerPaneKind.Floating)throw Conflict("Only floating blocks have a stacking order.");w=FindWindow(s,p.Window);key=p.Layer??blockId;}
            else{var g=Group(s,blockId);w=g.Window;key=g.Layer.Id;}
            Guid Frame(Guid id)=>s.Placements[id].Layer??id;
            var frames=w.Floats.Select(Frame).Distinct().ToList();int i=frames.IndexOf(key),end=raise?frames.Count-1:0,to=toEdge?end:Math.Clamp(i+(raise?1:-1),0,frames.Count-1);
            if(i!=to){var members=w.Floats.ToLookup(Frame);frames.RemoveAt(i);frames.Insert(to,key);w.Floats.Clear();foreach(var id in frames)w.Floats.AddRange(members[id]);s.Changed();}return Describe(s);
        }
    }
    /// <summary>Exchange placement slots, retaining each process identity. Compound tree leaves are rewritten atomically with the flat compatibility projection.</summary>
    public MultiplexerSessionDescriptor SwapBlocks(Guid sessionId,Guid firstId,Guid secondId,long? expectedRevision=null)
    {
        lock(_gate)
        {
            var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var a=GetPlacement(s,firstId);var b=GetPlacement(s,secondId);if(firstId==secondId)return Describe(s);
            var first=FindWindow(s,a.Window);var second=FindWindow(s,b.Window);Guid Exchange(Guid id)=>id==firstId?secondId:id==secondId?firstId:id;
            MultiplexerLayoutNode Leaf(MultiplexerLayoutNode n)=>Exchange(n.BlockId!.Value) is var id&&id!=n.BlockId?n with{BlockId=id}:n;
            void Replace(Window w){w.Layout=Rewrite(w.Layout,Leaf);for(int i=0;i<w.Floats.Count;i++)w.Floats[i]=Exchange(w.Floats[i]);foreach(var layer in w.Groups.Values.ToArray())w.Groups[layer.Id]=layer with{Layout=Rewrite(layer.Layout,Leaf)!};}
            Replace(first);if(first!=second)Replace(second);s.Placements[firstId]=b;s.Placements[secondId]=a;
            foreach(var w in s.Windows)if(w.Zoom is Guid z && (s.Placements[z].Window!=w.Id || s.Placements[z].Kind==MultiplexerPaneKind.Unplaced))w.Zoom=null;
            s.Changed();return Describe(s);
        }
    }
    private static MultiplexerLayoutNode? Rewrite(MultiplexerLayoutNode? n,Func<MultiplexerLayoutNode,MultiplexerLayoutNode> leaf)
    {if(n is null)return null;if(n.BlockId.HasValue)return leaf(n);var a=Rewrite(n.First,leaf);var b=Rewrite(n.Second,leaf);return ReferenceEquals(a,n.First)&&ReferenceEquals(b,n.Second)?n:n with{First=a,Second=b};}
    /// <summary>Move a window to a one-based display position.</summary>
    public MultiplexerSessionDescriptor ReorderWindow(Guid sessionId,Guid windowId,int position,long? expectedRevision=null)
    {lock(_gate){var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var w=FindWindow(s,windowId);if(position<1||position>s.Windows.Count)throw new ArgumentOutOfRangeException(nameof(position));if(s.Windows[position-1]!=w){s.Windows.Remove(w);s.Windows.Insert(position-1,w);s.Changed();}return Describe(s);}}
    /// <summary>Close a window, preserving its blocks on an explicitly selected shelf.</summary>
    public MultiplexerSessionDescriptor CloseWindow(Guid sessionId,Guid windowId,Guid? preserveIn=null,long? expectedRevision=null)
    {lock(_gate){var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var w=FindWindow(s,windowId);if(s.Windows.Count==1)throw Conflict("Cannot remove the last window; close the session explicitly instead.");if(preserveIn==windowId)throw new ArgumentException("The destination must be a different window.",nameof(preserveIn));var dest=preserveIn is {} id?FindWindow(s,id):null;var owned=s.Placements.Where(x=>x.Value.Window==windowId).Select(x=>x.Key).ToArray();if(owned.Length!=0&&dest is null)throw Conflict("Specify a destination for this window's running or exited blocks.");foreach(var b in owned)s.Placements[b]=new(dest!.Id,MultiplexerPaneKind.Unplaced);s.Windows.Remove(w);s.Changed("window_closed",windowId);return Describe(s);}}
    /// <summary>Wait for a structural revision without polling processes. A removed session raises NotFound.</summary>
    public async ValueTask<MultiplexerSessionDescriptor> WaitForSessionChangeAsync(Guid sessionId,long afterRevision,CancellationToken cancellationToken=default)
    {ArgumentOutOfRangeException.ThrowIfNegative(afterRevision);Session s;Task signal;lock(_gate){s=FindSession(sessionId);if(afterRevision>s.Revision)throw Conflict("Revision is ahead of this session.");if(afterRevision<s.Revision)return Describe(s);if(s.WatcherCount>=64)throw Capacity("Too many structural change waiters for this session.");signal=(s.PendingChange??=new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;++s.WatcherCount;}try{await signal.WaitAsync(cancellationToken).ConfigureAwait(false);return GetSession(sessionId);}finally{lock(_gate)--s.WatcherCount;}}
}
