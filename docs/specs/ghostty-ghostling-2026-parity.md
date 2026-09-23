# Ghostty and Ghostling parity update (2026-09-22)

## Reopened completion audit

The complete upstream review is **in progress**. The prior API inventory and green
test suite establish the implemented C surface, but do not establish full native
and managed feature or performance parity. The [308-commit source inventory](ghostty-upstream-inventory-2026.md)
is the review input. The upstream heads were reverified on 2026-09-23. Ghostty
advanced by three documentation/contributor-list commits; Ghostling is unchanged.
The new Ghostty head changes no source, build, ABI, or renderer files.

The renewed review found the following missing or weakly verified requirements:

| Requirement | Evidence needed for completion | Current review state |
| --- | --- | --- |
| Managed Kitty graphics, relative placements, animation, validation, deletion and file-medium changes | Native/managed protocol and pixel/placement differential tests for the upstream graphics changes | Command parsing, bounded image loading, PNG/media providers, tracked anchors, graphics store and live APC execution/projection are implemented with focused tests, including direct/chunked load, resize, scrollback/anchor reprojection, alternate-screen restoration, timed animation advancement and delete selector families; virtual placeholder projection, protocol-response/streaming edge cases and full native differential coverage remain open |
| Native Kitty animation playback | Animation tick in the built library, scheduling without further PTY input, deterministic frame tests | Repository-owned extension and timed refresh implemented; deterministic frame tests pass, and all six native RID variants cross-build; non-macOS runtime execution remains CI validation |
| Synchronized output | Completed prefix visible at enable; following output frozen; release/reset/one-second timeout without additional input | Both engines now implement prefix publication, frozen presentation and idle timeout; managed copy-on-write state transfer and focused native/managed tests pass |
| Managed snapshot and continuation features | Restore both screens, history, parser/UTF-8 continuation, modes, styles, links and saved cursors with bounded validation | Managed ground-boundary processing, bounded continuation export and replay tests pass; Ghostty-compatible CRC32C framing and style/hyperlink codecs pass upstream golden, corruption, truncation and allocation tests; complete state codecs and incremental READY/history restore remain open |
| Managed resize/reflow optimization | Baseline/after measurements plus wide/grapheme/style/link/cursor/anchor regressions | Bulk reflow copies and redundant initialization removal implemented and measured below; tracked cell identity/reflow/COW/pruning regressions pass; end-to-end Kitty anchor comparisons remain part of graphics integration |
| Managed row allocation/recycling | Stable content/metadata after eviction and measured allocations | Evicted row storage is reused with a focused zero-allocation steady-state test |
| Parser/clipboard throughput and bounds | Split-input protocol tests, malformed UTF-8/base64 tests, limits, before/after measurements | Review added span payload scanning, bulk base64 decode, correct 64 MiB configurable clipboard bound, 65-codepoint grapheme bound and protocol fixes; measurement review pending |
| Third IO thread and render fairness | Dedicated lifetime/flush/failure tests, renderer demand handoff, batching/backpressure throughput and latency measurements | Dedicated worker, demand handoff and bounded gather/lease path implemented; isolated PTY throughput and allocation improvements measured; platform scheduling rejection handling and final lifecycle validation remain in review |
| Managed colors and VT formatter state | Dynamic override resets, pending-wrap and tabstop/cursor round-trip differential tests | OSC 104/110/111/112 resets and configured-vs-override state implemented; edge-cell replay, post-tabstop cursor home and CRLF replay tests pass; broader formatter audit remains open |
| Unicode 18 grapheme behavior | Authoritative generated grapheme/Indic/emoji properties and conformance tests, not only scalar widths | Full generated properties and boundary kernel pass official conformance/native comparisons; mode2027 on/off is implemented; differential testing found an upstream dirty-mark bug and a remaining managed right-edge spacer case |
| Platform renderer improvements | Per-change applicability evidence for Skia/native bridge vs upstream Metal/OpenGL, with relevant tests/measurements | Detailed inspection remains open; native dependency pin alone does not port full-app renderer behavior |

Rows above are requirements to finish, not exclusions from the requested scope.
Tests cited elsewhere in this document describe existing coverage; they must not
be used to mark these broader requirements complete until their specific evidence
has been inspected.

### Graphics viewport and lifecycle review (2026-09-23)

Managed image projection now retains immutable tracked-anchor recipes, including
offscreen placements. The screen resolves them against its own anchor state and
viewport on demand, matching Ghostty's `computeViewportPos` in
`src/terminal/c/kitty_graphics.zig`. Text output, history pruning, in-place row
movement and scrollback navigation no longer require another graphics command
to update placement positions. Synchronized-output screen copies share immutable
recipes and keep independent anchor positions. A cached projection is reused
until anchor revision or viewport geometry changes; 1,000 unchanged reads allocate
zero bytes in the focused allocation test.

The review also found and fixed dropped within-layer z-order in both integrations.
Snapshots now retain the exact signed z value and sort by z and then unsigned
image ID, following `src/renderer/image.zig`. A real Skia pixel test exercises
overlapping images through both processors, including an ID above `int.MaxValue`
and a placement update that changes only z.

