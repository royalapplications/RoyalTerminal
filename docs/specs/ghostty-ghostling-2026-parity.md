# Ghostty and Ghostling parity update (2026-09-22)

> **Scope revised by the user on 2026-09-23:** prioritize the native update, keep
> implemented managed improvements, and document deferred parity work. The
> [native-first delivery scope](ghostty-update-delivery-scope-2026.md) supersedes
> this historical audit's full-parity completion requirements. Open entries below
> remain open; they are not claims of completed ports.

## Reopened completion audit

### Password cursor and inactive appearance (2026-09-23)

Both processors now feed their password metadata into the Skia cursor policy.
Ghostty `renderer/cursor.zig` is authoritative: viewport exclusion wins, password
mode uses a steady lock even with DECTCEM off or focus lost, otherwise hidden mode
wins, an unfocused visible cursor is hollow, and focused cursors honor blinking
and the requested shape. Poll transitions update the renderer immediately; the
renderer reads processor metadata rather than a stale host hint after RIS.
Composition/preedit's higher-priority block remains part of the pending IME work.

The renderer requests Ghostty's U+F023 lock with cached font/glyph objects and
anchors wide tails to their lead cell. Unlike Ghostty's embedded Nerd Fonts,
RoyalTerminal allows hosts with no symbol font: a cell-bounded geometric lock is
the explicit fallback, not a missing-glyph box. The icon indicates password
metadata, not proof of OS secure input. Windows Terminal `renderer::_updateCursorInfo`
also gates viewport visibility and blinking; xterm.js `DomRendererRowFactory`
uses its inactive outline policy but has no local Unix password hint. We follow
Ghostty's password override and outline behavior here. Focused policy, pixel,
wide-tail and both-engine headless tests cover these decisions. No throughput
improvement is claimed for this correctness work. Validation follows commit/push.

### macOS secure-input ownership (2026-09-23)

