// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Avalonia Application setup.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Diagnostics;
using Avalonia.Markup.Xaml;
using RoyalTerminal.Demo.Services;

namespace RoyalTerminal.Demo;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow mainWindow = new();
            desktop.MainWindow = mainWindow;

            if (mainWindow.ViewModel is not null)
            {
                NativeMenu applicationMenu = NativeMenu.GetMenu(this) ?? ApplicationNativeMenuFactory.CreateShell();
                ApplicationNativeMenuFactory.Bind(applicationMenu, mainWindow.ViewModel);
                NativeMenu.SetMenu(this, applicationMenu);
            }
        }

        base.OnFrameworkInitializationCompleted();

#if DEBUG
        this.AttachDevTools();
#endif
    }
}
