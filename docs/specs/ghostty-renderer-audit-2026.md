# Ghostty renderer and native bridge audit

Audit range: `a60cd15` → `22391ed6491f2924361dcad1f9a9176a390fd20f`.
Reviewed against the pinned source on 2026-09-22. This is an applicability and
implementation audit, not a claim that every upstream renderer feature is
implemented by RoyalTerminal.

## Execution boundaries

RoyalTerminal's native VT processor calls `libghostty-vt`, copies its render
snapshot into `TerminalScreen`, and uses RoyalTerminal's renderer. The VT
library contains upstream terminal/parser/Kitty storage changes, but does not
run `src/renderer/generic.zig` or Ghostty's Metal/OpenGL renderer loops.

`native/ghostty-renderer-capi/src/main.zig` is a separate RoyalTerminal-owned
interop implementation, despite its name. Its `renderMetalToTarget` writes a
background buffer to an external Metal texture; `renderGenericToTarget`
validates target descriptors and produces a synchronization token; its CPU
path calls `fillRgbaFallback`. It does not import Ghostty's terminal renderer,
font grid, glyph atlas, `LatestFrame`, GTK DMA-BUF exporter, or DisplayLink.
These paths must not be presented as the complete upstream Ghostty renderer.

The actual terminal image/text renderer is
`src/RoyalTerminal.Rendering.Skia/Rendering/SkiaTerminalRenderer.cs`, with
font selection in `RoyalTerminal.Rendering.Text/TextShaping/TerminalFontResolver.cs`.
Its shaping scratch uses stack spans/pooled arrays and caches shaped runs;
the bitmap cache is keyed by dimensions, payload length, and pixel fingerprint.

## Changes transferred and verified