The host now requests macOS secure input for a detected password hint only while
the terminal is focused and its containing window is active. `AutoSecureInput`
defaults to true (matching Ghostty's macos-auto-secure-input), with an explicit
opt-out for accessibility tools. `SecureInputEnabled` reports this control's
successfully acquired ownership, not global OS state. Both native and managed VT
processors use the same host policy. Unsupported platforms do not invoke Carbon
or schedule retries. An injectable, control-exclusive scope preserves existing
constructor compatibility and lets headless tests avoid real OS state changes.

Apple's SDK `CarbonEventsCore.h` documents a per-process reference count and
non-thread-safe calls. Each control owns at most one balanced enable/disable pair
on the UI thread; no shared mutable singleton or cross-control disable is needed.
Failed enables do not create ownership. Failed disables retain ownership and are
retried, including after detach, without issuing a second enable. Deactivation,
focus loss, opt-out, detach, close and session termination release the scope;
reactivation reacquires only when the full policy still applies. Termios polling
also stops while the window is inactive.

References: Ghostty `Features/Secure Input/SecureInput.swift`,
`SurfaceView_AppKit.passwordInput/focusDidChange`, and `Config.macos-auto-secure-input`;
Apple's installed SDK secure-event-input contract is the ABI/lifetime authority.
Windows Terminal's ConPTY and xterm.js's browser input do not expose this macOS
privilege, so unsupported-platform behavior remains a no-op. This adds automatic
OS ownership, not the remaining password cursor glyph or broader IME work.
Six ownership/ABI tests and three headless lifecycle/failure cases are added;
validation follows implementation commit/push. Tests resolve native symbols but
do not enable secure input on the developer's machine.
The final 13-test secure-input/password subset passed at `50d17a6` (zero skips).
Avalonia 12 raises Activated before setting IsActive; lifecycle policy observes
the settled IsActive property instead. Earlier test attempts failed at compile
time on inaccessible platform callbacks and are not counted as passing runs.

### Live Unix password-input detection (2026-09-23)

Unix PTYs now expose a thread-safe optional input-mode source. `tcgetattr` reads
the supported Darwin/Linux x64/arm64 ABI without heap allocation, and descriptor
closure shares its probe lock so a recycled fd cannot be queried. PTY transports
forward the capability; unsupported sources do not acquire a polling timer.
The control polls at Ghostty's 200 ms cadence only while focused and attached,
queries outside the processor lock, then updates both engine implementations under
the screen's demand lock. A read-only control property exposes the hint to hosts.
Focus gain refreshes immediately; focus loss/detach stop polling; stop/exit clear
the hint. Failure/unavailable probes resolve to false, not a stale password hint.
Only changed hints update processor state, matching Ghostty's timer and avoiding
unchanged samples undoing RIS. Endpoint changes switch ownership; refocusing an
exited process cannot restart its poller. Monitoring startup is posted without an
await when transport startup resumes off-thread, preserving synchronous StartPty.

Reference decision: Ghostty `termio/Exec.zig::termiosTimer/focusGained` and
`pty.zig::getMode` use `ICANON && !ECHO`; raw no-echo mode is not password input.
Windows Terminal's ConPTY path and xterm.js's browser core do not expose the same
Unix termios capability. No Windows password detection or text-based guessing is
introduced. This is metadata detection, **not** macOS secure-input activation or
the renderer lock glyph; those remain separate completion items. Seven Unix
ABI/lifetime/allocation cases, one transport-capability case and two real-engine
headless focus/session cases were initially added (16 focused tests passed).
The expanded four headless cases also cover natural exit and reset/no-change
semantics. The first full run exposed a synchronous-start/UI-callback deadlock;
live managed stacks identified the wait, the run was terminated, and startup was
corrected to post without blocking. That aborted run is not passing validation.
Final validation follows commit/push.

### Native allocation model and key registry follow-up (2026-09-23)

The native snapshot allocation model now accounts for runtime standard pages,
pooled allocation minima, row/cell alignment, Zig packed-RGB style layout,
grapheme/string bitmaps, hyperlink maps and minimum byte/line quotas. All 14
focused cases pass against native restore boundaries, including zero quotas,
independent capacity hints and widths 1 through 32768. This is semantic native
allocation accounting, not a CLR memory estimate. It is not yet wired into live
managed retention: page ownership, mutation, reflow and pruning accounting remain
required before claiming exact quota parity.

The next implementation batch completes mappings for Ghostty's Kitty keypad
registry, adds the ScrollLock alias and rejects malformed/unknown managed key IDs
even when callers supply text or layout scalars. Legacy keypad navigation follows
Ghostty's cursor mode independently of keypad application mode; keypad Enter uses
the application-keypad table, not ordinary Enter's IME handling. The reference
decision follows `input/kitty.zig`, `function_keys.zig` and `key_encode.zig`;
Windows Terminal's virtual-key translation and xterm.js's browser keyCode mapping
are host adapters rather than interchangeable string-ID registries. Added native
differential matrices cover flags, modes, modifiers, actions and text/composition.
All **165 focused allocation/key cases pass**, including 45 new key-registry
cases. No performance gain is claimed.

Both built-in processors now opt into an authoritative key-encoding policy. A
deliberate empty result must not fall through to legacy or win32-input encoding;
the session mode-source adapter preserves that policy. Third-party encoders
without the capability retain their existing fallback contract. Committed text
still reaches Kitty mode when win32-input is also set. This follows Ghostty's
empty-output contract and Windows Terminal's Kitty-before-win32 precedence;
xterm.js's unhandled browser fallback is retained only for non-authoritative
sources. Four new real-engine adapter cases cover direct/proxied suppression,
IME Back, releases and separate committed text; validation follows commit/push.

The Avalonia byte-input adapter now preserves physical keypad Enter/Equal and
NumLock-off navigation identities for both down and up, including repeats.
Ghostty GTK's KP key map and Windows Terminal's separate keypad Return handling
establish the distinction; xterm.js and Ghostling's simpler logical-key adapters
do not provide the complete Kitty keypad identity. Fifteen adapter cases exercise
both engines, NumLock on/off, ordinary Enter, and application-keypad Enter. This
does not infer layout-derived unshifted scalars or consumed modifiers.

Windows ARM64 CI runs 35901210398 and 35902114973 fail before compilation with a
missing cached Zig build.exe. CI and release Windows jobs now disable restored
Zig build caches while leaving compiler installation unchanged. This is a cache
failure mitigation pending a fresh run, not completed Windows platform sign-off.

### Legacy input and lifecycle follow-up (2026-09-23)

The managed processor now implements Ghostty's legacy key pipeline rather than
only mode-2 extensions: PC keys, modified F3, F1–F25, keypad/1035/application
policies, DECBKM, mode 2, C0 exceptions, fixterms, Unicode Alt prefixes,
consumed modifiers, IME suppression and native-default macOS Option/Command
behavior. `input/key_encode.zig` and `function_keys.zig` are the byte oracle;
Windows Terminal `_encodeRegular` and xterm.js `Keyboard.ts` have distinct legacy
policies, so Ghostty's deliberately different Ctrl+I/M/[ rules are retained.
All **56 new legacy differential cases** pass, covering 32 mode combinations,
64 modifier combinations, press/repeat/release, and text/layout/IME matrices.
No throughput gain is claimed for this correctness addition.

Transport Stop/Dispose failure now still clears service ownership and joins the
session output worker before queued leases are discarded. Two headless tests
verify clean restart and rejection of stale callbacks after either failure.
Ghostty `termio/Thread.zig` requires joining before deinit; xterm's WriteBuffer
checks disposal before queued callbacks, and Windows Terminal owns connection
shutdown separately. This is lifecycle hardening, not six-platform sign-off.

Avalonia has no repeat flag in KeyEventArgs. The surface-owned adapter now tracks
physical down/up identities (logical fallback for synthetic events), supplies
Repeat to both input endpoints and VT encoders, and clears state on focus loss,
detach and session boundaries. The reset hook also clears Windows AltGr state
and forwards through the app decorator. Ghostty consumes explicit repeat actions;
Windows Terminal similarly tracks repeats before Kitty encoding. xterm's browser
keyboard path is not a source for native physical-key lifecycle. **76 input
adapter/headless focused cases** pass, including six new cases covering both
engines, physical identity changes and focus/detach/restart. Layout-derived
unshifted scalars, consumed-modifier and full IME event production remain open;
the adapter does not invent layout data from physical US key positions.

Full post-push Release through `d71eaf8`: **3,774 unit/headless + 231 integration
passed, 16 conditional skips, zero failures** (`export-legacy-lifecycle-full.trx`).
This includes 20 binary export, 56 legacy and two shutdown-failure cases. Native
VT and renderer rebuild with Zig 0.16 on macOS arm64.

Final full Release through `ff9a7be`: **3,780 unit/headless + 231 integration
passed, 16 conditional skips, zero failures** (`export-legacy-repeat-verified.trx`).
The complete solution also builds with **zero warnings/errors** at `eecae9b`.
The new required-native CI gate passes all **232 integration cases**, zero skips,
with `ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1` (`native-required-verified.trx`).
Together the final suites cover **4,012 passing tests / 85 new cases**. CI now
executes this separate integration suite on its three managed runtime runners;
six native RID builds alone are not six-platform runtime sign-off. Remote HEADs
remain Ghostty `622b4eecd` / Ghostling `63842bf8` at the final recheck.

### Managed binary export (2026-09-23)

Implemented and validated: `BasicVtProcessor.GetBinarySnapshot`
and `WriteBinarySnapshotTo` capture live (not frozen render) terminal/processor
state without VT replay or screen switches. The stream remains caller-owned;
an IO/encoding failure can leave a prefix without FINISH. Invalid or unavailable
continuation and planning-limit failures emit no envelope. The same configurable
cell/page/string/suffix/payload/continuation bounds used by decode apply to export.

Ghostty `snapshot/snapshot.zig`, `screen.zig`, and `Terminal.switchScreenMode`
define record ordering and dormant cursor state. Departing pens/protection and
snapshot-installed dormant hyperlinks now survive capture. Windows Terminal's
DECRC restores per-buffer cursor state, while xterm.js SerializeAddon replays VT
and intentionally omits temporary synchronized-output mode; neither replaces the
Ghostty binary contract, which preserves the raw mode bank and continuation.

Pages group equal physical widths, with native style/hash-map capacity headroom;
link-dense rows split rather than silently dropping identities. READY carries the
viewport, HISTORY carries older pages newest-first without overlap. Planning
retains row references/ranges, not a full cell copy; streaming buffers one payload
at a time. A single row exceeding wire or configured capacity is rejected.
Managed export has no native page-byte policy, so bytes are unlimited in the
header and the managed row policy is written explicitly. Exact native quota
parity, wider differential coverage and performance measurements remain open.
All 20 focused export cases pass with native available, including both-screen
switch/reset capture and the complete upstream-compatible normalized fixture.

### Public restore, Kitty encoding and pointer lifecycle (2026-09-23)

Implemented and pushed before validation, then corrected against the native oracle:

- `ManagedTerminalSnapshot.Restore` provides transactional memory/stream restore;
  `ManagedTerminalSnapshotDecoder.Ready/Next` publishes resident state before
  streamed history. The caller owns the returned terminal independently of the
  decoder and stream. Exact memory restores reject trailing data; stream restores
  stop at FINISH. Every record, including dropped pages, spends the public decode
  budgets. Errors poison the decoder without destroying an already returned terminal.
- Assembly installs colors, mode/default banks, cursor policy, geometry, both
  screen states, metadata and password state, then replays and verifies continuation
  once. Retention can be disabled after replay. History targets the live COW screen
  during render holds and updates semantic prompt-seen only after successful prepend.
  Managed row quotas deliberately use RoyalTerminal's host contract, including no
  additional alternate history; they do **not** emulate native PageList allocation
  capacity/minimum-byte accounting. READY overlap is preserved. Exact native quota
  parity and a public managed binary exporter remain open.
- Both engines expose host password-input metadata, including snapshot and reset
  semantics. Ghostty `termio/Exec.zig` derives this from canonical/no-echo termios;
  `Surface.passwordInput` separately integrates OS secure input. This batch adds
  the terminal flag, not platform termios notification or OS secure-input support.
- Managed Kitty encoding ports all five progressive flag combinations, functional
  key mappings, repeat/release events, modifier locks, layout alternates, associated
  text and composition handling. The public key request now carries layout-derived
  unshifted scalars and consumed modifiers; native forwards these unchanged. New
  differential tests compare all 31 flag values, 64 modifier combinations and three
  actions, plus IME/layout cases. Avalonia repeat/layout event production and legacy
  non-Kitty encoder parity remain separate from the encoding API implementation.
- Pointer capture loss clears both processors' physical/deduplication state without
  synthesizing PTY input. Outside drag/release reports retain raw pixel coordinates,
  independently of selection hit-testing. An additional worker failure/reschedule
  test exercises concurrent flush waiters and disposal; it is not platform sign-off.

Reference decisions: Ghostty `snapshot/snapshot.zig` and `c/snapshot.zig` define
restore ownership/order and continuation verification. xterm.js SerializeAddon
emits replayable VT, not this binary wire format; Windows Terminal cursor restore
does not supply a corresponding binary snapshot API. Ghostty `input/key_encode.zig`
and `input/kitty.zig` are the byte oracle. Windows Terminal `terminalInput.cpp`
also distinguishes Kitty repeat/release and associated text but uses different
functional-key/legacy fallback rules; xterm.js `input/Keyboard.ts` provides the
legacy comparison, not a replacement for Ghostty's progressive-flag behavior.
Pointer reports follow Ghostty `Surface.mouseReport`/`input/mouse_encode.zig`;
host selection coordinates remain distinct from encoded terminal coordinates.

No throughput improvements are claimed for these correctness/API additions.
Full rendering/performance review, managed binary export/native quota parity,
remaining input/OS integration and platform runtime gates are still open.

Post-push full Release through `a3565d8`: **3,695 unit/headless + 231 integration
tests passed, 16 conditional skips, zero failures** (`restore-kitty-full2.trx`).
This adds **72 cases**, including 38-key Kitty matrices spanning all 31 flag values,
64 modifier combinations and three actions, plus layout/IME differentials. Native
is available on macOS arm64; both native libraries rebuild successfully at latest
verified upstream `622b4eecd7d2ce1a10930537c17f0d61abdba817`. The two new upstream
commits after `4ae9f1a2d` only update contributor lists; Ghostling remains
`63842bf8e5e481160f81d348da9ff6fd27986798`.

Initial validation found a ref-struct span capture compile error, missing keypad
base-layout alternates, two test setup errors (same clamped motion cell and the
fixture's inactive primary history), and two old UI assertions expecting clamped
outside coordinates. Those are corrected; the raw SGR-pixel assertion now runs
through both backends. The final full run passes without excluding those tests.
Cross-platform CI at `35897093967` was queued at inspection; earlier superseded
runs were cancelled, not successful platform sign-off.

### OSC 22 pointer shape (2026-09-23)

Both engines expose the terminal's requested pointer shape. Managed parsing
accepts all 34 W3C names and 22 xterm/foot aliases from Ghostty `terminal/mouse.zig`,
case-sensitively, with canonical OSC 22 selection and the native 2,047-byte payload
limit. Unknown names are ignored. Shape is global and live during render holds;
Ghostty's `fullReset` leaves the separate `mouse_shape` field intact,
so both engines retain it through RIS, DECSTR and session reset. Snapshot header
byte 37 installs it directly, including native unknown-value normalization.

Native state copies reuse the effective mouse query, avoiding another per-frame
interop call. The control publishes cursor changes only on the UI thread, caches
standard cursors per attached control and releases them on detach. Hyperlink hover
temporarily takes precedence and restores the requested cursor on exit. Ghostty's
`Surface` follows this same override model; its GTK runtime supports the full CSS
registry while AppKit ignores some shapes. Windows Terminal/xterm.js do not offer
this OSC 22 registry in the inspected handlers, so Ghostty is the reference.

Avalonia 12.1.1's standard cursor registry cannot represent every W3C shape.
Documented host fallbacks retain exact terminal state: context-menu→Arrow;
vertical-text→Ibeam; cell/zoom-in/zoom-out→Cross; grab/grabbing→Hand;
no-drop/not-allowed→No; diagonal bidirectional resize→the matching corner cursor.
Other shapes map to their corresponding standard cursors. This is a platform
presentation limitation, not lossy VT/snapshot state. The official API inspected
is `Avalonia.Base/Input/Cursor.cs` at tag 12.1.1.

Differential tests cover every name/alias at every split, reset/hold semantics,
all 256 snapshot wire values, and allocation-free warm parsing. Headless tests
cover every mapping through both real backends, cache reuse, hyperlink precedence,
hold updates and detach/reattach. All **99 focused cases** (70 new) pass after
the native rebuild, with native available on macOS arm64. The complete Release
build has zero warnings/errors. Post-push full Release through `65f29af`:
**3,623 unit/headless + 231 integration tests passed, 16 conditional skips,
zero failures** (`mouse-shape-full.trx`). Platform runtime sign-off is separate.
Password
input state and complete snapshot orchestration remain open.

### modifyOtherKeys mode 2 (2026-09-23)

Ghostty `stream.zig` and `stream_terminal.zig` are authoritative for CSI > m/n:
resource 4/value 2 enables the flag; supported other forms reset it, unsupported
resources/arity are ignored, CSI > n resets irrespective of numeric parameters,
and colon separators are accepted for CSI m but not CSI n. State is global,
survives DECSTR/buffer switching, clears on RIS/session reset and installs from
snapshot header byte 33 without replay. Both engines expose it through a focused
capability, also forwarded through the session mode source.

Managed encoding adds Ghostty's CSI-27 mode-2 extension before legacy Ctrl/C0
conversion, including full modifier combinations, scalar text rules, special
Back/Tab/Return/Escape mappings and DECBKM exceptions. Kitty still takes priority.
Unchanged legacy/Kitty mappings remain delegated to the existing host fallback;
this is not a claim that the complete managed key encoder is now ported.
The native encoder already implements the byte protocol, but the host previously
bypassed it for Shift text input. Routing now consults live mode-2 state first.
macOS Option remains text input under the C encoder's default host policy.

Windows Terminal `terminalInput.cpp` and xterm.js `input/Keyboard.ts` retain
different legacy Tab/Return mappings; Ghostty's mode-2 extension is the selected
behavior, not their legacy defaults. Tests cover parser input splits, every
modifier/Backarrow combination, Unicode, special keys, hold/reset/snapshot state,
native ABI guards and both backends through session input routing. All **55
focused cases**, including the text-lifetime tests below, pass against the rebuilt
native library. Post-push full Release through `199c337`: **3,553 unit/headless +
231 integration tests passed, 16 conditional skips, zero failures**
(`modify-other-keys-full.trx`). Native VT/renderer rebuilds and symbol checks pass
on macOS arm64; the complete solution Release build has zero warnings/errors.
At inspection CI run `35892233269` passed native macOS arm64 and both Linux
architectures; Windows arm64/x64 and macOS x64 were still running. This is not
complete platform sign-off. Broader keyboard/IME and
repeat-event parity remain open; the public input contract currently has only
press/release actions.

Reviewing `c/key_event.zig:set_utf8` exposed a borrowed-pointer lifetime bug: the
old wrapper retained neither the temporary UTF-8 array nor its pin after setting
the native event. The event now owns a reusable pinned-object-heap buffer, and
the encoder keeps that owner alive through both native encode calls. Forced
compacting-GC tests cover Unicode, large growth, shorter replacement, embedded
NUL, replacement encoding of invalid UTF-16, empty/reset and disposal. Warm
updates reuse capacity without managed allocation.

Two sequential fixed-JIT Release benchmark pairs used 20,000 warmups and seven
samples of 100,000 setter calls. ASCII medians changed **1.849→1.157 ms** and
**1.476→1.070 ms**; `é😀` changed **1.840→1.502 ms** and **1.719→1.449 ms**.
Timed allocations fell from **3,200,000 to zero bytes** for each workload.
Only setter work was timed; the unsafe baseline pointer was not dereferenced.
Bindings were hash-verified (`c3483499…` before, `a34c82cc…` after), and both
used native library `064222fc…`. This narrow comparison establishes neither
end-to-end keyboard latency nor complete managed/native performance parity.

### Shift-mouse capture and physical button state (2026-09-23)

Both VT engines implement XTSHIFTESCAPE, expose its nullable application override,
and preserve its global/reset/snapshot semantics. The control resolves Ghostty's
four-way host policy before encoding: Disabled/Enabled permit application
overrides, while Never/Always force selection/capture. Native input endpoints
retain their own host policy. Physical button transitions are observed even when
Shift selection or reporting-off suppresses the report, avoiding stale pressed
state without encoding ignored input or advancing motion history.

Reference decisions and detailed coverage are below in the Shift-mouse section.
Post-push full Release through `beca5f2`: **3,498 unit/headless + 231 integration
tests passed, 16 conditional skips, zero failures** (`shift-capture-full2.trx`).
All **44 new cases** pass with native available on macOS arm64, including actual
Shift-drag selection/reporting through both backends. Native Zig 0.16 VT/renderer
builds and exported-symbol checks pass. Warm native state/set loops allocate zero
bytes; this is not an end-to-end performance claim.

The first full run had one unrelated font-fallback allocation assertion failure
(6,696 bytes); its unchanged test passed in the focused rerun and final full run.
The initial native headless fixture omitted provider registration; that setup was
fixed, not skipped. A concurrent solution build hit an Avalonia intermediate PDB
lock; the sequential complete solution build passes with zero warnings/errors.
Cross-platform CI remains a separate gate.
Broader capture-loss lifecycle, outside-viewport UI normalization, remaining
keyboard flags, complete snapshot orchestration and IO/rendering/platform gates
remain open.

### Stateful mouse encoding and geometry (2026-09-23)

Native pointer configuration is now cached: unchanged effective modes/geometry
do not call native setters that reset last-cell tracking. The managed processor
has a session-owned encoder with matching motion history and pressed-button
state. Motion in the same reported cell is suppressed, but Ghostty's raw-pixel
mode intentionally continues emitting identical positions. Mode/geometry changes,
explicit resize and session resets invalidate the appropriate state.

Geometry follows Ghostty `renderer/size.zig`, `input/mouse_encode.zig`, its C
wrapper and `Surface.mouseReport`: f32 surface coordinates, padding subtraction,
context-derived grid bounds, unclamped rounded terminal-space SGR pixels,
out-of-viewport filtering with release/drag exceptions, and rejection rather than
clamping beyond legacy 223-cell limits. Windows Terminal tracks last mouse
position/button; xterm.js filters/deduplicates through its active protocol and
encoding service. Their deduplication/pixel policies are not substituted for
Ghostty's behavior. Invalid host geometry, nonfinite positions and values outside
native integer conversion ranges are rejected before calling unsafe native math.

Tests compare both processors against an independently configured persistent
native encoder across all 25 mode/format combinations and six geometry contexts,
with mixed press/release/drag/scroll/duplicate events. Dedicated tests cover
zero-allocation duplicate suppression, pixel repeats, resize/session invalidation
and invalid geometry. Old managed one-based-pixel/clamping assertions are updated
to the native reference; legacy boundary acceptance remains covered. Far-negative
cell releases remain valid independently of pixel integer limits, and a current
drag button is honored even if its initial press was outside this surface.

Post-push full Release validation through `3332b76`: **3,454 unit/headless + 231
integration tests passed, 16 conditional skips, zero failures**
(`pointer-encoder-full.trx`). All **37 new cases** pass with native available on
macOS arm64. The complete solution builds with zero warnings/errors. Earlier
focused validation passed 131 cases before the final six boundary cases were
added. No native library source changed in this batch.

Two sequential Release benchmark pairs used 20,000 warmups and seven samples of
100,000 SGR motion events. Default-runtime timings varied during tiered JIT
optimization, so the table reports repeated fixed-JIT runs with
`DOTNET_TieredCompilation=0` on both sides. Assemblies were hash-verified; both
native-adapter runs used the same `fcce428d…` library. Timings are medians, not an
end-to-end latency claim.

| Workload | Baseline → current, run 1 | Baseline → current, run 2 | Timed allocations |
| --- | --- | --- | --- |
| Managed repeated same cell | 2.513 → 1.952 ms | 2.417 → 1.991 ms | 4,000,000 → 0 bytes |
| Native repeated same cell | 13.643 → 5.253 ms | 14.508 → 5.198 ms | 4,000,000 → 0 bytes |
| Managed alternating cells | 2.363 → 4.001 ms | 2.415 → 4.331 ms | 4,000,000 bytes, unchanged |
| Native alternating cells | 14.056 → 13.237 ms | 14.277 → 13.154 ms | 4,000,000 bytes, unchanged |

Repeated motion previously emitted 100,000 duplicate reports; now it emits none
after warmup. Alternating cells still emit 100,000 reports. **Managed moving-cell
encoding regressed by roughly 16–19 ns/event** with the additional geometry,
filtering and state checks; no blanket managed throughput improvement is claimed.
Baseline/current managed hashes: `c2a3572b…` / `8d589bd3…`; native adapter:
`b8ae95ee…` / `0a14d4f7…`. Remaining mouse capture policy, input lifecycle,
keyboard flags, full snapshot orchestration and IO/rendering/platform review are
not closed by this encoder batch.

### Authoritative mouse state and input routing (2026-09-23)

Both processors now expose effective tracking/encoding via the optional
`ITerminalMouseModeStateSource` contract. The native extension copies exactly the
flags consumed by Ghostty `c/mouse_encode.zig:setopt_from_terminal`, rather than
ORing DEC mode bits like upstream's boolean query. Snapshot-restored flags and
mixed enable/reset commands can disagree with those independent bits. Live input
state remains live during synchronized-output holds.

The control respects authoritative reporting-off and encoder suppression instead
of retrying through its fallback tracker. Fractional-cell coordinate conversion
now uses the authoritative encoding, preserving raw SGR-pixel coordinates even
when the tracker still thinks cell encoding is active. Fallback remains only for
processors that do not implement the corresponding capabilities. Ghostty's
encoder, Windows Terminal `TerminalInput::HandleMouse`, and xterm.js
`MouseService._triggerMouseEvent` all apply filtering before emission; xterm.js
also chooses pixel/cell handling from its active encoding service. The chosen
behavior is to never override the owning encoder's no-output result.

ABI guards, all tracking/format snapshot combinations, all 64 ordered mode pairs,
live hold/session-reset behavior, and headless routing/pixel tests are added.
The post-push native rebuild succeeds with Zig 0.16; **129 focused tests pass**
with native available on macOS arm64. Initial validation exposed a signed Zig
enum conversion and a second press/release path that bypassed the reporting-off
gate; both are corrected. Full post-push Release validation through `a1427a7`:
**3,417 unit/headless + 231 integration tests passed, 16 conditional skips, zero
failures** (`mouse-authority-full.trx`). The complete solution builds with zero
warnings/errors. All **32 new cases** pass. The native state copy has a
zero-allocation warm-loop regression; no throughput gain is claimed. Other RID
runtime sign-off is still a separate gate.

This does not yet close mouse-shift policy, encoder lifetime/deduplication,
keyboard flags or broader snapshot/orchestration/performance requirements.
Follow-up source evidence (addressed by the subsequent encoder batch above):
native `setopt_from_terminal` and size setters reset
last-cell tracking, and the adapter invokes them for every pointer event. Native
pixel reporting uses rounded terminal-space coordinates (not clamped one-based
cells); managed pointer normalization currently differs. These are outstanding
encoder issues in that earlier batch, not covered by its state-source/routing
completion claim.

### Raw metadata and active status display (2026-09-23)

The shared optional `ITerminalMetadata` contract exposes owned host setters and
nonallocating raw-byte copies of title/PWD state in both processors. Host setters
do not synthesize OSC callbacks, decode UTF-8/URIs, or apply protocol byte limits.
Managed snapshot installation retains raw strings and status-display selection
without effects. CSI 21 t now returns the actual title, only when explicitly
enabled by the host; this policy survives resets while metadata/status reset.

Compatibility follows Ghostty `osc.zig`, its title/PWD/OSC 9/iTerm2 parsers,
`stream.zig`, `stream_terminal.zig` and `Terminal.print`: OSC 0/2 validate UTF-8
before byte truncation; OSC 1 is icon-only; raw directory reports are preserved;
fixed captures include their NUL reservation. DECSASD suppresses printing, not
controls, and preserves REP/charset state until printing resumes. Windows
Terminal delegates window titles to its host (`AdaptDispatch::SetWindowTitle`);
xterm.js distinguishes title/icon OSC handlers and gates title reports through
window options. Ghostty's byte limits, explicit title-report policy and
status-display behavior are the parity target, not those hosts' string models.

Focused tests cover host ownership, short destinations, raw/invalid UTF-8,
canonical selectors, every split, capture limits in scalar/bulk input, callbacks,
status/REP/control behavior, session resets and side-effect-free restoration.
The **103 new focused cases** pass with the native library available on macOS
arm64. The first full run exposed an old test that incorrectly expected a 16 KiB
title; it now checks that large allocating OSC 52 payloads remain supported,
while a separate regression requires rejection of oversized fixed title captures
without changing the prior title. Post-push full Release validation through
`25b8399`: **3,385 unit/headless tests and 231 integration tests passed**, with
**16 conditional skips, zero failures** (`metadata-full.trx` in each project's
results directory). The complete Release solution builds with zero warnings and
errors. This local macOS arm64 result is not cross-platform runtime sign-off.

Two sequential, hash-verified Release microbenchmark pairs measured 100,000
unobserved OSC 2 updates (100-byte titles), after 20,000 warmups, using seven
samples: median **46.587→32.951 ms** and **46.889→33.553 ms**; timed allocations
fell from **45,600,000 to 0 bytes**. The raw buffer reuses capacity and skips
string decoding when no title callback is subscribed. This narrow measurement
does not imply an equivalent gain with callbacks, rendering or transport work.
Baseline/current assembly hashes start `bde54cd9…` / `86317076…`.

Full snapshot orchestration, remaining keyboard/mouse consumers, quota policy
and platform sign-off remain open; these primitives do not establish full parity.

### Snapshot geometry and current/saved processor registers (2026-09-23)

New unpublished installation primitives assign exact tab stops, unsigned 32-bit
pixel geometry, scroll margins, nullable previous character and active-screen
routing without resizing or executing VT commands. SCREEN installation resolves
logical pens, preserves all charset slots (including arbitrary pending single
shift), restores independent saved cursors, protection modes, semantic pens/click
policies, hyperlink counters/current links, shapes and Kitty keyboard rings.
Prompt-seen state is derived from resident rows/cells and the current semantic
pen, not omitted history. Inactive cursor coordinates and alternate erase
background survive for subsequent resize and 1049 entry behavior.

Ghostty `snapshot/terminal.zig`, `snapshot/screen.zig`, `saveCursor`,
`restoreCursor` and `switchScreen` are the references: current coordinates clamp
to their physical page row, while saved coordinates clamp to terminal geometry;
pending wrap is retained only at the corresponding edge. Windows Terminal and
xterm.js save position, character maps and attributes, but their save policies
and history coordinate models differ; the snapshot-v1 contract follows Ghostty.
Dormant charset copies are updated at screen switches, leaving the printer's
active charset register direct. No throughput improvement is claimed.

Restored storage now carries its physical-row policy through COW publication.
Buffer routing no longer implicitly resizes those rows or trims incidental
history (including alternate history). Explicit resize and quota-aware mutation
remain separate operations. Differential tests require exact widths on install;
after input, newly allocated rows may have different backing capacities, so all
retained cells are compared with only absent capacity padded by default blanks.
Hidden nonempty cells and resident rows are separately tested across repeated
switches and publication with a zero host scrollback limit.

The first differential runs exposed two independent existing gaps: DECRQSS lost
curly/dotted/dashed underline variants and placed overline after blink, and buffer
switches normalized snapshot row widths. Both are corrected. All underline
variants now retain Ghostty's report order; underline color remains omitted from
DECRQSS exactly as upstream (unlike full VT style formatting).

These are processor assembly primitives, not yet a complete public restore:
remaining terminal flags/metadata, continuation, export and quota-aware history
orchestration are still open. The focused Release/native suite passes **286 tests**
including all **46 new cases**. Full post-push Release validation through
`acc5543`: **3,281 passed, 16 conditional skips, zero failures (3,297 total)**
(`snapshot-processor-full.trx`), with native available on macOS arm64 and no build
warnings/errors. All 65,536 encoded charset bit patterns are checked for normalized
installation, and warm packed charset installation allocates zero bytes. The
cross-platform native build matrix remains CI evidence, not local runtime sign-off.

### Cursor appearance and default policy (2026-09-23)

Ghostty `Terminal.setCursorStyle`, `cursor.zig`, `dcs.zig` and
`switchScreenMode` are the reference: shape belongs to each screen, mode 12 owns
blink state, and terminal-wide default-selection state determines whether later
host policy changes apply immediately. Screen modes 47/1047 copy shape both ways;
1049 copies it on entry but retains the primary shape on return. DECSC/DECRC does
not save shape or blink. Default selection and RIS restore configured shape and
blink, including hollow blocks; DECRQSS reports the resolved shape/blink pair.
Invalid DECSCUSR values and extra parameters are ignored, not clamped.

The managed processor now represents these states separately, captures both shape
and blink in synchronized-output publication, and installs snapshot policy and
per-screen shapes without replay or overwriting restored mode bits. Both engines
expose `ITerminalCursorDefaults` with matching default-shape/default-blink setters;
the native adapter forwards these to the existing C API and configures its lazy
Sixel overlay. History-preserving session reset reapplies cursor defaults after
resetting mode bits. Snapshot-only null blink policy is retained and resolves to
blinking; the C API's bool policy remains bool (clearing it means false upstream).

Reference comparison: Windows Terminal rejects invalid cursor styles and selects
its configured shape for zero; xterm.js also returns zero to host configuration
but derives blink from odd/even for unsupported nonzero styles. RoyalTerminal
follows Ghostty's complete rejection and per-screen copy rules. Existing managed
DECSTR reset support remains an explicit extension rather than native parity.
Post-push validation through `e199639`: **340 focused tests** pass, including all
**95 new cursor cases**; full Release passes **3,235 tests, 16 conditional skips,
zero failures (3,251 total)** (`cursor-parity-full.trx`). Native differential
tests ran on macOS arm64. The warm managed policy/read loop allocates zero bytes;
this correctness refactor makes no throughput claim. Build output has no warnings
or errors. All six native build jobs in CI run `35880301513` were in progress at
inspection, not completed platform validation. Full processor restoration remains
open, including geometry, parser/runtime state, history quotas and public
restore/export orchestration.

### Legacy color operations and native host report policy (2026-09-23)

Managed OSC 4 now skips empty tokens, retains valid operations before the first
invalid pair, preserves ordered set/query behavior, and handles Ghostty's signed
zero/plus/interior-underscore index syntax. OSC 10-12 accepts successive dynamic
color values, stops at invalid colors and skips empty parameters. OSC 110-112
rejects nonempty arguments. OSC 104 skips malformed indexes and resets the entire
palette when no target is accepted; valid unsupported special targets 256-260
instead suppress that fallback. All recognized legacy color payloads now have
the same 2,048-byte streaming capture limit as native, including split prefixes.
Replies are batched per OSC and preserve BEL versus ST. Canonical selectors are
required, and unsupported native special colors remain no-ops.

Reference decision: follow Ghostty `osc/parsers/color.zig` and
`stream_terminal.zig:colorOperation`. Windows Terminal also restores configured
colors; xterm.js's invalid-only palette reset and dynamic reset argument behavior
differ, so those are not the compatibility target. Focused native differentials
cover these deliberate choices and both complete color state and reply bytes.

The native adapter now honors the host's eight-bit OSC color-report option, which
managed already exposed. A bounded span formatter rewrites only complete native
OSC 4/10/11/12 report batches, preserving terminators/order and leaving Kitty,
clipboard/title/device replies and incomplete/malformed batches untouched.
It allocates no intermediate buffers; the callback allocates the exact final byte
array. The default sixteen-bit path stays a direct copy. This is an explicit
host policy applied above libghostty-vt, not a change to the native core ABI.

Post-push Release validation through `1c3d0fd`: **317 focused tests** pass with
native available, including **153 new cases**. The complete unit/headless suite
passes **2,975 tests, 16 conditional skips, zero failures (2,991 total)**
(`legacy-colors-full.trx`). Native core and adapter differentials ran on macOS
arm64. The native report formatter has a warm zero-allocation regression test.

A separate 80x24 managed palette benchmark performs 10,000 warmup updates and
seven samples of 10,000 OSC 4 batches (three RGB palette entries per batch).
Sequential Release measurements against the saved preceding binary produced
**42.768→41.739 ms** and **31,760,000→28,240,000 allocated bytes**. Removing
per-token strings/arrays and redundant selector decoding saves 352 bytes per
batch on this workload; the small timing change is not a broad throughput claim.
The binary hashes were verified before timing: preceding managed assembly
`81053658…`, current `9fa5d453…`. An initial attempted after run selected a stale
MSBuild candidate DLL and was discarded; explicit hint-path resolution corrected
the harness. No performance claim uses that stale result.

At CI inspection, the native macOS arm64 build for the implementation had passed;
the other native builds were queued/running. The preceding `f93439a` run was
cancelled by later pushes, not a completed platform validation. Full snapshot
orchestration and the broader renderer/IO/platform gates remain open.

### Kitty color protocol and shared VT color parsing (2026-09-23)

Managed OSC 21 now parses a bounded ordered batch before applying any changes.
It preserves Ghostty's exact 526-accepted-request limit, rejects the entire batch
when another token follows that limit (even an invalid/empty token), counts valid
unsupported keys, retains query ordering, and echoes BEL versus ST. Supported
queries report eight-bit channels regardless of host OSC report preferences;
absent dynamic colors produce empty values, with no OSC-12-style cursor fallback.
Color changes resolve the theme once per batch and obey synchronized publication.
The first native differential run found a second, independent limit: OSC 21 uses
the upstream fixed **2,048-byte payload capture**, not the allocating 8 MiB OSC
capture. Streaming bytewise and bulk paths now reject overflow before copying or
applying it, including splits inside `21;`. Tests independently exercise both the
byte and accepted-request limits. Noncanonical selectors are ignored; C1 ST stays
payload as in the already-aligned Ghostty stream parser (tests retain continuation
when taking a snapshot of an unfinished OSC).

The shared VT color parser now follows Ghostty `color.zig:RGB.parse` and
`fraction.zig`, including all generated X11 names, ASCII-only name folding,
case-sensitive protocol prefixes, one-to-four-digit mixed channel widths,
scaled/truncated hash colors, unsigned underscore syntax, and bounded-precision
decimal fractions. Existing OSC 4/10/11/12 use the same parser. Host theme-file
syntax remains unchanged. The generated table is reproducible with
`scripts/generate-ghostty-x11-colors.py --check` against the pinned source.

Reference decision: follow Ghostty `kitty/color.zig`,
`osc/parsers/kitty_color.zig` and `stream_terminal.zig:kittyColorOperation`.
Windows Terminal `ColorFromXTermColor` also supports X11 names, but its XParse
syntax differs; xterm.js `XParseColor.ts` excludes names/intensity and uses
different rounding/hash rules. Neither inspected OSC dispatcher implements Kitty
OSC 21. Native/managed comparisons cover protocol replies and every palette
override bit after split input, batch-limit rejection, invalid keys, resets,
absence, and legacy OSC updates. Parser differentials include every upstream X11
name and all 65,536 sixteen-bit channel values, plus 4,096 twelve-bit hash channel
values. After the capture correction, all **138 focused Release tests** pass with
native available, including **120 new cases**. The generated data check verifies
all **782** unique upstream names and now runs in Ubuntu CI.

The allocation/timing test compares the old theme parser used by managed OSC with
the replacement on identical supported strings (`#abcdef`, `rgb:12/34/56`, and a
tab-padded hash), 10,000 warmup iterations and seven samples of 100,000 parses.
An initial implementation measured 11.294 ms versus 9.194 ms; bypassing the name
table for protocol numeric prefixes corrected that regression. The subsequent
Release/macOS arm64 medians were **8.764 ms versus 9.183 ms**, and **0 versus
1,333,320 allocated bytes**. This isolated result is not an end-to-end rendering
or parser throughput claim. Names and intensity parsing also have a tested
zero-allocation warm path. The full post-push Release unit/headless suite through
`f93439a` passes **2,822 tests, 16 conditional skips, zero failures (2,838 total)**
(`kitty-colors-full.trx`), with native color differentials running on macOS arm64.
This does not establish cross-platform runtime completion.

The same audit identified pre-existing legacy color-operation differences and a
native host report-policy gap; the subsequent implementation is described above.

### Snapshot color-state installation (2026-09-23)

The unpublished managed processor can now install TERMINAL colors without
collapsing defaults and overrides into the rendered theme. Original palette
entries, sparse override identity (including equal-valued overrides), nullable
dynamic defaults and dynamic overrides remain independently available for
subsequent export. OSC resets expose the original defaults; later host theme
changes replace defaults while retaining application overrides. Missing native
defaults use host colors only for rendering, not for OSC query responses. Cursor
queries fall back to foreground when no cursor color is present.

Reference decision: follow Ghostty `snapshot/terminal.zig`,
`stream_terminal.zig:colorOperation` and `Terminal.zig:colorForXterm`. Windows
Terminal `AdaptDispatch::ResetColorTableEntry` and xterm.js
`InputHandler.restoreIndexedColor` also restore configured colors, but do not
define Ghostty's nullable snapshot fields. The new tests compare all 256 original
and current entries and override bits plus dynamic metadata with native snapshots
after restore, bytewise continued OSC input, resets and host configuration changes.
Additional tests cover absence versus black, query suppression/cursor fallback,
and recoloring logical cells without changing truecolor cells or the current pen.

Post-push focused Release validation passes all 27 cases, including the seven new
snapshot-color cases and both existing mode-allocation checks; native is available.
The first full run used Debug: 2,700 passed, 16 skipped and two allocation checks
failed (504,000 and 72,000 bytes). Both checks pass in Release; the Debug failures
are recorded rather than presented as a clean run. The complete post-push Release
suite through `51a7645` passes **2,702 tests, 16 conditional skips, zero failures
(2,718 total)** (`snapshot-colors-release-full.trx`). Native differential tests
executed on macOS arm64. Documentation CI passed; all six native build jobs were
still in progress at inspection, so cross-platform runtime sign-off remains open.
This is a prerequisite, not the complete managed snapshot restore/export
orchestrator; that remains open.

The continued color audit identified missing OSC 21 and incomplete shared color
parsing; the subsequent implementation and its validation are described above.

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
| Managed snapshot and continuation features | Restore/export both screens, history, continuation, modes, styles, links and saved cursors with bounded validation | Public transactional restore, READY/incremental history, state installation and streaming binary export pass native comparisons. Native allocation/minimum quota calculations pass focused boundary tests; integration with live managed page retention, broader differential and performance validation remain open |
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
commit `622b4eecd7d2ce1a10930537c17f0d61abdba817` (verified 2026-09-23).
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

### Saved modes and column-mode runtime state

The managed engine now implements XTSAVE/XTRESTORE (`CSI ? Pm s/r`) for all 39
Ghostty DEC modes. Its saved bank follows snapshot-v1's stable 43-bit registry;
ANSI modes occupy the first four bits but are not selected by these private CSI
commands. Unsaved modes use the native initial values, saves overwrite the slot,
restores do not pop it, and RIS resets the saved bank. Restore invokes the normal
mode handler even when unchanged so origin homing, cursor save/restore, margin
reset, alternate-screen clearing and size reports retain their side effects.
Repeated synchronized-output restore does not publish a partial frame. Current
and saved bank capture are internal prerequisites, not yet full snapshot installation.
The subsequent configurable-default implementation is described below.

DEC 47, 1047 and 1049 are independent protocol values, not aliases of the active
buffer. Native differential tests exposed a second issue in the native adapter:
it ORed stale mode values into `AlternateScreen`. Publication and restart routing
now use Ghostty's actual active-screen key. Mixed saves, restores and screen
switches are checked at every byte split, including their DECRQM responses.
Malformed DECRQM now requires exactly one parameter, matching Ghostty instead of
emitting invented multi-mode/omitted-mode replies.

DECCOLM now obeys mode 40, clearing its mode value when disabled. When allowed it
resizes to 80/132 columns, clears the display with current background, resets
margins through resize and homes the cursor, including on XTRESTORE. The native
mirror follows terminal-owned grid changes before extracting cells, so columns
past the old host width remain visible. Native resize can clear synchronized
output without its mode callback; the adapter now observes that cleared state.
Size callbacks read current native dimensions rather than the not-yet-refreshed
mirror during a write. Managed size reports retain host cell metrics across
column-mode changes, use zero for unknown geometry and round measured pixel
dimensions like native. VT-driven column changes do not emit host-resize reports.

References: Ghostty `modes.zig`, `stream.zig`, `stream_terminal.zig`,
`Terminal.deccolm` and `Terminal.switchScreenMode` are authoritative for saved
state and these side effects. Windows Terminal `_SetColumnMode` also gates on
DECCOLM permission, resizes, clears and homes; xterm.js gates its resize/reset
through `windowOptions.setWinLines`. Neither checked private-CSI dispatcher
provides Ghostty's saved bank. We follow Ghostty's mode-40 protocol gate while
retaining the existing managed DECSTR extension (native does not implement that
command); managed DECSTR resets its new bank alongside its other mode state.

Tests compare both native snapshot mode banks after each operation for every
mode, verify repeated/unsaved restore and RIS, mix screen aliases and parameter
orders, exercise cursor/margin/screen side effects and check held publication.
DECCOLM tests cover styled erase, origin/margins, both screens, restored column
mode, right-edge native/managed drawing, same-write queries, and rounded/unknown
geometry. Warm save/reset/restore cycles plus bank reads allocate zero managed
bytes. The focused mode/query/synchronized-output suite passed **163 tests** before
three additional geometry cases were added for the final full-suite run.
The complete Release suite through `5c4a881` then passed **2,521 tests,
16 conditional skips, zero failures (2,537 total)** in `saved-modes-full2.trx`,
including all 62 new saved-mode/column/query cases with native available. This is
local macOS arm64 evidence, not completed platform validation or full parity.

### Configurable mode reset policy

Both processors now expose `ITerminalModeDefaults.TrySetDefaultMode(mode, enabled,
ansi)`, matching Ghostty's C `mode_default` option for all four ANSI and 21 DEC
configurable modes. The shared registry validates mode identity and eligibility
before mutation. Transition-dependent modes, unknown integers and out-of-range
values cannot be smuggled through native tag truncation. Setting a default replaces
the current value even if the default was unchanged, leaves saved values intact,
and does not execute DECSET side effects: pending wrap survives changing mode 7,
and setting mode 2048 does not emit an unsolicited reply. Mode-change events and
held cursor visibility continue to follow the normal publication contract.

Managed state keeps an independent default bank. RIS and new-session preparation
restore it while clearing saved modes to the built-in initial bank, matching
Ghostty `ModeState.reset`. The history-preserving native restart cannot use RIS;
it resets saved DEC values using raw C bit updates plus XTSAVE, restores current
bits and then applies policy without invoking screen, resize or report effects.
It disables DEC 40 before cleanup so resetting mode 3 cannot unexpectedly resize
the retained grid. Lazy-created/recreated Sixel overlays inherit configured policy.

Reference decision: Ghostty `modes.zig` (`defaultConfigurable`/`setDefault`/`reset`)
and `c/terminal.zig` (`mode_default`) define the eligibility and mutation contract.
Windows Terminal's ordinary SetMode/ResetMode dispatcher and xterm.js CoreService's
fixed default clones are not equivalent configurable-policy APIs. RoyalTerminal
follows Ghostty and retains its existing managed-only DECSTR extension, which also
reapplies configured defaults. Native/managed snapshots still need full transactional
installation, including transition-dependent default bits that the policy API
intentionally refuses.

Coverage includes every eligible mode across native current/saved/default snapshot
banks, current-value overrides, repeated policy writes and RIS; every refused
transition mode and integer range guards; all policies on both integrations across
history-preserving restarts; held publication/events, pending wrap, absence of VT
side effects and Sixel overlay recreation. Warm managed policy writes/bank reads
allocate zero bytes. Reset applies only changed default bits; the normal unconfigured
path skips this work. Full-suite and paired reset measurements are recorded below.

Post-push Release validation through `520d267`: **2,607 passed, 16 conditional
skips, zero failures (2,623 total)** in `mode-defaults-full2.trx`, including all
86 new policy tests with native available on macOS arm64. Build reports no
warnings or errors. This does not establish other-platform runtime validation.
CI run `35867256990` passed all six native RID builds at this implementation head;
the Ubuntu, macOS and Windows build/test jobs were still pending at inspection.

A sequential Release reset microbenchmark compared `d8e7cd4` with `520d267`:
80x24 terminal, 10,000 warm-up resets, median of seven 10,000-reset samples.
For zero, one and ten overridden defaults, elapsed time was respectively
41.807→57.259 ms, 30.511→37.938 ms and 30.050→39.098 ms. Both implementations
allocated zero bytes in every timed sample. Although the new path visits fewer
mode bits, this pair was slower and does **not** establish a performance gain;
controlled repeated profiling remains required before making that claim.

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

## Snapshot storage staging and publication ownership

`GhosttySnapshotLiveScreen.Stage` now creates unpublished storage for both READY
buffers, with the final screen-height rows as the active area. It preserves the
complete resident suffix, including incidental history and mixed physical widths,
without replaying input or invoking ordinary buffer-switch resize/trim behavior.
Current palette overrides and dynamic color precedence resolve live cells; absent
dynamic colors use host fallback, while selection and cursor-text preferences stay
host-owned. SCREEN/TERMINAL processor state and continuation still need installation
before this staging object can be published as a usable managed terminal.

Reference decision: Ghostty `snapshot/screen.zig` constructs its PageList privately
and clamps cursor pins against the physical row before returning it. RoyalTerminal
follows that ownership/staging contract. Windows Terminal `TextBuffer::SerializeTo`
and xterm.js `SerializeAddon.serialize` emit VT text rather than this binary state;
they cannot replace Ghostty's no-replay restoration. Neither reference justifies
silently flattening mixed-width rows.

Screen publication previously transferred rows and then rebuilt destination
hyperlink/Kitty registries, allowing allocation failure to expose a partial state.
`AdoptStateFrom` now transfers registry ownership too, under its existing caller-lock
and discard-source contract. COW copies still independently copy registries; a
published destination keeps its original synchronization object. The new allocation
test publishes 1,001 raw identities plus a legacy URL into empty registries with
zero allocated bytes and verifies subsequent copy isolation.

The first differential run found unequal raw native PAGE allocation headers after
recapture; canonicalizing capacities and local style/link IDs makes complete native
snapshots equal, including after continuing unfinished CSI and switching buffers.
The broader snapshot/publication selection passes **228 tests**, zero skips/failures
(`snapshot-stage-focused2.trx`, macOS arm64 with native available).

A sequential Release publication microbenchmark (80x24, 1,000 raw hyperlinks,
100 warm publications, seven samples of 100 independently staged transfers) measured
median **17.260→0.053 ms** and **30,640,800→0 allocated bytes** from the preceding
`520d267` binary to `749b6a5`. Construction/copying is outside the timed region:
this measures publication only, not complete snapshot restore or frame throughput.

Full post-push Release suite through `8da188f`: **2,614 passed, 16 conditional
skips, zero failures (2,630 total)** in `snapshot-stage-full.trx`, native available
on macOS arm64. Build has no warnings/errors. All seven new staging/ownership
cases pass. Fresh six-RID CI run `35869030082` was still pending at inspection;
this is not cross-platform runtime sign-off or complete managed snapshot restore.

### Atomic incremental-history storage

`TerminalScreen.PrependSnapshotHistory` now commits one validated PAGE to the
requested existing buffer. The ring-buffer prepend preserves existing row identity
and mixed widths, and grows its reference storage before mutation. Capacity growth
uses overflow-safe arithmetic. A spare-capacity ring prepend/remove loop allocates
zero bytes. Plain pages avoid copying the global hyperlink registry; linked pages
decode into privately copied registries so failure cannot expose half-installed
identities. Existing row/cell storage is never cloned for this operation.

Tracked cell anchors and raster placements shift only within the target buffer;
bottom-relative viewport offsets stay unchanged. New raster placements and checked
anchor arithmetic are prepared before row insertion. Tests cover active/dormant
buffers, scrolled-back views, synchronized-copy isolation, circular wrap/growth,
raw hyperlinks, and a deliberately overflowing raster anchor after decode that
must leave published rows, registries and link numbering unchanged. Native golden
history pages produce identical progress counts and final staged cell state.

Reference: Ghostty `snapshot/history.zig.decodePage` and `PageList.PageAllocation`
decode into detached pages before finalization, retaining existing page/pin identity.
The existing Windows Terminal/xterm.js VT serialization comparison still applies:
neither substitutes for binary history insertion. The storage primitive deliberately
requires its future adapter caller to check applicability and budgets first.

The renewed source audit found two important integration constraints:

- `snapshot.zig.nextPage` tests current columns and ScreenSet generation, not an
  ever-resized flag. A height-only resize, or width changed and restored before the
  next page, need not discard primary history. RIS resets primary in place but
  removes alternate storage. Once a page is dropped, restoring compatibility does
  not re-enable that sequence.
- Native byte/line limits have minimum page-granular allowances (`Limits.minMaxSize`
  and `minMaxLines`), even when configured to zero. Complete managed reconciliation
  must address those limits and processor semantic-prompt updates; this internal
  storage commit is not yet that reconciler or a public restore API.

Post-push Release validation: **2,628 passed, 16 conditional skips, zero failures
(2,644 total)** through `82b728c` (`snapshot-history-storage-full.trx`). The five
subsequent native applicability cases also pass, with production code unchanged;
all **19 history-storage/reference tests** pass at `7f74fee`
(`snapshot-history-storage-reference.trx`). Native was available on macOS arm64;
build has no warnings/errors. One 80-cell history row prepended to 10,024 resident
rows allocated **4,400 bytes**, including its new cell storage, rather than copying
resident row metadata. Linked-page registry staging and occasional ring growth
still allocate; this is not an allocation-free restore or end-to-end speed claim.

### Incremental-history lifecycle application

`GhosttySnapshotHistoryApplication` captures terminal lineage, declared keys and
alternate generation at READY. Per-page eligibility checks current columns, buffer
presence and generation. A rejected page permanently disables only its own sequence;
other screens continue. History application failures poison the session. Structural
validation and source ownership remain in `GhosttySnapshotStateReader`; quota
approval is an explicit required argument, not an implicit unlimited policy.

The managed screen retains lineage through synchronized-output copies and ownership
publication. A separately installed terminal has a different lineage. Alternate
removal advances its generation, while no-op removal and ordinary screen switching
do not. As in Ghostty, RIS leaves primary's logical screen identity intact. Identity
allocation is lazy, so ordinary processors without snapshot tracking pay no extra
allocation for the lineage. Repeated dropped-page decisions allocate zero bytes.

Native comparisons verify height-only resize, incompatible width, width restored
before/after the first drop, primary RIS, crafted alternate history, and alternate
removal/recreation followed by unaffected primary history. Other tests cover COW
publication, unrelated replacement state, explicit quota rejection, undeclared
routes/failure state and prompt detection on rows or cells. Prompt progress is emitted
only for installed pages; the processor adapter still must consume that signal.

Reference decision: Ghostty `snapshot.zig.nextPage`, `ScreenSet.remove` and
`Terminal.fullReset` define this contract. xterm.js `BufferSet.reset` replaces both
buffers, whereas Ghostty retains primary identity; RoyalTerminal follows Ghostty for
binary snapshot history, not xterm's different reset ownership. Windows Terminal's
VT text serialization does not provide this incremental binary contract.

The focused snapshot-history/storage/publication suite passes **69 tests**, zero
skips/failures (`snapshot-history-application-focused.trx`) at `92e830f`, native
available on macOS arm64. This closes lifecycle eligibility, not the outstanding
byte/line quota accounting, processor-state installation or public restore API.

Full post-push Release validation through `92e830f`: **2,651 passed, 16 conditional
skips, zero failures (2,667 total)** (`snapshot-history-application-full.trx`). All
18 new lifecycle/prompt tests pass, with native available on macOS arm64. Build has
no warnings/errors. Documentation CI passed; the six native builds in run
`35871055372` were pending at inspection. Other-platform runtime sign-off remains.

### Managed Kitty keyboard runtime and snapshot state

The processor restore audit found that the previous managed 32-entry list did not
match Ghostty's `kitty/key.zig.FlagStack`: the native state is an eight-slot ring
including its current entry. `ManagedKittyKeyboardState` now packs all eight
five-bit values and the three-bit index into eight bytes per screen. Push advances
and overwrites, small pops clear each removed slot and wrap, zero pops do nothing,
and a pop of eight or more clears the complete ring in constant time. No stack
arrays or list shifts are needed. Reset still follows the existing managed DECSTR
extension in addition to RIS/session reset.

`stream.zig` is the command reference: push accepts one 0–31 flag parameter, otherwise
defaults to zero for non-single arity; invalid single flags are ignored. Pop uses
the single parameter literally, including zero, or defaults to one. Set ignores
invalid flags/operations and uses its first two parameters. Native comparisons at
every split cover omitted/extra/colon parameters, zero, 31/32/65535 boundaries and
all three set operations. Per-screen ring state survives ordinary buffer switches
and synchronized output. Internal SCREEN installation restores every slot and index
without input replay, preserving subsequent operations on restored state.

Reference decision: Windows Terminal uses an eight-entry vector of saved flags plus
its current register and treats zero pop as a no-op; xterm.js uses 16 saved entries
and treats zero as one. Neither is the same snapshot-v1 ring. RoyalTerminal follows
Ghostty for exact managed/native runtime and SCREEN compatibility.

All **44 new cases** pass, including full native slot comparisons after overflow,
underflow, RIS, independent buffers and installation of arbitrary snapshot rings.
The broader query/session selection passes **146 tests**, zero skips/failures
(`keyboard-ring-focused.trx`, native available on macOS arm64). Warm packed
push/pop/capture loops allocate zero bytes. Full processor restore remains open.

A sequential Release comparison against the preceding `92e830f` binary used
10,000 warm-up push/pop pairs and the median of seven 100,000-pair samples.
The parser workload measured **48.454→47.268 ms**, with zero timed allocations
in both implementations and final flags zero. The small timing difference is
not an end-to-end speed claim; the structural gain is eliminating heap-backed
keyboard stacks and bounding large pops independently of requested count.

Full post-push Release validation through `102719a`: **2,695 passed, 16 conditional
skips, zero failures (2,711 total)** (`keyboard-ring-full.trx`). Native was available
on macOS arm64 and the build reports no warnings/errors. Documentation CI passed;
native builds in run `35872036749` were pending at inspection. Full processor
installation and other-platform runtime sign-off remain open.

### Independent mouse state and snapshot mode banks

Ghostty `stream_terminal.zig` retains individual mouse protocol bits but each
tracking/encoding command selects the effective state independently: disabling
any tracking mode selects none, and disabling any encoding selects X10. The
managed processor and UI fallback tracker now follow these transitions instead
of choosing the highest-priority enabled bit. The tracker also saves/restores
individual mouse bits through XTSAVE/XTRESTORE. Windows Terminal delegates these
commands to TerminalInput; xterm.js likewise uses last-command protocol selection,
but lacks Ghostty's UTF-8/URXVT formats. Ghostty is the compatibility reference.

Internal snapshot mode installation assigns all 43 current/saved/default bits and
the independent mouse event/format flags without executing escape sequences or
triggering resize, buffer switching, erase, replies or synchronized-output holds.
This follows `snapshot/terminal.zig` decode; it is a prerequisite, not the complete
processor restoration API. Tests cover every bit, arbitrary defaults through RIS,
all 64 ordered mouse-mode pairs, save/restore, bytewise input, pointer encoding and
warm allocation behavior. The first differential run exposed Ghostty's additional
RIS step: selecting the default cursor overwrites mode 12 after mode-bank reset.
Installation now retains the normalized default blink policy (null means true)
and RIS reapplies it; all three nullable-policy representations are covered.
Complete cursor shape/per-screen state installation remains a separate open item.

Post-push Release validation through `e02a8c6`: **3,140 passed, 16 conditional
skips, zero failures (3,156 total)** (`mouse-mode-full.trx`). All **165 new cases**
pass with native available on macOS arm64; build output has no warnings/errors.
The initial 43 failures were the single cursor-policy discrepancy described above,
not accepted as a validation pass. Cross-platform runtime sign-off remains open.

Sequential before/after Release microbenchmarks used 10,000 warmup pairs and
seven samples of 100,000 `1000;1006` enable/disable pairs plus reporting-state
reads. Two paired runs measured median **36.453→19.328 ms** and
**42.565→31.916 ms**, zero timed allocations in both versions. Host timing varied;
these are limited workload observations, not an end-to-end speed guarantee.
The new effective-state field avoids repeated hash-set lookups when reading mouse
state. Loaded binaries were hash-verified against the saved preceding/current
assemblies (`9fa5d453…` / `a981cc92…`).

## Shift-mouse capture reference details (September 23 follow-up)

Ghostty `Surface.mouseShiftCapture` and `stream.zig` XTSHIFTESCAPE are the
compatibility reference: CSI > s / > 0 s disables capture, > 1 s enables it,
and other parameter forms are ignored. The nullable application override is
global, survives DECSTR and buffer switches, and clears on RIS/session reset.
Both engines expose this state, including snapshot installation, and the native
state copy retrieves modes and capture together without an extra per-frame call.

The control's VT transport path defaults to Shift selection, with application
override permitted. Enabled reverses that default; Never/Always ignore the
application override. Native input endpoints retain their own host policy.
Windows Terminal `ControlInteractivity::_canSendVTMouseInput` also reserves Shift;
xterm.js `SelectionService.shouldForceSelection` has platform-specific force
selection behavior. Ghostty's four-way configurable policy is chosen here.
Headless tests cover all twelve host/nullable-application combinations; parser
tests cover every split and malformed forms, reset, hold, snapshot and ABI guards.
The initial 67 focused cases pass against the rebuilt native library. The host
now observes physical button transitions before reporting/Shift suppression,
following Ghostty's `mouseButtonCallback`; it does not encode suppressed input or
advance motion history. Regression coverage checks suppressed releases and presses
while reporting is off. Final full-suite results are recorded at the top of this audit.
This does not claim full mouse parity: broader capture-loss lifecycle, hyperlink
policy and outside-viewport host normalization still require separate review.

## Validation requirements

- Build the release native library with Zig 0.16 using `scripts/build-native.sh --release`.
- Build the complete .NET solution with no warnings.
- Run all xUnit unit and native integration tests.
- On platforms for which native assets are published, rebuild those assets from the
  new submodule pin before release. The repository's macOS arm64 native binary was
  used for the API integration tests in this change; generated binaries remain
  excluded from source control.
