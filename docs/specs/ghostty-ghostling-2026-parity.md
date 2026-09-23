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
| Managed Kitty graphics, relative placements, animation, validation, deletion and file-medium changes | Native/managed protocol and pixel/placement differential tests for the upstream graphics changes | Command parsing, bounded image loading, PNG/media providers, tracked anchors, graphics store, live APC and virtual-placeholder projection are implemented with focused tests; vertical clipping, top-origin history, stationary IL/DL and horizontal containment now match native cases; protocol-response/streaming edges and full differential coverage remain open |
| Native Kitty animation playback | Animation tick in the built library, scheduling without further PTY input, deterministic frame tests | Repository-owned extension and timed refresh implemented; deterministic frame tests pass, and all six native RID variants cross-build; non-macOS runtime execution remains CI validation |
| Synchronized output | Completed prefix visible at enable; following output frozen; release/reset/one-second timeout without additional input | Both engines now implement prefix publication, frozen presentation and idle timeout; managed copy-on-write state transfer and focused native/managed tests pass |
| Managed snapshot and continuation features | Restore both screens, history, parser/UTF-8 continuation, modes, styles, links and saved cursors with bounded validation | Ordered wire decoding, all payload codecs and non-executing continuation validation pass golden/native tests; ESC/CSI replay, bounded parameters and colon SGR now have focused native comparisons. Managed state installation, remaining runtime semantics and incremental READY/history reconciliation remain open |
| Managed resize/reflow optimization | Baseline/after measurements plus wide/grapheme/style/link/cursor/anchor regressions | Bulk reflow copies and redundant initialization removal implemented and measured below; tracked cell identity/reflow/COW/pruning regressions pass; end-to-end Kitty anchor comparisons remain part of graphics integration |
| Managed row allocation/recycling | Stable content/metadata after eviction and measured allocations | Evicted row storage is reused with a focused zero-allocation steady-state test |
| Parser/clipboard throughput and bounds | Split-input protocol tests, malformed UTF-8/base64 tests, limits, before/after measurements | Review added span payload scanning, bulk base64 decode, correct 64 MiB configurable clipboard bound, 65-codepoint grapheme bound and protocol fixes; measurement review pending |
| Third IO thread and render fairness | Dedicated lifetime/flush/failure tests, renderer demand handoff, batching/backpressure throughput and latency measurements | Dedicated worker, demand handoff and bounded gather/lease path implemented; isolated PTY throughput and allocation improvements measured; platform scheduling rejection handling and final lifecycle validation remain in review |
| Managed colors and VT formatter state | Dynamic override resets, pending-wrap and tabstop/cursor round-trip differential tests | OSC 104/110/111/112 resets and configured-vs-override state implemented; edge-cell replay, post-tabstop cursor home and CRLF replay tests pass; broader formatter audit remains open |
| Unicode 18 grapheme behavior | Authoritative generated grapheme/Indic/emoji properties and conformance tests, not only scalar widths | Full generated properties and boundary kernel pass official conformance/native comparisons; mode2027 on/off is implemented; managed right-edge printing and explicit spacer identity now match focused native comparisons; native combining/spacer dirty-row defects have tested source overlays; exhaustive mode/margin transition coverage remains part of the full audit |
| Platform renderer improvements | Per-change applicability evidence for Skia/native bridge vs upstream Metal/OpenGL, with relevant tests/measurements | Detailed inspection remains open; native dependency pin alone does not port full-app renderer behavior |

Rows above are requirements to finish, not exclusions from the requested scope.
Tests cited elsewhere in this document describe existing coverage; they must not
be used to mark these broader requirements complete until their specific evidence
has been inspected.

### Live semantic cell and row metadata (2026-09-23)

Both native extraction paths now retain OSC 133 cell classification and physical
row prompt markers. Managed printing records output/input/prompt in the existing
cell metadata byte, keeping the 48-byte cell size. Row markers share a byte with
the copy-on-write flag instead of increasing every row object's allocation.
State copies, row reuse, history snapshots and synchronized-output publication
preserve the classifications. Erased cells become output; EL2 retains its row
marker, while an unprotected full-row display erase clears it.

The managed live cursor now implements OSC 133 A/N/L fresh-line behavior,
P prompt kinds, B/I input lifetimes, C output and D completion. Options use the
first matching key, including invalid-first values, as Ghostty does. Malformed
command suffixes and L options are ignored. Explicit newlines end I input;
soft wraps (including right margins and late variation selectors) do not.
Prompt/B-input newlines mark continuations; C at column zero removes Fish's
tentative continuation marker. Legacy screen switches copy the semantic pen;
1049 exit retains the primary pen, and DECSC/DECRC do not restore this state.

Reference decisions: Ghostty `osc/parsers/semantic_prompt.zig`,
`Terminal.semanticPrompt/index/printWrap`, `Screen.cursorSetSemanticContent`
and `PageList.ReflowCursor` define the behavior. Windows Terminal's
`AdaptDispatch::DoFinalTermAction` handles A/B/C/D command marks; xterm.js
`InputHandler` has no built-in OSC 133 registration and permits external handlers.
RoyalTerminal follows Ghostty's richer cell/row semantics. PowerShell's
[ConsoleHost prompt loop](https://github.com/PowerShell/PowerShell/blob/master/src/Microsoft.PowerShell.ConsoleHost/host/msh/ConsoleHost.cs)
evaluates `prompt`, writes the returned text and then invokes line reading;
RoyalTerminal's existing PowerShell bootstrap emits markers through Console.Write
around this workflow. Marker handling must consume protocol bytes without
printing them; no shell-startup or bootstrap change is made in this batch.

Native testing reproduced a further upstream dirty-publication defect: after a
clean frame, OSC P or an implicit prompt/input newline changed live row markers
without dirtying the render snapshot. A fifth hash-checked source correction
marks the affected cursor row after semantic commands and implicit continuations.
It does not force full-grid scans or full repaint, and does not modify the
submodule checkout. Metadata-only changes and hold/release have focused tests.

Reflow carries source row markers with linear, coalesced metadata runs. Empty
marked rows remain meaningful. Valid wide tails are copied with their own pen:
Ghostty can print a late-selector tail with different background, protection and
semantic content. The old managed reflow discarded a final tail or regenerated
it from the head. A native-owned snapshot regression now covers this case;
malformed tails containing text are still normalized.

This is live cell/row and cursor-semantic groundwork, **not complete shell or
snapshot parity**. The following implementation section closes the initially
identified wrap-continuation, prompt-policy, redraw, ED2 heuristic and active
cursor/viewport reflow gaps with native differential tests. Host prompt navigation,
click/selection consumers, formatter integration and managed snapshot installation
remain part of the full goal.

The earlier semantic-only working tree passed **2,349 unit/headless tests with
16 conditional skips**, plus **228 native integration tests** (external SSH
excluded). These counts predate the implementation below and do not validate it.
The previous pushed commit `b862691` completed all six native-build jobs, all
three OS build/test jobs, macOS integration, and NuGet packing. Fresh validation
is explicitly deferred until after implementation is committed and pushed, per
the user's requested sequence. No performance improvement is claimed for the
unvalidated extension of this batch.

### Wrap, resize, prompt policy and hyperlink implementation (2026-09-23)

Implementation and regression coverage (post-push results below):

- Independent packed wrap-continuation row storage, preserved by row copies,
  synchronized publication and native extraction. Implicit wrap sets the target
  marker; EL/DCH/ECH clear the next continuation when breaking a wrap. Full-width
  row shifts reset moved wrap metadata. A sixth source overlay dirties the next
  native row when `Screen.cursorResetWrap` clears its continuation flag.
- Reflow includes the cursor's actual blank cell, maps ordinary cursor positions
  as cells rather than end-of-line offsets, and discards trailing unpinned blank
  rows before padding the target viewport. Pinned blank rows remain. The explicit
  ConPTY preserve-viewport policy remains separate. Printed spaces, wide spacers
  and background-only cells are meaningful, following Ghostty `Cell.isEmpty`.
  The native bridge no longer deletes trailing styles/spaces after resize.
- Managed OSC 133 retains screen-specific prompt-seen/click policy and global
  `redraw=0/1/last`, with first-option precedence and click-events priority.
  Resize clears the requested prompt cell range without removing rows/metadata;
  primary ED2 applies the upstream bottom-row prompt-scroll heuristic. A read-only
  native extension exposes the same live policy through `ITerminalPromptStateSource`.
- Owned arbitrary-byte hyperlink identity, distinguishing explicit IDs and
  per-cursor implicit counters. Byte-span registry lookup avoids decoding and
  allocating on repeat native extraction. COW snapshots share immutable values,
  not mutable registry chains. Native extraction includes linked spacer cells;
  managed OSC 8 parses original bytes, handles option precedence, and ends links
  on screen switches. Styled export preserves explicit IDs and implicit link
  boundaries; its string return type is not a binary snapshot byte-preservation
  guarantee. Late-selector tails retain their independent current pen through
  printing, normalization and reflow.

Reference decisions: Ghostty `PageList.ReflowCursor`, `Screen.cursorResetWrap`,
`Screen.clearPromptForRedraw`, `Terminal.eraseDisplay`, `Screen.startHyperlink`
and `osc/parsers/hyperlink.zig` define the compatibility target. Windows Terminal
`TextBuffer::Reflow` also includes cursor column plus one and stops after the
last written/cursor row; xterm.js `BufferReflow` trims empty continuation rows
and optionally skips cursor-line reflow. RoyalTerminal follows Ghostty's cursor
reflow policy, retaining its explicit ConPTY policy for repaint interoperability.
WT `AdaptDispatch::AddHyperlink` and xterm.js `InputHandler` retain application
IDs rather than treating the URL alone as a link identity.

Still open: full live managed snapshot installation/export and incremental
READY/history reconciliation; prompt click/navigation/selection host consumers;
broader simultaneous-axis/standalone-notification resize coverage; formatter and
remaining graphics/protocol edges; the complete renderer/per-change performance
audit and third-IO-worker platform/lifecycle sign-off. Local native rebuild and
focused/full tests have passed; fresh platform CI is still required.

Post-push validation: native extension build succeeds after an explicit signed
C-enum conversion correction (`a6f44f3`); native integration passes **228/228**.
The focused suite then exposed lib-VT's construction default (`redraw=0`, unlike
the full-app/RIS default), actual-cell pending-wrap cursor mapping, and two old
managed-only expectations. These were corrected against native comparisons:
wide-tail cursor pins stay on the tail, and LF does not erase an existing wrap
link. The expanded focused suite passes **170/170**, including original-byte
hyperlinks and native policy/viewport differentials. Full-suite and platform CI
validation initially exposed old URL-coalescing, trailing-blank and selection
expectations. Corrected tests retain real-content pruning coverage and exercise
rectangular selection with explicitly registered native and managed providers.
The complete suite on `fff6a85` passes **2,398 tests / 16 conditional skips / zero
failures**. Platform CI validation remains pending. Prompt redraw clears
with the default pen, matching native's temporarily detached resize cursor pen.

