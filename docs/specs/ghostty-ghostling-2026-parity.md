# Ghostty and Ghostling parity update (2026-09-22)

## Scope and pinned references

This update moves the `external/ghostty` submodule from
`a60cd15bb5a197d8e2596e86442031cbece06bcc` to the then-current Ghostty `main`
commit `22391ed6491f2924361dcad1f9a9176a390fd20f`.

The comparison also used:

- Ghostling `63842bf8e5e481160f81d348da9ff6fd27986798`. Ghostling is a useful minimal C
  consumer, but its Ghostty pin (`f64f4ac...`) predates the Ghostty commit above.
  Therefore, the current Ghostty C API is authoritative where they differ.
- Windows Terminal `7c92ecd037476f957809d0813b14d8bc44bb071a`, especially
  `ConptyConnection::_OutputThread`.
- xterm.js `c58ea3637f3968e0e6e79cd92cf9aace7ef89ee2`, especially `WriteBuffer`.

PowerShell was not changed or emulated by this work. No shell startup, command
invocation, environment, prompt, or PowerShell/ConPTY contract changed, so a
PowerShell source change was not required. The terminal still consumes the byte
stream produced by the configured transport.

## C API audit

The generated Ghostty type metadata and public header were compared against every
`LibraryImport` declaration in `RoyalTerminal.GhosttySharp`. All non-WASM exported
C declarations are bound. The only unbound exports are the five `ghostty_wasm_*`
allocation functions, which are WebAssembly-host functions and are not callable by
the desktop native libraries.

A second audit rebuilt the arm64 library from the final pin and compared its dynamic
symbol table with the managed entry points. The library exports 204 `ghostty_*`
symbols: all 198 public desktop functions are bound, and the remaining six are
private Highway/SIMD implementation helpers. The final two upstream commits after
the first review affect GTK GLES context selection and OpenGL DMA-BUF validation;
they do not change `libghostty-vt`, but the submodule is still pinned to the actual
current Ghostty head rather than an almost-current revision.

The update preserves older key and mouse helpers that remain exported while adding
the following surfaces.

| Ghostty area | Native binding and managed wrapper | RoyalTerminal integration |
| --- | --- | --- |
| Search | `GhosttySearch` owns needle, selection, iterator, and result lifetimes | Native search replaces formatting the entire scrollback and scanning a managed string |
| Snapshot | `GhosttySnapshot` supports buffer and streaming encode/decode, terminal restore, and continuation | Integration tests exercise buffer and managed `Stream` round trips plus continuation output |
| Parser continuation | `WriteUntilGround`, continuation buffer/alloc options, ground-state query | Exposed without copying native continuation storage |
| Render state | Structured cursor and colors, raw cell spans, dirty-row iteration, explicit clean | Screen synchronization only visits dirty rows and reads native cell spans |
| Synchronized output | Render-hold callback | The last complete frame remains visible during DEC mode 2026; a one-second safety timeout prevents a stuck application from freezing display updates |
| Clipboard | Multi-MIME read/write requests and replies, request metadata, grants, remember capability | Normalized terminal effects serve native and managed engines; legacy callback compatibility is retained |
| Paste | Terminal-aware writer-based paste API | Native Ghostty determines bracketed paste encoding when its mode agrees with the active terminal; the stateless encoder remains a safe fallback |
| Unknown sequences | Bounded unknown-sequence callback | APC payloads are surfaced consistently by both engines, including truncation metadata |
| Modes | Structured mode configuration, complete current constants, and generic get/set | Existing mode state now uses the current ABI; synchronized output, visibility reporting (2033), and paste events (5522) are included |
| Formatter | Streaming writer callback | `GhosttyFormatter.WriteTo(Stream)` avoids building one large intermediate buffer |
| Terminal options | terminfo, title reports, clipboard limit, continuation limit, resize scrollback pull | Explicit, bounded defaults are set by `GhosttyVtProcessor` |
| Result values | I/O, limit, and rejected errors | Managed result enum matches the current ABI |
| Platform support | TinyIO-backed current C API | The same bindings can use Ghostty's new Windows implementation when the updated Windows native asset is built |

ABI-sensitive structs and enum values have focused tests. Search, snapshot,
continuation, render cursor/raw-cell/dirty-state, and streaming formatter behavior
have native integration tests. Managed stream callbacks preserve Ghostty's synchronous
reader/writer contract and rethrow the original managed I/O exception after native
control returns.

The safe wrapper also covers every current `GhosttyTerminalData` family. The second
review added the remaining title/PWD setters, pending-wrap and cursor-style reads,
total/scrollback row counts, and default color/palette reads. Formatter instances
now retain a native terminal lifetime lease, and incremental snapshot decoders keep
their READY terminal alive until FINISH or decoder disposal. This enforces the C
API's borrowing rules even if managed callers dispose objects out of order. Partial
render-state construction now frees every already-created native handle on failure.

## Managed-engine parity

The managed VT engine cannot reuse Ghostty's internal Zig storage types, but its
observable terminal behavior and normalized application effects now match the new
interfaces where they apply.

