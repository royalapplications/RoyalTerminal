using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

namespace RoyalTerminal.Multiplexing.Desktop;

/// <summary>Native terminal viewer and launcher for the session overview.</summary>
public sealed partial class MainWindow : Window
{
    private OverviewWindow? _overview;

    /// <summary>Loads the compiled workspace view.</summary>
    public MainWindow() => InitializeComponent();

    private void OpenOverview(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (!Guid.TryParse(vm.SessionId, out Guid id) || id == Guid.Empty || string.IsNullOrWhiteSpace(vm.Token) ||
            !Uri.TryCreate(vm.Endpoint, UriKind.Absolute, out var endpoint) ||
            !(endpoint.Scheme == "https" || (endpoint.Scheme == "http" && endpoint.IsLoopback)) ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            OverviewError.Text = "Select a session and enter its token. Use HTTPS, or HTTP on loopback.";
            return;
        }
        OverviewError.Text = string.Empty;
        // Opening the same command with new credentials must never reuse another principal's panes.
        _overview?.Close();
        var view = new OverviewWindow(); _overview = view;
        view.Connect(endpoint, vm.Token, id);
        view.Closed += (_, _) => { if (ReferenceEquals(_overview, view)) _overview = null; };
        view.Show(this);
    }
}
