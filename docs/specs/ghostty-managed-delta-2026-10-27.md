# Managed Ghostty delta, 2026-10-27

Range: `246f702876b924a1cb7cade1e99274d1470302fc` to
`4a4fc017940bec38c93b278c10c6a18c591f3e63` (Ghostty 1.4.1-dev).
This is the incremental terminal-engine audit; the full 1.4 release coverage and
Ghostling host audit are separate documents. Zig 0.17 mechanical changes were
separated from behavior changes using each merge's first-parent diff.

## Reference decisions

- Ghostty `Terminal.zig`, `stream.zig`, `stream_terminal.zig`,
  `stream_continuation.zig`, `osc/parsers/program_status.zig`, and their new
  regression tests define native/managed equivalence.
- [Windows Terminal AdaptDispatch](https://github.com/microsoft/terminal/blob/main/src/terminal/adapter/adaptDispatch.cpp)
  uses origin-relative absolute coordinates and distinguishes unrestricted
  HPR/VPR from margin-clamped CUF/CUD. Follow that behavior and updated Ghostty.
- [xterm.js InputHandler](https://github.com/xtermjs/xterm.js/blob/master/src/common/InputHandler.ts)
  aliases HPA/CHA and supports private CPR, but omits DECXCPR's page number.
  RoyalTerminal follows Ghostty's `CSI ? row ; column ; 1 R` with origin-relative
  coordinates. Window reports use the embedded terminal's character dimensions.
- [xterm.js EscapeSequenceParser](https://github.com/xtermjs/xterm.js/blob/master/src/common/parser/EscapeSequenceParser.ts)
  separates string cancellation from successful completion. Ghostty #14812 now
  applies that rule to recognized APC commands as well. Cancelled Kitty/glyph
  commands must have no side effects.
- OSC 7501 has no corresponding dispatcher in Windows Terminal or xterm.js.
  Revision 0.4 capability discovery and Ghostty's strict ID validation are the
  authority. The existing ancestor inheritance, bounded record store, and
  lifecycle cleanup remain compatible. OSC 9;4 / PowerShell progress handling
  is unchanged by this delta.
- [xterm.js SerializeAddon](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-serialize/src/SerializeAddon.ts)
  also uses ECH for erased backgrounds and CUF for blank gaps. Styled VT now
  follows that semantic approach and Ghostty's trimming rules. `IncludeHyperlinks`
  restores the active pen; cell hyperlinks are always included in styled output.

## Core implementation and tests

| Upstream | Managed disposition | Focused evidence |
| --- | --- | --- |
| #14761 / `63fd4eab8` | CUB/BS stop at the left margin unless already left of it | October27TerminalRegressionTests |
| #14762 / `746c895c6` | HPR/VPR reach screen edges outside origin mode; add HPA alias; CHA/VPA retain the other coordinate | October27TerminalRegressionTests |
| #14672 / `19a14dda3` | Unsupported ANSI modes 1,5,7,10,11,13–19 report permanently reset; other queries unchanged | October27TerminalRegressionTests |
| #14673 / `0550d0489` | DECXCPR includes origin-relative coordinates and page 1; malformed extra parameters ignored | October27TerminalRegressionTests |
| #14633 / `640896052` | CSI 19 t replies CSI 9;rows;columns t; malformed extra parameters ignored | October27TerminalRegressionTests |
| #14610 / `d66a353aa` | Managed policy-gated CSI 8 t host callback already present; native adapter adopts official upstream callback | existing window resize tests |
| #14643 / `aeda37b30` | DECSTR resets active screen's ISO protection family, allowing subsequent ordinary erases | October27TerminalRegressionTests |
| #14644 / `34346087b` | Parameterless TBC already clears the cursor tab stop | October27TerminalRegressionTests |
| #14652 / `698f8f4ed` | Printing with DECAWM off already consumes pending wrap | October27TerminalRegressionTests |
| #14668 / `52f7969ab` | ED2 prompt search skips empty rows below prompt; stops at actual command output | October27TerminalRegressionTests |
| #14630 / `9cd19b40d` | OSC 7501 query lists supported states/kinds with original terminator | TerminalProgramStatusTests, every input split |
| #14793 / `8c425a7bd` | Validate every raw ID and bounded app/title/message value before alphabet filtering, preventing malformed targeted clear from clearing root | TerminalProgramStatusTests |
| #14814 / `08a951834` | Restart continuation at effective C1 introducers in CSI/ESC/DCS headers/APC, export canonical ESC form, reject noncanonical snapshot tails | ManagedContinuationTests, GhosttySnapshotContinuationTests |
| #14812 / `802665102` | Discard recognized Kitty/glyph commands on CAN, SUB, or aborting C1 transitions; reset captured storage | ManagedApcParserParityTests, ManagedKittyGraphicsStreamingTests, ManagedGlyphProtocolTests |
| #14552 / `2551d9323` | Capture grapheme suffix before wrap/row recycling, write spacer with current pen, restore suffix in one allocation with page-growth retry | ManagedGraphemeWrapTransferTests, including one-row screens, margins, COW, allocation pressure |
| #14725 / `891a4dba5` | VT skips blank gaps with CUF, restores runs of erased backgrounds with ECH/CUF, resets style and links before materializing wrapped blank spaces | October27FormatterRegressionTests and existing formatter suites |
| #14711 / `a2e440396` | Every cell's OSC 8 hyperlink, including explicit ID, is emitted independently of active-pen extras | October27FormatterRegressionTests |
| #14766 / `c7cbc9bfd` | Untrimmed VT retains trailing blank rows to preserve replay's viewport and scrollback | October27FormatterRegressionTests |
| #14670 / `58d23cc02` | Pending wrap over an erased edge cell survives replay without converting that cell into a space | October27FormatterRegressionTests |

The grapheme change removes the obsolete move/per-scalar-copy helper. A 63-scalar
suffix now fits the tested cross-page 1024-byte capacity without the former
2048-byte growth caused by simultaneously retaining intermediate append slices.
The tests assert allocation accounting and retained-copy independence.

## Remaining delta ownership

### Pending Kitty pixels and stable transmissions

`eea7beaf7` adds pending animation canvases and an image transmission stamp that
survives displayed-frame changes. Managed images now reserve their full decoded
storage before pixel records arrive, retain RGB/RGBA/gray-alpha/gray wire formats,
and accept late bytes only through a ticket for the exact resident transmission.
Root completion preserves eviction age; completing a displayed animation frame
updates its content generation. Replacements and eviction invalidate old tickets.

Incomplete displayed frames are omitted from publication, and playback waits
until every frame is complete. Deleting a pending frame releases its reservation;
deleting the root can promote another pending frame. Composition and frame edits
reject missing pixels without changing gaps or reserved bytes. Snapshot copies
share immutable pixel owners while keeping completion and playback state separate.
The playback check uses a maintained pending count, adding no per-tick scan or
allocation. The wire codec and processor snapshot import/export are the companion
snapshot tranche.

The closest references reviewed are
[xterm.js ImageStorage](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-image/src/ImageStorage.ts)
for image ownership/eviction and
[Windows Terminal ImageSlice](https://github.com/microsoft/terminal/blob/main/src/buffer/out/ImageSlice.hpp)
for pixel/revision ownership. Neither provides Ghostty's pending animation snapshot
contract; RoyalTerminal follows Ghostty here. Ordinary chunked Kitty `a=f` loads
still use the native executor's generation check, distinct from snapshot tickets.

### Integration work

These entries are explicitly tracked by the parent integration work, not declared
complete merely because the native submodule contains them:

- Wide selection across page boundaries (#14632) is already handled by the
  managed formatter's global row selection; renderer highlighting (#14651) is
  part of rendering parity.
- Kitty snapshots, render placements/placeholder resolution, animation tick,
  interrupted nonchunk loads, alias memory, shared-memory names and macOS temp
  paths (#14801, #14789, #14780, #14776, #14811, #14678, #14750, #14569):
  parent/graphics tranche.
- Erased history anchors and placements (#14838, #14832), pruned cursor
  style/hyperlink lifetime (#14857, #14686): screen/graphics lifecycle tranche.
  Managed top-row anchor removal already marks discarded positions invalid.
- Snapshot capacity ceiling (#14595): snapshot validation tranche. Row-cap
  saturation (#14856) already exists in `GhosttySnapshotAllocation.TryAdjustColumns`.
- Hierarchical context rename/escaping (#14700, #14667), public write-until and
  prompt navigation APIs (#14802, #14618): binding/host tranche.
- Paste carriage-return protection (#14687), mouse button count (#14759),
  prompt press/release and resize/input ordering: host/input tranche.
- tmux control protocol layout/parser/active-screen fixes (#14858, #14680,
  #14676, #14671) apply to Ghostty's tmux control client. RoyalTerminal's ordinary
  tmux sessions use its PTY VT stream; a Ghostty tmux control client is not present.
- FreeBSD C library CI and Zig 0.17 migration are native build changes.

## Validation

Required-native development gate: **274 passed, 0 failed, 0 skipped** with
`ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1`, using freshly rebuilt Zig 0.17 native
libraries. Coverage includes core, OSC 7501, continuation, grapheme wrapping,
APC cancellation, glyph protocols, and Kitty streaming/failure behavior.
The earlier managed-only gate was 245 passed with 25 explicit native skips;
the fresh native gate supersedes that limited result.

Formatter gate: **223 passed, 0 failed, 0 skipped** with required native enabled.
Tests replay native and managed exports into both engines and check empty versus
text cells, background identity, links/IDs, pending wrap, blank-row geometry,
selection boundaries, styles, and HTML behavior.

Pending Kitty gate: **138 passed, 0 failed, 0 skipped** with required native
enabled, covering pending pixel/ticket lifecycle, animation behavior and failures,
immutable publication, pixel formats, and quota/allocation failure semantics.

Snapshot-page and pending-image follow-up gate: **35 passed, 0 failed, 0 skipped**.
The page differential now includes an untouched native encode/decode baseline.
Ghostty's formatter closes hyperlinks at allocation-page boundaries, so restoring
native pages can move an OSC 8 close across CRLF without changing cells. Differing
exports are replayed into matching fresh native page layouts before comparing
their full styled output; link/style/mode effects remain part of the assertion.

Review follow-up: **63 passed, 0 failed, 0 skipped** across logical-line selection
and the October terminal regressions. The bounded-history cursor tests exercise
one-row and multi-row screens with both zero and retained history, and verify the
cursor's style and OSC 8 hyperlink after page pruning and continued scrolling.
They also check native grid references directly; this exposed the native adapter's
zero-history coordinate projection issue, fixed in its companion change.

The existing long-line allocation test initially measured **72 bytes per query**
in the managed path. Default whitespace no longer materializes a constant span
through `RuntimeHelpers.CreateSpan`; a direct scalar predicate preserves NUL,
space, and tab handling. The unchanged 20-query measurement now reports **0 bytes**.

Metadata failure and grapheme-pressure follow-up: **70 passed, 0 failed, 0 skipped**.
The previous scalar-by-scalar transfer test now verifies successful whole-suffix
installation without scratch fragmentation. When a full destination page refuses
the whole suffix, the source has already been cleared, while the destination tail
and cursor advance remain untouched. Retained COW owners keep their original
graphemes and metadata usage through both success and refusal checkpoints.

Independent snapshot review confirmed page-relative placement ancestry, pending
payload tickets, bounded malformed-record handling, and current-frame/viewport
payload priority against the new upstream encoder. The payload writer emits visible
frames after READY, history image pixels beside their page, and inactive/unplaced
pixels after history; companion snapshot tests verify the exact ordering.