| Behavior | Managed implementation |
| --- | --- |
| Clipboard write | Emits multi-MIME normalized writes and consumes the structured reply |
| Clipboard read | Emits a normalized read request and encodes the returned content as OSC 52 |
| Kitty clipboard | Implements bounded, strict OSC 5522 read/write/alias transactions, MIME listing and chunking, session grants, and one-time paste-event grants |
| Unknown APC | Supports ESC APC and C1 APC forms with the same 4096-byte bounded payload policy |
| Mode queries | Handles private and ANSI DECRQM, retains full integer mode values rather than truncating to a byte, and includes current visibility-reporting (2033) and paste-event (5522) state |
| Device reports | Matches Ghostty color-scheme (`CSI ? 996 n`) and visibility (`CSI ? 998 n`) responses; mode 2033 emits its immediate visibility report |
| In-band resize | Mode 2048 reports committed geometry immediately when enabled and after pixel-aware resizes, using Ghostty's floor-to-cell geometry |
| XTGETTCAP | Answers the complete 272-key Ghostty capability surface, including dynamic `TN`; a checked generator derives the managed table from the pinned Ghostty terminfo source |
| Permanent modes | Reports DECECM 117 as permanently reset, matching Ghostty's current DECRQM behavior |
| Title privacy | CSI 21 t is disabled by default and requires an explicit managed option, matching the native wrapper's opt-in policy |
| Unicode | Cell-width overrides are updated to Unicode 18; an exhaustive scalar-value parity test compares managed widths to the pinned native library |
| UTF-8 C1 | UTF-8-encoded U+0080..U+009F values are ignored in ground state, matching Ghostty; single-byte C1 controls retain their control meaning |
| Print throughput | Contiguous printable ASCII is processed as a run, avoiding a parser-state dispatch for every ordinary byte |
| Paste | The shared interface supports terminal-aware encoding; the managed engine continues to produce the same bracketed-paste protocol directly |

Native snapshot bytes are deliberately not treated as a cross-engine serialization
format. They encode Ghostty's terminal implementation. The managed engine retains
its existing engine-independent text snapshot contract, while native snapshot APIs
are fully exposed for native-terminal restore and continuation workflows.

Ghostty's background search thread is part of the full Ghostty application, not the
`libghostty-vt` C search iterator. RoyalTerminal keeps search orchestration in its
existing presentation/service layer and uses the native iterator for the scan.

OSC 99 and Kitty drag-and-drop were reviewed separately because their parsers exist
inside Ghostty. They are not current public `libghostty-vt` features: the stream
handler still classifies Kitty OSC 99 notification commands as unimplemented, and
the C terminal wrapper installs no drag-and-drop effect (`drag_and_drop = null`) or
public option. RoyalTerminal therefore does not invent a managed-only host contract
that the native backend cannot provide. OSC 9/777 notifications remain normalized
across both engines. This boundary should be re-audited when Ghostty exposes either
protocol through its C API.

## Performance work carried into RoyalTerminal

The updated native dependency directly includes Ghostty's intervening changes for
pooled page nodes, zero-initialized and cache-line-aligned cells, shared default
palettes, retained hash-map capacity, reduced page preheating, SIMD OSC/snapshot
and reflow paths, formatter improvements, and render-state access optimizations.

Managed and integration changes apply the same principles without copying Zig
internals:

- Raw render cells are consumed as a `ReadOnlySpan` instead of repeatedly crossing
  the native boundary for individual cell properties.
- Dirty-row iteration avoids rebuilding unchanged viewport rows, followed by an
  explicit `render_state_clean` acknowledgement.
- Search no longer formats all scrollback into a managed string before scanning.
- Streaming formatting, snapshot encoding/decoding, and continuation export avoid
  full-result allocations when the consumer or source is a stream.
- Printable ASCII runs use one tight managed loop and preserve the slower state
  machine only at control, non-ASCII, or line-drawing boundaries.
- Unicode width parity uses compact sorted ranges and binary search rather than a
  per-codepoint table or reflection.

## Dedicated terminal-output thread

The earlier draft queued parser drains on the shared thread pool and temporarily
changed a pool thread's priority. That approach did not establish thread affinity,
could consume multiple workers under bursts, and changed priority on a process-wide
shared resource.

`TerminalOutputWorker` is now a session-owned serial worker:

1. Transport callbacks enqueue into the existing bounded output queue.
2. Repeated notifications coalesce into one scheduled drain.
3. One named background thread (`RoyalTerminal.Output`) performs all VT parsing for
   that session at `BelowNormal` priority.
4. Parsed changes continue through the existing bounded UI-batch queue, so Avalonia
   objects are still updated only on the UI thread.
5. Resize, explicit flush, process exit, and stop use a barrier before touching
   state that depends on all earlier output.
6. Disposal signals and joins the worker, preventing parser work from surviving the
   session that owns it. Worker failures are captured and rethrown at the next
   scheduling or flush boundary.

This follows Ghostty's ownership model: its PTY read thread feeds a single termio
thread through a mailbox, while rendering remains separately scheduled. It also
matches the important properties of the reference implementations:

- Windows Terminal owns a dedicated ConPTY output thread, queues the next overlapped
  read before delivering the previous output, and waits for that thread during
  shutdown so pending output events are complete.
- xterm.js `WriteBuffer` serializes input, coalesces scheduling, has a synchronous
  flush barrier for operations such as resize, limits queued data, and yields to the
  renderer during long write bursts.

RoyalTerminal intentionally keeps transport reading in the transport implementation
instead of adding another duplicate PTY reader. The new worker is the third stage of
the existing transport -> parser -> UI-render pipeline. Existing byte and batch
limits remain the source of backpressure.

## Validation requirements

- Build the release native library with Zig 0.16 using `scripts/build-native.sh --release`.
- Build the complete .NET solution with no warnings.
- Run all xUnit unit and native integration tests.
- On platforms for which native assets are published, rebuild those assets from the
  new submodule pin before release. The repository's macOS arm64 native binary was
  used for the API integration tests in this change; generated binaries remain
  excluded from source control.
