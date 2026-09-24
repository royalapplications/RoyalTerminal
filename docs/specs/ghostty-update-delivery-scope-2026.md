# Ghostty update: native-first delivery scope

## Scope decision (2026-09-23)

The user reduced the original full-parity request to prioritize the native Ghostty
update, retain the already implemented managed improvements, and document the
remaining work. This document supersedes the completion criteria in the earlier
[full-parity audit](ghostty-ghostling-2026-parity.md). That audit remains the detailed
engineering history; its open items are not silently declared implemented.

This delivery updates **libghostty-vt, its .NET bindings, RoyalTerminal's native
integration and native renderer bridge**, not every feature of the standalone
Ghostty application. Shared Avalonia host limitations affect both VT engines.
There is no claim of full managed, full Ghostty application, or pixel-identical
renderer parity.

## Included

- Ghostty pinned at `622b4eecd7d2ce1a10930537c17f0d61abdba817`; remote HEAD and the
  submodule were reverified on September 23. Original pin:
  `a60cd15bb5a197d8e2596e86442031cbece06bcc`.
- Ghostling reviewed at `63842bf8e5e481160f81d348da9ff6fd27986798`, also reverified.
  It is a reference consumer, not an added runtime dependency. Its older Ghostty
  pin does not override the current Ghostty C API.
- Desktop VT API bindings and ownership-safe wrappers, including search, binary
  snapshots/continuation, dirty render state, streaming formatter, clipboard/paste
  effects, mode configuration and terminal options. WebAssembly-only allocation
  functions are intentionally outside desktop packaging.
- Six native build targets: Windows, Linux and macOS, each x64 and arm64.
  Cross-build success does not imply runtime execution on all six architectures.
- The implemented managed parser/input/Unicode/graphics/snapshot improvements,
  with their existing regression and differential tests. No additional full
  managed rewrite is required for this delivery.
- Clean session-owned output worker, bounded Unix gather pipeline, generation-aware
  buffer leases, flush/failure/stop/join behavior and renderer demand handoff.
- Unix password-mode detection, balanced macOS secure-input ownership and password
  cursor rendering for both engines. Hosts without Nerd Fonts use a documented
  geometric lock fallback.

## Deferred work and explicit limitations

September 24 follow-ups: IME/layout and snapshot quota admission are implemented;
see the [scope and validation](ime-layout-snapshot-quotas-2026.md) for the remaining
platform/allocator boundaries. [Managed search](managed-search-parity-2026.md)
now has streaming literal matching and logical multi-row ranges in both adapters;
incremental history caching/background search remain deferred. [Glyph font
coverage](glyph-font-coverage-2026.md) now connects both adapters to the shared
host's configured font/fallback coverage with bounded, owned caches. The table below
records the original native-first delivery boundary, not the updated follow-ups.

| Area | Current boundary | Follow-up acceptance evidence |
| --- | --- | --- |
| Managed snapshot quotas | Restore/export and incremental history work; native allocation calculations are tested but live retention uses the managed host's row policy | Native-equivalent byte/minimum-line quotas through mutation, COW, reflow, pruning and live incremental restore |
| Shared host IME/layout | Encoders accept composition, unshifted scalar and consumed-modifier metadata; full Avalonia production of those fields/preedit is unfinished | Real composition/commit/cancel, candidate positioning, focus/session reset and layout tests on all host platforms, including no duplicated or leaked keys |
| Managed graphics/formatter/protocol parity | Existing focused cases pass, not an exhaustive native-equivalence claim | Remaining response/streaming, animation, placement/anchor and formatter edge matrices |
| Full upstream performance port | Applicable ports and individual measurements are documented; the 308-change inventory is not a completed per-change port audit | Per-change applicability decisions and representative before/after rendering, parsing and IO measurements |
| Standalone Ghostty renderer/platform behavior | RoyalTerminal uses its own Avalonia/Skia host and native bridge | Separate review of upstream Metal/OpenGL, display-link, GPU lifetime, font and application-only changes before claiming host parity |
| macOS IO QoS | The managed runtime rejects user-initiated QoS with EPERM; existing scheduling is preserved | A supported scheduling implementation plus measured benefit; current code makes no successful-QoS claim |
| Platform coverage | Six native cross-builds; runtime CI on the three configured managed runners | Dedicated runtime/interactive IME/GPU/PTY validation on additional architectures where required for product support |

The incomplete IME experiment was removed before the original native-first
delivery. The separately requested follow-up now implements reviewed composition
handling and quota admission; it does not claim exact mutable allocator parity.

## Validation and release gate

At code commit `52ad9a9`:

- Full Release: **3,894 unit/headless + 232 integration tests passed**, 16 conditional
  skips, zero failures (`secure-input-cursor-full.trx`).
- Full solution build: **zero warnings and zero errors**.
- Required-native integration: **232 passed, zero skips**, with
  `ROYALTERMINAL_REQUIRE_NATIVE_TESTS=1` (`secure-input-cursor-required-native.trx`).
- Native ABI audit `scripts/audit-ghostty-abi.py <library> --check`: all **159 types
  and 27 callback signatures** checked against the built macOS arm64 library.
- All six native build jobs passed in CI run
  [35908252151](https://github.com/royalapplications/RoyalTerminal/actions/runs/35908252151).
  The three managed build/test jobs were still running when this scope was written;
  they are not recorded as passes here. Final current-head results belong in the PR.

The PR must retain honest CI status and resolve failures attributable to these
changes. Remaining managed parity is a documented follow-up, not a release gate
under the user's revised scope. Performance evidence and its limitations are in
[the IO report](ghostty-io-performance-2026.md); its isolated throughput/allocation
results must not be described as whole-application gains.
