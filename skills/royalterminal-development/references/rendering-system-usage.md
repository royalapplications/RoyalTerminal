# Rendering System Usage

This is the rendering reference entrypoint. Load this file first, then open only the rendering sub-files needed for the task.

## Table Of Contents

- [Scope](#scope)
- [Load Order](#load-order)
- [Decision Guide](#decision-guide)
- [Implementation Workflow](#implementation-workflow)
- [Critical Invariants](#critical-invariants)
- [Validation Gate](#validation-gate)
- [Code Examples](#code-examples)

## Scope

The rendering reference set covers:
- managed terminal rendering pipeline (`TerminalScreen` -> presenter -> composition draw handler -> `SkiaTerminalRenderer`)
- text shaping, font fallback, grapheme handling, and diagnostics counters
- selection/scroll integration and resize behavior
- texture interop backend pipeline and CPU fallback path
- rendering-specific tests and failure triage entrypoints

## Load Order

1. [`rendering-managed-pipeline.md`](rendering-managed-pipeline.md)
2. [`rendering-text-shaping-font-fallback-and-diagnostics.md`](rendering-text-shaping-font-fallback-and-diagnostics.md)
3. [`rendering-interop-and-backends.md`](rendering-interop-and-backends.md) (when texture interop or render-target descriptors are involved)
4. [`modes-and-settings.md`](modes-and-settings.md) (for operational toggles and demo runtime mode mapping)
5. [`build-test-validation.md`](build-test-validation.md) before finalizing

## Decision Guide

| If you are changing... | Read first | Then read |
|---|---|---|
| `TerminalControl`/`TerminalPresenter` render lifecycle | [`rendering-managed-pipeline.md`](rendering-managed-pipeline.md) | [`control-types-catalog.md`](control-types-catalog.md) |
| `SkiaTerminalRenderer` drawing behavior/cursor/selection | [`rendering-managed-pipeline.md`](rendering-managed-pipeline.md) | [`rendering-text-shaping-font-fallback-and-diagnostics.md`](rendering-text-shaping-font-fallback-and-diagnostics.md) |
| shaping, ligatures, direction, fallback fonts | [`rendering-text-shaping-font-fallback-and-diagnostics.md`](rendering-text-shaping-font-fallback-and-diagnostics.md) | [`modes-and-settings.md`](modes-and-settings.md) |
| text-render diagnostics counters | [`rendering-text-shaping-font-fallback-and-diagnostics.md`](rendering-text-shaping-font-fallback-and-diagnostics.md) | [`build-test-validation.md`](build-test-validation.md) |
| texture interop backend/descriptor/handles | [`rendering-interop-and-backends.md`](rendering-interop-and-backends.md) | [`native-loader-resolution.md`](native-loader-resolution.md) |
| render + scroll/selection interactions | [`rendering-managed-pipeline.md`](rendering-managed-pipeline.md) | [`endpoint-contracts-and-input-pipeline.md`](endpoint-contracts-and-input-pipeline.md) |

## Implementation Workflow

1. Classify the rendering path first:
- managed Skia grid path (`TerminalControl` + `TerminalPresenter` + `TerminalDrawHandler`)
- interop path (`GhosttyRenderedTerminalControl` + `SkiaInteropRenderer`)
2. Confirm the underlying screen/update source (`BasicVtProcessor`, `GhosttyVtProcessor`, or endpoint-backed updates).
3. Apply rendering behavior changes in the right layer:
- screen model (`TerminalCell`/`TerminalRow`/`TerminalScreen`)
- draw dispatch (`TerminalPresenter`/`TerminalDrawHandler`)
- paint logic (`SkiaTerminalRenderer` and shaping/fallback components)
4. Re-check resizing and scroll integration:
- columns/rows recalculation
- cell metrics and viewport extent updates
- presenter invalidation and scroll invalidation
5. Run rendering-focused tests, then full solution tests for shared-path changes.

## Critical Invariants

- keep rendering thread-safety intact (`TerminalScreen.SyncRoot` lock boundaries in draw handlers and state updates).
- maintain deterministic fallback behavior when shaping or interop validation fails.
- preserve cursor/selection behavior after font-size, theme, or resize changes.
- maintain endpoint-first input routing semantics when rendering and input paths intersect.
- keep render mode fallback deterministic when a backend is unsupported or native interop is unavailable.

## Validation Gate

Minimum rendering checks:
- `tests/RoyalTerminal.Tests/RenderingTests.cs`
- `tests/RoyalTerminal.Tests/HeadlessSkiaRenderingTests.cs`
- `tests/RoyalTerminal.Tests/TerminalScreenTests.cs`
- interop-focused suites when applicable:
  - `tests/RoyalTerminal.Tests/RenderingContractsTests.cs`
  - `tests/RoyalTerminal.Tests/RenderingInteropTests.cs`
  - `tests/RoyalTerminal.Tests/RenderingSkiaInteropTests.cs`
  - `tests/RoyalTerminal.Tests/RenderingAvaloniaAdapterTests.cs`

Then run:
- `dotnet test RoyalTerminal.sln -c Release`

## Code Examples

### Configure managed renderer options through `TerminalControl`

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;

TerminalControl control = new()
{
    FontFamilyName = "Consolas",
    TerminalFontSize = 14,
};

SkiaTerminalRenderer? renderer = control.Renderer;
if (renderer is not null)
{
    renderer.EnableTextShaping = true;
    renderer.EnableLigatures = true;
    renderer.TextDirectionMode = TextDirectionMode.Auto;
    renderer.EnableTextRenderDiagnostics = true;
}
```

### Read and reset text-render diagnostics snapshot

```csharp
SkiaTerminalRenderer? renderer = control.Renderer;
if (renderer is not null)
{
    TextRenderDiagnostics snapshot = renderer.GetTextRenderDiagnostics(reset: true);
    Console.WriteLine(
        $"Shaped={snapshot.ShapedRuns}, " +
        $"Fallback={snapshot.FallbackRuns}, " +
        $"FallbackFont={snapshot.FallbackFontHits}, " +
        $"Clamped={snapshot.GridClampedRuns}");
}
```

### Demo runtime toggles for shaping and diagnostics

```bash
ROYALTERMINAL_DISABLE_TEXT_SHAPING=1 \
ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS=1 \
dotnet run --project samples/RoyalTerminal.Demo
```
