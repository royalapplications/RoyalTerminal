---
title: Demo Product Shell
---

# Demo Product Shell

`samples/RoyalTerminal.Demo` is an end-user style terminal application sample.
It demonstrates how to compose the lower-level RoyalTerminal libraries into a
resource-conscious product surface without moving behavior into Avalonia view
code-behind.

## Shell Layout

The demo shell is intentionally compact:

- terminal content is the primary surface;
- the top command bar is placed inside the extended titlebar;
- the tab strip stays visible and scannable;
- a left rail exposes high-frequency panels and actions;
- settings, profiles, command history, capture, and diagnostics are available
  but not dominant;
- diagnostics expose selectable Ghostty/runtime text plus the runtime event-log
  count, timeline, and clear action;
- replay playback and seek controls appear in the status bar only while replay
  mode is active;
- most low-frequency settings live in the native/menu surface or settings panel.

The shell avoids large marketing-style sections and nested card surfaces. Styling
is defined in resource dictionaries under `samples/RoyalTerminal.Demo/Styles`.

## Native Menus

On macOS, application-level commands belong in the application menu, not the
window menu. The demo declares the application native menu in `App.axaml` so
Avalonia sees it during startup and does not install the default
`About Avalonia` item. `ApplicationNativeMenuFactory` then binds the declared
menu items to the main ViewModel commands:

- About RoyalTerminal;
- Preferences;
- Quit RoyalTerminal.

Window-level menus contain Shell, Edit, View, Session, Window, and Help actions.
They expose the same low-frequency command surface that is available from the
demo UI: command-history suggestions, profile refresh and launch, settings,
settings profile actions, apply/save, font browsing, text-highlight rule
creation, shell panel visibility, titlebar search visibility, status bar
visibility, theme and shader controls, diagnostics, capture and replay controls,
pane navigation, tab switching, and snapshot actions. Per-rule text-highlight
removal remains contextual in the settings panel because each rule owns its own
remove command.
This prevents the default
Avalonia application menu from surfacing an `About Avalonia` item. About and
Quit stay application-level commands; Preferences also appears in the View menu
as a cross-platform fallback.

The app also sets:

```csharp
new MacOSPlatformOptions
{
    DisableDefaultApplicationMenuItems = true,
}
```

## Extended Titlebar

The main window uses Avalonia extended client area:

```xml
ExtendClientAreaToDecorationsHint="True"
WindowDecorations="Full"
ExtendClientAreaTitleBarHeightHint="-1"
```

The titlebar band is marked with
`WindowDecorationProperties.ElementRole="TitleBar"` so the OS receives native
drag, double-click, and titlebar gesture hit testing. Interactive controls placed
inside the titlebar, such as search and New Tab, are marked with
`ElementRole="User"` so they keep normal pointer and keyboard behavior.
The `-1` titlebar-height hint follows Dock's BrowserTabTheme sample: native
chrome placement remains platform-managed, which keeps macOS traffic lights
vertically centered, while RoyalTerminal keeps a compact titlebar command band.

The layout reserves space for macOS traffic lights and for right-side decoration
margins:

- `MacTrafficLightReserve`;
- `TitleBarRightDecorationReserve`.

## Settings Panel

The settings panel is styled as an integrated overlay rather than a heavy card.
The overlay:

- clips to the available terminal host area;
- disables horizontal scrolling;
- constrains panel width and height;
- uses the shared terminal shell palette;
- keeps category navigation and content independent through the settings panel
  ViewModel state.

## ViewModel Boundaries

The sample follows the repository MVVM rules:

- XAML defines layout and visuals;
- `MainWindowViewModel` exposes commands and interactions;
- `MainWindowController` handles concrete window, terminal, storage, and menu
  orchestration;
- code-behind only initializes the window and activation.

Titlebar, menu, pane, settings, search, capture, and command-history actions all
route through commands or ReactiveUI interactions.

## Product Startup

The demo starts in resource-conscious mode:

- restore a saved workspace when available;
- otherwise open one terminal tab;
- start diagnostic tabs for every supported render mode only when
  `ROYALTERMINAL_DEMO_START_ALL_RENDER_MODES=1` is set;
- disable session autostart in tests with
  `ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART=1`.

## Tests

Focused coverage:

- `MainWindowViewModelFlowTests` for titlebar role wiring, menu placement,
  command bindings, settings overlay sizing, and app title;
- `MainWindowControllerModeStartupTests` for startup mode, workspace restore,
  pane behavior, and active terminal surfaces;
- `TerminalSettingsPanelLayoutTests` and
  `MainWindowControllerSettingsPanelTests` for settings panel layout and state.
