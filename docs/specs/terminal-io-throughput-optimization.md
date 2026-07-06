# Terminal IO Throughput Optimization

Date: 2026-07-06

## Goal

Match Ghostty-style PTY IO throughput behavior for RoyalTerminal's Unix PTY path while preserving interactive latency, MVVM boundaries, existing `IPty` contracts, AOT compatibility, and focused regression coverage.

## Reference Review

Required terminal references inspected before implementation:

- Ghostty [`src/termio/Exec.zig`](https://github.com/ghostty-org/ghostty/blob/main/src/termio/Exec.zig), local shallow checkout commit `2da015cd6ac06cedc89e09756e895d2c1715205d`.
- Windows Terminal [`ConptyConnection.cpp`](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalConnection/ConptyConnection.cpp), local shallow checkout commit `e58bd4bdab46f7b0de02b6b3494be5a81e4940ad`.
- xterm.js [`WriteBuffer.ts`](https://github.com/xtermjs/xterm.js/blob/master/src/common/input/WriteBuffer.ts), local shallow checkout commit `8aab310366549d8d865bd8fc4bd509051f2bb2a1`.

Findings:

- Ghostty now uses a two-stage POSIX read pipeline: `io-gather` drains the PTY into a small ring of preallocated 64 KiB buffers, while `io-reader` performs VT processing from completed batches. It treats sub-1 KiB reads as interactive and delivers them quickly, but bridges saturated 1 KiB refill gaps with bounded spin/poll work.
- Windows Terminal does not use the same POSIX PTY strategy. Its ConPTY path uses overlapped pipe IO and queues the next `ReadFile` before raising terminal output so slow output handlers do not stop pipe draining.
- xterm.js is not a PTY implementation, but its `WriteBuffer` confirms the same architectural split at the parser boundary: queue incoming chunks, process in bounded slices, and avoid unbounded parser/render starvation.

Decision:

- RoyalTerminal should follow Ghostty for Unix PTY reads because the previous RoyalTerminal loop had the same serial shape: `read()` then `DataReceived`/VT processing on the same thread.
- RoyalTerminal should not blindly apply the Ghostty POSIX gather strategy to Windows ConPTY. Windows already has a separate pipe implementation and different kernel behavior; a Windows change needs a separate ConPTY measurement pass.
- The tweet text says "3 nanoseconds", but Ghostty source uses `3 * ns_per_ms`, i.e. 3 milliseconds. RoyalTerminal follows the source because 3 ns is below practical syscall/timer granularity.

## Implemented Design

Files:

- `src/RoyalTerminal.Terminal.Pty.Unix/Terminal/UnixPty.cs`
- `src/RoyalTerminal.Terminal.Pty.Unix/Terminal/UnixPtyReadBatchPolicy.cs`

Implementation:

- Split Unix PTY output into two background stages per terminal instance:
  - `PTY-Gather`: nonblocking `read(2)`/`poll(2)` loop that owns the PTY read fd.
  - `PTY-Dispatcher`: delivers completed batches through the existing `DataReceived` event.
- Added a fixed ring of 4 preallocated buffers, each 64 KiB.
- Added backpressure by blocking gather when all 4 buffers are published and not yet released.
- Added low-latency interactive behavior:
  - any positive read below 1024 bytes publishes immediately;
  - EAGAIN before 1024 gathered bytes publishes immediately.
- Added saturated-stream behavior:
  - after at least 1024 gathered bytes, EAGAIN triggers up to 16 immediate read retries;
  - if still dry, poll waits 1 ms at a time;
  - total bridge budget is 3 ms per batch.
- Kept the public `IPty` surface unchanged.
- Kept the implementation allocation-free after pipeline startup for the read hot path.

## Modern API, SIMD, And AOT Notes

- The read path uses spans/pointers over preallocated arrays and avoids per-read `byte[]` allocation.
- Fixture generation uses `FileStream.Write(ReadOnlySpan<byte>)` and one reusable buffer.
- SIMD is not applied directly in the PTY syscall loop because no byte transformation happens there. The relevant vectorized work for this change is in benchmark/test marker searching and future parser/scanner paths, where `Span<T>` APIs can map to runtime SIMD. VT parser SIMD should be measured separately before implementation.
- `RoyalTerminal.Terminal.Pty.Unix` and `RoyalTerminal.Terminal.Pty.Platform` now set `IsAotCompatible=true` so the SDK AOT/trim analyzers cover the touched packages.
- `RoyalTerminal.Terminal` now sets `IsAotCompatible=true`; reflection-based `System.Text.Json` serializers were moved to source-generated metadata in `TerminalJsonSerializerContexts`.
- A tiny `RoyalTerminal.PtyIoAotSmoke` executable publishes and runs through NativeAOT to validate the PTY stack end-to-end.
- The demo app has a NativeAOT publish path:
  - native library resolution uses `AppContext.BaseDirectory` instead of `Assembly.Location`;
  - `PublishAot=true` disables the optional Pretext text pipeline because the current Pretext package uses trim-unsafe reflection;
  - `PublishAot=true` excludes `ReactiveUI.Avalonia`; the shell uses a plain Avalonia `Window`, explicit open/close lifetime disposal, and trim-safe property-change observables;
  - ViewModel settings-panel completion marshals through `Dispatcher.UIThread` directly instead of `AvaloniaScheduler`.
- Test-only reflection against `UnixPty` was removed by exposing an internal `SlavePtyPath` property to `RoyalTerminal.Tests`.

## Benchmarks And Fixtures

The existing benchmark runner now supports Ghostty-style IO fixture generation:

```bash
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- \
  --skip-render \
  --io \
  --vt-parse \
  --io-mode managed-vt \
  --io-repeats 5 \
  --fixture-size-mb 150 \
  --fixtures /tmp/royalterminal-io-fixtures \
  --output /tmp/royalterminal-io.md
```

PTY IO modes:

- `--io-mode raw`: measures PTY delivery and marker scanning only.
- `--io-mode managed-vt`: feeds each delivered PTY batch through `BasicVtProcessor`.
- `--io-mode both`: runs both rows for each fixture.

The report includes terminal-side elapsed throughput and child-process wall time parsed from `/usr/bin/time -p cat`. The child columns show whether the writer process is stalling behind PTY read/dispatch behavior.

`--vt-parse` adds a parser-only section that excludes file IO and PTY kernel behavior, reports fixture byte shape, and measures `BasicVtProcessor.Process` directly. This is important because PTY throughput and VT parse/screen mutation are separate bottlenecks.

Generated fixtures:

- `{N}MB_ascii.txt`
- `{N}MB_unicode.txt`
- `{N}MB_csi.txt`

### Profiling Result

The initial managed-VT numbers looked suspicious because the benchmark was mixing PTY dispatch shape with VT parser/screen allocation cost. Parser-only profiling on 16 MiB fixtures showed the actual bottleneck:

| Scenario | Before MiB/s | After MiB/s | Speedup | Before alloc/MiB | After alloc/MiB | Allocation reduction |
|---|---:|---:|---:|---:|---:|---:|
| ASCII | 15.359 | 76.998 | 5.01x | 49,923,344 B | 3,045,861 B | 16.4x |
| Unicode | 19.117 | 60.862 | 3.18x | 37,642,356 B | 5,801,346 B | 6.5x |
| CSI | 81.661 | 114.322 | 1.40x | 1,025 B | 1,025 B | 1.0x |

Root cause:

- Whole-screen scrolling allocated a fresh `TerminalRow` for every new line even after scrollback was already full.
- Printable ASCII took the full Unicode grapheme/category and width path per codepoint.
- CSI-heavy fixtures perform less printable cell mutation, so they were already much faster and allocated almost nothing.

Fixes:

- Recycle the trimmed top `TerminalRow` as the new bottom row once scrollback is at capacity.
- Add printable ASCII fast paths for grapheme-append checks and codepoint width calculation.
- Extend the benchmark report with callback timing/allocation and parser-only profiling.

### Main vs Optimized

Compared `origin/main` (`97d4c4d`) against this branch on macOS arm64 with the same 16 MiB fixtures and five repeats. Each run used a real PTY, `/usr/bin/time -p cat`, and managed VT parsing through `BasicVtProcessor`; the table reports median runs.

| Scenario | Main terminal MiB/s | Optimized terminal MiB/s | Terminal speedup | Main child MiB/s | Optimized child MiB/s | Child speedup | Main batches | Optimized batches | Batch reduction |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ASCII | 13.778 | 64.584 | 4.69x | 13.913 | 66.667 | 4.79x | 16,590 | 445 | 37.3x |
| Unicode | 17.609 | 50.040 | 2.84x | 17.778 | 51.613 | 2.90x | 16,567 | 408 | 40.6x |
| CSI | 60.073 | 89.657 | 1.49x | 61.538 | 94.118 | 1.53x | 16,627 | 511 | 32.5x |

The optimized branch now improves both sides of the original problem: saturated PTY output is delivered as 64 KiB batches instead of serial 1 KiB dispatches, and the managed parser no longer spends most of its time allocating rows for steady-state scrolling.

## Validation Log

Commands run:

```bash
dotnet build src/RoyalTerminal.Terminal.Pty.Unix/RoyalTerminal.Terminal.Pty.Unix.csproj -c Release
dotnet build tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release
dotnet build samples/RoyalTerminal.Demo/RoyalTerminal.Demo.csproj -c Release
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release
dotnet publish tests/RoyalTerminal.PtyIoAotSmoke/RoyalTerminal.PtyIoAotSmoke.csproj -c Release -r osx-arm64 -p:PublishAot=true --self-contained true -o /tmp/royalterminal-pty-aot-smoke-clean
/tmp/royalterminal-pty-aot-smoke-clean/RoyalTerminal.PtyIoAotSmoke
dotnet publish samples/RoyalTerminal.Demo/RoyalTerminal.Demo.csproj -c Release -r osx-arm64 -p:PublishAot=true --self-contained true -o /tmp/royalterminal-demo-aot
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --skip-render --io --fixture-size-mb 1 --fixtures /tmp/royalterminal-io-fixtures-smoke --output /tmp/royalterminal-io-smoke.md
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --skip-render --io --io-mode both --io-repeats 2 --fixture-size-mb 1 --fixtures /tmp/royalterminal-io-fixtures-better-smoke --output /tmp/royalterminal-io-better-smoke.md
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --skip-render --io --io-mode managed-vt --io-repeats 3 --fixture-size-mb 32 --fixtures /tmp/royalterminal-io-compare-fixtures --output /tmp/royalterminal-io-managed-vt-optimized.md
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --skip-render --vt-parse --io-repeats 5 --fixture-size-mb 16 --fixtures /tmp/royalterminal-io-profile-fixtures --output /tmp/royalterminal-vt-parse-after.md
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --skip-render --io --io-mode both --io-repeats 5 --fixture-size-mb 16 --fixtures /tmp/royalterminal-io-profile-fixtures --output /tmp/royalterminal-io-profile-after.md
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "FullyQualifiedName~UnicodeWidthTests|FullyQualifiedName~TerminalScreenTests"
```

Results:

- Unix PTY package build: passed.
- Benchmark project build: passed.
- Demo app build: passed.
- Full test suite: 1238 passed, 16 skipped.
- PTY NativeAOT smoke publish and binary run: passed.
- Demo NativeAOT publish for `osx-arm64`: passed. The macOS linker emitted debug-info module-cache warnings only.
- IO benchmark smoke: passed, report written to `/tmp/royalterminal-io-smoke.md`.
- IO benchmark both-mode smoke: passed, report written to `/tmp/royalterminal-io-better-smoke.md`.
- IO benchmark managed-VT 32 MiB run: passed, report written to `/tmp/royalterminal-io-managed-vt-optimized.md`.
- Managed VT parser profile: passed, report written to `/tmp/royalterminal-vt-parse-after.md`.
- PTY IO profile after parser/scroll optimization: passed, report written to `/tmp/royalterminal-io-profile-after.md`.
- Focused terminal screen and Unicode width tests: passed, 87 tests.

## Follow-Up Work

- Run full 150 MiB benchmark fixtures on target machines and compare against Ghostty/Alacritty/Kitty with identical shell, font/render settings, and hardware.
- Continue profiling deeper VT parser paths after the read-side and steady-state scroll allocation bottlenecks.
- Consider SIMD only in measured byte-processing paths such as UTF-8 classification, ASCII fast paths, CSI scanning, or marker/search helpers.
- Add CI jobs for PTY NativeAOT smoke and demo NativeAOT publish on each supported RID.
- Audit the optional Pretext pipeline again when the package exposes trim/AOT-safe metadata.
