# Managed plain-text formatter parity (September 24, 2026)

## Reference decision

Follow the pinned Ghostty `622b4eecd` `src/terminal/formatter.zig`
PageFormatter: defer erased cells and blank rows, trim only ASCII spaces,
preserve deferred spaces across soft-wrap continuations, and normalize wide
selection boundaries. Plain text uses LF on every platform. The upstream
formatter feature reference is [Ghostty #13643](https://github.com/ghostty-org/ghostty/pull/13643).

Windows Terminal `src/buffer/out/textBuffer.cpp` `_RowCopyHelper` similarly
adjusts selections to whole glyphs and distinguishes forced wraps; its clipboard
path uses CRLF. xterm.js `SelectionService.selectionText` joins wrapped rows,
keeps rectangular rows separate, converts NBSP to spaces and uses platform line
endings. RoyalTerminal follows Ghostty's raw LF and preserved NBSP instead of
those clipboard conversions. Existing RoyalTerminal rectangle semantics remain:
no unwrapping for a column band. The native adapter now enforces this same host
policy (including styled/HTML exports). No shell/PowerShell behavior is changed.

## Implemented

- One managed plain formatter for selection and full-history text exports.
- Interior erased cells become spaces only before subsequent text; trailing
  erased cells and empty rows are omitted independently of the trim option.
- Explicit ASCII spaces are retained when trim is disabled; other Unicode
  whitespace and grapheme text are not trimmed or normalized.
- Soft-wrap continuation preserves meaningful spaces; hard breaks use LF.
- Inclusive viewport selections are normalized and clamped like the native
  adapter; wide tails include their leading glyph and unwrapped spacer-head
  endpoints include the next-row glyph when available.
- Empty plain exports are successful and return an empty string in both engines.
- Read-only cell spans avoid copy-on-write detachment; scalar encoding uses
  stack scratch instead of one temporary string per character. No throughput
  improvement is claimed without profiling.

## Boundaries and validation

Styled VT/HTML equivalence remains deferred. Managed storage does not reproduce
native page-boundary-specific spacer behavior. This is a focused semantic port,
not exhaustive formatter equivalence or a port of Ghostty's SIMD implementation.

Focused tests include native comparisons for whitespace, empty output, wrapping,
wide/grapheme selections, clamping and 400 seeded history/selection comparisons.
Additional tests cover scrolled viewport selection, working-state reads during
synchronized-output render holds, COW storage identity and native rectangular
row boundaries in all three formats.

At `1d8f6bc`, full local Release validation with CI flags and required native
tests passed **4,004 unit/headless + 240 native integration = 4,244 tests**,
with **16 conditional unit skips and zero failures**. Both projects wrote
`plain-formatter-release.trx`. The final focused formatter/selection/font suites
passed **63 tests, zero skips/failures** (`plain-formatter-focused.trx`). Full
solution Release build: **zero warnings and errors**.
Platform CI is tracked separately in the PR;
these local macOS results are not full Linux/Windows sign-off.

Subsequent CI `36012473893` passed Linux/macOS managed jobs and all six native
builds. Windows failed a PowerShell-table regression expectation still splitting
LF snapshots by `Environment.NewLine`; the [styled-SGR follow-up](managed-styled-sgr-parity-2026.md)
corrects that test without changing PowerShell/PTY/reflow behavior.

## Font-coverage CI follow-up

CI 36007943014 at `f0acddb` failed one Linux configured-file font fallback test;
macOS/Windows tests and all six native builds passed. The resolver now retries
global discovery when family-specific discovery fails, including incorrect
candidates. Rejected candidate font-cache entries are released before a retry
to avoid native-handle reuse. Deterministic fixture tests cover both failure
paths. Reproduced on Ubuntu 24.04 ARM64 with SkiaSharp 3.119.4: matching `A`
against the file family `Noto Emoji` returns null; matching without a family
returns `Noto Sans`, with a real `A` glyph and `head.flags=0x0007`. The updated
resolver successfully returns that fallback. This isolated Linux diagnostic is
not a full Linux suite or x64 CI sign-off. The VM was accessed through the
Parallels CLI skill without configuration changes.

Ghostty font discovery uses character coverage in its system font search;
Windows Terminal delegates fallback to DirectWrite and xterm.js to the browser
font stack. RoyalTerminal retains Skia discovery and verifies candidate glyphs.
