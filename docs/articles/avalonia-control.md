---
title: Avalonia Control
---

# Avalonia Control

`RoyalTerminal.Avalonia` is the primary host package for application developers. It provides `TerminalControl` and the default input, selection, scrolling, VT, and session-service composition used across the samples and tests.

## `TerminalControl` capabilities

`TerminalControl` is a `TemplatedControl` with:

- terminal grid sizing (`Columns`, `Rows`, `ScrollbackLimit`)
- font and palette configuration
- keyboard input with IME support
- mouse selection, mouse reporting, and scroll handling
- virtualized scrollback
- VT mode aware input encoding
- hyperlink and text selection support
- thread-safe transport output ingestion
- capture/replay integration
- terminal snapshot export integration

Core events include:

- `DataReceived`
- `TitleChanged`
- `Bell`
- `ProcessExited`
- `CloseRequested`
- `TerminalResized`

## Styling and theming

The control exposes terminal-centric styling properties directly:

- `FontFamilyName`
- `TerminalFontSize`
- `DefaultForeground`
- `DefaultBackground`
- `AutoScroll`
- `Theme`
- `VtProcessorPreference`

`TerminalTheme`, `TerminalPalette`, `TerminalPaletteGenerator`, `TerminalThemeParser`, and `TerminalThemeSerializer` live in the shared `RoyalTerminal.Terminal.Theming` namespace so themes can stay outside Avalonia-specific code.

## Session startup

For new integrations, start sessions through `StartSessionAsync(...)` or the typed helpers such as `StartSshAsync(...)`. The control keeps VT preference changes and active session state synchronized and avoids swapping processors during a running session.

```csharp
TerminalControl terminal = new()
{
    FontFamilyName = "JetBrains Mono",
    TerminalFontSize = 14,
    Columns = 120,
    Rows = 36,
};

await terminal.StartSessionAsync(
    new PtyTransportOptions(
        Command: null,
        WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment: null,
        Dimensions: new TerminalSessionDimensions(120, 36, 1200, 800)));
```

## Capture and replay

Capture/replay is built as reusable infrastructure, not demo-only logic. The main entry point is `RoyalTerminal.Avalonia.Capture.TerminalCaptureRuntime`.

```csharp
using RoyalTerminal.Avalonia.Capture;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

TerminalControl terminal = new();
TerminalCaptureRuntime captureRuntime = new(terminal);

captureRuntime.StartCapture();
terminal.SendInput("ls\r");
terminal.WriteOutput("file1\nfile2\n"u8);

TerminalCaptureSession session = captureRuntime.StopCapture();
await TerminalCaptureSessionSerializer.SaveToFileAsync(session, "session.rtcap.json");
```

Replay uses the same runtime object and a serialized `TerminalCaptureSession`. The recommended file extension is `.rtcap.json`.

## Snapshot export

Snapshot export support lives behind `ITerminalSnapshotExportSource` and the shared `TerminalSnapshotExportFormat` enum:

- `PlainText`
- `StyledVt`
- `Html`

Both the managed and Ghostty VT processors expose these formats. This lets you build clipboard, inspection, or export workflows without tying them to one processor implementation.

## Settings panel package

`RoyalTerminal.Avalonia.Settings` adds a reusable settings surface that matches the demo’s tabbed settings flyout.

To use it, include the control theme in `App.axaml`:

```xml
<StyleInclude Source="avares://RoyalTerminal.Avalonia.Settings/Settings/TerminalSettingsPanel.axaml" />
```

Then host the panel with a `TerminalSettingsPanelState` data context:

```xml
<settings:TerminalSettingsPanel DataContext="{Binding SettingsPanelState}" />
```

The state object covers:

- session metadata
- transport-specific settings
- appearance
- terminal behavior
- SSH configuration
- session/event logging
- profile CRUD state

## Threading behavior

The control is designed for asynchronous transport callbacks. Background SSH, pipe, network, or PTY reads can feed terminal data into the control without forcing the caller to marshal onto the UI thread manually.

That behavior is important in real transports because session output may arrive on non-UI threads with bursty read patterns.

## Demo-only behavior vs reusable behavior

The reusable pieces live in shared packages. The demo app adds:

- tabs and mode switching
- file dialogs
- capture toolbar commands
- search/replay UI chrome
- session and event logging panels
- sample content injection for hyperlinks and Kitty graphics

Keep that distinction in mind when reading the demo source. The demo is a showcase and validation host for reusable building blocks, not the public API surface itself.
