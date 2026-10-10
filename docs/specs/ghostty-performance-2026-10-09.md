# Ghostty update performance validation — 2026-10-09

This pass compares the pre-update application (`ad857e6f`, Ghostty `b40acce5`),
the completed update before optimization (`3ede1f13`, Ghostty `246f7028`), and
the optimized update (`b892a35d`, same native library). The native library's
SHA-256 is identical before and after the managed optimizations.

## Method

- Apple M3 Pro, macOS arm64, 18 GiB RAM; .NET SDK 10.0.201 / runtime 10.0.5;
  Release builds, Zig 0.16.0 native ReleaseFast builds.
- Frozen, separate build directories retain each revision's managed and native
  dependencies. The common terminal-update harness is compiled unchanged at the
  pre-update baseline; the new status/compression harnesses compare the completed
  feature implementation with its optimized implementation.
- Seven samples per process after a workload warmup. Three independent processes
  per revision, alternating execution order. Reported time is the median of the
  three process medians. Comparisons use `DOTNET_TieredCompilation=0` to prevent
  short workloads reaching optimized JIT tiers at different times. Default JIT
  runs were also exercised, but do not support small timing claims.
- Setup, payload construction, forced collections and checksum reporting are
  outside the new harness's timing. Allocations use
  `GC.GetAllocatedBytesForCurrentThread`; native memory, pool retention, and other
  threads are excluded. Timings include the necessary output allocation.
- Other work was running on this machine. Absolute timings and small differences
  remain sensitive to scheduling and CPU power state. These are fixed-work
  microbenchmarks, not UI frame-rate or PTY latency measurements.

The new benchmark switches are `--terminal-update`, `--program-status`, and
`--history-compression`. Existing print, reflow, snapshot encode/reflow, render,
and native viewport extraction workloads provide broader regression coverage.
Run one process at a time, for example:

```sh
dotnet build tests/RoyalTerminal.Benchmarks -c Release
DOTNET_TieredCompilation=0 dotnet tests/RoyalTerminal.Benchmarks/bin/Release/net10.0/RoyalTerminal.Benchmarks.dll --history-compression
DOTNET_TieredCompilation=0 dotnet tests/RoyalTerminal.Benchmarks/bin/Release/net10.0/RoyalTerminal.Benchmarks.dll --program-status
DOTNET_TieredCompilation=0 dotnet tests/RoyalTerminal.Benchmarks/bin/Release/net10.0/RoyalTerminal.Benchmarks.dll --terminal-update
```

## Changes and reference decisions

`dotnet-trace` sampled thread stacks before and after optimization. The original
row restore repeatedly entered `BrotliDecoder.Decompress` through `BinaryReader`
and `BrotliStream` for individual cell fields. Row encoding also allocated a
complete temporary `MemoryStream` buffer for each row. Both now operate on pooled
spans and perform one Brotli operation per row. Explicit numeric fields and raw
UTF-16 retain null/empty graphemes, unpaired surrogates, all colors/attributes,
and hidden columns. Only the immutable compressed result is retained. All
fallible work still completes before replacing row storage.

The reference comparison informed this boundary:

