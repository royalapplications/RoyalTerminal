// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Sample multi-tab terminal application.

using Avalonia;
using ReactiveUI.Avalonia.Reactive;

namespace RoyalTerminal.Demo;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (RoyalTerminal.Avalonia.App.Services.TerminalNotificationLaunch.IsInertActivation(args)) return;
        // This self-contained, non-faulting task overlaps the macOS font query
        // with Avalonia startup. No UI/native-renderer resources escape it.
        _ = RoyalTerminal.Avalonia.Rendering.TerminalFontWarmup.StartAsync();
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                OverlayPopups = false,
            })
            .With(new MacOSPlatformOptions
            {
                DisableDefaultApplicationMenuItems = true,
            })
            .UseReactiveUI(_ => { })
            .WithInterFont()
            .LogToTrace();
}
