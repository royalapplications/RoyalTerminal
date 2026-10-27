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
  scheduling. Snapshot serialization/interoperability is still being validated.
- Native text-only refreshes reuse unchanged image scenes. At 128 placements,
  allocation falls from 10,304,000 to 64,000 bytes per 1,000 updates. Existing
  geometry projection is retained because the public unified iterator omits
  anonymous placement sizing identity needed for fractional-cell scaling.

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

## Validation recorded so far

- macOS arm64 native build, native history tests and renderer unit tests passed
  with Zig 0.17. Linux x64 and Windows x64 GNU cross-builds passed; the Windows
  DLL passed AVX/VEX disassembly checks. MSVC builds use the Windows CI SDK.
- Native integration: 301 passed; generated ABI audit covers 187 types,
  31 callbacks and 205 entry points.
- Core/protocol/grapheme checks: 274 passed; formatter checks: 223 passed.
- Image history/lifecycle/matrix checks: 183 passed; pending image/animation
  checks: 138 passed; local file/shared-memory checks: 48 passed.
- Mouse/state checks: 47 passed; new paste/clipboard-alias/chunk replacement
  checks: 14 passed. These groups overlap and are not a unique-test total.

Full CI, final snapshot interoperability, final review and the next preview
publication remain in progress. Release claims must be updated only after
their corresponding checks and package publication succeed.