Managed reset/erase integration now clears both stores on RIS, keeps the active
store on DECSTR, recalculates placement sizes on pixel-only resize, and handles
ED2 by clearing visible placements and unplaced data while retaining graphics
wholly in history. Native/managed tests exercise each case. For DECSTR graphics,
the chosen behavior follows Ghostty (graphics survive); xterm's image addon resets
its storage on DECSTR. Windows Terminal ignores Kitty APC strings, so it is not
an image-storage reference. xterm's renderer confirms viewport-relative rendering
from buffer content (`ydisp`), rather than retaining display-time row coordinates.

The combined graphics, renderer, anchor and synchronized-output run passes
118 tests on macOS arm64, with native Ghostty confirmed available in the test
output. This closes those specific regressions, not the remaining graphics or
whole-project parity requirements above.

## Scope and pinned references

### Graphics protocol differential follow-up (2026-09-23)

Eight command conversations initially disagreed with the actual native library.
The managed processor now matches native replies for animation frame numbers,
missing targets, queries during quiet chunked uploads, conflicting identifiers,
silent successful animation controls, image-number composition and stale-target
frame uploads. Image content generations change on displayed-frame edits,
composition, selection and ticks; a chunked frame cannot commit to a changed
target. Frame geometry/base validation precedes quota eviction, and failed
replacement mutations still invalidate published placements. Focused tests cover
both quota preservation and removal of stale relative children.

Both integrations suppress animation wakeups during synchronized output and
advance playback when publication resumes. Native prefix capture now starts the
animation clock before entering a hold, including control and DECSET in one input
chunk. Deterministic-clock tests exercise both processors. The combined graphics,
renderer, anchor, protocol and synchronized-output run passes **130 tests** with
the native library available on macOS arm64. These are targeted results, not a
claim of complete protocol or whole-project parity.

After rebuilding the native VT library and renderer bridge at `4ae9f1a2d`, the
full Debug unit/headless suite passes **1,716 tests**, with 16 conditional skips
and zero failures. The native integration suite passes **227 tests**, with no
skips or failures. This includes the dedicated output-worker, gather/ring and
session transport regression tests; it does not substitute for the remaining
platform/performance audit.

## Dependency revision

This update moves the `external/ghostty` submodule from
`a60cd15bb5a197d8e2596e86442031cbece06bcc` to the then-current Ghostty `main`
commit `4ae9f1a2de5484de3d6a13fe03676b8853b9c41c` (verified 2026-09-23).
The preceding reviewed head, `22391ed6491f2924361dcad1f9a9176a390fd20f`, has
identical runtime sources: the intervening commits update `.github/VOUCHED.td`
and remove `CLAUDE.md` only.

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

The [generated ABI inventory](ghostty-abi-inventory-2026.md) covers all 159
manifest types, 27 callback signatures, 17 header-only flags and 43 modes. Its
runtime tests check all enum values, aggregate sizes/alignment/field offsets and
primitive widths without reflection. This audit restored 35 omitted key values,
four side-specific modifier flags and missing OSC/RNG declarations. The focused
ABI/native wrapper/PNG run passed 170 tests before the subsequently described
upstream bug-fix overlays.

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

The managed VT engine cannot reuse Ghostty's internal Zig storage types. The
following implemented behaviors have focused coverage; this list is not a claim
that the open requirements above have already reached full parity.

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

Native snapshot bytes encode Ghostty's terminal implementation. The managed engine's
existing text snapshot contract does not restore equivalent terminal state and is
therefore not evidence of managed snapshot feature parity. Managed state snapshots
require additional implementation and validation. Parser continuation is now
implemented separately and has split-input/reset/limit/replay tests.

The current Ghostty version1 binary format itself omits Kitty images/placements and
glyph glossary registrations (`snapshot/terminal.zig`, field classification). Virtual
placeholder text is preserved, but the graphics are not. Native binary snapshot
wrappers therefore must not be described as complete graphics-preserving backups.
Managed compatible restoration will follow those documented format limits rather
than silently claiming graphics persistence that the native wire format lacks.

Ghostty's background search thread is part of the full Ghostty application, not the
`libghostty-vt` C search iterator. RoyalTerminal keeps search orchestration in its
existing presentation/service layer and uses the native iterator for the scan.

OSC 99 and Kitty drag-and-drop were reviewed separately because their parsers exist
inside Ghostty. They are not current public `libghostty-vt` features: the stream
handler still classifies Kitty OSC 99 notification commands as unimplemented, and
the C terminal wrapper installs no drag-and-drop effect (`drag_and_drop = null`) or
public option. OSC 9/777 notifications remain normalized across both engines.
An unimplemented upstream stream action offers no working behavior to port;
full-application drag-and-drop, however, needs a separate applicability and host
contract decision. The absent public C option alone does not establish completion
or exclude applicable functionality from this request.

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
- Unicode properties use a deduplicated two-level packed table generated from
  the pinned upstream Unicode 18 UCD, shared by width and grapheme lookup. The
  generated private tables initialize once: focused tests caught and eliminated
  debug-build allocations from repeated span-literal construction.