### Saved cursor resize tracking

Managed resize now temporarily tracks the active screen's DECSC cell through
reflow, then updates only its coordinates and pending-wrap flag. Ghostty
`Screen.resize` is the compatibility target: a saved pending-wrap cursor moves
one column forward and clears pending wrap when its tracked cell is no longer
at the right edge; a pin outside the active area resets to the top-left.
The pin is released even when resize fails and does not follow ordinary output
between DECSC and resize. Existing logical colors, protection and charsets remain
in the saved pen. xterm.js `Buffer.resize` adjusts saved Y during history trim
and bounds saved X; Windows Terminal `AdaptDispatch::SaveCursorState` uses
screen-specific coordinates and restores pen/origin/delayed wrap. Their coordinate
policies differ, so this managed implementation deliberately follows Ghostty's
cell-pin behavior. New native differential cases cover width/height changes,
pending wrap, wide tails, blank pins, history and the active alternate screen;
all 17 saved-cursor cases pass with native available. The full unit/headless
suite on `9adaaa9` passes **2,415 / 16 conditional skips / zero failures**
(`saved-cursor-full.trx`). The Release benchmark project builds with zero warnings
and errors. The following inactive-buffer section supersedes the earlier
inactive-screen gap; broader resize-policy coverage remains part of the audit.

The differential also exposed two adjacent discrepancies now corrected:
ordinary blank pins clamp against the reflow cursor before its deferred newline
or pending wrap advances, and DEC 1049 clears with the dormant alternate screen's
logical background before copying the entering cursor. The latter retains the
alternate background across exits, resolves palette changes on re-entry and
resets it on RIS. Repeated 1049 also clears even when already on the alternate
screen. Native comparisons cover first entry, re-entry and reset. Reflow retains
the previous destination column directly from its copy loop, avoiding an added
full-cell scan on the ordinary unpinned path.

A sequential Release/.NET 10.0.5 ARM64 smoke comparison against the existing
`b862691` archive (seven samples per workload, no concurrent test/build processes)
measured ASCII 47.581→50.830 ms, CJK 43.090→44.251 ms, graphemes
39.821→31.981 ms and mixed input 41.692→40.532 ms. Reflow allocations were
112.92–112.93 MB per workload in both versions (current deltas: +56 ASCII,
+1,080 CJK, zero grapheme/mixed bytes); final row counts agree. Steady-state
50,000-row scrolling measured 7.729→7.786 ms and zero allocated bytes in both.
This single paired run includes the entire semantic/wrap/identity batch and
does not isolate the saved-cursor change. It is not evidence of an overall
performance gain; the slower ASCII/CJK cases and end-to-end rendering remain
part of the outstanding performance audit.

### Current/saved charsets and cursor state (2026-09-23)

Inactive-buffer resize is implemented and locally validated. The managed
processor resizes primary first and alternate second, without publishing a
temporary screen switch. A stack-only scope reuses the existing screen resize,
tracked-anchor and raster machinery while preserving active geometry/buffer
identity on exit. Both dormant current and saved cursor positions are remapped;
only the primary can reflow, and wraparound-disabled primary state does not.
Primary prompt redraw also runs while that screen is hidden. Ghostty
`Terminal.resize`/`Screen.resize` define this policy; xterm.js `BufferSet.resize`
also resizes both buffers, whereas Windows Terminal `PageManager::_getBuffer`
lazily normalizes inactive pages without reflow. RoyalTerminal intentionally
follows Ghostty. Differential tests switch back into both buffers after resizing
and compare contents, cursor, wrap/prompt markers and pens, including held output.

Height shrink retires unpinned text-free bottom rows before creating history,
using remapped pin coordinates after column reflow. A no-scrollback alternate
buffer retains the remaining bottom active rows and resets offscreen saved pins;
it no longer unconditionally retains the top rows. No-reflow shrinking discards
truncated columns, including dormant alternate content; the explicit Windows
PTY mirror preservation path remains separate. Native comparisons cover both
engines' no-reflow control behavior and active/dormant alternate height changes.

`BasicVtProcessorOptions.ResizePullScrollback` exposes the previously implicit
policy. Its default matches `GhosttyVtProcessor.ConfigureOptionalNativeFeatures`:
true on Windows and false elsewhere, unlike standalone lib-VT's true default.
The false policy tracks the old active-area top and pads the bottom as needed,
so history is not silently pulled into the viewport during a widen/grow cycle.
The existing tests that intentionally exercise history pulling now request it
explicitly; both option values have a dedicated test. Height-only resizes retain
custom tab stops, while column changes recreate defaults. Pixel-only resizes
update metrics and end synchronized output without resetting grid state.

All **23 new resize tests** and the complete unit/headless suite pass on macOS:
**2,439 passed / 16 conditional skips / zero failures / 2,455 total** at
`a19bcd9` (`inactive-resize-full2.trx`). The preceding focused native differential
run passed 68 tests. A warmed 1,000-iteration buffer-context test allocates zero
bytes and preserves row identity: the scope does not copy the dormant screen.
Actual resizing necessarily processes both existing buffers; this is a parity
correction, not a claim of lower end-to-end resize time. Fresh platform CI remains
pending. Broader simultaneous-axis cases, pull-enabled history differentials and
legacy standalone `NotifyResize` coverage remain in the full resize audit.

`ManagedCharsetState` replaces two line-drawing booleans with all four G0–G3
designations, GL/GR and a pending single shift in the SCREEN-compatible compact
registry. ESC `(`, `)`, `*`, `+` accept ASCII, British and DEC Special Graphics;
unknown designations are ignored instead of resetting the slot. SI/SO, SS2/SS3,
LS2/LS3 and LS1R/LS2R/LS3R now retain full state. The previous DEC table's `_`,
`0`, `b`–`e`, `h` and `i` mappings were incorrect and are corrected.

