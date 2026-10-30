# Ghostty update — October 27, 2026

The native pin advances from `246f702876b924a1cb7cade1e99274d1470302fc` to
`4a4fc017940bec38c93b278c10c6a18c591f3e63`, the 1.4.1 development version bump
immediately following Ghostty 1.4.0. `main` was merged before the update.
[Ghostling has no new commits](ghostling-update-2026-10-27.md).

The [full 1.4 release inventory](ghostty-1.4-coverage-2026-10-27.md) distinguishes
new terminal-library behavior from previously implemented features and separate
Ghostty desktop application features. Its source-inventory classifications are
not test results. This report records the actual implementation and validation.

## Implementation

- [Managed core and formatter parity](ghostty-managed-delta-2026-10-27.md):
  OSC 7501 revision 0.4 capabilities and malformed-ID rejection; cursor origin
  and margin rules; extended cursor and screen-size replies; reset/mode reports;
  prompt-aware ED2; APC cancellation and canonical continuations; grapheme
  preservation across wrapping; blank backgrounds, links and trailing rows in
  styled VT exports.
- [Public native APIs and ABI coverage](ghostty-native-api-2026-10-27.md):
  bounded writes, render-image iteration/generation, snapshot image progress and
  budgets, prompt scrolling, resize requests and animation ticks. The native
  build now uses Zig 0.17. Superseded resize/animation exports and continuation
  overlays were removed. Retained overlays remain hash- and fragment-checked.
  POSIX builds explicitly link libc independently of SIMD; Linux also links
  librt so the upstream shared-memory loader is available in portable packages.
  Linux GNU targets default to glibc 2.28 when the caller leaves the version
  unspecified; explicit caller-selected versions remain authoritative.
  Generated static pkg-config metadata names the installed archive and includes
  its POSIX system-library dependencies while preserving upstream fields.
- History erasure removes Kitty placements anchored in deleted rows and their
  relative descendants before moving surviving anchors. Pixel data remains
  reusable. The former history-pin restore scratch is no longer needed.
- Shared-memory names pass unchanged to the host OS, including Darwin app-group
  names; empty, bare-slash, NUL-containing and oversized names remain rejected.
  Canonical `/private/tmp` paths are accepted on macOS. Existing opened-file
  identity checks, quotas and unlink-after-open behavior remain in place.
- A non-direct Kitty transmission abandons an unfinished image/frame upload and
  starts its own quiet policy. Repeated OSC 5522 alias updates replace the
  existing target; the managed implementation already retained one target per
  alias rather than an append-only arena.
- Pending Kitty pixel buffers and animation frames use stable transmission
  identities, remain invisible until complete, and prevent premature animation
  scheduling. Managed snapshots now preserve image/frame pixels, placements,
  history anchors and unfinished transmissions using Ghostty's wire records.
  Active viewport images and displayed frames arrive first, history images with
  their pages, and unseen images last. Cross-engine tests cover pending/stale
  pixels, quotas, malformed records, relative/history placements and resumed
  raw, compressed and PNG transfers.
- Skia selection highlighting expands to complete wide glyphs, including wrapped
  spacer cells, and refreshes the following row after a wrapped glyph is replaced.
  Native coordinate mapping now accounts for hidden allocation rows when
  scrollback is disabled, preserving hyperlinks, selections, search and snapshots
  without exposing those rows as user scrollback.
- Managed page pruning restores the cursor's existing implicit hyperlink ID
  before any subsequent page movement. Zero-byte primary scrollback uses the
  same non-growing LF/SU dispatch as the alternate screen; explicit scroll-clear
  remains a separate operation. Both direct and synchronized-output paths are
  checked against native behavior.
- Native text-only refreshes reuse unchanged image scenes. At 128 placements,
  allocation falls from 10,304,000 to 64,000 bytes per 1,000 updates. Existing
  geometry projection is retained because the public unified iterator omits
  anonymous placement sizing identity needed for fractional-cell scaling.
- Logical-line queries avoid materializing default whitespace spans, reducing
  measured allocations from 72 bytes per query to zero. Immutable mode tables
  also avoid runtime field-handle allocations in unoptimized builds: 1,000 mode
  cycles fell from 72,000 bytes to zero, 1,000 mode installations/reads from
  6,408,000 to zero, and 1,000 mouse tracking cycles from 864,000 to zero. Release
  mode operations were already allocation-free and remain so.
- Snapshot history placements are indexed once instead of rescanning the image
  store for each page. A same-process component benchmark over 20,000 rows,
  40 pages and 1,024 placements measured 40 captures at 22.2268 ms and
  8,146,240 bytes before, versus 15.0379 ms and 5,851,200 bytes after. This is
  about 32% less component time and 28% fewer allocations; timing outliers and
  the distinction from full snapshot serialization are recorded in the audit.

