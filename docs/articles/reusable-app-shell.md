---
title: Reusable App Shell
---

# Reusable App Shell

`RoyalApps.RoyalTerminal.Avalonia.App` packages the product-style terminal shell
that used to live directly in `samples/RoyalTerminal.Demo`.

Use this package when you want a ready-to-run terminal application surface rather
than assembling `TerminalControl`, profiles, settings, native menus, capture,
replay, command history, split panes, and titlebar chrome yourself.

## What It Provides

- `MainWindow`, a reusable terminal window with native menu declarations,
  key bindings, extended titlebar chrome, and platform window settings.
- `MainView`, the reusable terminal shell view containing the titlebar command
  surface, tab strip, left rail, terminal host, settings overlay, diagnostics,
  replay controls, and fallback `NativeMenuBar`.
- `MainWindowViewModel`, command state, settings state, shell visibility state,
  capture/replay state, and ReactiveUI interactions.
- Shell services for startup mode selection, profile/theme catalogs, SSH trust
  prompts, native application menu binding, and runtime controller orchestration.
- Shared resource dictionaries for the RoyalTerminal app palette, titlebar,
  tabs, scroll buttons, search panel, settings overlay, status bar, and replay UI.

The package intentionally does not provide an Avalonia `Application` class or
own the desktop/theme bootstrap. The executable host owns `Avalonia.Desktop`,
`Avalonia.Themes.Fluent`, `FluentTheme`, platform options, and root-window
creation.

## Minimal Desktop Host

The demo executable configures Avalonia and launches its own `App` class:

```csharp
using Avalonia;
using ReactiveUI.Avalonia;

public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new MacOSPlatformOptions
        {
            DisableDefaultApplicationMenuItems = true,
        })
        .UseReactiveUI(_ => { })
        .WithInterFont()
        .LogToTrace();
```

Keep `DisableDefaultApplicationMenuItems` enabled on macOS. The host
`App.axaml` declares the RoyalTerminal application menu, and
`ApplicationNativeMenuFactory` binds it to the shared `MainWindowViewModel` so
Avalonia's default `About Avalonia` item is not installed.

## Custom Application Hosts

Applications that need their own `Application` class can still reference the
package and merge the shared resources:

```xml
<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://RoyalTerminal.Avalonia.Settings/Settings/TerminalSettingsPanel.axaml" />
  <StyleInclude Source="avares://RoyalTerminal.Avalonia.App/Styles/Tabs.axaml" />
</Application.Styles>
```

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://RoyalTerminal.Avalonia.App/Styles/Theme.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

Then create `RoyalTerminal.Avalonia.App.MainWindow` from your desktop lifetime.
The window owns the top-level shortcuts and native window menu; `MainView` owns
the reusable visual shell.

Custom application classes can use `ApplicationNativeMenuFactory.CreateShell()`
and `ApplicationNativeMenuFactory.Bind(...)` to install the same macOS
application menu used by the demo host.

## Shell Boundaries

The split is intentional:

- `MainWindow` owns top-level behavior: window metadata, extended titlebar
  settings, key bindings, and native menus.
- `MainView` owns visual composition and named terminal UI parts.
- `MainWindowController` bridges the top-level window to the view, storage
  provider, dialogs, transport startup, workspace restore, tabs, panes, capture,
  replay, and runtime diagnostics.
- `samples/RoyalTerminal.Demo` remains useful as the executable launcher, but it
  no longer owns the product shell implementation.