Ghostty `Terminal.printCell` and `charsets.zig` are authoritative: map after
width/grapheme handling, consume single shift only when a cell is printed
(including spacer cells), retain GR without using it in the current UTF-8 printer,
and map values above 255 to space for British/DEC tables. This deliberately
differs from xterm.js's ASCII-only substitution and Windows Terminal's broader
GR/table translation in `TerminalOutput::TranslateKey`; the native differential
tests cover Unicode, combining marks, wide edges and ignored controls.

DECSC/DECRC and CSI s/u now save/restore independent state per screen: logical
colors/attributes, position, origin, protection, pending wrap and full charset
state. Missing saved state restores defaults. Current hyperlinks are deliberately
not saved/restored, matching native. Logical saved colors resolve against the
current theme when restored, so inactive pens do not need eager theme updates.
RIS/session reset clears both saved slots. RoyalTerminal's existing DECSTR
support remains a documented extension (Ghostty currently ignores it); it no
longer partially corrupts a saved pen while preserving its other fields.

Styled VT export restores all designations and pending single shift after its
synthetic cursor print. Screen selection now precedes content and origin-mode
restoration: the regression run exposed mode 1049 previously clearing alternate
content or resetting restored origin. This is VT replay compatibility, not a
claim that styled VT preserves every binary snapshot field. Native's current VT
formatter omits pending single-shift emission; binary SCREEN does preserve it.
Full formatter/native parity remains part of the outstanding audit.

Validation: **2,302 passed / 16 conditional skips / 2,318 total**, zero failures
(`charset-final.trx`), including 36 new tests with native available. Coverage
includes every split for protocol/state cases, 2,304 printable-byte/designation/
slot combinations, native snapshot registry comparisons, both saved slots,
hyperlinks, theme changes, origin, held output and snapshot replay ordering.
Preceding commit `25e4c09` CI run `35851323211` passed six native builds and Ubuntu
build/tests; macOS/Windows jobs were still running at inspection. This change
requires fresh CI.

The new `--managed-print` Release harness was copied unchanged to an isolated
archive of `25e4c09`; final runs were sequential after tests/builds finished.
On this macOS arm64/.NET 10 host, median of seven warmed samples (25,000 feeds,
zero scrollback) measured:

| Workload | Before | After | Timed managed allocation |
|---|---:|---:|---:|
| 79 ASCII `q` cells + CR | 23.468 ms | 23.330 ms | 0 bytes |
| 79 DEC-special `q` cells + CR | 32.386 ms | 24.518 ms | 0 bytes |
| DECSC + DECRC pair | 0.867 ms | 1.960 ms | 0 bytes |

Checksums match. Mapping after width classification avoids unnecessary Unicode
width/grapheme work for mapped ASCII, reducing this DEC workload by about 24%.
Complete saved-state restoration costs about 78 ns/pair versus 35 ns for the old
incomplete implementation. This is an explicit correctness/performance tradeoff,
not an across-the-board speedup. The harness excludes rendering and PTY IO.

### Character protection and screen-state prerequisite (2026-09-23)

Review of snapshot PAGE/SCREEN fields exposed missing live character protection.
`TerminalCell.IsProtected` is now packed with spacer metadata without increasing
the 48-byte cell budget. Native viewport and owned history extraction use the
public cell protection getter; cleared cells reset the flag. Managed printing,
wide-cell construction/reflow, copies and synchronized-output publication retain
it. DECSCA (`CSI Ps " q`) and ISO SPA/EPA (`ESC V` / `ESC W`) control the pen;
turning it off does not forget the most recent non-off mode for that screen.
SGR leaves protection unchanged and DECSC/DECRC save/restore it per screen.

Reference decision: follow Ghostty `Terminal.setProtectedMode`, `eraseLine`,
`eraseDisplay`, `eraseChars` and `Screen.clearUnprotectedCells`. DEC selective
ED/EL preserves protected cells; ordinary erasure respects protection only when
ISO was most recent. Erased cells use the current background and discard other
attributes. xterm.js `InputHandler.selectProtected`/`_eraseInBufferLine` supports
the DEC protection rule. Windows Terminal `AdaptDispatch::_SelectiveEraseRect`
instead keeps erased cells' attributes; this is an intentional Ghostty-aligned
difference, covered by styled/link/grapheme tests. Ghostty's documented ISO ECH
wide-boundary behavior (split the pair before protection filtering) is preserved,
including fully clearing the preceding wrapped spacer head and its protection.

Differential testing also exposed mode 47 restoring an obsolete primary cursor.
Screen switches now carry the entering cursor, mode 1047 clears on exit (not
entry), and mode 1049 clears before preserving the copied pending-wrap state.
ISO-protected alternate cells survive these clears just as native cells do.
Session restart retains its separate primary-position restoration contract.
Two theme/synchronized-output fixtures now explicitly home their alternate-screen
content instead of depending on the previous incorrect implicit home.

The tests include every input split for protocol/save/restore/screen-switch cases,
288 erase-mode/cursor combinations, 768 wide/wrapped erase combinations, native
owned history capture, held publication, reflow and protection-bit independence.
A warmed 1,000-operation selective erase loop allocates zero managed bytes.
These close a runtime storage prerequisite, not managed snapshot installation:
semantic/row metadata, full saved state, live encoding/READY installation and
history reconciliation still require implementation and integration validation.

Validation: **2,266 passed / 16 conditional skips / 2,282 total**, zero failures
in the full macOS run (`protection-final.trx`), including all 36 new protection
tests with native availability confirmed. The previous full run exposed the
wide spacer protection defect and two implicit-home fixtures; the final run
includes their corrections. For preceding commit `41a378d`, CI run `35850135929`
passed all six native builds and Ubuntu/macOS build/tests; Windows build/tests
were still running at inspection. This change requires fresh CI.

### Downloaded glyph foundation (2026-09-23)

The [renderer audit](ghostty-renderer-audit-2026.md) now records implemented bounded
managed glyf decoding and direct Skia path construction. Native registration
acceptance/error differentials and real pixel/zero-managed-allocation tests pass.
Managed APC handling and a session glossary are now connected: support/query,
bounded registration, FIFO eviction/replacement, clear, disable, alternate-screen
sharing and reset behavior match focused native conversations. Synchronized-output
copies share immutable entries but isolate glossary mutation until publication.
All 103 focused glyph/APC/synchronized-output tests pass, with native availability
confirmed. Native extraction and native-processor model publication now use three
tested read-only exports with owned-copy lifetime, normalized metadata and dirty
tracking; the 127-test focused glyph/APC suite passes. Both engines now draw registered
glyphs through the common Skia row/cursor passes with normalized Ghostty placement,
cell clipping and publication-aware path ownership. Actual cell widths remain
authoritative; multi-scalar clusters retain font shaping. See the renderer audit
for the explicit upstream boundary and remaining host-font coverage policy.
The live-renderer batch adds 12 focused regressions; the full macOS run passes
**2,230 tests / 16 conditional skips / 2,246 total**, zero failures
(`glyph-render-full.trx`), with native glyph drawing confirmed available.

### DCS parser review (2026-09-23)

The managed parser now separates DCS entry, parameters, intermediates, ignored
headers and passthrough payloads. The previous combined buffer could dispatch
malformed headers and incorrectly abandon payloads on prompt control bytes.
Ghostty `parse_table.zig`, `Parser.zig` and `dcs.zig` are the byte-level reference:
header C0/DEL are ignored, parameters saturate at 16 bits with a 24-slot hook
limit, malformed headers enter ignore, payload DEL is ignored and other C0/high
bytes remain payload except CAN/SUB/ESC. ESC immediately unhooks and starts a new
escape sequence; completed query effects are not replayed by continuation.
DECRQSS has a two-byte request bound; XTGETTCAP has a 1 MiB payload bound instead
of the former shared 4 KiB limit. Unsupported hooks retain no payload buffer.

Windows Terminal's DCS entry/passthrough/ignore states and xterm.js's transition
table corroborate separate header parsing. Ghostty's byte-level C1 rules and
query-unhook behavior are authoritative where decoded-character parsers differ.
Optional managed Sixel remains an extension absent from Ghostty's DCS handler;
it keeps xterm's image-addon policy of discarding CAN/SUB-aborted images.
No PowerShell startup or shell invocation behavior changes.

