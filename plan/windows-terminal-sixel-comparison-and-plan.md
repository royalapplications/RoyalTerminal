# Windows Terminal Sixel Comparison And Implementation Plan

## Source Baseline

- Microsoft Terminal repository: `https://github.com/microsoft/terminal`
- Revision inspected locally: `c17029a` (`2026-04-26`, `Update dictionaries (#20148)`)
- Primary Windows Terminal files reviewed:
  - `/tmp/microsoft-terminal/src/terminal/parser/OutputStateMachineEngine.cpp`
  - `/tmp/microsoft-terminal/src/terminal/adapter/SixelParser.hpp`
  - `/tmp/microsoft-terminal/src/terminal/adapter/SixelParser.cpp`
  - `/tmp/microsoft-terminal/src/buffer/out/ImageSlice.hpp`
  - `/tmp/microsoft-terminal/src/buffer/out/ImageSlice.cpp`
  - `/tmp/microsoft-terminal/src/renderer/atlas/AtlasEngine.cpp`
  - `/tmp/microsoft-terminal/src/terminal/adapter/adaptDispatch.cpp`
  - `/tmp/microsoft-terminal/src/types/colorTable.cpp`
  - `/tmp/microsoft-terminal/src/types/utils.cpp`

## High-Level Comparison

| Area | Windows Terminal | RoyalTerminal Before This Pass | Action |
| --- | --- | --- | --- |
| Parser dispatch | DCS `q` is identified by the VT state machine and streamed to `SixelParser::DefineImage`. Unsupported DCS strings are ignored by the DCS state machine. | Managed VT buffered DCS payloads and decoded them at ST. DCS identification was correct and guarded against DECRQSS `$q`. | Keep buffered approach because it is simpler and bounded by `SixelDecoderOptions.MaxInputBytes`; no behavioral gap for image payloads. |
| Runtime enablement | Sixel support is a terminal feature, while DEC private mode 80 controls sixel display mode. | Sixel rendering had an app-level enable/disable setting but did not implement DECSDM (`CSI ? 80 h/l`). | Implement DECSDM state, query responses, reset behavior, and display-mode placement. |
| DCS parameters | `P1` macro parameter sets initial pixel aspect ratio. `P2` selects transparent or opaque background. `P3` is a VT240 background color feature. | `P2` background select was parsed. `P1` was ignored. | Implement `P1` aspect mapping. Keep VT240 `P3` out of scope because RoyalTerminal does not emulate VT240-specific color-map protection. |
| Raster attributes (`"`) | DECGRA updates pixel aspect ratio as `ceil(Pan/Pad)`, clamps it, tracks declared background width/height, and performs a carriage return. Existing pixels are not rescaled or shrunk. | Width/height were honored, but aspect ratio and the implicit carriage return were missing. | Implement aspect updates, declared size handling, and carriage return behavior. |
| Sixel pixels | A sixel row is six virtual pixels, vertically expanded by the active pixel aspect ratio. | Every sixel bit produced one output row. | Implement vertical device-pixel expansion. |
| Color table | Starts with VT340 colors plus xterm 256-color extension. Color numbers map modulo the color table. HLS uses DEC orientation: blue at 0 degrees, red at 120, green at 240. Invalid color models are ignored. | First 16 colors existed, extended registers were grayscale, color numbers above the limit failed, and HLS used conventional CSS orientation. | Implement xterm extension, modulo mapping, DEC HLS orientation, and invalid-model ignore behavior. |
| Image storage | `ImageSlice` stores one image slice per text row and participates in row/cell copy and erase operations. | Whole decoded images are stored as `TerminalRasterImageSource` plus absolute-row placements. Text mutation/scroll/reflow clears or remaps placements. | Keep whole-image placement model. It is better for reusable bitmap caching and existing Kitty/sixel sharing. Existing row mutation and reflow support covers the same visible behavior. |
| Renderer | Renderer visits visible rows, uses row image revisions, and uploads changed image rows to the atlas. | Renderer previously resolved and uploaded images before sufficient culling; this was already fixed with viewport culling, content-fingerprint cache, bounded LRU, and diagnostics. | Keep existing culling/cache design. It avoids decoding/upload work for offscreen placements and deduplicates repeated `img2sixel` output. |
| Streaming output | Windows Terminal can flush partial image buffers during long streams while avoiding excessive video flushes. | RoyalTerminal decodes complete DCS payloads after ST. | No change for current scope. Full DCS buffering is adequate for `img2sixel` and bounded by configuration. Streaming partial flush would be a future feature if we need progressive rendering before ST. |
| DRCS sixel fonts | Windows Terminal also reuses sixel parsing for DECDLD downloadable fonts via `FontBuffer`. | RoyalTerminal has no DRCS font download/rendering subsystem. | Not implemented here. This is separate terminal-font emulation, not image rendering. |

