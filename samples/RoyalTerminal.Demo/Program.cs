// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Sample multi-tab terminal application.

using Avalonia;
#if !ROYALTERMINAL_PUBLISH_AOT
using ReactiveUI.Avalonia;
#endif

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
            .With(new Win32PlatformOptions
            {
                OverlayPopups = false,
            })
            .With(new MacOSPlatformOptions
            {
                DisableDefaultApplicationMenuItems = true,
            })
#if !ROYALTERMINAL_PUBLISH_AOT
            .UseReactiveUI(_ => { })
#endif
            .WithInterFont()
            .LogToTrace();
}
