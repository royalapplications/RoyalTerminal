# PR Summary: Optional Pretext Text Rendering Pipeline

## Overview

This PR adds an optional PretextSharp-backed text rendering pipeline for RoyalTerminal and optimizes the Skia text path for terminal workloads. HarfBuzz remains the default runtime rendering pipeline. The Pretext pipeline is compiled by default through the stable PretextSharp `0.1.0` NuGet packages and can still be excluded with `RoyalTerminalEnablePretextTextPipeline=false`.

The main goal is to reduce text rendering overhead in color-fragmented terminal rows, where the existing HarfBuzz path spends significant time shaping and drawing many small runs. The latest round also brings HarfBuzz memory usage in the benchmark down to the same per-frame allocation class as Pretext for simple ASCII rows.

## Changes

### Optional Pretext build wiring

- Adds `RoyalTerminalEnablePretextTextPipeline` MSBuild plumbing in `Directory.Build.props`.
- Pins the stable PretextSharp `0.1.0` package set in `Directory.Packages.props`:
  - `Pretext.Contracts`
  - `Pretext`
  - `Pretext.SkiaSharp`
- Adds conditional package references from `RoyalTerminal.Rendering.Skia`.
- Defines `ROYALTERMINAL_PRETEXT_TEXT_PIPELINE` only when the optional pipeline is enabled.
- Removes the previous dependency on a sibling `../PretextSharp` source checkout.

### Public text pipeline selection

- Adds `TerminalTextRenderPipeline` with:
  - `HarfBuzz`
  - `Pretext`
- Adds `SkiaTerminalRenderer.TextRenderPipeline`.
- Adds `SkiaTerminalRenderer.IsPretextTextRenderPipelineAvailable`.
- Adds `TerminalControl.TextRenderPipeline` as an Avalonia styled property.
- Adds demo selection via:

```bash
ROYALTERMINAL_TEXT_RENDER_PIPELINE=pretext
```

- Adds a demo status-bar indicator showing the effective text rendering path:
  - `Text: HarfBuzz`
  - `Text: Pretext`
  - `Text: HarfBuzz (Pretext unavailable)`
  - `Text: cell fallback`

### Renderer optimizations

- Adds a Pretext run cache for prepared text runs.
- Uses Pretext prepared segment widths directly instead of re-measuring natural width through the full layout helper.
- Caches natural `SKTextBlob` instances for reusable Pretext runs.
- Avoids unnecessary clipping and transforms for natural simple runs.
- Adds a shared simple-row fast path for Pretext and safe HarfBuzz cases:
  - applies only to simple one-cell glyph rows,
  - rejects overlays, text-highlight overrides, decorations, sprites, graphemes, wide cells, hidden cells, and symbol glyph clip candidates,
  - groups glyphs by foreground color and typeface,
  - builds positioned row text blobs directly through `SKTextBlobBuilder.AddPositionedRun`.
- Uses the shared fast path for HarfBuzz only when shaping is not needed for the row:
  - text shaping is enabled and HarfBuzz is selected,
  - ligatures are disabled,
  - direction is not explicit right-to-left,
  - logical and measured cell widths match,
  - text-render diagnostics are disabled,
  - glyphs are printable ASCII one-cell glyphs.
- Replaces the simple-row single-glyph blob cache with a glyph-id cache so the row batch no longer retains unused per-glyph `SKTextBlob` instances.
- Keeps the existing HarfBuzz and fallback paths for complex text.
- Caches natural HarfBuzz blobs as well, reducing repeated blob construction in the existing path.

### Diagnostics

- Extends `TextRenderDiagnostics` with:
  - `PretextRuns`
  - `PretextFallbackRuns`
- Clears Pretext-related caches when relevant render settings change.

### Benchmarks and tests

- Adds a Pretext render benchmark scenario when the optional pipeline is available.
- Adds tests to verify:
  - Pretext can render without using HarfBuzz runs when available.
  - clamped Pretext runs avoid fallback.
  - diagnostics remain reset correctly.

## Performance

Latest benchmark run on May 1, 2026:

```text
Runtime: .NET 10.0.5
OS: macOS 26.4.1
Architecture: Arm64
CPU logical cores: 11
```

Render baseline:

```text
full-160x48 HarfBuzz: 4.608 ms/frame, 32,914.309 B/frame
pretext-full-160x48: 4.340 ms/frame, 32,914.309 B/frame
```

Compared with the pre-HarfBuzz-row-batch measurement on this PR branch (`6.854 ms/frame`, `1,969,662.926 B/frame`), the latest HarfBuzz path is approximately 1.49x faster and allocates about 59.8x less per frame in the full 160x48 benchmark.

HarfBuzz and Pretext now have effectively matching allocation profiles for this simple-row workload. The two timings are close enough that their relative order can vary between benchmark runs on the same machine.

## Validation

The following commands passed:

```bash
dotnet build src/RoyalTerminal.Rendering.Skia/RoyalTerminal.Rendering.Skia.csproj
dotnet build src/RoyalTerminal.Rendering.Skia/RoyalTerminal.Rendering.Skia.csproj -p:RoyalTerminalEnablePretextTextPipeline=false
dotnet build samples/RoyalTerminal.Demo/RoyalTerminal.Demo.csproj
dotnet build samples/RoyalTerminal.Demo/RoyalTerminal.Demo.csproj -p:RoyalTerminalEnablePretextTextPipeline=false
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj --filter RenderingTests
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj --configuration Release
git diff --check
```

All validation commands succeeded with zero errors.

## Review Notes

- The Pretext pipeline is intentionally optional and does not change default behavior.
- The simple-row fast path is narrowly gated to avoid changing behavior for complex text, decorations, overlays, wide glyphs, grapheme clusters, sprites, symbol clip candidates, highlighted text, ligature shaping, or explicit RTL shaping.
- The batched simple-row path trades a small amount of per-frame managed allocation for a large reduction in Skia draw calls and frame time.
- HarfBuzz remains the fallback for cases Pretext cannot handle or when the optional pipeline is unavailable.