The focused query/continuation/Sixel suite passes **208 tests**, with the native
library confirmed available. New differential cases cover all 256 bytes in five
DCS states at every input split, malformed and saturated headers, parameter
overflow, cancellation, DEL, false terminators and exact query payload bounds.
The obsolete prompt-control recovery test now requires explicit termination.
The full macOS unit/headless run passes **2,124 tests / 16 conditional skips /
2,140 total**, zero failures (`dcs-full.trx`). At the preceding commit e38d0a2,
CI has passed all six native builds, documentation and Ubuntu build/tests;
macOS and Windows build/test jobs were still running at inspection. This is
not a claim of CI validation for the subsequent DCS commit.
This closes these DCS parser discrepancies, not the remaining snapshot-install,
graphics, renderer or platform-validation requirements above.

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

## Graphics protocol differential follow-up (2026-09-23)

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

## Virtual-placeholder prerequisites (2026-09-23)

Both processors now preserve original foreground, background and underline color
identities separately from resolved ARGB. This is required by Ghostty
`graphics_unicode.IncompletePlacement.colorToId`: palette index 42 encodes ID 42,
not that palette entry's RGB. Default, palette zero and explicit RGB black remain
distinct. Native synchronization retains style identities and raw erased-cell
backgrounds; managed SGR/save/restore, erasure, row movement, wide spacers, reflow
and held-screen copies preserve the metadata. Windows Terminal `TextColor` and
xterm.js `AttributeData` likewise retain color representation before resolving
it for display. The new identity is four bytes; grouping the cell fields keeps
`TerminalCell` within a 48-byte budget, enforced by a regression test.

The review additionally corrected managed whole-screen scroll blanks to retain
the active background and changed live/saved pen theme resolution to use logical
color kind rather than ambiguous RGB matching. Existing-cell theme resolution
now follows the same rule, as detailed below.

The shared placeholder scanner ports Ghostty's contiguous-run rules, including
nullable row/column/high-byte inheritance, invalid marks, palette and underline
IDs, supplementary codepoints and the complete 297-entry diacritic table (exact
source comparison passed). The shared geometry routine matches all five upstream
dog-image golden cases, plus pillarbox, unspecified-grid and overflow tests.
These helpers are now wired into both integrations, as detailed below.

The scanner's first Debug allocation test exposed 72 bytes per valid diacritic
lookup. Using a once-created immutable table avoids the runtime RVA-span lookup
allocation; explicit binary search also avoids a boxed comparison value. Three
warm allocation regressions (zero/one/three diacritics) now pass with zero bytes
over 1,000 scans of 80 cells. The combined focused run passes 41 tests.
The subsequent full run is confirmed by a TRX report: **1,751 passed**, 16
conditional skips, zero failures (1,767 discovered tests). Native integration
also passes 227 tests. Earlier partial console summaries were not treated as
whole-suite verification.

### Virtual-placement integration (2026-09-23)

Both processors now publish immutable virtual-placement recipes, preserve
internal/external namespaces, prefer external then lowest-ID default targets,
and re-project against visible placeholder runs. Explicit IDs can target ordinary
placements too. Relative children use independent minimum visible root coordinates,
including roots whose visible fragment is entirely letterbox padding. Relative
chains retain upstream signed-32-bit offset saturation.

The new native metadata extension reads exact iterator keys and invokes upstream
`resolveChain`; it does not guess namespaces from numeric IDs. Shared geometry
uses Ghostty's `renderer/image.zig` and `graphics_unicode.zig` semantics. Skia
suppresses placeholder glyphs as in `font/shaper/run.zig` while rendering their
images below text. Windows Terminal ignores Kitty APC; xterm's image renderer
supports the viewport-relative coordinate decision but is not a Kitty oracle.

Nineteen focused tests pass with the native library available: actual Skia pixels,
default/explicit targeting, relative chains, animation, resize, scrollback,
synchronized output, alternate screens, erase, deletion and reset. Unrelated text
updates retain image buffers and cached projections. One thousand unchanged
projection reads allocate zero bytes in both engines. Three native ABI tests cover
layout, exact metadata, disposal and invalid arguments. The native extension builds
for all six supported RIDs; only macOS arm64 runtime execution was performed here.

This closes the tested virtual-placement integration gap, not scroll-margin
clipping, exhaustive protocol coverage or the broader parity requirements above.

Final verification of this integration: the full unit/headless suite passes
**1,770 tests**, with 16 conditional skips and zero failures (1,786 total,
confirmed by TRX). The full native integration suite passes **230 tests**,
with zero skips or failures. `git diff --check` passes.

## Existing-cell theme resolution follow-up (2026-09-23)

Removed the legacy RGB-to-RGB remap dictionary. Existing default, palette and
explicit RGB cells now remain distinct even when all three initially have the
same displayed color. Foreground, background and explicit underline palette
colors resolve against the new theme; truecolor stays unchanged. Resolution
covers history, inactive screens and columns retained by non-reflow resize.
Copy-on-write rows detach only when a resolved value actually changes. Unchanged
or cursor-only themes skip cell/history traversal entirely; equal palettes are
compared without allocating a remap dictionary.

Reference decision: Ghostty `terminal/style.zig` (`fg`, `bg`, `underlineColor`),
Windows Terminal `TextColor::GetColor`, and xterm's WebGL `CellColorResolver`
all retain color kind before theme resolution. RoyalTerminal follows this
representation-based behavior; inverse/dim rendering remains downstream and
is not baked into the logical identities.

The initial focused theme/query/screen/copy regression run passes **193 tests**,
including six new cases. A seventh regression verifies that unused palette changes
neither allocate shared-row copies nor dirty rows. Both real processors agree on equal-RGB palette collisions,
explicit RGB preservation, underline updates, OSC 4/104 and erased backgrounds.
Managed history/hidden/alternate cells and snapshot isolation have dedicated
coverage. One hundred unchanged theme applications to a shared 80x24 screen
allocate **zero bytes**, eliminating the previous remap dictionary/set and
unconditional shared-row copies. This does not close the separate full snapshot
codec or formatter requirements.

Final full unit/headless verification passes **1,777 tests**, with 16 conditional
skips, zero failures and 1,793 total tests confirmed by TRX. An earlier partial
601-test run was not counted as complete verification.

## Wide-edge and spacer-state follow-up (2026-09-23)

Native differential tests reproduced managed failures when a wide glyph arrived
in the last column: missing wrap padding with autowrap, and incorrectly printing
a squeezed one-cell glyph without autowrap. The managed printer now follows
Ghostty `Terminal.print`: a distinct spacer head precedes a wrapped wide glyph,
unfittable wide glyphs are ignored with autowrap disabled, and a one-column
terminal receives an empty narrow cell with normal pending-wrap behavior.
Windows Terminal `TextBuffer::FitTextIntoColumns` likewise pads the final column;
xterm `InputHandler.print` pads on wrap and rejects unfittable wide glyphs without
wrap. Ghostty's explicit spacer metadata is authoritative for our representation.

`TerminalCell.IsWideSpacerHead` distinguishes wrap padding from same-row wide
tails in both processors and raw native snapshots, while the cell stays within
48 bytes. Reflow recreates heads and copies preserve them independently. Raster
overlap does not treat a head as the trailing half of the unrelated glyph to its
left. Grapheme widening retains head styling; narrowing retains the old tail's
style when making it narrow, as upstream does.

Overwrite, ECH/DCH, ICH, EL, IL/DL and reflow have focused regressions. Cleanup
follows the specific upstream operation: EL/ICH and edits below a preceding row
can retain its spacer; ECH/DCH reset their row's soft wrap; row shifting clears
heads within moved rows. This is not a blanket normalization that destroys all
spacers after any edit.

The review also found native storage becoming narrow while a clean render
snapshot retained a head after overwriting either half of a wrapped glyph.
Raw-grid versus incremental-snapshot tests failed before correction. A
full-file-hash and two-site-count guarded overlay marks the previous row dirty
in the two `printCell` branches. It leaves ordinary trailing spacers untouched.
The existing upstream `splitCellBoundary` dirty-row fix covers erasure, but not
these printing branches. No upstream report or upstream fix is claimed.

The initial focused run passes **112 tests** and the native integration suite
passes **231 tests**. Host native rebuild plus five cross-builds succeeded for
all six supported RIDs. Only macOS arm64 runtime execution was performed here.
Final full unit/headless verification passes **1,796 tests**, with 16 conditional
skips and zero failures (1,812 total, confirmed by TRX), including 19 new
wide-edge/spacer cases. This closes the reproduced spacer gap, not the remaining
snapshot, graphics, renderer/performance or exhaustive state-transition audit.

