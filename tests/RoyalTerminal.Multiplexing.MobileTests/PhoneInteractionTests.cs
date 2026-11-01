using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Mobile;
using Xunit;
[assembly:AvaloniaTestApplication(typeof(RoyalTerminal.Multiplexing.MobileTests.PhoneTestApp))]
namespace RoyalTerminal.Multiplexing.MobileTests;
public sealed class PhoneTestApp { public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<MobileApp>().UseHeadless(new(){UseHeadlessDrawing=true}); }
public sealed class PhoneInteractionTests
{
    [Theory]
    [InlineData("http://remote.example")][InlineData("https://user:secret@work.test")][InlineData("https://work.test/?token=x")][InlineData("https://work.test/#secret")]
    public void CredentialEndpointRejectsUnsafeForms(string url)=>Assert.Throws<ArgumentException>(()=>MobileWorkspaceView.ParseEndpoint(url));
    [AvaloniaTheory][InlineData(true)][InlineData(false)]
    public async Task PhoneUsesAuthorizedRoleAndReleasesWhenViewDisappears(bool control)
    {
        var s=Guid.NewGuid();var b=Guid.NewGuid();var socket=new TestWebSocket();
        socket.Queue($$"""{"type":"attached","attachmentId":"{{Guid.NewGuid()}}","hasControl":{{(control?"true":"false")}}}"""); socket.Queue("""{"sequence":1,"kind":"resize","dimensions":{"columns":20,"rows":4,"widthPixels":0,"heightPixels":0}}""");socket.Queue("""{"type":"ready","sequence":1}""");
        var connector=new TestSocketConnector(socket);var view=new MobileWorkspaceView(new Api(s,b,control),connector);var w=new Window{Content=view,Width=390,Height=820};w.Show();
        T Find<T>(string n) where T:Control=>view.FindControl<T>(n)!;
        void Click(string n)=>Find<Button>(n).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Find<TextBox>("Host").Text="https://work.test";Find<TextBox>("Secret").Text="private";Click("Connect");await Until(()=>Find<ComboBox>("Blocks").SelectedItem!=null);
            Assert.Equal(control,Find<Button>("ControlButton").IsVisible);Assert.Equal(string.Empty,Find<TextBox>("Secret").Text);
            Click(control?"ControlButton":"Observe");await Until(()=>Find<Button>("Detach").IsVisible);
            Assert.Equal(control,Find<StackPanel>("InputPanel").IsVisible);Assert.Contains("role="+(control?"controller":"observer"),connector.Endpoint!.Query,StringComparison.Ordinal);Assert.Empty(socket.Sent);
            if(control) {var enter=new Button{Tag="enter"};var panel=Find<StackPanel>("InputPanel");var keys=Assert.IsType<WrapPanel>(panel.Children[1]);var key=keys.Children.OfType<Button>().Single(x=>Equals(x.Tag,enter.Tag));key.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>socket.Sent.Count==1);}
            Click("Forget"); await Until(()=>socket.Disposed); Assert.Null(Find<ScrollViewer>("Viewport").Content); Assert.Null(Find<ComboBox>("Sessions").ItemsSource);
        }
        finally {w.Close();await view.Shutdown.WaitAsync(TimeSpan.FromSeconds(8),TestContext.Current.CancellationToken);}
    }
    private static async Task Until(Func<bool> ready){using var t=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);t.CancelAfter(TimeSpan.FromSeconds(8));while(!ready())await Task.Delay(10,t.Token);}
    private sealed class Api(Guid session,Guid block,bool control):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            Assert.Equal("private",r.Headers.Authorization?.Parameter);
            var json=r.RequestUri!.AbsolutePath.EndsWith("/context",StringComparison.Ordinal)?$$"""{"administrator":false,"canControl":{{(control?"true":"false")}}}""":$$"""{"sessions":[{"id":"{{session}}","label":"Build","blocks":[{"id":"{{block}}","label":"Shell","state":"Running","dimensions":{"columns":20,"rows":4,"widthPixels":0,"heightPixels":0}}]}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
}
