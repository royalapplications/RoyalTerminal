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
  --fixture-size-mb 150 \
  --fixtures /tmp/royalterminal-io-fixtures \
  --output /tmp/royalterminal-io.md
```

Generated fixtures:

- `{N}MB_ascii.txt`
- `{N}MB_unicode.txt`
- `{N}MB_csi.txt`

Latest smoke result on this machine with 1 MiB fixtures:

| Scenario | MiB/s | Largest batch |
|---|---:|---:|
| ASCII | 44.37 | 65536 |
| Unicode | 32.937 | 65536 |
| CSI | 49.848 | 65536 |

The largest-batch result confirms saturated PTY output is now delivered as 64 KiB batches instead of serial 1 KiB read/dispatch cycles.

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
```

Results:

- Unix PTY package build: passed.
- Benchmark project build: passed.
- Demo app build: passed.
- Full test suite: 1232 passed, 16 skipped.
- PTY NativeAOT smoke publish and binary run: passed.
- Demo NativeAOT publish for `osx-arm64`: passed. The macOS linker emitted debug-info module-cache warnings only.
- IO benchmark smoke: passed, report written to `/tmp/royalterminal-io-smoke.md`.

## Follow-Up Work

- Run full 150 MiB benchmark fixtures on target machines and compare against Ghostty/Alacritty/Kitty with identical shell, font/render settings, and hardware.
- Profile VT processing after the read-side stall is removed; the bottleneck should move to VT parse/screen update.
- Consider SIMD only in measured byte-processing paths such as UTF-8 classification, ASCII fast paths, CSI scanning, or marker/search helpers.
- Add CI jobs for PTY NativeAOT smoke and demo NativeAOT publish on each supported RID.
- Audit the optional Pretext pipeline again when the package exposes trim/AOT-safe metadata.
