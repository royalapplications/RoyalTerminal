---
title: App Shell Hosting
---

# App Shell Hosting

`RoyalApps.RoyalTerminal.Avalonia.App` exposes the product-style terminal shell
as a reusable library. Use it when your application wants a complete terminal
window with tabs, settings, command history, capture/replay, native menus,
titlebar chrome, split panes, diagnostics, and workspace restore.

The package deliberately stops short of owning the executable bootstrap. Your
application still owns the Avalonia `Application`, theme selection, platform
options, and desktop lifetime.

## Public Entry Points

| Type | Namespace | Purpose |
| --- | --- | --- |
| `MainWindow` | `RoyalTerminal.Avalonia.App` | Reusable top-level terminal window with key bindings, native window menu, titlebar integration, and shell activation. |
| `MainView` | `RoyalTerminal.Avalonia.App.Views` | Reusable visual shell hosted by `MainWindow`. |
| `MainWindowViewModel` | `RoyalTerminal.Avalonia.App.ViewModels` | Shell state, commands, and ReactiveUI interactions. |
| `AboutRoyalTerminalWindow` | `RoyalTerminal.Avalonia.App.Views` | Reusable about dialog view. |
| `AboutRoyalTerminalViewModel` | `RoyalTerminal.Avalonia.App.ViewModels` | Product metadata for the about dialog. |

`MainWindow` is the easiest integration point. `MainView` is available for hosts
that need to embed the shell in a custom window.

## Minimal Host

Reference the app-shell package from your executable project, then keep
`Avalonia.Desktop` and `Avalonia.Themes.Fluent` in the executable, not in the
shared app-shell library.

```xml
<ItemGroup>
  <PackageReference Include="Avalonia.Desktop" />
  <PackageReference Include="Avalonia.Themes.Fluent" />
  <PackageReference Include="ReactiveUI.Avalonia" />
  <PackageReference Include="RoyalApps.RoyalTerminal.Avalonia.App" />
</ItemGroup>
```

Configure Avalonia from the executable:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI.Avalonia;
using RoyalTerminal.Avalonia.App;

public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseReactiveUI(_ => { })
        .WithInterFont()
        .LogToTrace();

public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.MainWindow = new MainWindow();
    }

    base.OnFrameworkInitializationCompleted();
}
```

## Required Resources

The executable `App.axaml` should merge the app-shell resources and include the
Fluent theme:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://RoyalTerminal.Avalonia.App/Styles/Theme.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>

<Application.Styles>
  <FluentTheme />
  <StyleInclude Source="avares://RoyalTerminal.Avalonia.Settings/Settings/TerminalSettingsPanel.axaml" />
  <StyleInclude Source="avares://RoyalTerminal.Avalonia.App/Styles/Tabs.axaml" />
</Application.Styles>
```

## Window Responsibilities

`MainWindow` owns top-level behavior:

- native window menu;
- key bindings;
- extended titlebar configuration;
- controller activation;
- app-owned pane split policy.

`MainView` owns visual composition:

- titlebar command strip;
- tab strip;
- optional left rail;
- terminal host;
- settings overlay;
- command launcher;
- diagnostics;
- status bar and replay controls.

`MainWindowViewModel` owns state and commands. `MainWindowController` is an
internal orchestration service that binds the top-level window, view model,
terminal controls, storage provider, file pickers, capture runtime, menus, tabs,
and panes together.

## Custom Pane Policy

Hosts can replace the default split behavior before activation:

```csharp
MainWindow window = new()
{
    PaneSplitPolicy = TerminalPaneSplitPolicies.PtyOnly,
};
```

Use [Pane Split Policy](/articles/split-pane-policy) when SSH, Rebex, MFA, or
transport-specific cloning rules must stay application-owned.

## Related API Articles

- [App Shell Command Surface](/articles/app-shell-command-surface)
- [Native Menu Integration](/articles/app-shell-native-menus)
- [Settings Panel API](/articles/settings-panel-api)
- [Terminal Pane Layout API](/articles/terminal-pane-layout-api)
- [Pane Split Policy](/articles/split-pane-policy)
