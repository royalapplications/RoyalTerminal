// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
namespace RoyalTerminal.Multiplexing.Server;

internal sealed partial class ApiCatalog
{
    private async ValueTask<JsonElement> LaunchLayout(ApiArguments a,AccessGrant grant,CancellationToken ct)
    {
        var old=Mutable(a,grant,ct);Guid session=old.Id;var plan=LayoutTemplate.Parse(session,a.Object("layout"));var broker=_workspaces.Broker;long revision=old.Revision;
        Guid? anchor=a.OptionalId("blockId");var axis=LayoutTemplate.ParseAxis(a.String("direction","horizontal"));bool before=a.String("side","after") switch{"before"=>true,"after"=>false,_=>throw new ArgumentException("Invalid split side.")};
        double ratio=Fraction(a);var original=anchor is Guid id?Block(old,id):null;if(original?.Placement==MultiplexerPaneKind.Unplaced)throw new ArgumentException("Select a visible split anchor.");
        bool floating=original?.Placement==MultiplexerPaneKind.Floating;
        Guid window=original?.WindowId??broker.CreateWindow(session,a.String("label","Window"),revision++).Id;
        List<Guid> created=[];List<(Guid Id,LayoutTemplate.Leaf Leaf)> inputs=[];
        try
        {
            var tree=await Materialize(plan);ct.ThrowIfCancellationRequested();
            if(floating)broker.SplitFrame(session,anchor!.Value,tree,axis,ratio,before,revision++);
            else{if(anchor is Guid target)tree=Insert(old.Windows.Single(w=>w.Id==window).Layout!,target,tree);broker.SetWindowLayout(session,window,tree,revision++);}
            foreach(var (bid,leaf) in inputs)SendInitial(session,bid,leaf,ct);
            return Element(new LayoutReceipt(window,[..created],revision),LayoutTemplateJson.Default.LayoutReceipt);
        }
        catch(Exception failure)when(failure is not OutOfMemoryException)
        {
            List<Exception> errors=[];foreach(var owned in created.AsEnumerable().Reverse())try{await _workspaces.CloseBlockAsync(session,owned);}catch(MultiplexerException e)when(e.Code==MultiplexerErrorCode.NotFound){}catch(Exception e){errors.Add(e);}
            if(!anchor.HasValue)try{broker.CloseWindow(session,window);}catch(MultiplexerException e)when(e.Code is MultiplexerErrorCode.Conflict or MultiplexerErrorCode.NotFound){}
            if(errors.Count!=0){errors.Insert(0,failure);throw new AggregateException("Layout launch and cleanup failed.",errors);}throw;
        }
        async ValueTask<MultiplexerLayoutNode> Materialize(LayoutTemplate n)
        {
            ct.ThrowIfCancellationRequested();if(n is LayoutTemplate.Leaf leaf){var b=await _workspaces.CreateBlockAsync(session,leaf.Block with{WindowId=window,ExpectedRevision=revision++},ct,floating?MultiplexerPaneKind.Unplaced:MultiplexerPaneKind.Tiled);created.Add(b.Id);inputs.Add((b.Id,leaf));return MultiplexerLayoutNode.Leaf(b.Id);}
            var split=(LayoutTemplate.Split)n;return MultiplexerLayoutNode.Split(split.Orientation,await Materialize(split.First),await Materialize(split.Second),split.Ratio);
        }
        MultiplexerLayoutNode Insert(MultiplexerLayoutNode n,Guid target,MultiplexerLayoutNode added)=>n.BlockId==target?MultiplexerLayoutNode.Split(axis,before?added:n,before?n:added,ratio):n.BlockId.HasValue?n:n with{First=Insert(n.First!,target,added),Second=Insert(n.Second!,target,added)};
    }
}
