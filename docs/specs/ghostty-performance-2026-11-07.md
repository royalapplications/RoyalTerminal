# November libghostty performance comparison

Measured on an Apple M3 Pro, macOS 26.6 ARM64, .NET SDK 10.0.201 / runtime 10.0.5.
Baseline is RoyalTerminal `c5e5365c` with Ghostty `4a4fc017`; updated production
code is RoyalTerminal `8ab3c2de` with Ghostty `5813c946` and the reviewed wide-overwrite overlay. Both native libraries
use Zig 0.17.0 ReleaseFast. Baseline binaries were built and preserved before
updating the dependency.

The identical terminal-update harness DLL ran against each saved dependency
set, with tiered compilation disabled (`DOTNET_TieredCompilation=0`). Each cell
below is the median of seven warmed samples of 250,000 operations, with full GC
between samples. Setup is excluded. The longer run replaces exploratory 25,000
operation samples whose shortest cases were too noisy for a useful comparison.
No other builds or tests ran during these measurements. Negative changes mean
less elapsed time. CSVs retain min/max, managed allocation counts and checksums.

## Terminal update

| Workload | Engine | Before ms | After ms | Change | Managed allocation change |
| --- | --- | ---: | ---: | ---: | ---: |
| ascii | managed | 89.478 | 90.630 | +1.29% | +0 B |
| ascii | native-adapter | 1557.191 | 1241.107 | -20.30% | +0 B |
| ascii | native-core | 14.221 | 11.118 | -21.82% | +0 B |
| unicode | managed | 181.851 | 182.986 | +0.62% | +0 B |
| unicode | native-adapter | 1648.843 | 1330.635 | -19.30% | +0 B |
| unicode | native-core | 42.126 | 42.075 | -0.12% | +0 B |
| wide | managed | 911.280 | 917.781 | +0.71% | +0 B |
| wide | native-adapter | 1915.215 | 1464.201 | -23.55% | +0 B |
| wide | native-core | 263.638 | 121.374 | -53.96% | +0 B |
| styled | managed | 120.996 | 119.858 | -0.94% | +0 B |
| styled | native-adapter | 1660.919 | 1306.102 | -21.36% | +0 B |
| styled | native-core | 29.797 | 22.216 | -25.44% | +0 B |
| title | managed | 40.109 | 40.185 | +0.19% | +0 B |
| title | native-adapter | 140.807 | 140.532 | -0.20% | +0 B |
| title | native-core | 12.338 | 12.366 | +0.23% | +0 B |
| prompt | managed | 331.019 | 328.246 | -0.84% | +0 B |
| prompt | native-adapter | 1846.608 | 1520.084 | -17.68% | +0 B |
| prompt | native-core | 53.166 | 52.234 | -1.75% | +0 B |
| soft-reset | managed | 63.163 | 62.912 | -0.40% | +0 B |
| soft-reset | native-adapter | 141.515 | 136.919 | -3.25% | +0 B |
| soft-reset | native-core | 19.920 | 15.647 | -21.45% | +0 B |

No material regression was observed in these workloads: managed medians vary
from −0.94% to +1.29%, with overlapping sample ranges. The native adapter is
17.7–23.5% faster on the five main text/prompt workloads. Managed allocation
counts are identical in every terminal-update case.

### Wide-overwrite investigation

The unmodified upstream update initially regressed direct native wide-cell
overwrites by about 4%, despite improving the application adapter. Six isolated
million-operation runs, alternating revision order, confirmed this result.
Inspection showed that a rejected wide-cell fill rescanned the remaining
codepoints before each scalar retry. The overlay returns early only for
unlinked wide fills whose current target is already a wide cell or spacer and
has no pending wrap; upstream scalar printing still performs all cleanup.

| Isolated native wide overwrite | Median ms | Managed bytes |
| --- | ---: | ---: |
| Previous dependency, three process medians | 1,061.269 | 0 |
| Upstream update, three process medians | 1,097.915 | 0 |
| Update with reviewed overlay | 478.540 | 0 |

The overlay is 54.9% faster than the previous dependency in this isolated test.
The final full-workload run independently confirms a 54.0% improvement.
`GhosttyWideOverwriteTests` compares complete native bulk/scalar snapshots at
360 checkpoints covering widths 1/2/3/8/80, grapheme modes, links, margins,
wide heads/tails, style changes, delayed wrap and subsequent REP/combining input.
All 305 native integration tests and 249 focused managed/native parity cases
pass with this library. Raw initial-update and alternating-process CSVs are
retained alongside the final results.

## Snapshot-tracked reflow

Three process runs per revision, alternating order; each process uses seven
warmed samples, 512 source rows and 13 resizes per sample, excluding setup and
restore. The table reports medians of the three process medians (and allocation
counts). Each run ends at 524 rows.

| Workload | Before ms | After ms | Change | Before managed bytes | After managed bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| plain | 11.927 | 11.765 | -1.36% | 39,401,288 | 39,409,728 |
| graphemes | 67.289 | 67.049 | -0.36% | 90,287,120 | 90,295,544 |
| shared-links | 117.029 | 118.110 | +0.92% | 110,354,136 | 110,365,664 |
| long-shared-links | 141.911 | 138.499 | -2.40% | 124,385,736 | 109,903,064 |

Process ranges overlap for every workload. The shared-link median increase
is 0.92% (before process medians 115.591–118.658 ms; after 115.726–121.279 ms),
so these measurements do not establish a timing regression. Additional capacity
and history metadata adds 8–12 KB over a complete resize sequence in three
cases (less than 0.03%). Long shared links allocate about 14.48 MB fewer bytes.
The dedicated page-growth test checks zero managed allocations over 1,000
warmed plain-page growth calls.

## Reproduce and limits

```sh
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
  --project tests/RoyalTerminal.Benchmarks -- --terminal-update-long
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
  --project tests/RoyalTerminal.Benchmarks -- --managed-snapshot-reflow
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build \
  --project tests/RoyalTerminal.Benchmarks -- --terminal-wide-overwrite
```

Use the same harness binary with each revision’s saved application and native
dependencies. Do not compare a newly built library against an old managed ABI.
These measurements cover CPU VT updates and managed reflow on this host. They
do not measure GPU presentation, native heap allocation, PTY throughput, or
performance on other architectures. Small timing differences remain subject
to scheduling and hardware variation.

Raw evidence: [before CSV](performance/ghostty-2026-11-07/terminal-before.csv),
[after CSV](performance/ghostty-2026-11-07/terminal-after.csv),
[before reflow](performance/ghostty-2026-11-07/reflow-before.txt),
[after reflow](performance/ghostty-2026-11-07/reflow-after.txt).