## Kitty margin-scroll integration (2026-09-23)

The previously isolated clipping geometry helpers are now used by live managed
SU/SD/IND/RI scrolling. Following Ghostty `ImageStorage.scrollMarginsBegin/end`,
the store records final positions before text rows move and restores anchors
afterwards, including anchors temporarily pruned by the row operation. Placements
wholly inside vertical margins move and permanently clip their source rectangle;
straddling placements stay stationary. Fully clipped placements and relative
orphans are removed, while image data remains available for later redisplay.
Virtual placements follow their text, and relative placements follow the root.

SU/SD clamp counts to the region height and clip once for the complete command,
not once per shifted row: an 8-pixel image spanning five rows loses three source
pixels on SU 2, rather than two successive one-pixel crops. IL/DL restore image
positions without clipping even when their original text rows are discarded,
matching the distinct upstream operation semantics. Unreported pixel dimensions
do not establish containment. Publication after clipping keeps synchronized-output
snapshots unchanged until release and reuses unchanged image pixel buffers.

Seventeen focused tests pass against the real native library on macOS arm64,
covering scaled and native-size crops, offsets, large scrolls, straddling images,
orphan cleanup, image reuse, IL/DL, missing metrics and synchronized output.
A warmed loop of 1,000 SU/SD pairs with an unclipped image allocates **zero bytes**:
restoration scratch is reused and unchanged geometry is not republished. The
no-placement path skips the placement adjustment entirely.

The full unit/headless rerun passes **1,813 tests**, with 16 conditional skips
and zero failures (1,829 total, `margin-final.trx`). An older IL test expectation
was corrected to match the native-verified stationary-image rule. The initial
run also encountered a PTY command-start timeout; that test passed on the full
rerun without a production change.

Reference decision: Ghostty is authoritative for Kitty margin rules. Windows
Terminal ignores Kitty APC images; xterm's image addon attaches Sixel/iTerm image
tiles to buffer cells and is not a Kitty placement/clipping oracle. Its scrolling
code was inspected as a coordinate/lifetime reference, not copied as Kitty policy.

This closes the tested in-place vertical clipping gap, not all margin behavior.
Managed horizontal-margin state was not yet integrated at that checkpoint; the
later horizontal-margin follow-up below addresses it. The top-origin history gap
found during this review is addressed by the next follow-up.

### Top-origin partial-scroll history (2026-09-23)

Managed SU/IND now retain history when the primary scrolling region begins at
row zero, even when its bottom margin is above the last row. The initial native
differential reproduced missing managed history while native passed; native
history is read through its viewport-state/snapshot APIs, not its viewport-only
`TerminalScreen` projection. Alternate-screen operations continue in place.

`TerminalScreen.AddRowAtActiveRow` follows Ghostty `Screen.cursorScrollAbove`:
append/recycle a blank row, then rotate row references below the margin. Work is
proportional to status-row count, not scrollback or cell count. Row storage,
wrap metadata and copy-on-write ownership are preserved; tracked and raster
anchors below the margin follow the rotation. The existing full-screen fast
path is unchanged. A warmed 1,000-scroll test at the history limit allocates
**zero bytes**; identity tests prove status-row storage is reused, not copied.

Kitty margin adjustment now handles the window-shift path as well as in-place
scrolling. It restores all active placements after row movement, including
straddlers whose original anchors were pruned, while history remains tracked.
Ten additional native/managed differential cases cover SU/IND, primary/alternate
buffers, status rows, source geometry, synchronized output and background erase
colors. Five storage tests cover zero/one/growing history limits, row identity,
anchors, raster placements and allocation behavior; the combined margin/storage
suite passes all 32 cases on macOS arm64 with native available.

Reference choice: Ghostty is the behavior and optimization target. xterm.js
`BufferService.scroll` likewise inserts at the bottom margin and advances history
when the top margin is zero. Windows Terminal `AdaptDispatch::ScrollUp` instead
uses `_ScrollRectVertically` for SU, rotating full-width rows in place; its storage
rotation supports the optimization choice but its SU history policy is not used.
No shell or PowerShell startup/invocation contract is changed.

Validation accounts for all **1,844** unit/headless cases: **1,828 passed**
after one isolated retry, and 16 conditional skips. Two monolithic runs exited
successfully with incomplete counts (790 and 13) during the PTY contract group;
those are not accepted as full-suite passes. The remaining 1,830 cases ran as
one partition (1,813 passed, one PTY startup timeout, 16 skipped), and all 14 PTY
contract cases passed in separate hosts. The timed-out headless OSC-flood case
passed separately without a code change. TRX reports are
`partial-history-without-pty-contract.trx`, `pty-contract/*.trx` and
`partial-history-osc-retry.trx`. Runner early termination and intermittent PTY
startup behavior remain open IO-validation concerns, not resolved by this patch.
Windows-only test bodies are platform-guarded on this macOS host; these counts
do not establish Windows runtime coverage.

## Horizontal-margin integration follow-up

The managed engine previously reported mode 69 while ignoring its geometry.
The first 22-case native comparison reproduced 18 failures. The engine now
implements DECLRMM/DECSLRM state, margin-aware origin/cursor/tab/CR behavior,
rectangular SU/SD/IND/RI/IL/DL, and bounded ICH/DCH. Erase-in-line/display and ECH
retain their full-screen-coordinate semantics. Explicit DECSLRM parameters are
ignored when mode 69 is disabled; parameterless CSI s retains cursor-save meaning.
Invalid margins do not move the cursor. Disabling mode 69 resets horizontal
margins, resize resets both axes, and switching screen buffers retains the
terminal-wide margin state. SU/SD preserve pending wrap as native does.

Rectangular scrolling copies cell spans once per affected row, regardless of
scroll count, and creates no history. It retains outside cells and row metadata,
cleans split wide glyphs without replacing their styles, and normalizes orphaned
real-edge spacer heads under Ghostty's `rowWillBeShifted` rules. Wide printing
and selector-driven grapheme widening wrap at the effective margin, but only
the real screen edge creates spacer-head/soft-wrap metadata. A warmed 1,000-pair
SU/SD test allocates zero bytes; this is an allocation invariant, not an
end-to-end throughput claim.

Kitty placement adjustment now also tests horizontal containment, clamping the
right extent to the physical screen edge exactly as Ghostty does. Straddling
placements remain stationary; wholly contained placements move/clip. Tracked
text anchors move only inside both rectangle axes. CPR and DECRQSS report the
new state, and styled-VT export/replay retains origin, margins and pending wrap.

Reference choice: Ghostty `Terminal.zig`, `kitty/graphics_storage.zig`,
`stream_terminal.zig`, `dcs.zig` and `formatter.zig` are authoritative. Windows
Terminal `SetLeftRightScrollingMargins` agrees on mode-gated DECSLRM/CSI-s
ambiguity; the inspected xterm.js input handler does not implement mode 69.
RoyalTerminal deliberately retains its existing DECSTR implementation, including
margin reset; the pinned Ghostty stream handler ignores DECSTR. A managed-only
test records that divergence rather than treating it as native equivalence.

The focused suite has **102 passing cases** with the native library available,
covering text/cursor/reply differentials, wide/grapheme/wrap metadata, backgrounds,
screen switches, resize, formatter round trips, anchor movement, Kitty placement
containment and allocation behavior (`horizontal-boundaries.trx`). This closes
the reproduced horizontal-margin integration gap, not exhaustive reverse-wrap,
all graphics protocol transitions, complete binary snapshot restore or the
remaining per-change renderer/performance audit.

Final batch validation: **1,908 passed, 16 conditional skips, zero failures
(1,924 total)** in a single macOS unit/headless run,
`/private/tmp/royalterminal-margin-validation/horizontal-complete-batch.trx`.
No native source or ABI changed in this batch; the pinned macOS native fixture
was available for the differential tests. Non-macOS runtime coverage remains
a separate validation requirement.

## Unix launch and IO validation follow-up

The incomplete test runs above exposed a real launch bug, not just a runner
quirk. With 25 immediate start/interrupt/stop cycles, the old managed `forkpty`
path invoked parent SIGINT/SIGHUP registrations 40 times in child processes.
Resetting masks alone removed that signal leak but still intermittently hung
before exec: returning into a multithreaded managed runtime after fork is unsafe.