## Dedicated terminal-output thread

The earlier draft queued parser drains on the shared thread pool and temporarily
changed a pool thread's priority. That approach did not establish thread affinity,
could consume multiple workers under bursts, and changed priority on a process-wide
shared resource.

`TerminalOutputWorker` is now a session-owned serial worker:

1. Transport callbacks enqueue into the existing bounded output queue.
2. Repeated notifications coalesce into one scheduled drain.
3. One named background thread (`RoyalTerminal.Output`) performs transport-output VT
   parsing for that session. Explicit UI `WriteOutput` APIs retain their synchronous
   contract. Dedicated macOS gather/parser threads request Ghostty's user-initiated
   QoS, with a normal-priority fallback; shared thread-pool threads are never changed.
   Priority variants are being measured separately from gather throughput.
4. Parsed changes continue through the existing bounded UI-batch queue, so Avalonia
   objects are still updated only on the UI thread.
5. Resize, explicit flush, process exit, and stop use a barrier before touching
   state that depends on all earlier output.
6. Disposal signals and joins the worker, preventing parser work from surviving the
   session that owns it. Worker failures are captured and rethrown at the next
   scheduling or flush boundary.

Ghostty's current `Exec.zig` separates a gather thread from the IO reader/parser,
using four 64 KiB buffers, a 1 KiB saturation threshold, sixteen EAGAIN spins and
bounded 1 ms polling (3 ms bridge cap). Its termio thread primarily handles
control/write work. RoyalTerminal's Unix reader now implements that gather stage
with a generation-checked four-slot ownership ring and idle/cancellation wake
pipes. Optional leases pass buffer ownership through transport/session to the
parser; legacy public byte events still receive stable copies. Renderer demand
now causes bounded lock handoff at parser batch boundaries. Focused ordering,
full-ring/quiet shutdown and lease-lifetime tests pass; throughput, latency and
platform scheduling measurements are still being consolidated.
The reference implementations contribute these properties:

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

## Renewed audit: measured managed storage changes

The reflow port follows Ghostty `c249b9de3`, `ec5b36961` and `88ed6bebf` at the
behavioral level: copy complete normalized cell runs, retain the scalar path for
wide pairs that cross a destination edge or need normalization, and preserve
tracked cursor/anchor positions. Managed cells already contain managed references
and resolved style values, so they do not need native style-ID remapping. The row
reset follows `b31fbc846`: recycled storage loses wrap/transient flags, graphemes,
hyperlinks and prior visual state. Focused reflow and screen tests pass.

`TerminalReflowBenchmark` is runnable with `--terminal-reflow`. An isolated archive
of pre-review commit `b8b16e9` receives the same harness for comparison. One paired
Release/.NET 10.0.5 ARM64 run without concurrent builds (median of seven samples,
2,000 rows and thirteen resizes per sample) measured 40.605→32.041 ms ASCII,
40.246→31.148 ms CJK, 33.126→23.416 ms grapheme and 37.343→27.200 ms mixed input.
Reflow allocations remain
approximately 94–96 MB per sample; this change improves copying/initialization
work, not total reflow storage. These are model-only microbenchmarks, not end-to-end
render timings. A steady-state 50,000-row scroll improved 15.951→5.296 ms and
162,800,000→0 allocated bytes. The optimized row-reuse unit test also asserts zero
allocation after reaching the scrollback limit.

A correctness-first managed synchronized-output copy initially cost 1.1535 ms and
3,421,736 bytes per begin/release with 1,000 history rows. Copy-on-write cell storage
and omission of a throwaway viewport reduced the measured operation to 0.0583 ms
and 41,752 bytes. Only row metadata/registries are copied at the boundary; each
subsequently mutated row detaches its cell array. The operation is internal, runs
under the screen lock, and permits no writable cell reference to span that copy
boundary. Both mutation directions, hidden-column preservation, and evicted-row
recycling have dedicated isolation tests. The remaining O(history-row-count)
metadata cost is explicit; this is not a claim of allocation-free TUI frames.

The detailed [IO performance report](ghostty-io-performance-2026.md) records isolated
gather throughput, allocations, latency, priority variants and the initial spin-order
regression that the measurements exposed. The [renderer audit](ghostty-renderer-audit-2026.md)
separates code that executes in Skia/libvt from Ghostty's full application renderer,
and records applicable transfers plus unresolved gaps.

## Validation requirements

- Build the release native library with Zig 0.16 using `scripts/build-native.sh --release`.
- Build the complete .NET solution with no warnings.
- Run all xUnit unit and native integration tests.
- On platforms for which native assets are published, rebuild those assets from the
  new submodule pin before release. The repository's macOS arm64 native binary was
  used for the API integration tests in this change; generated binaries remain
  excluded from source control.