## Implemented Plan

### 1. Decoder Parity

Status: completed.

- Parse the sixel DCS macro parameter (`P1`) and initialize pixel aspect ratio using the Windows Terminal mapping:
  - `0,1,5,6 => 2`
  - `2 => 5`
  - `3,4 => 3`
  - all other values => `1`
- Add `SixelDecoderOptions.MaxPixelAspectRatio` as a resource guard.
- Apply the active aspect ratio when drawing sixel bit columns.
- Handle DECGRA raster attributes:
  - update aspect ratio from `Pan;Pad` using rounded-up division
  - honor declared device-pixel width and height
  - reset the sixel graphics cursor X position
  - preserve already drawn content dimensions
- Improve color behavior:
  - use DEC HLS hue orientation
  - initialize extended registers with the xterm 6x6x6 cube and grayscale ramp
  - map color numbers modulo the configured table size
  - ignore unknown color models instead of treating them as RGB
- Add decoder coverage for raster attributes, aspect scaling, color wrapping, and DEC HLS behavior.

### 2. Managed VT Integration

Status: completed.

- Add DEC private mode 80 (`CSI ? 80 h/l`) for sixel display mode when sixel support is enabled.
- Add DEC mode query support for `CSI ? 80 $ p`.
- Reset DECSDM on soft/full reset and when the app-level sixel setting is disabled.
- In normal mode, preserve current behavior: image anchors at the current cursor, scrolls as needed, and advances the cursor.
- In display mode, match Windows Terminal semantics for the important observable behavior:
  - render from viewport home
  - clamp by renderer clipping instead of scrolling the text buffer
  - leave the text cursor unchanged
- Add managed VT tests for DECSDM placement and query responses.
- Native Ghostty mode benefits through the existing managed overlay processor because the overlay uses `BasicVtProcessor`.

### 3. Rendering And Scrolling Performance

Status: already implemented before this comparison pass and retained.

- Renderer culls raster/Kitty placements against the viewport before resolving image payloads or creating Skia bitmaps.
- Raster and Kitty bitmap caches are keyed by decoded content fingerprint instead of image id, so repeated `img2sixel` frames share one uploaded bitmap.
- The caches are bounded by `ImageBitmapCacheBudgetBytes` and protect current-frame entries from eviction.
- `ImageRenderDiagnostics` exposes placement visits, visible placements, draws, cache hits/misses, evictions, and cache size.

Windows Terminal's `ImageSlice` row model is not copied directly. RoyalTerminal's whole-image placement model now avoids the expensive parts that made scrolling slow while keeping images reusable across repeated placements and across sixel/Kitty rendering. If profiling later shows a bottleneck with tens of thousands of retained placements, the next targeted optimization should be a row-range placement index inside `TerminalScreen`, not a full rewrite to row-slice pixel storage.

## Validation Plan

- Run focused sixel and image-render tests:
  - `dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Debug --filter "Sixel|ManagedSixel|GhosttyVtProcessor_Sixel|SkiaTerminalRenderer_Raster|SkiaTerminalRenderer_Kitty"`
- Run the full RoyalTerminal test project:
  - `dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Debug`
- Manual smoke tests:
  - `img2sixel -w 800 "/tmp/Sixels1.png"; echo ""`
  - `for i in {1..100}; do img2sixel -w 800 "/tmp/Sixels1.png"; echo ""; done`
  - toggle app-level sixel support off/on and confirm disabled mode ignores sixel payloads
  - send `printf '\033[?80h'; img2sixel -w 800 "/tmp/Sixels1.png"; printf '\033[?80l'` and confirm display mode renders at viewport home without cursor movement