Unix launch now uses `posix_spawn`, with parent-prepared UTF-8 argv/environment,
default child signal dispositions and an empty child mask. Parent signal masks,
environment and cwd remain unchanged. On macOS, a small independent native
launcher establishes the controlling terminal after Darwin applies SETSID, then
uses libc `execvp`; an exec-close status pipe reports errors synchronously and
failed children are reaped. This preserves PATH lookup, argument boundaries,
working directories and no-shebang script fallback without a wrapper shell.
Linux uses session creation followed by a slave-open spawn action. No child
executes managed code. The separate Unix package builds/packages the universal
macOS launcher; CI/release carry that artifact and reject packages missing it.

Ghostty `pty.zig` supplies the session/controlling-terminal contract; node-pty's
native launcher and Apple's spawn ordering explain the Darwin-specific step.
xterm.js's demo uses node-pty (`demo/server/server.ts`); Windows Terminal uses
ConPTY and its close/join ordering remains unchanged. No PowerShell behavior is
modified. PTY tests now require markers produced by commands, not echoed command
text; the Ctrl+Z test also waits for actual ANSI flood output before interrupting.
Direct foreground-job and headless Ctrl+Z regressions pass without relaxing the
existing latency budget.

Two monolithic macOS runs completed normally: first 1,840 passed/16 skipped,
then **1,842 passed/16 skipped (1,858 total)** after adding argv tests. Reports:
`pty-launcher-full.trx` and `pty-launcher-final.trx` under
`/private/tmp/royalterminal-launcher-validation`. Signal isolation, repeated
startup, failure/reuse, Unicode environment, script fallback and argument
validation have focused xUnit coverage. A fresh NuGet-only consumer, restored
from the generated packages into an isolated cache, successfully starts zsh
with its `monitor` job-control option enabled. These macOS results do not
establish Linux or Windows runtime coverage or complete the remaining parity
requirements in the matrix above.

## Scope and pinned references

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
| Unknown APC | Supports ESC APC and C1 APC introductions from non-ground parser headers with a 4096-byte bounded payload policy; ground raw C1 is invalid UTF-8 |
| Mode queries | Handles private and ANSI DECRQM, retains full integer mode values rather than truncating to a byte, and includes current visibility-reporting (2033) and paste-event (5522) state |
| Device reports | Matches Ghostty color-scheme (`CSI ? 996 n`) and visibility (`CSI ? 998 n`) responses; mode 2033 emits its immediate visibility report |
| In-band resize | Mode 2048 reports committed geometry immediately when enabled and after pixel-aware resizes, using Ghostty's floor-to-cell geometry |
| XTGETTCAP | Answers the complete 272-key Ghostty capability surface, including dynamic `TN`; a checked generator derives the managed table from the pinned Ghostty terminfo source |
| Permanent modes | Reports DECECM 117 as permanently reset, matching Ghostty's current DECRQM behavior |
| Title privacy | CSI 21 t is disabled by default and requires an explicit managed option, matching the native wrapper's opt-in policy |
| Unicode | Cell-width overrides are updated to Unicode 18; an exhaustive scalar-value parity test compares managed widths to the pinned native library |
| UTF-8 C1 | UTF-8-encoded U+0080..U+009F values are ignored in ground state, matching Ghostty; standalone raw C1 bytes are invalid UTF-8 and replaced, not dispatched as commands |
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

The next restore layer now decodes and streams complete PAGE payloads in managed
code. `GhosttySnapshotGrid` implements all four row transport widths, trailing
default-cell elision, row flags, background content kinds, protected/semantic cell
bits, style/hyperlink IDs, and separate UTF-32 grapheme suffixes. It follows
upstream's normalization of invalid scalars, reserved semantic values, incomplete
wide pairs, misplaced spacer heads, missing suffixes and undeliverable/duplicate
grapheme entries. Its encoder chooses the smallest admissible row width and emits
suffixes in row-major order using one stack buffer, with no temporary per-row arrays.

`GhosttySnapshotPage` assembles style and hyperlink tables, preserves arbitrary
link bytes, resolves missing/invalid IDs to defaults, and enforces first-entry-wins
semantics even when the first duplicate was invalid. It owns accepted payload
data, rejects trailing bytes and checks configured cell/string/grapheme limits.
Native capacity hints are retained as wire metadata but never drive allocations.
No partial page escapes a failed decode. PAGE payload integrity uses the existing
bounded, CRC-validating record reader.

Evidence includes exact upstream `grid-v1`, `page-v1`, `page-empty-record-v1` and
`complete-v1` golden round trips; every-prefix truncation tests; malformed and
duplicate table entries; resource bounds; and 1,000 deterministic random-word
canonicalization cases. Re-encoding warmed grids/pages to `Stream.Null` allocates
zero bytes. Live Ghostty snapshots containing 80 styled history lines, CJK,
combining suffixes, hyperlinks and either active screen are rewritten through the
managed codecs, decoded by native Ghostty, then compared via styled formatters
and cursor state. The containing terminal/screen/history records are passed
through in this test: this is evidence for PAGE interoperability, **not** proof
that BasicVtProcessor can yet restore the full binary snapshot.

The live PAGE bridge now converts these records directly to/from `TerminalRow`
and `TerminalCell`, without parser replay. It retains physical page widths,
independent wide-tail styles, wrap/protection/prompt flags, full grapheme text,
unresolved color identities and original hyperlink bytes/IDs. Per-page style/link
lookup caches avoid repeated identity construction. Small graphemes use stack
scratch space; large UTF-16 conversions rent a buffer. Repeated PAGE emission
remains allocation-free; live capture/decode themselves allocate owned state.

Reference decisions: Ghostty's `Style.bg` gives inline background content priority
over the style background, including RGB black. Live capture writes the effective
background into the style table, preserving semantics rather than original IDs or
bytes. Windows Terminal's text-buffer serialization and xterm.js SerializeAddon
produce text/VT, not the native GHOSTSNP contract, so they cannot substitute for
this conversion. Ghostty `Page.exactRowCapacity`, `RefCountedSet.capacityForCount`
and snapshot grid suffix decoding define the generated allocation budgets. Native
decoding uses the hints as fixed capacities: zero hints initially lost styles and
graphemes in differential tests. Capture now includes hash-set load-factor space,
linked-cell map capacity, 32-byte string chunks and 16-byte grapheme chunks with
growth headroom. Overflow requests a PAGE split instead of emitting lossy output.

All **121 focused snapshot tests pass** in `live-page-focused.trx` on macOS arm64
with native available. Added cases cover malformed live metadata/UTF-16, cell
budgets, raw invalid-UTF-8 link identities, ID collisions, independently styled
wide tails, inline RGB/palette backgrounds, pooled large-grapheme conversion,
zero-allocation re-emission and hyperlink-capacity overflow. Native differential
tests include both screens and subsequent input, plus 128 dense history rows with
64-scalar grapheme suffixes, indexed foreground/RGB background/curly underline and
200-byte hyperlink URIs. Non-PAGE records are still passed through; full READY
installation, mixed-width live integration and incremental-history reconciliation
remain open. Native's 64-suffix runtime cap is separate from the larger wire-codec
limit; pooled conversion testing does not assert that native retains larger clusters.
The complete Release unit/headless suite subsequently passed **2,459 tests,
16 conditional skips, zero failures (2,475 total)** in `live-page-full.trx`.
All six native-build CI jobs for `707289d` were still running at inspection;
this local result does not establish fresh cross-platform runtime coverage.

`GhosttySnapshotTerminalHeader` and `GhosttySnapshotTerminalState` now decode and
re-encode the complete TERMINAL payload: geometry/pixel dimensions, per-axis
margins, routing, previous codepoint, cursor/mouse/input policies, all three
43-bit mode sets, optional dynamic colors, scrollback policies, packed tab stops,
original palette plus sparse overrides, and arbitrary-byte PWD/title. Invalid
semantic fields normalize exactly as upstream; structural dimensions/screen
counts fail. Source policies never determine allocation, and the combined string
limit is checked before copying. Unused tab bits and reserved mode bits are
cleared; present black, absent color, finite zero and unlimited remain distinct.

