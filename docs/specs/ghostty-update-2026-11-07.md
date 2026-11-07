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