| Upstream change | RoyalTerminal implementation and evidence |
|---|---|
| [`aee7bf347`](https://github.com/ghostty-org/ghostty/commit/aee7bf347) animation ticks; [`73903f76a`](https://github.com/ghostty-org/ghostty/commit/73903f76a) current animation frame through C image data | Added the repository-owned `ghostty_royal_kitty_graphics_animation_tick` export, calling upstream `ImageStorage.animationTick` in the same library/module. `GhosttyVtProcessor` implements `ITerminalTimedRefreshSource`; the presentation timer runs without new PTY input. Native tests advance red/blue frames using a deterministic clock and verify stopped animations stop requesting ticks. |
| [`169213cd2`](https://github.com/ghostty-org/ghostty/commit/169213cd2), [`b17abd96d`](https://github.com/ghostty-org/ghostty/commit/b17abd96d) fuse owned copy and pixel-format conversion | `GhosttyKittyGraphicsImage.CopyRgbaData` expands borrowed RGB/grayscale pixels directly into one owned RGBA array. There is no intermediate managed native-payload copy. All four pixel formats and allocation size have focused tests. Skia already uploads explicitly as RGBA8888/unpremultiplied, so it does not need Ghostty's GPU-specific BGRA swizzle. |
| Image/storage generation-based upload avoidance | `GhosttyVtProcessor` caches visible image sources by upstream generation, skips clean storage entirely, re-evaluates placement geometry without recopying image bytes, and compares placement values before replacing graphics. Tests cover same-sized retransmission and text-only updates not dirtying unrelated rows. This also avoids rehashing unchanged pixels. |
| [`e3056658d`](https://github.com/ghostty-org/ghostty/commit/e3056658d), [`74a233b54`](https://github.com/ghostty-org/ghostty/commit/74a233b54) faster render-state reads | Native improvements are included by the dependency update. The managed processor now batches required style/grapheme-length reads with `get_multi`. Wrapper success paths no longer allocate eagerly interpolated diagnostic strings. Per-cell allocation regression tests and the `--ghostty-render-metadata` benchmark compare the legacy reader, allocation-free individual reads, and batched reads. |
| [`0d37f2d34`](https://github.com/ghostty-org/ghostty/commit/0d37f2d34), [`b4079f00c`](https://github.com/ghostty-org/ghostty/commit/b4079f00c) dirty-row iterator and structured cursor | Native integration reads cursor metadata in one call and uses the dedicated dirty-row iterator plus `Clean`. It does not walk/copy all rows for a one-row update. |
| [`9490f7134`](https://github.com/ghostty-org/ghostty/commit/9490f7134), [`f5b3efe45`](https://github.com/ghostty-org/ghostty/commit/f5b3efe45), [`c5a3c7e2e`](https://github.com/ghostty-org/ghostty/commit/c5a3c7e2e), [`52190a5d8`](https://github.com/ghostty-org/ghostty/commit/52190a5d8) relative placement, offsets and source clipping | Native integration obtains placement source/destination/viewport geometry from the current C render-info API, rather than independently reimplementing native placement arithmetic. Managed Kitty placement parity is covered separately in the main parity report. |
| [`e50779498`](https://github.com/ghostty-org/ghostty/commit/e50779498) synchronized-output render hold | The callback publishes a completed prefix before a hold even when prefix/hold/content arrive in one write. A timed refresh expires the hold after one second without requiring more PTY output. Focused tests cover both cases. |

Ghostling `main.c`'s Kitty drawing example performs a texture upload for every
draw and warns that production renderers should cache images. It does not drive
the new native animation clock. RoyalTerminal therefore follows upstream
Ghostty storage/generation/timing semantics, not this deliberately minimal
sample's per-frame upload strategy.

## Renderer work that is not automatically inherited

These remain implementation/review items until the corresponding RoyalTerminal
behavior is present and tested. A missing public C API alone does not exclude
an applicable feature from the user's requested scope.

| Upstream changes | Current execution evidence and required disposition |
|---|---|
| [`ca8868a29`](https://github.com/ghostty-org/ghostty/commit/ca8868a29) allocation-free whole-grapheme font selection | Audit found the span overload resolved only the **first rune**. A correction now checks every substantive cluster component and discovers candidates lazily, with no cluster-key/candidate-list allocation. Deterministic tests use the pinned JetBrains Mono/Noto Emoji font fixtures and a controlled matcher; validation is recorded below when completed. |
| [`6f02d9aad`](https://github.com/ghostty-org/ghostty/commit/6f02d9aad) rasterize downloaded glyf directly into output bitmap, and associated APC glyph limits | Bounded managed outline decoding and direct reusable Skia paths are implemented and tested below. Live glossary/protocol integration, native extraction, placement, cache invalidation and terminal-row drawing remain open; these foundations alone do not enable the feature in either processor's presentation. |
| [`5beb94c16`](https://github.com/ghostty-org/ghostty/commit/5beb94c16) / `88abb77b1` apply font-thicken to IME preedit | No `font-thicken` setting or matching preedit glyph raster path exists in the current RoyalTerminal renderer. The upstream fix cannot be applied merely by updating the library. Any new thickness option must affect ordinary and preedit text consistently and have rendering tests. |
| [`6688aa072`](https://github.com/ghostty-org/ghostty/commit/6688aa072), [`97f57edcc`](https://github.com/ghostty-org/ghostty/commit/97f57edcc), [`72cf50855`](https://github.com/ghostty-org/ghostty/commit/72cf50855) idle DisplayLink, unfocused dirty redraws, lock-order fix | There is no Ghostty DisplayLink in RoyalTerminal. The equivalent obligations belong to Avalonia presentation scheduling: idle work must stop, dirty unfocused surfaces must still present, and stopping presentation must not invert terminal/render locks. The third-thread/presentation audit and tests must establish these obligations. |
| [`c4e16970a`](https://github.com/ghostty-org/ghostty/commit/c4e16970a), [`4b4a5b241`](https://github.com/ghostty-org/ghostty/commit/4b4a5b241), [`a177ba90a`](https://github.com/ghostty-org/ghostty/commit/a177ba90a) hidden GPU resources, Metal callback teardown, DisplayLink failure | Ghostty-specific objects are absent. RoyalTerminal still needs its own renderer detach/dispose/visibility ownership review; the absence of those object types does not prove equivalent lifecycle behavior. |
| [`de1336fad`](https://github.com/ghostty-org/ghostty/commit/de1336fad), [`131b293db`](https://github.com/ghostty-org/ghostty/commit/131b293db), [`c454a3bf4`](https://github.com/ghostty-org/ghostty/commit/c454a3bf4) Metal/font warmup | No matching Ghostty command queue/pipeline/font warmup code runs here. Skia/Avalonia owns those GPU resources. Cold-start profiling is required before adding an equivalent warmup to the actual renderer. |
| [`a925a97e3`](https://github.com/ghostty-org/ghostty/commit/a925a97e3), [`4ff699343`](https://github.com/ghostty-org/ghostty/commit/4ff699343), [`22391ed64`](https://github.com/ghostty-org/ghostty/commit/22391ed64), related GTK/EGL/export changes | RoyalTerminal does not export Ghostty's DMA-BUF/GTK frames or call its OpenGL presentation code. These are not fixes executed by the libvt pin. RoyalTerminal interop descriptor/ownership/synchronization tests remain necessary for its separate external-target bridge. |
| [`afc79b8cc`](https://github.com/ghostty-org/ghostty/commit/afc79b8cc), [`daeed25b3`](https://github.com/ghostty-org/ghostty/commit/daeed25b3), [`d166c05ed`](https://github.com/ghostty-org/ghostty/commit/d166c05ed), [`28b5bf905`](https://github.com/ghostty-org/ghostty/commit/28b5bf905) CoreText emoji lookup/OOM/display names and embedded emoji fonts | Skia font-manager calls, not Ghostty CoreText wrappers, execute here; RoyalTerminal does not ship Ghostty's embedded Noto emoji asset. Font availability and cluster coverage therefore need RoyalTerminal-specific testing, not assumed equivalence. |

## Downloaded-glyph implementation foundation (2026-09-23)

`TerminalGlyphDecoder` and `TerminalGlyphOutline` provide renderer-independent,
owned, read-only outline data. They follow the pinned Ghostty `glyf.zig` decoder
and `glyph/request.zig` resource limits: 64 KiB decoded payload, at most 5,461
12-byte points per allocation, monotonic contour ends, bounded repeat expansion,
signed/short/unchanged deltas, quadratic controls and full Int32 accumulation.
Composite records and hinting instructions produce the corresponding protocol
rejection categories; malformed records never return partial outlines. Bounding
box hints are not trusted, zero-contour header-only records and trailing bytes
match upstream. Validation precedes persistent allocations, and a warmed 1,000
malformed-record loop allocates zero bytes.

The decoder's result/reason agrees with native registration for every byte value
at every position in Ghostty's triangle fixture, every truncation, multi-contour
errors, short-vector records, empty records and both sides of the compressed-point
and payload limits. Native availability was confirmed, not inferred from a green
test that could skip its body. These checks validate acceptance/errors; they do
not yet compare extracted native point arrays.

`SkiaTerminalGlyphPath` preserves contour winding and uses native quadratic path
segments, including implied midpoints and first/last off-curve cases. A cached path
can draw directly into the destination canvas with a caller-provided foreground
and placement transform, avoiding an intermediate bitmap/copy. Real Skia pixel
tests cover a Y-flipped triangle, contour rotation, all-off-curve contours, holes
and empty/degenerate outlines. Repeated drawing allocates zero managed bytes;
this is not a claim about GPU/native allocations or end-to-end terminal speed.
All 19 focused decoder/path tests pass (`glyph-path-final.trx`).
The full macOS unit/headless run passes **2,143 / 16 conditional skips / 2,159
total**, zero failures (`glyph-foundation-full.trx`).

Reference decision: the pinned Ghostty implementation is authoritative for this
new protocol; Windows Terminal's SOS/PM/APC path ignores these strings and xterm.js
has no corresponding `25a1` handler. Ghostling has no glyph example. Neither is a
substitute for Ghostty's glossary semantics. The protocol summary is vendored in
`external/ghostty/src/terminal/apc/glyph.zig`, referencing the pinned Rio spec.

Important upstream boundary: at the pinned revision, `Terminal.glyphProtocol`
stores the glossary and sets a dirty flag, but production `Terminal.printCell`
does not consult it for width. Source references to `glyf_rasterize.rasterize`
are currently in rasterizer tests, not the application renderer. The protocol's
documented width/render intentions must not be reported as already wired upstream
application behavior. Integration work must distinguish that intended contract
from the observable current libvt behavior and test any chosen divergence.

Still required before claiming feature parity: bounded APC request/options/base64
handling; session-owned FIFO glossary and reset/disable/synchronized-output rules;
native glossary/outline extraction; system-font query coverage; width/layout
overrides; Ghostty-compatible sizing/alignment/padding; renderer cache ownership,
row invalidation and drawing in both engines; end-to-end differential/pixel tests.
The path helper is not yet wired into live terminal rendering. This remains an
explicit implementation requirement, not a scope exclusion.

## Whole-cluster fallback risk and implementation contract

Before this audit's correction, `TerminalFontResolver.ResolveTypeface(ReadOnlySpan<char>)` detected
emoji presentation from the full span, then passes only the first rune into
`ResolveTypefaceCore`. For `#` plus U+20E3 or a base plus an uncommon combining
mark, a primary font may cover the base but lack the rest. A HarfBuzz run can
then contain missing glyphs even though another available font covers the
whole cluster. The shaping cache does not fix an incorrectly selected typeface.

Upstream `font/shaper/run.zig` checks the primary candidate first, then lazily
checks the font selected for each remaining codepoint. Each candidate must
cover the base and every substantive component. U+FE0E, U+FE0F and U+200D are
ignored as standalone glyph-coverage requirements. Additional components do
not inherit the base's emoji/text presentation requirement. It returns the
first candidate covering the whole grapheme, without allocating a candidate
array. Upstream's regression uses `#` + U+20E3 explicitly.

The managed correction preserves candidate ordering and existing emoji
preference, inspects complete clusters using spans, reuses cached SKFont
instances, and never transfers ownership of the caller's primary typeface.
The existing per-codepoint cache stores candidates, not the final cluster
decision; every candidate is checked against the current complete span. This
prevents collisions between different clusters sharing a first rune without
allocating a cluster string key. If no complete candidate exists, the existing
non-null public contract returns the base candidate as best effort, leaving
missing components as `.notdef`; unlike upstream's nullable result, it does not
drop the entire cluster.

Reference comparisons: Windows Terminal's `AtlasEngine::_mapCharacters`
passes the complete text range to DirectWrite `MapCharacters`, not just its
first codepoint; xterm.js `TextureAtlas` draws the complete `chars` string into
the browser canvas and relies on browser font fallback. Ghostty's explicit
whole-cluster algorithm is the appropriate direct model for our explicit
Skia typeface selection.

New controlled-font tests cover a base-only primary font with a whole-cluster
fallback, VS/ZWJ sequences, first-rune cache collisions, no-covering-font
behavior, warm-cache allocations and resolver disposal. The fixtures come
from the pinned upstream submodule, so selection is independent of the
developer machine's installed fallback fonts.

## Measured native metadata improvement

On the macOS arm64 host using .NET 10, `--ghostty-render-metadata` measured the
median of seven samples of 200,000 reads after warmup. The workload reads
style, grapheme length, foreground and background for a styled ASCII cell;
it excludes parsing, screen copying, shaping and drawing. The legacy reader
reproduces the previous individual native reads plus eager diagnostic strings.

| Metadata path | Nanoseconds/cell | Allocated bytes/cell |
|---|---:|---:|
| Legacy individual reads | 158.80 | 384.04 |
| Individual reads, diagnostics only on failure | 53.20 | 0 |
| Batched required metadata, diagnostics only on failure | 47.70 | 0 |

All paths produced checksum `554400000`. This demonstrates the bridge
metadata improvement (about 3.33× for the combined change), not a claim of
3.33× end-to-end terminal rendering speed. Timing varies by host; allocation
regressions are also covered by focused tests.

## Validation scope

The extension package was cross-built for macOS arm64/x64, Linux arm64/x64 and
Windows GNU arm64/x64. On macOS arm64 the exported-symbol difference from the
plain upstream library is exactly the one repository-owned animation export.
Windows/MSVC and non-host runtime execution remain CI validation obligations;
cross-compilation is not runtime testing. Shell/Zig formatting checks passed.
Focused native integration and native processor tests cover the implemented
paths above; final suite/benchmark results belong in the main parity report.
