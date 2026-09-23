# Unix output pipeline measurements (2026-09-22)

The review found that a dedicated parser thread alone did not port Ghostty's
gather optimization. `UnixPty` still performed blocking reads and copied each
approximately 1 KiB macOS PTY read into the UI queue. The revised pipeline uses
four 64 KiB buffers, generation-aware leases through transport/session/control,
adaptive refill bridging, and a parser-idle wake pipe. The existing session-owned
parser consumes those leases and returns storage after parsing. Subscribed public
`TerminalControl.DataReceived` events receive a stable copy so callers can retain
their payloads.

The gather policy follows Ghostty `src/termio/Exec.zig` at the pinned revision:
immediate delivery below 1 KiB; up to 16 nonblocking retries per saturated refill
gap; a 1 ms readiness wait while the parser is busy; and a 3 ms total bridge budget.
Parser-idle and stop pipes interrupt readiness waits. Buffer capacity supplies
backpressure. A separate renderer demand handoff prevents output from repeatedly
overtaking a waiting frame, following `src/renderer/State.zig`.

## Method

- Host: macOS 26.6 (25G72), arm64; .NET 10 Release builds.
- Baseline: `b8b16e93947133b7ff9590075e2769059016fcc9` archived into a separate directory.
- Harness: `tests/RoyalTerminal.Benchmarks/PtyOutputBenchmark.cs`, identical in both
  builds; the baseline compiles without `ROYALTERMINAL_OUTPUT_LEASES` and copies
  conventional output events into a bounded parser queue. The new build transfers
  leases. Both use the same independent byte-checksum consumer, so parser/model
  optimizations do not affect this IO comparison.
- Six 16 MiB `/dev/zero` transfers per run through a real PTY. Iterations 0 and 1
  warm the runtime; summaries use iterations 2 through 5. No other builds/tests ran
  during measurements. Allocations include startup and the fixed ring allocation.
- Interactive measurement: 100 single-character shell request/response exchanges,
  with echo disabled and 2 ms idle intervals, measured through the same parser queue.
- Commands: `ROYALTERMINAL_BENCHMARK_PRIORITY=below dotnet <baseline-benchmark.dll>
  --pty-output` and `ROYALTERMINAL_BENCHMARK_PRIORITY=initiated dotnet
  <current-benchmark.dll> --pty-output`. Current-only `normal` and `below` variants
  keep the gather thread's upstream QoS policy and vary the parser scheduling.

These measurements isolate IO throughput and ownership allocations. They are not
application FPS or full VT-processing throughput measurements.

## Results

| Metric | Baseline | Revised |
| --- | ---: | ---: |
| Median steady throughput, two runs | 95.4295 MiB/s | 123.911 MiB/s |
| Allocations per 16 MiB, steady | 17,179,904 B | 263,992 B |
| Median callback count per 16 MiB | 16,384 | 382.5 |
| Interactive median, range of two runs | 0.072–0.073 ms | 0.070–0.072 ms |
| Interactive p95, range of two runs | 0.168–0.190 ms | 0.116–0.203 ms |

This workload improved throughput by 29.85% and reduced allocations by 98.46%.
Interactive latency distributions overlap; no latency improvement is claimed.

Raw steady throughput samples (MiB/s), including the lower-throughput outlier:

| Run | Iteration 2 | Iteration 3 | Iteration 4 | Iteration 5 |
| --- | ---: | ---: | ---: | ---: |
| Baseline, run 1 | 97.851 | 96.402 | 95.173 | 95.686 |
| Baseline, run 2 | 95.076 | 96.440 | 95.031 | 93.706 |
| Revised, user initiated requested, run 1 | 123.697 | 120.981 | 81.727 | 117.897 |
| Revised, user initiated requested, run 2 | 124.970 | 124.125 | 124.712 | 124.275 |
| Revised, normal | 126.312 | 124.521 | 126.196 | 123.831 |
| Revised, below normal | 123.368 | 124.345 | 124.215 | 124.299 |

Parser priority medians were 123.911 MiB/s (user initiated requested, both runs), 125.3585
MiB/s (normal), and 124.257 MiB/s (below normal). This checksum workload shows no
material scheduling gain. The implementation requests Ghostty's macOS user-initiated
policy for its dedicated gather/parser threads, retaining the existing policy if the
OS rejects it, and uses normal scheduling elsewhere. It does not alter process
priority or shared pool threads. The original benchmark did not record whether the
QoS request succeeded, so these samples establish an IO/gather improvement, not
successful application of QoS. The harness now records the request result explicitly.
Apple's `pthread/qos.h` documents that using `pthread_setschedparam` permanently
opts a thread out of QoS and later requests fail with `EPERM`; runtime-managed
threads may therefore reject this policy. .NET 10's thread startup explicitly
[sets each new thread's priority before starting it](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/vm/comsynchronizable.cpp#L243-L254),
and its PAL priority implementation
[calls `pthread_setschedparam`](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/pal/src/thread/thread.cpp#L1064-L1070).
Focused tests cover both application and rejection with the original policy preserved.
The final .NET 10/macOS test run observed `applied=False, error=1 (EPERM),
before=0x0, after=0x0`: QoS is not applied on this runtime. Normal-priority fallback
is implemented, but user-initiated scheduling parity remains a runtime limitation.

The first implementation checked parser-idle before the 16 read retries and
regressed to approximately 73 MiB/s. Rechecking upstream showed that parser-idle
must prevent sleeping, not the short saturated read retries. Moving that check
to the bridge-poll boundary produced the results above. Small interactive output
still takes neither the spin nor poll bridge path.
