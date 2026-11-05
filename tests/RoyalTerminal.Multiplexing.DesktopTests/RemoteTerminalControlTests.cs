using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Multiplexing.ClientTests;
using RoyalTerminal.Multiplexing.Desktop;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class RemoteTerminalControlTests
{
    private static MultiplexingTransportOptions Options(bool observer = false) => new(
        new Uri("https://example.test/"), "secret", Guid.NewGuid(), Guid.NewGuid(), new(80, 24, 0, 0))
    { Observer = observer };

    private static string Attached(bool control = true)
        => $$"""{"type":"attached","attachmentId":"{{Guid.NewGuid():D}}","hasControl":{{(control ? "true" : "false")}}}""";
    private static string Resize(long sequence, int columns, int rows)
        => $$$"""{"sequence":{{{sequence}}},"kind":"resize","dimensions":{"columns":{{{columns}}},"rows":{{{rows}}},"widthPixels":0,"heightPixels":0}}""";
    private static string Output(long sequence, string data)
        => $$"""{"sequence":{{sequence}},"kind":"output","data":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(data))}}"}""";
    private static string Ready(long sequence) => $$"""{"type":"ready","sequence":{{sequence}}}""";

    [AvaloniaFact]
    public async Task GeometryDrainsEarlierOutputAndWindowLayoutCannotChangeRemoteGrid()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached()); socket.Queue(Resize(1, 4, 2));
        socket.Queue(Output(2, "ABCDE\u001b[2;1HZ")); socket.Queue(Resize(3, 8, 2)); socket.Queue(Ready(3));
        RemoteTerminalControl control = new(new TestSocketConnector(socket));
        Window window = new() { Width = 700, Height = 500, Content = new ScrollViewer { Content = control } };
        window.Show();
        await using WorkspaceTerminalController controller = new(control);
        try
        {
            await controller.AttachAsync(Options(), TestContext.Current.CancellationToken);
            Assert.Equal(8, control.Columns);
            Assert.Equal(2, control.Rows);
            Assert.Equal(8, control.Screen!.Columns);
            Assert.StartsWith("ABCDZ", RowText(control.Screen.GetViewportRow(0)), StringComparison.Ordinal);
            window.Width = 320;
            window.Height = 160;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(8, control.Columns);
            Assert.Equal(2, control.Rows);
            Assert.Equal(8, control.Screen.Columns);
            Assert.Empty(socket.Sent);
            await controller.DetachAsync();
            Assert.False(controller.IsAttached);
            Assert.True(socket.Disposed);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ObserversKeepSelectionSurfaceButCannotSendInputOrAutomaticResize()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached(false)); socket.Queue(Resize(1, 80, 24)); socket.Queue(Ready(1));
        RemoteTerminalControl control = new(new TestSocketConnector(socket));
        Window window = new() { Width = 600, Height = 400, Content = control };
        window.Show();
        await using WorkspaceTerminalController controller = new(control);
        try
        {
            await controller.AttachAsync(Options(true), TestContext.Current.CancellationToken);
            control.SendInput("attempt");
            control.RemoteSession.ResizeSession(100, 30, 800, 600);
            Assert.True(control.IsHitTestVisible);
            Assert.False(controller.HasControl);
            Assert.Throws<InvalidOperationException>(() => controller.RequestDimensions(new(100, 30, 0, 0)));
            Assert.Empty(socket.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ServerOwnsLiveQueryRepliesButControllerUserInputIsPreserved()
    {
        TestWebSocket socket = new();
        socket.Queue(Attached()); socket.Queue(Resize(1, 80, 24)); socket.Queue(Ready(1));
        RemoteTerminalControl control = new(new TestSocketConnector(socket));
        Window window = new() { Width = 600, Height = 400, Content = control };
        window.Show();
        await using WorkspaceTerminalController controller = new(control);
        try
        {
            await controller.AttachAsync(Options(), TestContext.Current.CancellationToken);
            TaskCompletionSource parsed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            control.DataReceived += (_, _) => parsed.TrySetResult();
            socket.Queue(Output(2, "\u001b[6n\u001b[c"));
            await parsed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            control.FlushPendingTransportOutput();
            Assert.Empty(socket.Sent);
            control.SendInput("echo user\r");
            await ((MultiplexingTerminalTransport)control.RemoteSession.Transport!).FlushAsync(TestContext.Current.CancellationToken);
            Assert.Single(socket.Sent);
            Assert.Contains(Convert.ToBase64String("echo user\r"u8), Encoding.UTF8.GetString(socket.Sent[0]), StringComparison.Ordinal);
            controller.RequestDimensions(new(100, 32, 0, 0));
            await ((MultiplexingTerminalTransport)control.RemoteSession.Transport!).FlushAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, socket.Sent.Count);
            Assert.Equal(80, control.Columns); // local dimensions wait for the server's ordered resize echo
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CompiledWorkspaceViewLoadsTheNativeRemoteControl()
    {
        MainWindow window = new();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            RemoteTerminalControl control = Assert.IsType<RemoteTerminalControl>(window.FindControl<RemoteTerminalControl>("Terminal"));
            control.ApplyRemoteDimensions(new(300, 24, 0, 0));
            Dispatcher.UIThread.RunJobs();
            ScrollViewer viewport = Assert.IsType<ScrollViewer>(window.FindControl<ScrollViewer>("TerminalViewport"));
            Assert.True(viewport.Extent.Width > viewport.Viewport.Width);
            viewport.Offset = new Vector(100, 0);
            Assert.True(viewport.Offset.X > 0);
            Assert.Equal(300, control.Columns);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemoteGeometryIsBoundedBeforeMutatingTheNativeSurface()
    {
        RemoteTerminalControl control = new(new TestSocketConnector(new TestWebSocket()));
        Assert.Throws<ArgumentOutOfRangeException>(() => control.ApplyRemoteDimensions(new(int.MaxValue, 24, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => control.ApplyRemoteDimensions(new(4096, 4096, 0, 0)));
        Assert.Equal(80, control.Columns);
        Assert.Equal(24, control.Rows);
    }

    private static string RowText(TerminalRow row)
    {
        StringBuilder text = new();
        for (int column = 0; column < row.Columns; column++)
        {
            int codepoint = row[column].Codepoint;
            text.Append(codepoint <= 0 ? ' ' : (char)codepoint);
        }
        return text.ToString();
    }
}
