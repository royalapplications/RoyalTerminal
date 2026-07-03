---
title: App Shell Command Surface
---

# App Shell Command Surface

`MainWindowViewModel` is the public command and state boundary for the reusable
app shell. The view model is intentionally broad because it backs both the
native menu surface and the in-window terminal shell.

Hosts normally use `MainWindow` and let the shell bind everything. Custom hosts
that embed `MainView` directly can bind to the same commands and interactions.

## Public Option Models

| Type | Purpose |
| --- | --- |
| `TransportModeOption` | Transport selector entry for PTY, pipe, raw TCP, Telnet, serial, and SSH. |
| `ShellProfileOption` | Discovered local shell profile entry. |
| `SessionLaunchOption` | Runnable profile shown by launch/search UI. |
| `TerminalCaptureFormatOption` | Capture save format entry. |
| `SettingsCategoryOption` | Settings category selector entry and stable category ids. |
| `SshAuthModeOption` | SSH authentication mode selector entry and stable auth ids. |
| `TerminalThemeApplyRequest` | Theme payload sent through the theme-apply interaction. |
| `TerminalShaderSampleOption` | Shader sample entry exposed by the app shell. |

These types are lightweight view-model records. Durable profile, workspace,
theme, capture, and transport documents still live in the lower terminal
packages.

## Command Groups

The reusable shell groups commands by workflow:

| Group | Examples |
| --- | --- |
| Tabs | `NewTabCommand`, `CloseCurrentTabCommand`, `ActivateTabCommand`, `CycleTabForwardCommand`, `CycleTabBackwardCommand` |
| Clipboard and selection | `CopySelectionCommand`, `PasteClipboardCommand`, `SelectAllCommand` |
| Search | `ApplySearchCommand`, `NextSearchCommand`, `PreviousSearchCommand`, `ClearSearchCommand` |
| Sessions | `RefreshSessionLauncherCommand`, `LaunchSessionProfileCommand`, `RestartActiveSessionCommand`, `ClearActiveScrollbackCommand` |
| Panes | `SplitPaneRightCommand`, `SplitPaneDownCommand`, focus-pane commands, resize-pane commands |
| Capture and replay | `ToggleCaptureCommand`, `SaveCaptureCommand`, `LoadReplayCommand`, `ReplayPlayPauseCommand`, `StopReplayCommand` |
| Settings | `PrepareSettingsPanelCommand`, profile CRUD commands, `ApplySettingsPanelCommand`, `SaveSettingsPanelCommand` |
| View state | left panel, search panel, status bar, diagnostics, tabs-in-titlebar, shader, theme, and font commands |
| Application | `ShowAboutCommand`, `QuitApplicationCommand` |

The native menu and fallback menu bar bind to the same commands, so menu state
and in-window UI state stay aligned.

## ReactiveUI Interactions

`MainWindowViewModel` uses ReactiveUI interactions for operations that need
window, storage, or terminal-control services. `MainWindowController` registers
the default handlers.

Important interaction groups:

| Interaction | Host responsibility |
| --- | --- |
| Tab interactions | Create, activate, close, and cycle tabs. |
| Clipboard interactions | Copy, paste, and select all in the active terminal. |
| Search interactions | Apply, navigate, and clear active terminal search. |
| Capture interactions | Start/stop capture, save capture, load replay, control replay playback. |
| Session interactions | Refresh launch profiles, launch selected profiles, restart or clear the active session. |
| Pane interactions | Split, focus, and resize the active pane. |
| Settings interactions | Prepare/apply/save profile settings and browse font or log paths. |
| Dialog/application interactions | Show about dialog and quit the host application. |

When you use `MainWindow`, these handlers are already installed. If you host
`MainView` yourself, provide equivalent handlers or reuse the window-level shell.

## Visibility State

The shell exposes public state for the view features that can also be toggled
from native menus:

- left panel visibility;
- titlebar search panel visibility;
- status bar visibility;
- tabs in titlebar;
- diagnostics panel visibility;
- command history overlay;
- settings panel visibility.

This is why the View menu can offer the same feature set as the visible UI.

## Theme And Shader Commands

Theme and shader selection are shell-level features over lower-level rendering
models:

- `TerminalThemeApplyRequest` carries a `TerminalTheme` plus display name.
- `TerminalShaderSampleOption` carries a stable shader sample id and display
  name.
- `ApplyThemeModelInteraction` applies a concrete terminal theme.
- `ApplyShaderSampleInteraction` applies the selected shader sample to the
  active terminal surface.

Use the lower-level shader articles when you need to provide your own shader
source:

- [Shader Support](/articles/shaders)
- [Applying Shaders](/articles/shaders-applying)

## Related Articles

- [App Shell Hosting](/articles/app-shell-hosting)
- [Native Menu Integration](/articles/app-shell-native-menus)
- [Pane Split Policy](/articles/split-pane-policy)
- [Settings Panel API](/articles/settings-panel-api)
