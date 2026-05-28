# Build Test Validation

## Table Of Contents

- [Prerequisites](#prerequisites)
- [Bootstrap](#bootstrap)
- [Build Commands](#build-commands)
- [Test Commands](#test-commands)
- [Native Direct Build Commands](#native-direct-build-commands)
- [Integration And Harness Commands](#integration-and-harness-commands)
- [Validation Matrix](#validation-matrix)
- [Failure Triage Guide](#failure-triage-guide)

## Prerequisites

Required toolchain:
- .NET 10 SDK
- Zig 0.15.2+
- initialized Ghostty submodule

Optional but commonly required:
- SSH test endpoint credentials for integration tests
- platform-native build deps for Ghostty submodule (Xcode/GTK stack as applicable)

## Bootstrap

```bash
git submodule update --init --recursive
```

Verify tool versions:
```bash
dotnet --version
zig version
```

## Build Commands

Build native + managed baseline:

```bash
# macOS/Linux
bash scripts/build-native.sh --release

# Windows
pwsh scripts/build-native.ps1 -Release

# VT utility native path (macOS/Linux)
bash scripts/run-integration-tests.sh               # build libghostty-vt + run integration tests
bash scripts/run-integration-tests.sh --skip-build  # optional test-only rerun

# Managed solution
dotnet build RoyalTerminal.sln -c Release
```

Note:
- `scripts/build-native.sh` currently writes Linux artifacts to `linux-x64` RID paths.

Run demo:
```bash
dotnet run --project samples/RoyalTerminal.Demo
```

Run demo with rendering diagnostics toggles:
```bash
ROYALTERMINAL_DISABLE_TEXT_SHAPING=1 \
ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS=1 \
dotnet run --project samples/RoyalTerminal.Demo
```

## Test Commands

Full pass:
```bash
dotnet test RoyalTerminal.sln -c Release
```

Transport + SSH/security subset:
```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalTransportFactoryTests|PtyTerminalTransportTests|PipeTerminalTransportTests|TerminalSessionServiceTransportTests|SshCredentialProvidersTests|SshHostKeyValidationTests|KnownHostsSshHostKeyValidatorTests|SshNetTerminalTransportSecurityTests|SshNetAgentAuthenticationMethodContributorTests"
```

VT/mode/input subset:
```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalInputAdapterTests|TerminalModeResolverTests|TerminalControlTests|MainWindowControllerModeStartupTests|MainWindowViewModelFlowTests|TerminalMouseProtocolTests"
```

Rendering/native interop subset:
```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "RenderingInteropTests|RenderingSkiaInteropTests|RenderingAvaloniaAdapterTests|RenderingContractsTests|GhosttyComponentTests|PackageBoundaryTests|WindowsArm64NativePackagingTests"
```

## Native Direct Build Commands

Upstream Ghostty build:
```bash
cd external/ghostty
zig build -Doptimize=ReleaseFast -Dapp-runtime=none
```

Windows x64 release/debugging build from the repository root:
```powershell
.\scripts\build-native.ps1 -Arch x64 -Release
```

Direct Ghostty build from `external/ghostty`:
```powershell
zig build -Doptimize=ReleaseFast -Dapp-runtime=none -Dtarget=x86_64-windows-msvc
```

Renderer C API:
```bash
cd native/ghostty-renderer-capi
bash build.sh release
bash build.sh test
```

Use direct builds for component-level debugging; use top-level `scripts/build-native.sh` / `scripts/build-native.ps1` for full packaging sync.
`libghostty-vt` is produced by the same upstream Ghostty build graph and is also validated by the dedicated integration/validation scripts (`scripts/run-integration-tests.sh`, `scripts/validate-macos.sh`).

## Integration And Harness Commands

SSH integration tests:
```bash
ROYALTERMINAL_IT_SSH_HOST=127.0.0.1 \
ROYALTERMINAL_IT_SSH_PORT=22 \
ROYALTERMINAL_IT_SSH_USERNAME=test-user \
ROYALTERMINAL_IT_SSH_PASSWORD=secret \
ROYALTERMINAL_IT_SSH_HOST_KEY_SHA256=SHA256:your-host-key-fingerprint \
dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj -c Release --filter "SshTransportIntegrationTests"
```

Optional private key input:
- `ROYALTERMINAL_IT_SSH_PRIVATE_KEY` (PEM string or file path)

Integration sweep script:
```bash
bash scripts/run-integration-tests.sh
bash scripts/run-integration-tests.sh --skip-build
bash scripts/run-integration-tests.sh --verbose
```

Ncurses harness:
```bash
RT_HARNESS_LOG=/tmp/rt-harness.log \
RT_HARNESS_TIMEOUT_SEC=300 \
TERM=xterm-256color \
python3 tests/RoyalTerminal.Tests/Fixtures/NcursesHarness.py
```

Watch harness log:
```bash
tail -f /tmp/rt-harness.log
```

## Validation Matrix

| Change Area | Minimum Required Commands |
|---|---|
| UI/view-model only | managed build + focused view-model/control tests |
| transport contract/provider/session | transport subset + full tests |
| SSH auth/trust | SSH unit subset + SSH integration subset + full tests |
| VT processor/factory/input mode | VT subset + integration VT tests + full tests |
| renderer interop/native loader | native build scripts + rendering subset + full tests |
| package/runtime targets | native builds + packaging tests + full tests |
| shared contracts (`RoyalTerminal.Terminal`) | full solution tests (non-negotiable) |

## Failure Triage Guide

| Failure Type | First Action |
|---|---|
| compile error in native bindings | confirm native headers/symbol changes and managed binding signatures |
| missing native library at runtime | run native build scripts and verify runtime asset copy targets |
| transport regression | run transport subset and inspect provider matching + session lifecycle |
| mode/input regression | run VT subset and inspect mode source and key encoder behavior |
| flaky integration SSH | verify env vars, host key fingerprint, and endpoint reachability |

Benchmark command (optional baseline capture):
```bash
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --output /tmp/royalterminal-render-baseline.md
```

## Code Examples

### Transport change validation recipe

```bash
dotnet build RoyalTerminal.sln -c Release

dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalTransportFactoryTests|PtyTerminalTransportTests|PipeTerminalTransportTests|TerminalSessionServiceTransportTests"

dotnet test RoyalTerminal.sln -c Release
```

### VT/mode change validation recipe

```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalInputAdapterTests|TerminalModeResolverTests|MainWindowControllerModeStartupTests|TerminalControlTests"

dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj -c Release --filter "TerminalNativeTests|KeyEncoderTests|OscParserTests|SgrParserTests"
```

### Native loader change validation recipe

```bash
bash scripts/build-native.sh --clean --release
dotnet build RoyalTerminal.sln -c Release

dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "RenderingInteropTests|PackageBoundaryTests|WindowsArm64NativePackagingTests"
```
