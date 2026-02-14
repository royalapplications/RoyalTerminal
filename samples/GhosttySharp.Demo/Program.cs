// Licensed under the MIT License.
// GhosttySharp.Demo — Sample multi-tab terminal application.

using Avalonia;
using ReactiveUI.Avalonia;

namespace GhosttySharp.Demo;

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