Evidence: the upstream header and complete golden fixtures round-trip exactly;
every payload/header truncation, trailing bytes, sparse palette boundaries and
configured resource limits are checked. Native snapshots with custom palettes,
tabs, margins, saved modes and either active screen retain every terminal-wide
field after managed rewriting and native restoration. One hundred deterministic
malformed semantic headers produce identical native terminal state whether
decoded directly or normalized first by managed code. Warm streaming writes
allocate zero bytes. All **56 snapshot tests pass** (`snapshot-terminal-all.trx`),
with native available on macOS arm64. SCREEN/PAGE/history records in these new
terminal tests are passed through; this does not prove managed installation.
The full follow-up macOS suite passed **1,939 tests, 16 conditional skips, zero
failures (1,955 total)** in `snapshot-terminal-full.trx`.

SCREEN and HISTORY payload codecs now complete the fixed-state wire layers.
The screen codec owns cursor pens/flags/charsets, saved cursor, protected mode,
the entire eight-slot Kitty keyboard stack, semantic click state, hyperlink
counter and optional byte-preserving cursor hyperlink. Advisory history extents
are never allocation requests. Unknown semantic state normalizes as upstream;
any nonzero saved-cursor presence byte consumes its 23-byte suffix. Malformed
final cursor links degrade to absent links, while valid/null links must exhaust
the record. Structural header/saved-state truncations and invalid routing/counts
remain fatal. Current cursor installation clamps to its physical page width;
saved cursor installation uses terminal width, with pending wrap retained only
at the corresponding edge. HISTORY validates exactly six bytes and permits
empty page sequences. Full record routing and managed installation remain open.

Evidence: exact screen/saved-cursor/history and complete golden fixtures;
every charset bit pattern; structural truncations and discardable link tails;
limits, absent/implicit/explicit links, independent counters, semantic-click
registry cases and wide-row cursor clamping. Live snapshots with both screens,
saved cursor, pen, charset, keyboard stack and links round-trip through managed
SCREEN rewriting and native restoration, and still compare byte-for-byte after
subsequent printing and cursor/screen restoration. One hundred malformed SCREEN
records normalize identically to direct native decoding. Warm SCREEN encoding
and HISTORY header encoding/decoding allocate zero bytes. These tests restore
native Ghostty, not BasicVtProcessor.
The focused snapshot suite passed **77 tests, zero failures/skips** in
`snapshot-screen-history.trx`, with the native library available on macOS arm64.
The full unit/headless run passed **1,960 tests, 16 conditional skips, zero
failures (1,976 total)** in `snapshot-screen-history-full.trx`.

The ordered `GhosttySnapshotStateReader` now validates the entire snapshot
sequence: TERMINAL, unique declared SCREEN groups with counted PAGEs, validated
CONTINUATION, READY, unique declared HISTORY groups with counted newest-first
PAGEs, and FINISH. Either key order is accepted. READY state is published only
after its marker validates; history is returned one owned page at a time. Failure
invalidates subsequent reads but not already-owned state. Active rows must cover
the terminal height; mixed-width pages remain valid as upstream's PageList builder
permits. Cell/page/total-payload limits are aggregate rather than resetting per
record. These are explicit decoder resource policies, separate from source
scrollback policies. Source streams remain caller-owned; FINISH leaves trailing
transport bytes unread.

Continuation validation executes no terminal code or callbacks. Its non-allocating
scan follows `stream_continuation.zig` and `parse_table.zig`, preserving incomplete
UTF-8, inert builder state and DCS high-byte payloads while rejecting complete,
nonminimal or effectful fragments. Windows Terminal's `_ActionExecute` and xterm's
parser EXECUTE transitions likewise treat controls as immediate actions; Ghostty's
binary continuation registry remains authoritative. A comparison of all 256 bytes
after 16 parser-state prefixes, 1,000 random fragments and 10 UTF-8 boundary cases
(5,106 total) agrees with native validation. Every golden continuation is accepted
and warmed validation allocates zero bytes.

The **101-case snapshot suite passes** (`snapshot-sequence-transcode.trx`). It
covers every truncation of the complete fixture, removed records, duplicate and
undeclared routes, reordered groups, aggregate budgets, corrupt FINISH, short
non-seekable reads and injected IO failure. Combined re-encoding through all managed
payload codecs preserves the complete golden fixture exactly. Live native snapshots
with 100 styled history lines, Unicode, links, either screen and pending CSI compare
equal after full managed transcoding, native restoration and subsequent input.
This is wire interoperability, not BasicVtProcessor restoration: valid native parser
tails still need managed replay/state coverage and installation of decoded metadata.
The complete macOS unit/headless regression passed **1,984 tests, 16 conditional
skips, zero failures (2,000 total)** in `snapshot-sequence-full.trx`.

The managed replay review then found actual runtime parser gaps: executable C0
bytes aborted unfinished CSI, invalid parameters could become printed text,
decimal accumulation could overflow and colon-separated SGR was unsupported.
The parser now keeps ESC/CSI state across C0 execution and DEL, discards malformed
CSI through its final byte, restarts at a newer ESC, bounds parameter storage at
Ghostty's 24 entries and saturates numbers to u16. Unsupported intermediates no
longer accidentally dispatch ordinary cursor/margin commands. C1 transitions in
an unfinished header enter their corresponding state; existing legacy ground-state
C1 behavior is not changed by this batch. DECSTR remains the documented managed
extension rather than adopting Ghostty's ignored-reset behavior.

SGR retains colon separators rather than flattening them into semicolons. It
supports all six underline styles, defaulted/explicit color-space fields, indexed
and direct foreground/background/underline colors, malformed-group consumption
and upstream eight-bit truncation of out-of-range color components. This replaces
the previous intentional indexed-color clamping policy: indices 999 and 1000 now
resolve to 231 and 232, verified against native, and DECRQSS reports those effective
indices so replay reproduces the rendered colors. Repeated
executed controls are omitted from continuation using span segments, without an
unbounded index list. A warmed 100,000-BEL continuation run allocates zero bytes;
the cap applies to retained replay bytes, not already-committed effects.

Ghostty's `parse_table.zig`, `Parser.zig`, `sgr.zig` and `stream_continuation.zig`
define these choices. xterm's `EscapeSequenceParser` also keeps ESC/CSI state on
EXECUTE and ignores DEL; Windows Terminal's `_ActionExecute` performs immediate
control effects. The focused CSI/continuation/snapshot suite passed **160 tests**
in `csi-snapshot-parity.trx`, with native available; cases compare every input split
for colors/styles, malformed headers, parameter bounds, large decimals, C0/C1
controls and ESC restart. This does not close remaining DCS/OSC/UTF-8/charset
runtime parity or prove complete managed snapshot installation.

The final full macOS unit/headless run passed **2,032 tests, 16 conditional skips
(2,048 total)** with zero failures in `csi-parser-final.trx`. This includes the
updated DECRQSS effective-color policy regression and native comparisons for the
same out-of-range palette indices. Cross-platform runtime validation remains open.

The next parser pass replaces silent dropping of interrupted UTF-8 and acceptance
of overlong scalars with Ghostty's incremental error-replacement behavior. Lead
ranges C2–DF/E0–EF/F0–F4 and constrained second-byte ranges reject overlong values,
surrogates and values above U+10FFFF immediately. An invalid prefix produces
U+FFFD, then retries the offending byte, preserving a subsequent valid scalar or
control sequence. CAN/SUB no longer discard a pending scalar without replacement.
Only a new incomplete scalar remains in continuation after prior output commits.
Reference: Ghostty `UTF8Decoder.zig`/`stream.zig` specify replacement plus retry;
xterm.js `TextDecoder.ts` instead discards certain invalid prefixes, and Windows
Terminal `til/u8u16convert.h` retains partial input around the Windows UTF-8
conversion API. Ghostty is authoritative for this parity target. Existing managed
standalone raw C1 command support was still divergent at this stage; the following
ground/OSC pass resolves it. No shell startup or PowerShell behavior is changed.

The UTF-8 suite passed **31 tests**, including all **2,304** combinations of nine
lead-byte boundary classes and every second byte, plus every split of malformed,
valid-boundary, control-interrupted and new-lead sequences. The differential matrix
reproduced and fixed DEL handling on the decoder's retry path: unlike DEL in an
unfinished ESC/CSI header, this decoded scalar is printed by Ghostty. Ground-state
standalone DEL/C1 policy was still unaudited at this stage. Native was
available in `utf8-boundary-matrix.trx`; a warmed 1,000-iteration malformed-input
loop allocated zero bytes. The first combined parser/replay suite passed 89 tests
before the broader matrix and DEL regression were added.