- [Ghostty compressed pages](https://github.com/ghostty-org/ghostty/blob/246f702876b924a1cb7cade1e99274d1470302fc/src/terminal/compress/Page.zig)
  reuse scratch storage and retain independently owned compressed state until
  restoration succeeds. RoyalTerminal follows those ownership/failure semantics;
  managed rows use pooled byte buffers rather than native virtual-memory tricks.
- [Windows Terminal rows](https://github.com/microsoft/terminal/blob/main/src/buffer/out/Row.cpp)
  and [xterm.js buffer lines](https://github.com/xtermjs/xterm.js/blob/master/src/common/buffer/BufferLine.ts)
  retain directly accessible resident cell storage. Ordinary RoyalTerminal row
  access remains unchanged; the optimized codec is confined to cold history.
- OSC 7501 parsing still follows the pinned Ghostty implementation and the
  [protocol decisions in the update audit](ghostty-update-2026-10-09.md).
  A bounded index of explicitly named applications replaces repeated full-record
  scans during inheritance resolution. Span-based ancestor lookup allocates no
  substring. Replacement, subtree clear, eviction, prompt cleanup and reset all
  maintain the index; inherited results are never cached.

## Measured optimization results

Each row workload processes 2,000 copies. Times are milliseconds; allocations
are bytes for all 2,000 operations, including the resulting row/cells/strings.

| Managed workload | Before ms | After ms | Speedup | Allocated before | Allocated after |
|---|---:|---:|---:|---:|---:|
| Compress 80 ASCII cells | 20.208 | 9.607 | 2.10× | 7,312,000 | 496,000 |
| Compress 80 grapheme cells | 19.098 | 10.415 | 1.83× | 8,032,000 | 576,000 |
| Compress 240 ASCII cells | 36.711 | 13.021 | 2.82× | 20,432,000 | 496,000 |
| Compress 240 grapheme cells | 49.403 | 15.331 | 3.22× | 22,432,000 | 576,000 |
| Restore 80 ASCII cells | 116.406 | 6.922 | 16.82× | 8,352,224 | 7,872,000 |
| Restore 80 grapheme cells | 119.204 | 8.895 | 13.40× | 13,472,448 | 12,992,000 |
| Restore 240 ASCII cells | 325.704 | 15.223 | 21.40× | 23,712,672 | 23,232,000 |
| Restore 240 grapheme cells | 473.333 | 20.518 | 23.07× | 39,073,120 | 38,592,000 |

Compression allocates 92.8–97.6% fewer managed bytes in these cases. Output
checksums and encoded sizes are identical. The 1,201-row managed snapshot still
uses 293,710 bytes of resident row payload versus 4,616,640 uncompressed; the
encoded cold rows occupy 201,458 bytes. This accounting excludes row-object and
shared-pool overhead.

Five complete compressed snapshot restores take 109.856 → 68.633 ms (1.60×),
allocating 60,360,880 → 40,258,160 bytes (33.3% less). Plain restoration allocates
440 extra bytes across five terminals: the application index costs 88 bytes per
empty terminal. Its 40.508 → 37.223 ms difference is within this environment's
noise and is not an optimization claim. The unchanged native library also
varies between runs; native timings are controls, not claimed improvements.

| Application inheritance, 1,000 full-list resolutions | Before ms | After ms | Speedup | Allocation |
|---|---:|---:|---:|---:|
| 1 record | 0.012 | 0.040 | 0.30× | 0 |
| 64 records | 13.504 | 3.497 | 3.86× | 0 |
| 256 records | 181.270 | 14.370 | 12.61× | 0 |

Indexing trades about 28 ns per root-only lookup and small dictionary-update
work for bounded ancestor lookup at larger record counts. Parsing/applying
20,000 reports at 256 records takes 15.602 → 16.366 ms managed and 64.711 →
65.595 ms through the native adapter, with identical measured allocations.
These small differences are reported rather than presented as throughput wins.

## Update versus pre-update regression checks

The following use the original, unchanged print/reflow/extraction harnesses.
Baseline is `ad857e6f`; candidate is `b892a35d`. Each entry is the median of
three process medians with tiered compilation disabled.

| Workload | Baseline ms | Updated ms | Managed allocation observation |
|---|---:|---:|---|
| ASCII printing, 25,000 feeds | 8.761 | 9.198 | 0 → 0 |
| Unicode printing, 25,000 feeds | 18.084 | 18.154 | 0 → 0 |
| Wide printing, 25,000 feeds | 93.016 | 96.273 | 0 → 0 |
| Styled printing, 25,000 feeds | 20.383 | 20.700 | 0 → 0 |
| Tracked snapshot edits, 25,000 feeds | 123.007 | 118.004 | 0 → 0 |
| ASCII reflow, 2,000 rows / 13 resizes | 60.867 | 63.578 | 113,921,432 → 114,169,376 |
| Mixed reflow, 2,000 rows / 13 resizes | 58.381 | 59.387 | 113,921,336 → 114,169,376 |
| Native extraction, 24 rows / 500 moves | 68.256 | 68.515 | 32,000 → 32,000 |
| Native extraction, 24 rows + overscan / 500 moves | 73.525 | 74.860 | 2,651,976 → 2,657,312 |
| Native extraction, 2,400 rows + overscan / 20 moves | 272.505 | 294.135 | 107,336 → 107,552 |

The new optional compressed-storage reference costs eight bytes per row wrapper.
It accounts for approximately 248 KB additional allocation across the reflow
case (0.22%), and 4.8 MB across 25,000 untracked output-hold cycles (2.7%).
The latter creates 24 row wrappers per cycle. Overscan allocation increases are
also consistent with this reference. Resident cells remain directly accessible;
this cost is retained to support independent compressed COW owners.

The common mixed-engine harness records larger timing differences in some
cases: managed ASCII 9.765 → 12.508 ms and native-adapter ASCII 196.737 →
228.223 ms. The isolated print harness does not reproduce that magnitude, and
native-core ASCII is 1.458 → 1.450 ms. These differences remain sensitive to the
busy host and harness; this report does **not** certify the absence of every CPU
regression. Small throughput gates require a quiet, dedicated runner.

Some protocol work is intentionally new: native DECSTR used to be unimplemented
and now performs the actual soft reset (25,000 sequences: 1.713 → 2.322 ms).
Native prompt processing also adds the newly supported metadata/callback path
(4.888 → 6.491 ms). Managed prompt allocation drops from 42.2 MB to 37.8 MB per
25,000 four-marker cycles; native-adapter prompt allocation remains 47.4 MB.
No native ABI bypass, source patch, or feature disablement is used for these costs.

The snapshot encode/reflow and full Skia/Pretext rendering workloads also complete
at both revisions. Their exploratory timing runs are smoke coverage, not a
statistical speedup claim.

[Raw measurements](https://github.com/royalapplications/RoyalTerminal/blob/ad0112a39c1398d00d3984f4cda8115f9e770695/docs/specs/ghostty-performance-2026-10-09.csv) contain all 450 process
results, including per-process minimum/maximum where the harness emits them.
[Build fingerprints and settings](https://github.com/royalapplications/RoyalTerminal/blob/ad0112a39c1398d00d3984f4cda8115f9e770695/docs/specs/ghostty-performance-2026-10-09.json) identify
the frozen binaries. Trace files and complete console logs were retained in
`/tmp/royalterminal-perf-2026-10-09` on the measurement host.

## Correctness validation

- Release solution build: zero warnings and errors.
- Full unit/headless suite: 7,889 distinct passing cases and 16 existing skips;
  native integration: 276 passing; bounded history contracts: 45 passing.
  Total: 8,210 distinct passing cases across the three suites. Unit batches
  execute 14 duplicate theory cases; the total above deduplicates test names.
- Focused row ownership/status suite: 36 passing. Mixed-field roundtrips cover
  five widths, varying colors/flags, null/empty/long/invalid-UTF-16 graphemes,
  hidden columns, pool reuse, and independent compressed COW owners.
- Compression/status tests with hardware intrinsics disabled: 13 passing.
- Status tests cover nearest ancestors, ordinal IDs, replacement without an app,
  subtree boundaries, transient removal, eviction order and reset.

## CI correction

Linux exposed an invalid assertion in `GhosttyPasteAllocationTests`: the test
read `out_written` after `OutOfMemory`, although Ghostty defines that output only
on success. The native paste implementation and its upstream regression test
confirm that failure must perform no PTY writes. The corrected test asserts the
error and zero actual writes/bytes, then retries successfully and verifies the
complete five-byte paste. It does not depend on stack contents or suppress a
platform failure.
