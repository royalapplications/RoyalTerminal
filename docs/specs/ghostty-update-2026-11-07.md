# libghostty update — November 7, 2026

This work starts from RoyalTerminal `c5e5365c` and Ghostty
`4a4fc017940bec38c93b278c10c6a18c591f3e63`. The goal is to update the native
dependency and reconcile the public API and terminal behavior with both native
and managed RoyalTerminal engines.

## Work and validation ledger

| Area | Work | Status |
| --- | --- | --- |
| Upstream | Inspect latest Ghostty, libghostty 1.0 announcement and Ghostling | In progress |
| Dependencies | Pin inspected Ghostty revision; reconcile native overlays and build dependencies | Pending |
| Native API | Audit all public functions, types, callbacks and wrapper behavior | Pending |
| Managed behavior | Compare terminal changes with Ghostty, Windows Terminal and xterm.js; cover applicable differences with focused tests | Pending |
| Local validation | Native builds, ABI audit, xUnit integration and managed/headless suites | Pending |
| Performance | Compare repeatable Release workloads before/after; record timings and allocations | Pending |
| CI | Track final PR revision through all required jobs | Pending |

Parity claims will be tied to an explicit source inventory and validation evidence.
Ghostty desktop window management and Ghostling's demonstration frontend are
separate from the terminal library; applicable host integration is audited alongside
the terminal engine. New PowerShell-related behavior requires inspection of its
implementation before changes.

## Pinned source and native migration

The inspected latest `main` is `5813c9461d5af78b6307a1254d00b1bc190eaccd`
(libghostty `1.1.0-dev`). It contains the stable 1.0 release
`0fa3d577a5614150ff8e2c152de5fb9d5c6d2dec` (`vt-1.0.0`). The
[release announcement](https://mitchellh.com/writing/libghostty-1-0) describes
ABI stability and independent library versioning. RoyalTerminal follows the
latest inspected source as requested, rather than treating the Ghostty desktop
version as the library version.

Ghostling `HEAD` remains `63842bf8e5e481160f81d348da9ff6fd27986798`; its
[existing source audit](ghostling-update-2026-10-27.md) still applies.

The native extension overlays retain their existing behavior. All replacement
fragments match the new source; pinned full-file hashes are updated. History
and hyperlink exports use the new frozen grid reference without a size field.
Linux pkg-config correction now installs into the upstream library-relative
`pkgconfig` directory. The submodule remains unmodified.

Initial local evidence: the previous pin rebuilt with checksum-verified Zig
0.17.0 and passed its 187-type/31-callback ABI source audit. The new macOS arm64
VT and renderer libraries build; native history, package-target and metadata
unit tests pass. Managed bindings and full behavior validation remain in progress.

The complete baseline benchmark binaries and native libraries were preserved
outside the repository before migration. The fixed-work `--terminal-update`
workload measured seven samples per case after warmup, using the rebuilt old
pin. Final performance comparisons will use these same binaries and workloads.
