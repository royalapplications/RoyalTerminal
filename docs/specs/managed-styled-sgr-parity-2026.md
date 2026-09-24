# Managed styled-VT SGR parity (September 24, 2026)

## Reference decision

Ghostty `622b4eecd` `src/terminal/style.zig` `Style.VTFormatter` emits complete
styles with reset, all text attributes, `4:2` through `4:5` underlines and logical
default/indexed/RGB colors (including underline color). Its terminal C formatter
does not request palette-to-RGB conversion. The managed formatter follows those
semantics. Feature reference: [Ghostty #13643](https://github.com/ghostty-org/ghostty/pull/13643).

Windows Terminal `src/buffer/out/textBuffer.cpp` `SerializeToVT` and xterm.js
`addons/addon-serialize/src/SerializeAddon.ts` likewise preserve underline style
and indexed/default colors. WT may use `21m` for double underline; RoyalTerminal
uses Ghostty's `4:2m`. Byte-for-byte matching of whole snapshots is not claimed.

## Implemented

- All five underline styles survive styled-VT export/replay; curly/dotted/dashed
  no longer silently become single underline.
- Default, palette and RGB identities survive for foreground/background/underline
  colors. Equal displayed RGB values no longer collapse distinct style transitions.
- Current-pen restoration includes underline color, even after pending-wrap cell
  replay or on a completely empty trimmed screen. DECRPSS's separate current-SGR
  response is deliberately unchanged.
- Shared allocation-free style append helper writes invariant protocol numbers
  directly to the output builder, without temporary parameter lists/style strings.

Tests replay managed exports through both processors and compare native exports,
cover every underline/color family and attributes, equal-RGB/different-identity
transitions, palette/default recoloring after restore, current pen/pending wrap,
culture independence and warmed helper allocation.

## Boundaries

This implements the SGR subtask, not complete styled VT/HTML formatter parity.
Full row/blank/selection equivalence, byte-for-byte output, HTML presentation,
per-cell protection and native SIMD implementation details remain follow-ups.
No new public settings, ABI or native binary change is needed.

## Isolated allocation/throughput check

On macOS ARM64, .NET 10 Release, 100,000 warmed style appends into a reused
256-character builder, three runs compared the previous `938ebc6` implementation
against the new helper. The fixture uses bold/italic/single underline and three
RGB colors, so both outputs have equivalent SGR meaning. Old: **30.877–30.950 ms
and 73,600,000 allocated bytes**. New: **22.740–23.211 ms and zero allocated bytes**.
That is about 25% less helper elapsed time and 736 fewer allocated bytes per
style. New output uses 68 rather than 56 characters because style attributes are
separate sequences. Output transmission and whole export costs are not measured;
this is **not** a whole-terminal or renderer throughput claim.

## Previous Windows CI failure

CI `36012473893` at `938ebc6` passed all six native builds and Linux/macOS managed
jobs, but failed a PowerShell-table test splitting LF snapshot output with
`Environment.NewLine` on Windows. The expectation now uses the documented LF
export contract; PTY input, PowerShell behavior and reflow are unchanged.

PowerShell's `FormatAndOutput/common/TableWriter.cs` sends generated table rows
to `LineOutput.WriteLine`; `out-console/ConsoleLineOutput.cs` delegates physical
wrapping/newline output to `WriteLineHelper`, explicitly accounting for carriage
return. RoyalTerminal must consume that output normally, then serialize its
terminal state according to the selected formatter's contract. A plain snapshot
is not a byte-for-byte replay of PowerShell's original console stream.

## Validation

At code commit `3b71a81`, full local Release validation with CI flags and
`ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1` passed **4,031 unit/headless + 240 native
integration = 4,271 tests**, with **16 conditional unit skips and zero failures**.
Both projects wrote `styled-sgr-release.trx`. The final focused suite passed
**48 tests, zero skips/failures** (`styled-sgr-focused.trx`); the full solution
Release build completed with **zero warnings and errors**. Implementation was
committed/pushed before validation. Fresh platform CI results are tracked separately in PR #116;
the local run is not a claim of Linux/Windows or six-architecture runtime sign-off.
