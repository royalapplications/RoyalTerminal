using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Desktop;
public sealed partial class MainWindow : Window
{
    private OverviewWindow? _overview;
    private Task _lastClose = Task.CompletedTask;
    internal UnixWorkspaceEndpoint? LocalEndpoint { get; init; }
    public MainWindow() => InitializeComponent();
    internal Task CloseOverviewAsync() { _overview?.Close(); return _lastClose; }
    private void OpenOverview(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (!Guid.TryParse(vm.SessionId,out Guid id) || id == Guid.Empty || string.IsNullOrWhiteSpace(vm.Token) || !Uri.TryCreate(vm.Endpoint,UriKind.Absolute,out var endpoint) || !(endpoint.Scheme == "https" || endpoint.Scheme == "http" && endpoint.IsLoopback) || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        { OverviewError.Text = "Select a session and token. Use HTTPS or HTTP on loopback."; return; }
        OverviewError.Text = string.Empty; _overview?.Close();
        var view = LocalEndpoint is { } local ? new OverviewWindow(local.CreateHandler(),local) : new OverviewWindow(); _overview = view; view.Connect(endpoint,vm.Token,id);
        view.Closed += (_, _) => { _lastClose = Task.WhenAll(_lastClose,view.Shutdown); if (ReferenceEquals(_overview,view)) _overview = null; };
        view.Show(this);
    }
}
