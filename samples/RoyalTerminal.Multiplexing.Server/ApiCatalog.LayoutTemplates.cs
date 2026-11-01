// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text;
using System.Text.Json;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed partial class ApiCatalog
{
    private async ValueTask<JsonElement> LaunchLayout(ApiArguments a,AccessGrant grant,CancellationToken ct)
    {
        ServerHttp.RequireAdministrator(grant); ct.ThrowIfCancellationRequested();
        Guid session=a.Id("sessionId"); var plan=LayoutTemplate.Parse(session,a.Object("layout"));
        var broker=_workspaces.Broker; var old=broker.GetSession(session); long revision=a.Revision()??old.Revision;
        if(revision!=old.Revision) throw new MultiplexerException(MultiplexerErrorCode.Conflict,"Stale layout revision.");
        Guid? anchor=a.OptionalId("blockId");
        var axis=LayoutTemplate.ParseAxis(a.String("direction","horizontal"));
        bool before=a.String("side","after") switch {"before"=>true,"after"=>false,_=>throw new ArgumentException("Invalid split side.")};
        double ratio=a.Number("ratio",.5); if(!(ratio>0 && ratio<1)) throw new ArgumentException("Invalid anchor split ratio.");
        var original=anchor is Guid selected ? old.Blocks.SingleOrDefault(b=>b.Id==selected && b.Placement==MultiplexerPaneKind.Tiled) ?? throw new ArgumentException("Select a tiled split anchor.") : null;
        Guid window=original?.WindowId ?? broker.CreateWindow(session,a.String("label","Window"),revision++).Id;
        List<Guid> created=[]; List<(Guid Id,string Value)> inputs=[];
        try
        {
            var tree=await Materialize(plan);
            if(anchor is Guid target) tree=Insert(old.Windows.Single(w=>w.Id==window).Layout!,target,tree);
            ct.ThrowIfCancellationRequested(); broker.SetWindowLayout(session,window,tree,revision++);
            foreach(var entry in inputs) { ct.ThrowIfCancellationRequested(); _workspaces.Input(session,entry.Id,Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Value))); }
            return Element(new LayoutReceipt(window,[..created],revision),LayoutTemplateJson.Default.LayoutReceipt);
        }
        catch(Exception failure) when(failure is not OutOfMemoryException)
        {
            List<Exception> errors=[];
            foreach(var owned in created.AsEnumerable().Reverse())
                try { await _workspaces.CloseBlockAsync(session,owned); } catch(MultiplexerException ex) when(ex.Code==MultiplexerErrorCode.NotFound) { } catch(Exception ex) { errors.Add(ex); }
            // CloseWindow without a survivor refuses a concurrently populated window. Never terminate a foreign process to roll back.
            if(!anchor.HasValue) try { broker.CloseWindow(session,window); } catch(MultiplexerException ex) when(ex.Code is MultiplexerErrorCode.Conflict or MultiplexerErrorCode.NotFound) { }
            if(errors.Count!=0) { errors.Insert(0,failure); throw new AggregateException("Layout launch and cleanup failed.",errors); }
            throw;
        }
        async ValueTask<MultiplexerLayoutNode> Materialize(LayoutTemplate n)
        {
            ct.ThrowIfCancellationRequested();
            if(n is LayoutTemplate.Leaf leaf)
            {
                var block=await _workspaces.CreateBlockAsync(session,leaf.Block with {WindowId=window,ExpectedRevision=revision},ct); created.Add(block.Id); ++revision;
                if(!string.IsNullOrEmpty(leaf.InitialInput)) inputs.Add((block.Id,leaf.InitialInput));
                return MultiplexerLayoutNode.Leaf(block.Id);
            }
            var split=(LayoutTemplate.Split)n;
            var first=await Materialize(split.First); var second=await Materialize(split.Second);
            return MultiplexerLayoutNode.Split(split.Orientation,first,second,split.Ratio);
        }
        MultiplexerLayoutNode Insert(MultiplexerLayoutNode node,Guid id,MultiplexerLayoutNode added)=>node.BlockId==id ? MultiplexerLayoutNode.Split(axis,before?added:node,before?node:added,ratio) : node.BlockId.HasValue ? node : node with { First=Insert(node.First!,id,added), Second=Insert(node.Second!,id,added) };
    }
}
