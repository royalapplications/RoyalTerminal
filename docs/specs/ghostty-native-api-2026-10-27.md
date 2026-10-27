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
compile without warnings. Runtime and regenerated manifest results must be
recorded after the updated native build is available; an old library cannot
validate the added API.
