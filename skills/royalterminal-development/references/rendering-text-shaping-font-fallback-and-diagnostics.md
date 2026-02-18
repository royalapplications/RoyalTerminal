# Rendering Text Shaping Font Fallback And Diagnostics

## Table Of Contents

- [Scope And Primary Files](#scope-and-primary-files)
- [Shaping Components](#shaping-components)
- [Font Fallback Components](#font-fallback-components)
- [Run Caching And Placement](#run-caching-and-placement)
- [Runtime Options](#runtime-options)
- [Diagnostics Counters](#diagnostics-counters)
- [Demo Environment Toggles](#demo-environment-toggles)
- [Integration Constraints](#integration-constraints)
- [Validation And Regression Tests](#validation-and-regression-tests)
- [Code Examples](#code-examples)

## Scope And Primary Files

Primary shaping/fallback files:
- `src/RoyalTerminal.Rendering.Skia/Rendering/SkiaTerminalRenderer.cs`
- `src/RoyalTerminal.Rendering.Text/TextShaping/HarfBuzzTextShaper.cs`
- `src/RoyalTerminal.Rendering.Text/TextShaping/HarfBuzzTypefaceCache.cs`
- `src/RoyalTerminal.Rendering.Text/TextShaping/TerminalFontResolver.cs`
- `src/RoyalTerminal.Rendering.Text/TextShaping/ShapedRunCache.cs`
- `src/RoyalTerminal.Rendering.Text/TextShaping/TextRenderDiagnostics.cs`
- `samples/RoyalTerminal.Demo/Services/MainWindowController.cs`

## Shaping Components

`SkiaTerminalRenderer` text path:
- segments rows into runs with consistent style/color/typeface
- when `EnableTextShaping=true`, calls `HarfBuzzTextShaper.Shape(...)`
- uses shaped glyph ids/offsets to build and draw positioned text blobs

`HarfBuzzTextShaper` behavior:
- uses per-thread HarfBuzz buffer
- applies language/culture information
- supports direction modes:
  - `Auto`
  - `LeftToRight`
  - `RightToLeft`
- supports ligature disabling via HarfBuzz feature flags when requested

## Font Fallback Components

`TerminalFontResolver` behavior:
- checks primary typeface glyph coverage first
- resolves fallback via `SKFontManager.MatchCharacter(...)` when needed
- includes emoji-aware lookup paths for:
  - regional indicators
  - emoji presentation selectors
  - keycap/ZWJ/emoji-modifier sequences

Fallback result type:
- `TerminalFontResolution(SKTypeface Typeface, bool UsedFallback)`

`GlyphCache` provides base styled typefaces and font metrics used by renderer.

## Run Caching And Placement

`ShapedRunCache`:
- keyed by text hash, text length, typeface handle, font/cell metrics, direction, ligature option
- bounded capacity (clear-on-capacity strategy)

Grid placement modes inside `SkiaTerminalRenderer`:
- `Natural`: shaped width is close enough to grid run width
- `Clamped`: applies X scaling/clamping to fit grid tolerance
- `UnsafeFallback`: shaped run is considered unsafe for grid mapping and falls back to cell-anchored drawing

Fallback draw path:
- renders cell-anchored text via `canvas.DrawText(...)` per cell while clipping to run bounds

## Runtime Options

Key renderer options:
- `EnableTextShaping`
- `TextDirectionMode`
- `EnableLigatures`
- `EnableTextRenderDiagnostics`

Other related options affecting visual output:
- `CursorStyle`, `CursorColor`, `CursorVisible`
- `SelectionColor`, `SelectionStart`, `SelectionEnd`

## Diagnostics Counters

`TextRenderDiagnostics` snapshot fields:
- `ShapedRuns`
- `FallbackRuns`
- `FallbackFontHits`
- `GridClampedRuns`

Usage:
- `GetTextRenderDiagnostics(reset: false|true)`
- `ResetTextRenderDiagnostics()`

## Demo Environment Toggles

In demo controller (`MainWindowController`):
- `ROYALTERMINAL_DISABLE_TEXT_SHAPING`
- `ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS`

Applied in `ConfigureRenderer(...)`:
- shaping is disabled when `ROYALTERMINAL_DISABLE_TEXT_SHAPING` is enabled
- diagnostics collection is enabled when `ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS` is enabled

Note:
- these environment toggles are sample runtime configuration, not global library defaults.

## Integration Constraints

- preserve shaping fallback safety for grid alignment; do not bypass unsafe-fallback checks.
- clear shaped-run cache when direction/ligature/font/cell metrics change.
- keep fallback-font disposal and cache ownership rules intact.
- avoid per-frame allocations in hot paths; keep pooled/rented buffer patterns.
- when changing shaping behavior, validate cursor/selection/decorations remain aligned with grid cells.

## Validation And Regression Tests

Primary tests:
- `tests/RoyalTerminal.Tests/RenderingTests.cs`
- `tests/RoyalTerminal.Tests/HeadlessSkiaRenderingTests.cs`
- `tests/RoyalTerminal.Tests/TerminalScreenTests.cs`

Look especially at tests covering:
- HarfBuzz shaping stability
- font fallback consistency versus Skia `MatchCharacter`
- diagnostics counters behavior
- clamped vs fallback placement decisions

## Code Examples

### Configure shaping and diagnostics on a renderer

```csharp
using RoyalTerminal.Avalonia.Rendering;

SkiaTerminalRenderer renderer = new("Consolas", 14f)
{
    EnableTextShaping = true,
    EnableLigatures = false,
    TextDirectionMode = TextDirectionMode.LeftToRight,
    EnableTextRenderDiagnostics = true,
};
```

### Snapshot diagnostics after a render pass

```csharp
TextRenderDiagnostics diagnostics = renderer.GetTextRenderDiagnostics(reset: false);
Console.WriteLine(
    $"Shaped={diagnostics.ShapedRuns}, " +
    $"Fallback={diagnostics.FallbackRuns}, " +
    $"FontFallback={diagnostics.FallbackFontHits}, " +
    $"Clamped={diagnostics.GridClampedRuns}");
```

### Direct HarfBuzz shaping call

```csharp
using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;

using GlyphCache cache = new("Consolas");
using HarfBuzzTextShaper shaper = new();

TextShapingOptions options = new(
    FontSize: 14f,
    Culture: CultureInfo.InvariantCulture,
    Direction: TextDirectionMode.Auto,
    EnableLigatures: true);

ShapedTextRun run = shaper.Shape("RoyalTerminal shaping", cache.RegularTypeface, options);
Console.WriteLine($"GlyphCount={run.GlyphCount}, Advance={run.TotalAdvanceX}");
```

### Demo run with shaping disabled and diagnostics enabled

```bash
ROYALTERMINAL_DISABLE_TEXT_SHAPING=1 \
ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS=1 \
dotnet run --project samples/RoyalTerminal.Demo
```
