# Control Types Catalog

This catalog lists all control-type classes in repository scope (`src/` + `samples/`) and describes when/how to use them.

## Table Of Contents

- [Catalog Scope](#catalog-scope)
- [Core Avalonia Controls](#core-avalonia-controls)
- [Ghostty Avalonia Controls](#ghostty-avalonia-controls)
- [Demo Window Host](#demo-window-host)
- [Control Selection Matrix](#control-selection-matrix)
- [Integration And Lifecycle Notes](#integration-and-lifecycle-notes)
- [Validation Targets](#validation-targets)

## Catalog Scope

Included:
- concrete classes deriving `Control`, `TemplatedControl`, `NativeControlHost`, or `ReactiveWindow`
- runtime controls used by core package and sample app

Excluded:
- non-control services/view-models
- framework controls from Avalonia itself

## Core Avalonia Controls

### `TerminalControl`

- base: `TemplatedControl, ILogicalScrollable`
- file: `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`
- role: backend-neutral terminal surface and session orchestrator integration point

Key responsibilities:
- owns `TerminalScreen`, renderer, VT processor lifecycle
- handles keyboard, pointer, selection, clipboard, focus
- starts/stops transport sessions (`StartSessionAsync`, `StartPipeAsync`, `StartSshAsync`, `StartPty`)
- exposes state (`HasActiveSession`, `ActiveTransportId`, `HasPty`)

### `TerminalPresenter`

- base: `Control`
- file: `src/RoyalTerminal.Avalonia/Controls/TerminalPresenter.cs`
- role: visual presenter for terminal render state

### `VirtualizedTerminalScrollViewer`

- base: `Control, ILogicalScrollable`
- file: `src/RoyalTerminal.Avalonia/Scrolling/VirtualizedTerminalScrollViewer.cs`
- role: virtualized scrolling integration for large terminal history

## Ghostty Avalonia Controls

### `GhosttyNativeTerminalControl`

- base: `NativeControlHost, IDisposable`
- file: `src/RoyalTerminal.Avalonia.Ghostty/Controls/GhosttyNativeTerminalControl.cs`
- role: host embedded native Ghostty surface (airspace model applies)

### `GhosttyRenderedTerminalControl`

- base: `Control, IDisposable`
- file: `src/RoyalTerminal.Avalonia.Ghostty/Controls/GhosttyRenderedTerminalControl.cs`
- role: composited Ghostty rendered mode (`CpuCellRenderer` or `TextureInterop`)

## Demo Window Host

### `MainWindow`

- base: `ReactiveWindow<MainWindowViewModel>`
- file: `samples/RoyalTerminal.Demo/MainWindow.axaml.cs`
- role: sample host window wiring view-model interactions to controller orchestration

## Control Selection Matrix

| Need | Preferred Control |
|---|---|
| backend-neutral cross-platform terminal with pluggable transports | `TerminalControl` |
| embedded native Ghostty surface (max native fidelity, macOS capability path) | `GhosttyNativeTerminalControl` |
| Ghostty behavior with Avalonia composition and optional texture interop | `GhosttyRenderedTerminalControl` |
| sample app shell/window host | `MainWindow` |

## Integration And Lifecycle Notes

`TerminalControl` lifecycle details:
- initialize VT/render state in constructor path
- defer VT preference swaps while session active
- reset mouse state on session start/stop
- keep endpoint/transport input handling deterministic

Ghostty-specific controls:
- require embedded Ghostty app initialization path
- should be capability-gated in sample/host logic
- rendered control can switch between CPU and interop rendering modes

MVVM rule reminder:
- keep code-behind minimal and route behavior through view-model interactions/services

## Validation Targets

Primary tests touching control behavior:
- `tests/RoyalTerminal.Tests/TerminalControlTests.cs`
- `tests/RoyalTerminal.Tests/TerminalControlHeadlessInteractionTests.cs`
- `tests/RoyalTerminal.Tests/NativeTerminalControlTests.cs`
- `tests/RoyalTerminal.Tests/HeadlessSkiaRenderingTests.cs`
- `tests/RoyalTerminal.Tests/MainWindowControllerModeStartupTests.cs`

## Code Examples

### `TerminalControl` in XAML

```xml
<rt:TerminalControl
    x:Name="Terminal"
    Columns="120"
    Rows="40"
    TerminalFontSize="14"
    FontFamilyName="JetBrains Mono" />
```

### Start a PTY session in controller/service code

```csharp
await terminal.StartSessionAsync(new PtyTransportOptions(
    Command: null,
    WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment: null,
    Dimensions: new TerminalSessionDimensions(120, 40, 1200, 800)));
```

### `GhosttyRenderedTerminalControl` setup

```csharp
GhosttyRenderedTerminalControl rendered = new()
{
    RenderingMode = GhosttyRenderedTerminalRenderingMode.TextureInterop,
    TerminalFontSize = 14,
    FontFamilyName = "Menlo"
};

rendered.Initialize(ghosttyApp);
```

### `GhosttyNativeTerminalControl` setup

```csharp
GhosttyNativeTerminalControl native = new()
{
    TerminalFontSize = 14,
    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
};

native.Initialize(ghosttyApp);
```