## Reference decisions and host checks

Ghostty's pinned `graphics_image.zig`, `graphics_exec.zig`,
`graphics_storage.zig` and clipboard implementation define the Kitty behavior;
the [Kitty graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/)
also requires unmodified shared-memory names and unlinking after consumption.
Windows Terminal's row-image representation and xterm.js line markers do not
define Kitty placement lifetime or snapshot records. The cross-engine tests
therefore follow Ghostty for these features, retaining RoyalTerminal's host
capability policy and bounded allocations.

The managed paste safety query now rejects CR as well as LF, matching Ghostty
`5c9a3d8a9` and Windows Terminal's `TerminalPage.cpp` multiline detection.
xterm.js `Clipboard.ts` normalizes newlines before bracketed framing; it does not
provide the same safety query. RoyalTerminal keeps safety classification and
encoding separate. Its shared clipboard service already flags both CR and LF.

Mouse regression tests cover button 8/9 press, drag, release and capture reset.
Buttons 10/11 are safely ignored, as in Ghostty's `mouse_encode.buttonCode`;
the upstream click-state bounds fix does not add their wire encoding. Windows
Terminal's `mouseInput.cpp` and xterm.js `MouseService.ts` accept fewer physical
buttons, so no additional OS event mapping is inferred from that fix.

## Validation

- macOS arm64 native build, native history tests and renderer unit tests passed
  with Zig 0.17. Linux x64 and Windows x64 GNU cross-builds passed; the Windows
  DLL passed AVX/VEX disassembly checks. MSVC builds use the Windows CI SDK.
- Linux CI exposed missing libc linkage in the SIMD-disabled package: its
  shared-memory query returned `unsupported medium`. The build configuration
  repair passed x64/ARM64 cross-builds and export checks. A live Ubuntu ARM64
  probe through the official C API returned `OK` and verified unlinking for
  both leading-slash and non-leading-slash names. The temporary read-only VM
  share was removed and the VM restored to its original suspended state.
  The exact normalized ARM64 library passed the same live probe. Default x64
  and ARM64 GNU builds match explicit glibc 2.28 outputs byte-for-byte, with
  maximum required GLIBC symbol version 2.27; an explicit 2.34 build retains
  its requested symbol versions.
  Both Linux architectures also link a C consumer using only the emitted static
  pkg-config flags. Three metadata transformation tests and the native history
  suite pass; the corrected metadata installs after upstream's files.
- Native integration: 302 passed; bounded history contracts: 45 passed, both
  requiring native libraries with zero skips. Generated ABI audit covers 187 types,
  31 callbacks and 205 entry points.
- Core/protocol/grapheme checks: 274 passed; formatter checks: 223 passed.
- Image history/lifecycle/matrix checks: 183 passed; pending image/animation
  checks: 138 passed; local file/shared-memory checks: 48 passed.
- Mouse/state checks: 47 passed; new paste/clipboard-alias/chunk replacement
  checks: 14 passed. These groups overlap and are not a unique-test total.
- Wide-glyph rendering/selection checks: 250 passed; disabled-scrollback
  coordinate regressions: 30 passed across the two focused groups.
- Final managed selection/pruning checks: 63 passed; grapheme/metadata failure
  checks: 70 passed; mode and allocation checks: 293 passed in each of Debug and
  Release. The full Release solution builds with zero warnings and errors.
- Final Kitty snapshot and page-codec checks: 58 passed with zero skips,
  including placement indexing and native/managed interoperability.
- All 220 local unit batches completed. Four pruning failures were corrected;
  their two batches then passed all 65 tests. Aggregated unique results are
  8,239 passed, zero failed and 16 pre-existing quarantined macOS/headless tests.
  New zero-history/pruning coverage and related grapheme, graphics and allocation
  checks passed a separate 439-test gate. The final solution build again had
  zero warnings and errors.
- All six native CI jobs passed at `847e6699` with the final native pin/toolchain.
  The documentation site builds with the merged VitePress 2/Vite 8 dependencies;
  the existing source-index fallback handles local XML documentation tool loading.
  Docker reproduction now uses the same extension build, export verification and
  history tests as CI; shell syntax/help checks passed. Docker is not installed
  on this validation host.

Full matrix CI and preview publication status are tracked in
[PR 121](https://github.com/royalapplications/RoyalTerminal/pull/121) and its
associated Actions runs. The counts above are focused local validation groups,
which overlap; they are not a unique-test total for the full suite.
