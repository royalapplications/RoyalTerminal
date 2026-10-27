# Ghostty native API update — 2026-10-27

The public VT header delta from `246f702876b924a1cb7cade1e99274d1470302fc` to
`4a4fc017940bec38c93b278c10c6a18c591f3e63` is accounted for below. The upstream
[headers at this revision](https://github.com/ghostty-org/ghostty/tree/4a4fc017940bec38c93b278c10c6a18c591f3e63/include/ghostty/vt)
define the native binding contract. Windows Terminal and xterm.js do not expose
this C API; these wrapper changes preserve Ghostty's parser and ownership
semantics rather than selecting different VT behavior. Cross-engine behavior
and shared rendering remain covered by the companion implementation audit.

| Public header change | Binding and wrapper disposition |
| --- | --- |
| Unified render-image iterator; protocol/layer/Kitty-kind enums; geometry/data keys; placement-list generation | `GhosttyVtNative.RenderImages.cs`; reusable iterator and owned geometry values in `GhosttyRenderState.Images.cs`. Fixed, relative, and placeholder placements now share this API. Pixels remain terminal-owned and must be copied while terminal synchronization is held. |
| Snapshot image budget and page/image progress | New options/data/progress enums and typed `GhosttySnapshotDecoder` helpers. `Next()` can return image payloads between history pages; consumers must check `GetProgressKind()` before page-only progress queries. |
| Official Kitty animation tick | Uses `ghostty_kitty_graphics_animation_tick`. The C API returns an absolute monotonic deadline; `AdvanceAnimations()` converts it to the existing host delay contract. |
| Generalized bounded VT write | Binds boundary flags, string kind/introducer/end, sized write result and `ghostty_terminal_vt_write_until`. `WriteUntilGround()` remains a compatibility wrapper over the official ground flag. There is no second parser. |
| Official window resize callback | Binds tagged grid request and callback option 47. Existing `SetWindowResizeCallback()` roots an adapter, ignores unknown tags and unregisters before borrowed/leased native lifetimes outlive the wrapper. Removes use of the corresponding custom native export. |
| Prompt-relative viewport delta | Adds the enum/union member and `DeltaPrompts(nint)` constructor. |
| CSI 19 t size report | Adds the screen-size report style, available through the existing size-report encoder. |
| OSC 3008 enum naming | Adds upstream `HierarchicalContext`; retains `ContextSignal` as a source-compatible alias for value 25. |
| Formatter/selection trim, paste and existing terminal API documentation | No layout or signature change. Existing declarations remain ABI-compatible; behavior changes belong to the native pin and managed parity work. |
| `ghostty_surface_paste_text` in the standalone application header | Outside the VT library. RoyalTerminal embeds libghostty-vt and uses its terminal paste API; it does not create standalone Ghostty application surfaces. |

The full native VT export inventory contains 205 functions. All have native
bindings. The five helpers in `wasm.h` are explicitly limited upstream to the
Wasm module and are not exported by native builds. The ABI audit now checks
all native function imports/exports in addition to enums, struct layouts,
callbacks, packed aliases and constants, so future added or removed entry
points fail the update audit.

Focused integration coverage lives in `GhosttyWriteBoundaryTests`,
`GhosttyRenderImageTests`, `GhosttySnapshotImageProgressTests`, and the existing
window resize/animation tests. The GhosttySharp and integration test projects
compile without warnings. The freshly built macOS arm64 runtime validated
187 ABI types, 31 callback signatures and all 205 native entry points. The
initial focused API run passed 29 tests; the ABI/boundary/render run passed 171.

## Rendering compatibility and refresh cost

RoyalTerminal retains the existing storage iterator and shared placeholder
projection for drawing. The new public render iterator omits requested `c`/`r`
and maps every internally assigned placement ID to zero. Two anonymous
placements of one image can therefore expose identical public render data while
requiring different scaling when Skia's cell metrics are fractional: intrinsic
image pixels keep their size, while explicit cell extents scale with the cells.
There is no unambiguous metadata join for all such scenes. We do not guess from
geometry, depend on native hash-table iteration order, or add an overlay API.

`GhosttyRenderImageTests.AnonymousPlacementsWithDifferentScalingCanHaveIdenticalPublicRenderData`
demonstrates the information loss directly. Both-engine Skia pixel tests in
`NativeKittyRenderGenerationTests` preserve the distinct sizing behavior.
The complete new iterator remains available and tested for embedding consumers.

The native adapter does use the new placement generation to skip image scene
extraction after text-only updates. Both storage generation and native cell
metrics are checked as well; generations are committed only after successful
publication. Scroll, image pixel changes, resize and placeholder updates still
refresh their geometry. This also avoids allocating one placement object per
visible image on every text update.

Reproduce the local allocation/time measurement with:

```sh
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --native-kitty-text-refresh
```

Seven samples of 1,000 warmed text frames, macOS arm64/.NET 10, showed allocated
bytes fall from 144,000 to 64,000 with one image placement, and from 10,304,000
to 64,000 with 128 placements. The zero-placement baseline remains 64,000 bytes.
These are parser/mirror refresh measurements, not GPU frame times. A focused
allocation regression also checks resize, scrolling and changed pixels; the
renderer, animation, virtual-placement and overscan test group passed 76 tests.

## Review follow-up: wide selection and hierarchical context

Ghostty `4af7e19c7` fixes selection highlighting at wide-character boundaries.
RoyalTerminal's shared Skia overlays now select both cells when either half of a
wide glyph is selected. A linear selection ending on the right-edge spacer also
highlights the wrapped glyph on the next row; rectangular selections stay on
their original rows. Overwriting the spacer invalidates that following row even
when its text did not change. This applies to both native and managed engines,
endpoint selections and projected row spans, without allocating per row.

This follows Ghostty's rendering/copy agreement. Windows Terminal's
[`TerminalSelection.cpp`](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalCore/TerminalSelection.cpp)
adjusts DBCS endpoints, and xterm.js
[`SelectionService.ts`](https://github.com/xtermjs/xterm.js/blob/master/src/browser/services/SelectionService.ts)
also extends selections around zero-width wide-character tails.
`WideSelectionRenderingTests` checks fill, glyph foreground, reversed endpoints,
row spans, rectangle behavior and incremental repaint for both engines.
`GhosttySelectionTests` exercises copying across an actual native page boundary
for `8d8b1ce00`; managed selection formatting already walks global rows and has
no page-local boundary limitation.

The OSC 3008 rename (`2fa57fc45`) and metadata unescaping (`554ea6586`) were also
reviewed. The public C API only exposes the `HierarchicalContext` command type:
`osc.h` has no HC payload data keys, and `stream.zig` ignores HC at terminal-stream
level. Its lazy metadata-unescaping iterator is internal Zig API and arrives in
the native pin. No public payload fields are missing from the C# binding and no
private-memory access or extra parser is introduced. `OscParserTests` checks
the renamed enum on ordinary and escaped-metadata input and retains the old
`ContextSignal` alias for source compatibility.

The bounded-scroll review also found an adapter coordinate bug when history is
disabled. Ghostty's `PageList.scrollbar()` intentionally reports only the active
rows and offset zero, while `Screen` grid coordinates still include unused
allocation-page rows. The adapter now derives that hidden prefix from the
official total-row API and keeps it separate from the visible scrollbar range.
This preserves links after repeated scrolling and fixes the same mapping for
word/line selection, copying, search and screen snapshots, including the
alternate screen. No native overlay is needed. The four
`GhosttyZeroHistoryCoordinateTests` cover these paths and ensure that hidden
rows never become visible history or overscan; the bounded pruning regression
also checks one- and three-row terminals with zero and finite history.
