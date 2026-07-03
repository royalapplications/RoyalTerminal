---
title: Native Menu Integration
---

# Native Menu Integration

The reusable app shell exposes native menu helpers so hosts can install the same
application-level menu used by the demo while still owning their Avalonia
`Application`.

This is especially important on macOS, where the first menu next to the Apple
menu is the application menu and Avalonia can install default items such as
`About Avalonia` unless the host declares or disables them during startup.

## Public API

| Type | Purpose |
| --- | --- |
| `ApplicationNativeMenuFactory` | Creates and binds the RoyalTerminal application native menu. |
| `ApplicationNativeMenuFactory.Create(MainWindowViewModel)` | Creates a menu shell and binds it immediately. |
| `ApplicationNativeMenuFactory.CreateShell()` | Creates the unbound application menu. |
| `ApplicationNativeMenuFactory.Bind(NativeMenu, MainWindowViewModel)` | Binds an existing shell to the view model commands. |

The application menu contains:

- About RoyalTerminal;
- Preferences;
- Quit RoyalTerminal.

Window-level menus are owned by `MainWindow` and expose the shell, edit, view,
session, window, and help command surface.

## Recommended macOS Setup

Declare the application menu in the executable `App.axaml`:

```xml
<Application
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    x:Class="MyTerminal.App"
    Name="RoyalTerminal">
  <NativeMenu.Menu>
    <NativeMenu>
      <NativeMenuItem Header="_About RoyalTerminal" />
      <NativeMenuItemSeparator />
      <NativeMenuItem Header="_Preferences..."
                      Gesture="Meta+OemComma" />
      <NativeMenuItemSeparator />
      <NativeMenuItem Header="_Quit RoyalTerminal"
                      Gesture="Meta+Q" />
    </NativeMenu>
  </NativeMenu.Menu>
</Application>
```

Disable Avalonia's default application menu items in the executable bootstrap:

```csharp
using Avalonia;

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .With(new MacOSPlatformOptions
    {
        DisableDefaultApplicationMenuItems = true,
    });
```

Then bind the declared menu after creating `MainWindow`:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using RoyalTerminal.Avalonia.App;
using RoyalTerminal.Avalonia.App.Services;

public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        MainWindow mainWindow = new();
        desktop.MainWindow = mainWindow;

        if (mainWindow.ViewModel is not null)
        {
            NativeMenu menu =
                NativeMenu.GetMenu(this) ??
                ApplicationNativeMenuFactory.CreateShell();
            ApplicationNativeMenuFactory.Bind(menu, mainWindow.ViewModel);
            NativeMenu.SetMenu(this, menu);
        }
    }

    base.OnFrameworkInitializationCompleted();
}
```

## Why Bind Instead Of Replacing Every Time

Declaring the application menu in XAML lets Avalonia see the intended menu early
in startup. Binding it later keeps command ownership in `MainWindowViewModel`.
This avoids duplicate app menus and avoids the default `About Avalonia` item.

`Create(MainWindowViewModel)` remains useful for simple hosts that do not need an
early XAML declaration:

```csharp
NativeMenu.SetMenu(this, ApplicationNativeMenuFactory.Create(mainWindow.ViewModel));
```

For macOS product apps, the declared-then-bound path is more predictable.

## Command Ownership

The application menu binds to:

| Menu item | View model command |
| --- | --- |
| About RoyalTerminal | `ShowAboutCommand` |
| Preferences | `PrepareSettingsPanelCommand` |
| Quit RoyalTerminal | `QuitApplicationCommand` |

All other shell commands stay on the window native menu and fallback menu bar.
See [App Shell Command Surface](/articles/app-shell-command-surface) for the
full command grouping.
