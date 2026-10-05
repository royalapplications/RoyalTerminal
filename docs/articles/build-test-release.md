---
title: Build, Test, And Release
---

# Build, Test, And Release

RoyalTerminal combines managed .NET packages with native artifacts. A successful end-to-end build requires understanding both sides.

## Prerequisites

Minimum toolchain:

- .NET 10 SDK
- Zig 0.16.0
- initialized git submodules for native builds

Common optional requirements:

- SSH test endpoint credentials for integration tests
- platform-native dependencies needed by Ghostty/native builds

## Dependency compatibility

Dependency versions are centralized in `Directory.Packages.props`. Update related
managed and native packages together, then validate the Release build, isolated
unit batches, required-native integration tests, documentation build, and packaging.

The October 2026 dependency review retains these compatibility constraints:

- Avalonia 12.1.3 and Pretext 0.1.0 use SkiaSharp 3. Keep SkiaSharp at 3.119.4
  and HarfBuzzSharp on the 8.3.1 line, with every native asset on the same version
  as its managed package. SkiaSharp 4 requires a separate rendering migration.
- Avalonia.Headless.XUnit 12.1.3 targets xUnit 3.2.2 extensibility. Keep that test
  framework version while updating the Visual Studio adapter independently.
  xUnit 4 changes extensibility/parallelization APIs and defaults to Microsoft
  Testing Platform v2, which rejects the current VSTest invocation on .NET 10.
- Ghostty remains at the explicit submodule commit tested with RoyalTerminal's
  native overlays and generated ABI checks. Its build requires Zig 0.16.0 exactly;
  updating to Zig 0.17 or a newer upstream development commit requires a coordinated
  native/managed ABI and terminal behavior review.
- VitePress remains on its latest stable 1.6.4 release. The npm overrides update
  Vite and its Vue plugin together so the plugin's peer range supports Vite 8.

ReactiveUI 26 uses `Interaction.Handle` for task-based handling. Observable command
pipelines use `WhenHandled` to keep interaction dispatch tied to subscription.

Useful audit commands:

```bash
dotnet list RoyalTerminal.slnx package --outdated
dotnet list RoyalTerminal.slnx package --vulnerable --include-transitive
npm outdated
npm audit
```

## Bootstrap

Initialize submodules before native builds:

```bash
git submodule update --init --recursive
```

Verify tool versions:

```bash
dotnet --version
zig version
```

## Local build commands

Native plus managed baseline:

```bash
# macOS/Linux
bash scripts/build-native.sh --release

# Windows
pwsh scripts/build-native.ps1 -Release

# Managed solution
dotnet build RoyalTerminal.slnx -c Release
```

Run the demo:

```bash
dotnet run --project samples/RoyalTerminal.Demo
```

Run the demo with rendering diagnostics:

```bash
ROYALTERMINAL_DISABLE_TEXT_SHAPING=1 \
ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS=1 \
dotnet run --project samples/RoyalTerminal.Demo
```

## Test commands

Full solution test pass:

```bash
dotnet test RoyalTerminal.slnx -c Release
```

Focused transport/session validation:

```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalTransportFactoryTests|PtyTerminalTransportTests|PipeTerminalTransportTests|TerminalSessionServiceTransportTests"
```

Focused VT/input validation:

```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "TerminalInputAdapterTests|TerminalModeResolverTests|TerminalControlTests|MainWindowControllerModeStartupTests"
```

Focused renderer/native validation:

```bash
dotnet test tests/RoyalTerminal.Tests/RoyalTerminal.Tests.csproj -c Release --filter "RenderingInteropTests|RenderingSkiaInteropTests|RenderingAvaloniaAdapterTests|RenderingContractsTests|PackageBoundaryTests|WindowsNativePackagingTests"
```

Integration sweep:

```bash
bash scripts/run-integration-tests.sh
```

## SSH integration tests

The SSH integration tests are environment-driven:

```bash
ROYALTERMINAL_IT_SSH_HOST=127.0.0.1 \
ROYALTERMINAL_IT_SSH_PORT=22 \
ROYALTERMINAL_IT_SSH_USERNAME=test-user \
ROYALTERMINAL_IT_SSH_PASSWORD=secret \
ROYALTERMINAL_IT_SSH_HOST_KEY_SHA256=SHA256:your-host-key-fingerprint \
dotnet test tests/RoyalTerminal.IntegrationTests/RoyalTerminal.IntegrationTests.csproj -c Release --filter "SshTransportIntegrationTests"
```

Optional private-key input:

- `ROYALTERMINAL_IT_SSH_PRIVATE_KEY`

## CI shape

The main CI workflow does more than run `dotnet test`:

- builds native artifacts for macOS, Linux, and Windows first
- stages those artifacts into the managed interop/package layout
- runs a managed build on an OS matrix
- runs unit tests and Linux slice-based test splits
- uploads test result artifacts

That structure exists because native asset availability is part of the product contract.

## Release shape

The release workflow is tag-driven and builds native artifacts per platform/architecture before packing or publishing managed outputs.

Release tags must use one of these forms:

```text
vMAJOR.MINOR.PATCH
vMAJOR.MINOR.PATCH-prerelease
```

Examples:

```text
v0.1.24
v0.1.24-alpha.1
```

This is the core release invariant:

- native runtime artifacts are produced first
- required native files are verified for every RID before packing
- managed packages consume the staged artifacts
- every package in `artifacts/*.nupkg` is pushed to NuGet.org explicitly
- the GitHub release is created only after package publishing succeeds

The workflow requires the `NUGET_API_KEY` repository secret. Re-runs continue past an existing package only after verifying that the exact package ID and version are already visible in the NuGet.org feed.

The package version comes from the tag without the leading `v`; the repository `VersionPrefix` remains the local/default development version.

## Documentation release

The docs workflow builds on pull requests and pushes. Deployment is guarded by the repository variable `DOCS_DEPLOY_ENABLED`.

To publish docs:

1. Configure GitHub Pages to use GitHub Actions.
2. Set `DOCS_DEPLOY_ENABLED` to `true`.
3. Push to `main` or run the Docs workflow manually with `workflow_dispatch`.

Pull request runs never deploy.

## Benchmarks

You can capture benchmark baselines with:

```bash
dotnet run --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --output /tmp/royalterminal-render-baseline.md
```

## Documentation site

This docs site lives under `docs/` and uses VitePress. Local commands:

```bash
npm install
npm run docs:dev
npm run docs:build
npm run docs:preview
```
