using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string? path = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_SOCKET");
            UnixWorkspaceEndpoint? local = string.IsNullOrWhiteSpace(path) ? null : new(path);
            var http = new HttpClient(local?.CreateHandler() ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
            var window = new MainWindow { LocalEndpoint = local };
            var terminal = window.FindControl<RemoteTerminalControl>("Terminal") ?? throw new InvalidOperationException("Missing terminal surface.");
            if (local is not null) terminal.RemoteFactory.ConfigureBeforeFirstAttach(local);
            var controller = new WorkspaceTerminalController(terminal, http);
            var vm = new WorkspaceViewModel(new WorkspaceClient(http), controller)
            {
                Endpoint = local is not null ? "http://localhost/" : Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_URL") ?? "http://127.0.0.1:7331/",
                Token = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN") ?? string.Empty,
            };
            window.DataContext = vm; desktop.MainWindow = window;
            window.Closed += async (_, _) =>
            {
                vm.Dispose();
                try { await window.CloseOverviewAsync(); await controller.DisposeAsync(); }
                finally { http.Dispose(); local?.Dispose(); desktop.Shutdown(); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
