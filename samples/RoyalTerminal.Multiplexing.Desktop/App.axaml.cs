using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;

namespace RoyalTerminal.Multiplexing.Desktop;

/// <summary>Composition root and application lifetime for the experimental native client.</summary>
public sealed class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
            MainWindow window = new();
            RemoteTerminalControl terminal = window.FindControl<RemoteTerminalControl>("Terminal")
                ?? throw new InvalidOperationException("The compiled terminal surface was not found.");
            WorkspaceTerminalController controller = new(terminal);
            WorkspaceViewModel viewModel = new(new WorkspaceClient(http), controller)
            {
                Endpoint = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_URL") ?? "http://127.0.0.1:7331/",
                Token = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN") ?? string.Empty,
            };
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            window.Closed += async (_, _) =>
            {
                viewModel.Dispose();
                try { await controller.DisposeAsync(); }
                finally { http.Dispose(); desktop.Shutdown(); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
