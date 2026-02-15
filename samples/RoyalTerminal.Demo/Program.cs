// Licensed under the MIT License.
// RoyalTerminal.Demo — Sample multi-tab terminal application.

using Avalonia;
using ReactiveUI.Avalonia;

namespace RoyalTerminal.Demo;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .WithInterFont()
            .LogToTrace();
}