The subsequent full macOS run passed **2,063 tests, 16 conditional skips (2,079
total)** with zero failures (`utf8-parser-full.trx`). CI run `35840483213` at
preceding commit `fa116746a549dc992228b2aa9b80a49c1ca75f47` passed all six native
builds; its macOS/Linux/Windows build-and-test jobs were still pending when checked.
Those checks do not validate this later UTF-8 commit.

The following ground/OSC pass resolves the earlier raw C1/DEL divergence. All
**256 ground-byte cases** now match native cells/cursor effects: raw C1 bytes are
invalid UTF-8, encoded C1 remains ignored, and ground DEL is printed. Header C1
transitions and ignored ESC/CSI DEL remain state-specific. The former eight-bit
OSC/DCS terminator mode was removed; high payload bytes retain UTF-8 semantics.

OSC now dispatches immediately on ESC or CAN/SUB, ignores other embedded C0
controls, and processes the byte following ESC as a new escape operation rather
than appending a false terminator to the payload. This supersedes the prior
managed prompt-control abort policy. Only the new ESC remains in continuation,
so replay cannot repeat already-committed effects. Eleven native differential
cases compare title effects, query bytes, cells and cursors at every input split;
a dedicated continuation test checks the exact commit/replay boundary. The
combined parser/query/regression suite passed **170 tests** with native available
(`control-string-focused.trx`).

Reference decision: Ghostty `stream.zig`, `parse_table.zig` and `Parser.zig`
define these byte-stream and exit-action rules. xterm.js also ignores ordinary
C0 inside OSC and starts ESC processing at OSC termination, but aborts OSC on
CAN/SUB rather than dispatching it. Windows Terminal likewise ignores invalid
OSC controls but waits for the second ST byte before dispatch; a non-ST escape
continues as a new escape operation. RoyalTerminal follows Ghostty's immediate
dispatch because native/managed snapshot continuation must share the same commit
boundary. DCS/APC transitions and complete charset/save state remain open.

The first full regression run exposed one old headless test requiring automatic
OSC recovery from Ctrl+C and prompt newlines. That behavior contradicts Ghostty,
so its replacement checks both processors through the real Unix PTY: Ctrl+C stops
the producer, a subsequent marker remains inside OSC, and explicit ST makes the
next marker visible. Both managed and explicitly registered native cases passed
in `osc-pty-recovery.trx`. Applications must terminate OSC (or reset the parser),
not rely on managed-only prompt-byte recovery. No Ctrl+C transport behavior changed.

The final full macOS unit/headless run passed **2,077 tests, 16 conditional skips
(2,093 total)** with zero failures (`control-string-final.trx`), including both
real-PTY recovery cases. This is local validation, not a substitute for pending
cross-platform runtime and complete snapshot-installation gates.

### APC transition and bulk-ingestion follow-up

Managed APC now uses Ghostty's `consumeApcString` byte classes: ordinary C0 and
DEL are payload, A0–FF are ignored, CAN/SUB and state-changing C1 controls exit,
and SOS/PM/APC C1 bytes keep the existing APC state. ESC commits immediately and
starts a new escape operation. Unknown sequences are published only on normal
termination; valid Kitty commands finalize on abort too, matching the actual
`stream_terminal.apcEnd` protocol-specific behavior. The 256-byte native matrix
compares every input split (2,048 split comparisons), events, replies, cells and
cursors; it also exposed the missing DECID reply, now shared with primary DA.
Six complete Kitty query exit cases cover CAN/SUB/ESC/ST/CSI/NEL. The combined
APC/CSI/continuation suite passed **71 tests** with native available.

APC payloads are now scanned in spans and copied directly into bounded storage
rather than appended byte by byte. The checked-in `--apc-ingestion` benchmark
measures 25 × 4 MiB feeds per sample (median of seven, Release .NET 10.0.5 ARM64,
no concurrent builds; continuation disabled, abort/decoding outside timing).
Against `82ad183` with the identical harness, truncated unknown ingestion improved
**239.507 → 5.442 ms**, and Kitty buffer ingestion **262.074 → 7.241 ms**. Warm
timed ingestion allocates zero bytes in both versions. These are ingestion-only
measurements, not claims about decoded image upload or end-to-end rendering.

Snapshot review also reproduced an upstream/native export defect: writing
`ESC _ Ga=q,i=73,s=1,v=1,f=32;AAAA/w==` followed by raw `9B 33` exports that entire
original sequence via `ghostty_terminal_continuation_buf` (result success), even
though leaving APC already finalized the Kitty query. Managed export now retains
only `ESC [ 3`, which the pure wire validator accepts and which cannot repeat the
query. Native export was still defective at this stage; the correction and
validation are recorded in the following subsection.

Reference comparison: xterm.js also exits APC on ESC and distinguishes abort
from termination, but its payload/control classes differ; Windows Terminal
consumes unsupported SOS/PM/APC strings without implementing Kitty. Ghostty's
byte table and protocol handler remain authoritative here. DCS headers/unhook,
complete charset state, custom glyph protocol/rendering, snapshot installation
and the broader performance/platform gates remain open.

Full macOS regression after this APC batch: **2,087 passed, 16 conditional skips,
2,103 total**, zero failures (`apc-parser-full.trx`). The benchmark project also
builds/runs in Release with its explicit managed-engine project reference.

### Native continuation export correction

The APC-to-C1 defect above is now corrected in a fourth narrowly scoped,
full-file-hash and replacement-count guarded native overlay. Four focused cases
failed against the unpatched binary; after rebuilding they export `ESC P`,
`ESC [` or `ESC ]` plus only the pending suffix, not the committed APC. An embedded
BEL in the new CSI is still omitted. Direct buffer and callback-stream exports,
native snapshot decode/re-export and subsequent terminal state agree across every
input split. Three additional cases preserve literal C1 bytes in uncommitted
OSC/DCS payloads and CSI headers. An additional chained-APC case verifies that
export removes multiple committed prefixes. The final combined snapshot suite
passed **109 tests** (`native-continuation-final-focused.trx`).

The correction only changes export; continuation retention limits and the native
input-tracking path are unchanged. The extra boundary scan runs only when retained
bytes contain a possible C1 DCS/CSI/OSC introducer. Native snapshot validation is
not weakened. The source submodule remains unmodified, and future upstream source
changes invalidate the overlay until reviewed. Windows Terminal's text-buffer
serialization and xterm.js's serialize addon emit terminal text, not Ghostty's
binary parser continuation; Ghostty's replay-without-effects contract is the
reference for this correction. This does not complete the managed snapshot
installation or remaining parser/renderer/platform audit.

The rebuilt macOS arm64 library passed **228 native integration tests**, zero
failures/skips (SSH integration excluded), and the full macOS unit/headless suite
passed **2,094 tests with 16 conditional skips (2,110 total)**. The chained-APC
test was added afterward and passed in the 109-case focused run; production code
was unchanged. Generated native binaries remain uncommitted release artifacts.

Host macOS arm64 plus cross-builds for macOS x64, Linux arm64/x64 and Windows
arm64/x64 all succeeded with the overlay; the resulting binary architecture was
checked for each cross-target. Runtime validation here remains macOS arm64 only.

Reference choice: Ghostty's snapshot per-record Zig codecs define the format.
Windows Terminal `TextBuffer::SerializeTo` and xterm.js's serialize addon emit VT
text; neither is an interchangeable binary-state format. Remaining implementation
is explicit: constructing semantic records from live managed state, complete
managed parser replay semantics, conversion/installation into both
managed screens, and incremental READY/history
coordination with parser continuation. The public managed binary restore path
remains unavailable until those layers are complete and validated.

Validation for this codec batch: **1,930 passed, 16 conditional skips, zero
failures (1,946 total)** in `snapshot-grid-page-full.trx`, with native available
on macOS arm64. CI run
[`35834169770`](https://github.com/royalapplications/RoyalTerminal/actions/runs/35834169770)
for the preceding `a4375d0` commit separately passed the macOS and Ubuntu build,
unit-batch and startup-smoke jobs and all six native build variants. That is
evidence for the preceding launcher/margin work, not the newly added codecs;
Windows and later workflow stages were not yet confirmed complete at inspection.

Follow-up CI run
[`35835433632`](https://github.com/royalapplications/RoyalTerminal/actions/runs/35835433632)
at `28f71ac` passed Ubuntu build/unit/startup smoke and all six native builds;
macOS/Windows build-test jobs were still pending at inspection. These results
cover the PAGE/grid commit, not the subsequent TERMINAL codec work.

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
