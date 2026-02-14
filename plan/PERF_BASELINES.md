# Performance Baselines (Phase 6)

Date: 2026-02-14

## Scope
This baseline captures the key managed-path changes introduced in Phase 4:
- `GhosttyTerminalControl` data event payload path (`byte[]` copy -> `ReadOnlyMemory<byte>` passthrough).
- `WindowsPty.Write` write path (open stream per write -> reuse a single stream).
- `GhosttySurface.TryReadSelectionUtf8` contract change (borrowed span -> copy-on-read for safety).

## Environment
- CPU: Apple M3 Pro
- OS: macOS 15.6
- Runtime: .NET SDK 10.0.101 / .NET runtime 10.0.1
- Build: Release
- Runs: 3 per scenario (median reported)

## Methodology
A focused microbenchmark harness was executed locally to compare old/new code patterns under identical iteration counts. The harness isolates the changed operations to reduce unrelated UI/runtime noise.

Important:
- These measurements are operation-level baselines, not full application end-to-end timings.
- The first two scenarios should improve throughput and allocations in real workloads.
- The selection-copy scenario is a deliberate correctness/safety tradeoff.

## Baseline Results (Median)

| Scenario | Before pattern | After pattern | Iterations | Before time (ms) | After time (ms) | Time delta | Before alloc (B) | After alloc (B) | Alloc delta |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| Terminal data dispatch | `byte[]` clone (`ToArray`) per event | `ReadOnlyMemory<byte>` passthrough | 200,000 | 39.218 | 0.935 | -97.62% (41.95x faster) | 824,000,000 | 0 | -100.00% |
| PTY write loop | New `FileStream` each write | Reused `FileStream` | 20,000 writes | 442.217 | 4.873 | -98.90% (90.75x faster) | 87,042,496 | 4,576 | -99.99% |
| Selection UTF-8 read | Borrowed view (no copy) | Copy-on-read (`byte[]`) | 300,000 | 1.009 | 27.596 | +2635.98% (27.35x slower) | 0 | 160,800,000 | +160,800,000 B |

## Interpretation and Expectations
- Data dispatch and PTY write changes are substantial wins in both latency and allocation pressure and are expected to reduce GC churn in interactive sessions.
- Selection read now pays explicit copy cost. This is expected and acceptable because it removes unsafe ownership ambiguity and potential lifetime bugs.
- Operational guidance: selection text retrieval should remain on user-triggered paths, not high-frequency render loops.

## Raw Run Snapshot

### Run 1
- `dispatch.before.time_ms=41.509`
- `dispatch.before.alloc_b=824000000`
- `dispatch.after.time_ms=0.994`
- `dispatch.after.alloc_b=0`
- `pty.before.time_ms=442.217`
- `pty.before.alloc_b=87042496`
- `pty.after.time_ms=4.873`
- `pty.after.alloc_b=4576`
- `selection.before.time_ms=1.009`
- `selection.before.alloc_b=0`
- `selection.after.time_ms=27.596`
- `selection.after.alloc_b=160800000`

### Run 2
- `dispatch.before.time_ms=39.218`
- `dispatch.before.alloc_b=824000000`
- `dispatch.after.time_ms=0.935`
- `dispatch.after.alloc_b=0`
- `pty.before.time_ms=441.195`
- `pty.before.alloc_b=87042496`
- `pty.after.time_ms=4.536`
- `pty.after.alloc_b=4576`
- `selection.before.time_ms=0.969`
- `selection.before.alloc_b=0`
- `selection.after.time_ms=26.772`
- `selection.after.alloc_b=160800000`

### Run 3
- `dispatch.before.time_ms=38.383`
- `dispatch.before.alloc_b=824000000`
- `dispatch.after.time_ms=0.910`
- `dispatch.after.alloc_b=0`
- `pty.before.time_ms=445.306`
- `pty.before.alloc_b=87042464`
- `pty.after.time_ms=5.607`
- `pty.after.alloc_b=4576`
- `selection.before.time_ms=1.083`
- `selection.before.alloc_b=0`
- `selection.after.time_ms=29.272`
- `selection.after.alloc_b=160800000`
