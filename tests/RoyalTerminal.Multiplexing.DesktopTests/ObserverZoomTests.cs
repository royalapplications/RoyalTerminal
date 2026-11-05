using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class ObserverZoomTests
{
    [AvaloniaFact]
    public async Task HiddenObserverKeepsParsingAndUnzoomUsesTheLatestSharedRectangles()
    {
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        static TestWebSocket Socket()
        {
            var ws = new TestWebSocket(); ws.Queue($$"""{"type":"attached","attachmentId":"{{Guid.NewGuid()}}","hasControl":false}""");
            ws.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":16,"rows":4,"widthPixels":0,"heightPixels":0}}""");
            ws.Queue("""{"type":"ready","sequence":1}"""); return ws;
        }
        var ws1=Socket(); var ws2=Socket(); var left=new RemoteTerminalControl(new TestSocketConnector(ws1)); var right=new RemoteTerminalControl(new TestSocketConnector(ws2));
        var a=new Border{Child=left}; var b=new Border{Child=right}; var panel=new OverviewPanel();
        panel.Place(a,new Rect(0,0,.5,1),0); panel.Place(b,new Rect(.5,0,.5,1),0);
        var window=new Window{Content=panel, Width=640, Height=320}; window.Show();
        await using var first=new WorkspaceTerminalController(left,focusOnAttach:false); await using var second=new WorkspaceTerminalController(right,focusOnAttach:false);
        var options=new MultiplexingTransportOptions(new("https://test.example/"),"read-only",Guid.NewGuid(),Guid.NewGuid(),new(16,4,0,0)){Observer=true};
        try
        {
            await first.AttachAsync(options,limit.Token);await second.AttachAsync(options with{BlockId=Guid.NewGuid()},limit.Token);
            Assert.True(left.Focus()); Assert.True(panel.ToggleZoom()); Assert.Same(a,panel.ZoomedPane);Assert.False(b.IsVisible);
            var size=panel.Bounds.Size; panel.Measure(size);panel.Arrange(new Rect(size)); Assert.Equal(size,a.Bounds.Size);
            ws2.Queue("""{"sequence":2,"kind":"output","data":"Wg=="}""");
            var live=Assert.IsType<MultiplexingTerminalTransport>(right.RemoteSession.Transport);
            while(live.LastSequence<2) await Task.Delay(10,limit.Token);
            right.FlushPendingTransportOutput();Assert.Equal((int)'Z',right.Screen!.GetViewportRow(0)[0].Codepoint);
            panel.Place(a,new Rect(0,0,.25,1),0);panel.Place(b,new Rect(.25,0,.75,1),0);
            Assert.False(b.IsVisible);panel.Editing=true;Assert.Null(panel.ZoomedPane);Assert.True(b.IsVisible);Assert.False(panel.ToggleZoom());
            panel.Measure(size);panel.Arrange(new Rect(size)); Assert.Equal(size.Width*.25,a.Bounds.Width,1);
            Assert.All(new[]{ws1,ws2}, ws=>{Assert.False(ws.Disposed);Assert.Empty(ws.Sent);});Assert.False(first.HasControl);Assert.False(second.HasControl);
            panel.Editing=false;Assert.True(panel.ToggleZoom());panel.RemovePane(a);Assert.Null(panel.ZoomedPane);Assert.True(b.IsVisible);
        }
        finally { await first.DetachAsync();await second.DetachAsync();window.Close(); }
    }
}
