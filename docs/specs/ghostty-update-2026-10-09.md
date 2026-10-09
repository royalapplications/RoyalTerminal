# Ghostty update and OSC 7501 — 2026-10-09

## Target and acceptance

Work on `codex/ghostty-latest-osc-7501`, committing tested increments. The clean
starting point is `ad857e6f`. Advance the native dependency from
`b40acce58dcf77df52231c3798ea58e924647c89` to upstream main observed on October 9,
`246f702876b924a1cb7cade1e99274d1470302fc`. Audit every intervening first-parent
change, port relevant behavior to the managed engine and shared host, update the
public ABI, and validate native/managed behavior with focused xUnit tests.

Ghostling HEAD is still `63842bf8e5e481160f81d348da9ff6fd27986798` (August 9),
matching the existing integration reference. GitHub's commits/main response was
checked on October 9; there are no subsequent changes to port.

## Reference decisions before implementation

- Ghostty's new public `GHOSTTY_TERMINAL_OPT_PROGRAM_STATUS` callback validates
  OSC 7501 reports and answers detection queries. Its public semantic-prompt and
  reset callbacks provide the lifecycle hooks. Use these APIs directly; no
  source overlay or separate native byte-stream parser for this protocol.
- Implement shared per-terminal bounded records, ancestry/app inheritance,
  full replacement, subtree clearing, least-recently-updated eviction, prompt
  and process-exit cleanup, and RIS reset. DECSTR and alternate-screen switching
  retain records. The managed parser follows the upstream parser and the
  [protocol specification](https://www.superlogical.com/rex/docs/build/program-status),
  revision 0.3. Reports never trigger commands or echo their text back.
- Windows Terminal `src/terminal/adapter/adaptDispatch.cpp` and xterm.js
  `src/common/InputHandler.ts` were inspected from upstream. Neither implements
  OSC 7501 in those dispatchers. Their soft-reset implementations preserve screen
  content, reset rendition/margins/charset and active saved cursor state. Prefer
  Ghostty's precise DECSTR contract for cross-engine parity and test differences.
- Windows Terminal's rectangular checksum is feature-gated; the Ghostty checksum
  extension is opt-in too. Preserve default privacy behavior and match Ghostty's
  algorithm/flags when enabled, with native differential cases.
- Native parser/library fixes arrive through the pin. Review existing guarded
  extensions against changed source before refreshing hashes. Keep exact fragment
  checks and existing regressions. Do not edit the upstream submodule checkout.
- Standalone platform UI/build changes (Swift/GTK, translations, packaging and
  Ghostty CLI) require a recorded applicability decision; they are not evidence
  of a new VT feature. Shared-host behavior changes must be evaluated separately.
- PowerShell `4c462e3d39478277bb8e89a7e01783efa375f25d`,
  `ConsoleHostUserInterfaceProgress.cs`, writes OSC 9;4 progress only with VT,
  nonredirected stdout and `UseOSCIndicator`; completion writes OSC 9;4;0.
  Preserve that independent progress channel rather than replacing OSC 7501
  records. No heuristic matching of PowerShell prompts or screen text is added.

## Delivery checklist

- [x] Refresh native pin, review overlays, build both native libraries (macOS arm64).
- [x] Bind new public C types/options/callbacks/functions; regenerate ABI audit.
- [x] OSC 7501 parser, bounded record store, native callbacks, lifecycle and host UI.
- [ ] Unknown OSC capture, terminators, cancellation and numeric validation.
- [ ] DECSTR and RIS palette behavior; charset single shift and Unicode handling.
- [ ] DECRQCRA/XTCHECKSUM and configurable device attributes.
- [ ] Mouse shape, UTF-8 mouse buttons, modified backspace and prompt-click fixes.
- [ ] Resize/reflow, saved cursor, line-selection and Kitty placeholder regressions.
- [ ] Snapshot decode compression and memory queries, with managed equivalents.
- [ ] Evaluate renderer/shader, font, image/zlib, tmux and other upstream deltas.
- [ ] Run ABI, unit, native integration, history and headless/build checks.
- [ ] Document every delivered behavior and any concrete remaining limitation.

## Validation log

Initial environment: .NET SDK 10.0.201; Zig 0.16.0. Native runtime host: macOS.
Both native libraries rebuilt successfully. The six changed overlay inputs retain
all exact fragment matches; none of the upstream changes replaces the separate
dirty-row, hyperlink ownership, clone rollback or notification/resize hooks.
Reviewed changes add checksum/reset/parser effects, correct charset printing,
reflow pins/wide-cell cuts and add memory/compression APIs. The three other
overlay input files are byte-for-byte unchanged. All nine full-file hash guards
and fragment-count checks remain enabled.

The native ABI now contains 172 types and 30 callbacks. Source audit and generated
ABI checks pass. Release integration build: zero warnings/errors. Focused native
API and ABI suite: 152 passed, zero skipped. Cases cover OSC 7501 opt-in queries,
BEL/ST replies, validated report fields, prompt/reset callbacks, unknown OSC
capture/cancellation, null command access, memory queries, incremental compressed
snapshot restoration, pointer reset and opt-in rectangular checksums.

Full required-native integration suite: 274 passed, zero skipped. Initial OSC
7501 cross-engine suite: 12 passed, including every input split, hierarchy,
replacement, eviction, prompt/exit/reset lifetimes and malformed-report limits.
The adjacent regression suite found two cursor reflow differences from the
new upstream pending-wrap correction; these belong to the resize parity work.

OSC 7501 plus presentation/headless suite: 18 passed, zero skipped. Both engines
keep 256 records, inherit application names dynamically, preserve done/error/idle
on prompt and process exit, retain all records on DECSTR/screen switches, and
clear on RIS/new sessions. Native semantic prompt-start events (A/N/P) and their
managed equivalents drive transient cleanup. OSC 9;4 remains independent.
TerminalControl coalesces immutable snapshots onto the UI thread, handles real
transport exit/stop, and discards old processor state on replacement. The app's
compiled-XAML status bar displays the most urgent record and all records in a
tooltip, labels the originating terminal, and escapes invisible direction text.
