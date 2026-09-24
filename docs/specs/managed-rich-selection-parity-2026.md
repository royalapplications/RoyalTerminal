# Managed styled-VT / HTML selection parity (September 24, 2026)

## Reference decision

Ghostty `622b4eecd` `src/terminal/formatter.zig` `PageFormatter` includes the
leading cell when a selection starts on a wide tail, skips a starting spacer
head, and follows an ending spacer head to the next row when unwrapping a
non-rectangular selection. The C adapter clamps viewport-relative endpoints;
managed rich exports now follow the same rules. Rectangle columns are normalized
independently and row boundaries survive `Unwrap`.

Windows Terminal `src/buffer/out/textBuffer.cpp` `_RowCopyHelper` also adjusts
selection bounds to whole glyphs. xterm.js `addons/addon-serialize/src/SerializeAddon.ts`
`BaseSerializeHandler.serialize` iterates its supplied (exclusive-end) cell range;
`BufferLine.translateToString` advances by cell width without expanding a starting
tail. RoyalTerminal deliberately follows Ghostty's inclusive, whole-glyph rules.
No shell, PowerShell or PTY behavior changes.

Feature lineage: [Ghostty #13643](https://github.com/ghostty-org/ghostty/pull/13643)
for the formatter surface; the detailed boundary rules are verified against the
pinned formatter source, not attributed to a new upstream PR.

## Implemented scope

- Styled VT and HTML selection endpoints clamp to the current scrolled viewport,
  including extreme integer coordinates, before translating to screen rows.
- Reversed and rectangular selections retain their intended bounds. Endpoints
  collapsing onto one row after clamping are ordered again.
- Wide tails select their entire glyph with its style/hyperlink; right-edge wrap
  spacers are omitted and unwrapped end spacers select the following wide glyph.
- Export row traversal and visual trimming read immutable spans, preserving
  copy-on-write sharing instead of detaching rows during read-only export.

## Remaining boundaries

This is selection-boundary parity, not full styled/HTML serialization parity.
Deferred blank-row/cell behavior, style resets around line breaks, HTML
presentation, per-cell protection and exact output bytes remain follow-ups.
Managed rows have no native page boundary: following an ending spacer also works
across those boundaries, whereas the pinned Ghostty formatter has a documented
TODO for that case. Plain-text selection formatting is unchanged in this task.
Native/ghostling dependency pins, ABI and native binaries are unchanged.

## Validation

At code commit `0369f56`, full local Release validation with CI flags and
`ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1` passed **4,069 unit/headless + 240 native
integration = 4,309 tests**, with **16 conditional unit skips and zero failures**.
Both projects wrote `rich-selection-release.trx`. The focused suite passed
**38 tests, zero skips/failures** (`rich-selection-focused.trx`). Implementation
was committed/pushed before validation. The full solution Release build completed
with **zero warnings and errors**. Final-head platform CI is tracked
separately in PR #116; the local run is not multi-platform runtime sign-off.

## Isolated row-access allocation profile

.NET 10 Release on macOS ARM64, 10,000 shared rows of 120 cells, with setup and
state-copy creation outside the measured traversal: the previous mutable-indexer
access allocated **57,840,000–57,843,560 bytes** over three warmed runs; the new
read-only access allocated **zero bytes**, with identical checksums. This isolates
the exact cell access pattern changed in the formatter, not whole-export work.
String/HTML formatting still allocates; no whole-export or renderer speedup is
claimed. Focused tests also assert storage identity for every row after both
selected and full-screen exports, including the visual-trimming scan.
