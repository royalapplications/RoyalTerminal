using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ReactiveUI.Avalonia.Reactive;
using RoyalTerminal.Multiplexing.Desktop;

[assembly: AvaloniaTestApplication(typeof(RoyalTerminal.Multiplexing.DesktopTests.DesktopTestApp))]

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class DesktopTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
        .UseReactiveUI(_ => { });
}
