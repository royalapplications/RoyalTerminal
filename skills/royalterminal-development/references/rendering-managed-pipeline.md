# Rendering Managed Pipeline

## Table Of Contents

- [Scope And Primary Files](#scope-and-primary-files)
- [Core Data Model](#core-data-model)
- [Composition Rendering Path](#composition-rendering-path)
- [TerminalControl Rendering Lifecycle](#terminalcontrol-rendering-lifecycle)
- [Resize And Grid Reconciliation](#resize-and-grid-reconciliation)
- [Scroll And Selection Integration](#scroll-and-selection-integration)
- [Threading And Safety](#threading-and-safety)
- [Integration Constraints](#integration-constraints)
- [Validation And Regression Tests](#validation-and-regression-tests)
- [Code Examples](#code-examples)

## Scope And Primary Files

Primary files for managed rendering path:
- `src/RoyalTerminal.Terminal/Rendering/TerminalCell.cs`
- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`
- `src/RoyalTerminal.Avalonia/Controls/TerminalPresenter.cs`
- `src/RoyalTerminal.Avalonia/Rendering/TerminalDrawHandler.cs`
- `src/RoyalTerminal.Rendering.Skia/Rendering/SkiaTerminalRenderer.cs`
- `src/RoyalTerminal.Avalonia/Scrolling/TerminalScrollData.cs`
- `src/RoyalTerminal.Avalonia/Scrolling/VirtualizedTerminalScrollViewer.cs`
- `src/RoyalTerminal.Avalonia/Services/DefaultTerminalScrollService.cs`
- `src/RoyalTerminal.Avalonia/Services/DefaultTerminalSelectionService.cs`

## Core Data Model

Rendering model types in `TerminalCell.cs`:
- `TerminalCell`
- `TerminalRow`
- `TerminalScreen`

Key responsibilities:
- `TerminalCell`: per-cell codepoint/grapheme/colors/attributes/width
- `TerminalRow`: contiguous cell array + row dirty flag
- `TerminalScreen`: viewport + scrollback rows, viewport row access, resize and invalidation

Important notes:
- `TerminalScreen` exposes `SyncRoot` for cross-thread safety.
- `GetViewportRow(...)` resolves rows through viewport/scrollback coordinates.
- full repaint is triggered through `InvalidateAll()`.

## Composition Rendering Path

Managed composition path:
1. `TerminalControl` holds `TerminalPresenter` + `SkiaTerminalRenderer` + `TerminalScreen`.
2. `TerminalPresenter` creates `CompositionCustomVisual` with `TerminalDrawHandler`.
3. `TerminalPresenter.SetRenderState(...)` passes renderer/screen references.
4. `TerminalPresenter.Invalidate(...)` sends `InvalidateMessage`.
5. `TerminalDrawHandler` schedules animation-frame updates and renders via Skia lease.
6. Draw handler clears background and calls `renderer.RenderFull(canvas, screen)`.

Practical implication:
- `SkiaTerminalRenderer` supports dirty-row rendering (`Render(...)`), but current composition handler uses `RenderFull(...)` for each draw pass.

## TerminalControl Rendering Lifecycle

`TerminalControl.InitializeTerminal()`:
- creates `TerminalScreen`
- creates VT processor from `IVtProcessorFactory`
- creates `SkiaTerminalRenderer` via `CreateRenderer(...)`
- initializes scroll data/viewer

`CreateRenderer(previous)` behavior:
- creates a new renderer from current font settings
- carries forward cursor, selection, and shaping/diagnostics settings from prior renderer

`ApplyFontSettings()` behavior:
- recreates renderer with new font/cell metrics
- updates scroll metrics (`CellHeight`, viewport extent)
- invalidates screen and presenter with full redraw

`ApplyColorDefaults()` behavior:
- updates screen default colors
- invalidates entire screen and presenter

## Resize And Grid Reconciliation

`ArrangeOverride(...)` in `TerminalControl`:
- derives columns/rows from final size and current cell metrics
- applies terminal size updates when dimensions changed

`ApplyTerminalSize(...)` effects:
- updates screen size (`TerminalScreen.Resize`)
- updates scroll extent/viewport and scroll viewer
- updates endpoint size and session transport dimensions
- raises `TerminalResized` when grid actually changes
- notifies presenter resize + invalidate

## Scroll And Selection Integration

Scroll integration:
- `TerminalScrollData` tracks extent/viewport/offset and row conversions
- `VirtualizedTerminalScrollViewer` bridges to Avalonia `ILogicalScrollable`
- `DefaultTerminalScrollService` updates scroll position and triggers presenter invalidation

Selection integration:
- `SkiaTerminalRenderer` stores selection range and renders selection overlay
- `DefaultTerminalSelectionService` can read selected text from endpoint source or screen model
- selection clear path resets renderer selection and invalidates screen/presenter

## Threading And Safety

Key lock boundaries:
- `TerminalDrawHandler.OnRender(...)` locks `screen.SyncRoot` while drawing
- `TerminalControl` locks `screen.SyncRoot` during VT processing and full invalidation operations

Rules:
- do not mutate `TerminalScreen` row/cell state from rendering thread without lock
- preserve `SyncRoot` lock usage when adding new render/update code

## Integration Constraints

- keep view-side composition setup in `TerminalPresenter`; avoid moving draw logic into code-behind event handlers.
- preserve renderer state carry-over when recreating renderer (`CreateRenderer(previous)`).
- preserve scroll/view metrics consistency after font-size or grid-size changes.
- when changing draw path from `RenderFull(...)`, validate visual parity for selection/cursor/background and headless tests.
- if adding new renderer options, propagate them through renderer recreation logic and demo configuration paths.

## Validation And Regression Tests

Primary managed rendering tests:
- `tests/RoyalTerminal.Tests/RenderingTests.cs`
- `tests/RoyalTerminal.Tests/HeadlessSkiaRenderingTests.cs`
- `tests/RoyalTerminal.Tests/TerminalScreenTests.cs`
- `tests/RoyalTerminal.Tests/TerminalControlTests.cs`
- `tests/RoyalTerminal.Tests/TerminalControlHeadlessInteractionTests.cs`

## Code Examples

### Write screen data and force redraw

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;

TerminalControl terminal = new();
TerminalScreen? screen = terminal.Screen;
if (screen is not null)
{
    lock (screen.SyncRoot)
    {
        TerminalRow row = screen.GetViewportRow(0);
        row[0].Codepoint = 'H';
        row[1].Codepoint = 'i';
        row.IsDirty = true;
    }

    terminal.InvalidateTerminal();
}
```

### Apply font settings and keep renderer options

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;

TerminalControl terminal = new();
if (terminal.Renderer is SkiaTerminalRenderer renderer)
{
    renderer.EnableTextShaping = true;
    renderer.EnableLigatures = true;
    renderer.EnableTextRenderDiagnostics = true;
}

terminal.TerminalFontSize = 16; // triggers renderer recreation with option carry-over
terminal.FontFamilyName = "Cascadia Mono";
```

### Scroll and repaint explicitly

```csharp
terminal.ScrollByRows(-3);
terminal.ScrollToBottom();
terminal.InvalidateTerminal();
```
