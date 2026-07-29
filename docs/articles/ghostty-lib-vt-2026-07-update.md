---
title: Ghostty lib-vt Update Report — July 2026
---

# Ghostty lib-vt Update Report — July 2026

This report records the July 2026 synchronization of RoyalTerminal with the
latest Ghostty `libghostty-vt` C API, the associated Zig toolchain migration,
the managed/native VT parity work, and the reference implementations used to
make compatibility decisions.

## Audited revisions

| Component | Revision | Role in the audit |
| --- | --- | --- |
| RoyalTerminal baseline | [`9a0063f`](https://github.com/royalapplications/RoyalTerminal/commit/9a0063f1eebefb1b9b65b7cc7f08ed2cec9006e0) | Starting point before this update. |
| Previous Ghostty submodule | [`1547dd6`](https://github.com/ghostty-org/ghostty/commit/1547dd667ab6d1f4ebcdc7282adc54c95752ee67) | Previous native ABI and build behavior. |
| Updated Ghostty submodule | [`a60cd15`](https://github.com/ghostty-org/ghostty/commit/a60cd15bb5a197d8e2596e86442031cbece06bcc) | Normative ABI and terminal behavior for this update. |
| Ghostling | [`f9034e4`](https://github.com/ghostty-org/ghostling/commit/f9034e43a50a2f3a8101e35497f486090c1ddd6e) | Consumer architecture and integration comparison. |
| Ghostling's Ghostty pin | [`ae52f97`](https://github.com/ghostty-org/ghostty/commit/ae52f97dcac558735cfa916ea3965f247e5c6e9e) | Useful consumer sample, but older than the ABI integrated here. |
| Windows Terminal | [`0139556`](https://github.com/microsoft/terminal/commit/013955612c03f2d5102448907ab33c622aca51f8) | DECRQSS, indexed-color, and OSC color-report compatibility comparison. |
| xterm.js | [`904ae93`](https://github.com/xtermjs/xterm.js/commit/904ae935269eef5ec6a1415b64463c3d02eff1eb) | Browser-terminal DECRQSS and OSC behavior comparison. |

Ghostty's checked-in headers at the updated submodule revision are the ABI
source of truth. Ghostling is intentionally a small consumer and still uses
the older constructor/options shape from its pinned Ghostty revision; those
signatures were not copied into RoyalTerminal.

## Build and ABI migration

RoyalTerminal now uses Zig 0.16.0 for local scripts and every native CI target.
The build explicitly sets `-Demit-lib-vt=true`, so changes to Ghostty's default
build configuration cannot silently disable the VT library artifact.

The constructor migration changes:

```c
ghostty_terminal_new(allocator, out_terminal, columns, rows)
```

Terminal configuration that was previously supplied through the constructor is
now applied with `ghostty_terminal_set`. RoyalTerminal configures both:

- a byte budget, derived from the requested screen and scrollback dimensions;
- a physical-line limit with one page of pruning slack, matching Ghostty's
  page-based storage model.

Kitty temporary-file configuration now supplies a `GhosttyString` directory
instead of a Boolean option.

### Windows ARM64

Zig 0.16.0 currently fails in its Windows/MSVC ARM64 standard-library debug
path when SIMD is enabled. The verified build lane therefore uses:

- `aarch64-windows-gnu`;
- SIMD disabled for this target only.

The produced DLL and import/static libraries are still native Windows ARM64 PE
artifacts. Other targets retain their normal SIMD configuration.

## New native C API coverage

The raw `GhosttyVtNative` layer now declares all 44 new non-WASM exports found
in the updated Ghostty C surface. The additions are grouped below.

An export-by-export audit found 185 exports in the pinned header surface.
RoyalTerminal binds all 173 host-native exports; the remaining 12 are
WebAssembly-only allocation helpers. The callback structures used for
clipboard writes, notifications, and progress reports retain their native
leading `size` field. Managed callbacks validate that size before reading any
later field, and ABI tests assert both structure sizes and field offsets on
32-bit and 64-bit layouts.

### Color and color-scheme helpers

```text
ghostty_color_contrast
ghostty_color_luminance
ghostty_color_palette_default
ghostty_color_palette_generate
ghostty_color_parse
ghostty_color_parse_palette_entry
ghostty_color_parse_x11
ghostty_color_perceived_luminance
ghostty_color_scheme_report_encode
ghostty_color_x11_name_count
ghostty_color_x11_names
```

`GhosttyColorUtilities` provides safe managed parsing, palette, luminance,
contrast, X11-name, and mode-2031 report helpers.

### Render and Kitty graphics

```text
ghostty_render_state_begin_update
ghostty_render_state_end_update
```

The render wrapper also exposes row selection ranges, per-cell selection and
styling flags, and complete grapheme UTF-8 data. Kitty storage and image
generation stamps are available for renderer cache invalidation.

### Selection and tracked references

```text
ghostty_selection_gesture_event
ghostty_selection_gesture_event_free
ghostty_selection_gesture_event_new
ghostty_selection_gesture_event_set
ghostty_selection_gesture_free
ghostty_selection_gesture_get
ghostty_selection_gesture_get_multi
ghostty_selection_gesture_new
ghostty_selection_gesture_reset
ghostty_terminal_grid_ref_track
ghostty_terminal_select_all
ghostty_terminal_select_line
ghostty_terminal_select_output
ghostty_terminal_select_word
ghostty_terminal_select_word_between
ghostty_terminal_selection_adjust
ghostty_terminal_selection_contains
ghostty_terminal_selection_equal
ghostty_terminal_selection_format_alloc
ghostty_terminal_selection_format_buf
ghostty_terminal_selection_order
ghostty_terminal_selection_ordered
ghostty_tracked_grid_ref_free
ghostty_tracked_grid_ref_has_value
ghostty_tracked_grid_ref_point
ghostty_tracked_grid_ref_set
ghostty_tracked_grid_ref_snapshot
```

The high-level layer owns selection-gesture events/state and tracked grid
references with deterministic disposal. Selection formatting uses the
caller-buffer API so the managed layer controls allocation; the raw
allocator-based export remains available for advanced consumers.

Ghostty selection endpoints are untracked snapshots and are documented as
valid only until the originating terminal mutates. A gesture object may be
reused across terminals: RoyalTerminal resets state against a still-live
previous terminal, or frees and recreates the native gesture with a null
terminal after the previous terminal has already been disposed. This preserves
Ghostty's internal page-list pin lifetime and avoids stale native references.

### Terminal compression and Unicode

```text
ghostty_terminal_compress
ghostty_terminal_compression_activity
ghostty_unicode_codepoint_width
ghostty_unicode_grapheme_width
```

The terminal wrapper exposes incremental/full compression and the activity
token. `GhosttyUnicode` exposes Ghostty's exact codepoint and first-grapheme
width rules.

## Native and managed VT parity

New host-facing behavior is represented by framework-neutral contracts rather
than by native structs:

| Capability | Ghostty-backed VT | Managed VT |
| --- | --- | --- |
| Unicode codepoint width | `ghostty_unicode_codepoint_width` | RoyalTerminal tables plus the pinned Unicode 17 delta |
| First-grapheme width | `ghostty_unicode_grapheme_width` | allocation-free stack path with pooled fallback |
| Clipboard writes | Native normalized callback | Strict OSC 52 and OSC 1337 `Copy` decoding |
| Desktop notifications | Native normalized callback | OSC 9 and OSC 777 |
| Progress reports | Native normalized callback | OSC 9;4 |
| Working directory | Native PWD-changed callback | OSC 7, OSC 9;9, and OSC 1337 |
| Absolute viewport row | Native row scroll request | Existing managed absolute viewport state |

`ITerminalUnicodeWidthProvider` and `ITerminalEffectSource` let hosts use the
same capability shape with either VT engine. Clipboard payloads remain
binary-safe and may carry multiple MIME representations on the native path.
Host policy remains outside both parsers.

The managed clipboard parser follows Ghostty's normalized effect behavior:
OSC 52 accepts a zero- or one-character selection, treats `?` as a read query,
and rejects whitespace or malformed Base64. An empty OSC 52 payload requests a
clear. OSC 1337 keys are ASCII case-insensitive; `Copy=:BASE64` writes the
standard clipboard but does not treat an empty payload as a clear. Valid
non-notification ConEmu OSC 9 commands are consumed rather than being
misreported as desktop notifications. OSC 9 progress states accept only an
end-of-value or a semicolon-delimited decimal percentage; malformed suffixes
fall back to OSC 9 notification handling instead of producing a false 0%
progress report. Payloads `5` and `12` are recognized as no-payload ConEmu
commands only on exact matches, so notification text beginning with either
value remains visible.

Some APIs are inherently specific to libghostty's storage:

- native compression has no managed equivalent because the managed scrollback
  representation is not compressed;
- tracked native grid references and Ghostty's gesture state machine are
  exposed through `RoyalTerminal.GhosttySharp`; the managed terminal continues
  to use its existing screen/selection model;
- Ghostty palette generation is exposed as an opt-in native utility, while the
  managed VT keeps RoyalTerminal's theme and palette model.

The native byte budget and physical-line pruning slack are recalculated for
every resize. Before reflow, RoyalTerminal installs the larger of the previous
and resized byte budgets together with the resized line limit. This raises
capacity before a narrow-to-wide resize without prematurely tightening storage
for a wide-to-narrow resize. After reflow it commits the exact resized budget;
failures restore both previous limits.

## Terminal behavior decisions

For terminal reports, Ghostty and Windows Terminal agree on behavior that is
more precise than RoyalTerminal's previous managed implementation:

- DECRQSS SGR reports begin with reset parameter `0`;
- indexed SGR colors remain indexed instead of being expanded to RGB;
- double underline is reported as `4:2`;
- OSC RGB reports use lowercase hexadecimal digits.

The managed VT now follows that behavior. xterm.js also emits an initial SGR
reset but documents that its SGR status report is not a complete style
round-trip, so it was treated as corroborating rather than normative for the
full report.

The managed width implementation now also matches the pinned Ghostty Unicode
17 codepoint table across every value from U+0000 through U+10FFFF. It handles
surrogates, regional indicators, default ignorables, Hangul continuation
classes, and two-/three-em dashes with Ghostty's rules. VS15 and VS16 only
change a grapheme width when they immediately follow a base listed by Unicode
17's emoji-variation-sequence data; invalid selectors do not force text or
emoji presentation. First-grapheme segmentation also implements Unicode 17
UAX #29 rule GB9c with the official `Indic_Conjunct_Break` consonant and linker
sets, so Indic conjuncts are consumed as one cluster consistently with
Ghostty. Prepend sequences are also covered by managed/native parity tests;
the pinned Ghostty implementation currently consumes `U+0600 U+0041` as one
width-2 cluster, and the managed provider intentionally preserves that result.

The source comparison used Ghostty's
[DECRQSS encoding tests](https://github.com/ghostty-org/ghostty/blob/a60cd15bb5a197d8e2596e86442031cbece06bcc/src/terminal/dcs.zig#L465-L506)
and [OSC report tests](https://github.com/ghostty-org/ghostty/blob/a60cd15bb5a197d8e2596e86442031cbece06bcc/src/terminal/stream_terminal.zig#L1704-L1736),
Windows Terminal's
[DECRQSS dispatcher](https://github.com/microsoft/terminal/blob/013955612c03f2d5102448907ab33c622aca51f8/src/terminal/adapter/adaptDispatch.cpp#L4317-L4385)
and [OSC color formatter](https://github.com/microsoft/terminal/blob/013955612c03f2d5102448907ab33c622aca51f8/src/terminal/adapter/adaptDispatch.cpp#L3318-L3423),
and xterm.js's
[limited DECRQSS implementation](https://github.com/xtermjs/xterm.js/blob/904ae935269eef5ec6a1415b64463c3d02eff1eb/src/common/InputHandler.ts#L3490-L3570).

No PowerShell startup, invocation, environment, prompt, or ConPTY contract was
changed by this work. PowerShell-specific behavior therefore did not require a
compatibility divergence.

## Validation strategy

Coverage is split by responsibility:

- exact ABI enum values for terminal options/data;
- native color parsing, palette, color math, X11 names, and report encoding;
- native codepoint and grapheme width behavior;
- selection derivation, formatting, ordering, adjustment, containment,
  equality, gestures, and tracked references;
- two-phase render updates, row/cell selection, style flags, and UTF-8
  graphemes;
- Kitty generation stamps;
- scrollback limits, absolute viewport rows, and compression;
- native and managed clipboard, notification, progress, and working-directory
  effects;
- exhaustive managed/native codepoint-width parity across U+0000–U+10FFFF,
  plus focused grapheme consumption, Unicode 17 Indic-conjunct, and
  variation-selector edge cases;
- malformed OSC 9 progress suffix rejection and notification fallback;
- exact OSC 9 no-payload command recognition and notification-prefix fallback;
- bidirectional native resize scrollback retention across width-dependent byte-
  and line-limit recalculation;
- sized callback ABI layouts and undersized callback rejection;
- selection-gesture reuse across live terminals and after disposal of the
  previous terminal;
- existing DECRQSS and OSC parity suites.

The CI matrix builds native libraries for Linux x64/ARM64, macOS x64/ARM64, and
Windows x64/ARM64, then runs managed builds/tests on Linux, macOS, and Windows,
native macOS integration tests, documentation generation, and NuGet packing.

## Consumer guidance

Use the layers in this order:

1. use RoyalTerminal's VT abstractions for engine-neutral terminal behavior;
2. use `RoyalTerminal.GhosttySharp` for safe, owned libghostty features;
3. use `GhosttyVtNative` only when a custom integration needs the raw C ABI.

This keeps host policy and managed/native parity at the terminal-contract layer
while preserving full access to new libghostty capabilities.
