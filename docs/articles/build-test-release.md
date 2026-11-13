---
title: Build, Test, And Release
---

# Build, Test, And Release

RoyalTerminal combines managed .NET packages with native artifacts. A successful end-to-end build requires understanding both sides.

## Prerequisites

Minimum toolchain:

- .NET 11 SDK and .NET 10 runtime (for testing both target frameworks)
- Zig 0.17.0
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
  native overlays and generated ABI checks. Its build requires Zig 0.17.0 exactly;
  updating the toolchain or upstream development commit requires a coordinated
  native/managed ABI and terminal behavior review.
- The documentation dependencies merged from `main` use VitePress
  2.0.0-alpha.20 with the Vite 8.3.3 override. Keep the lockfile and overrides
  together and validate the generated API documentation and site build when
  updating either dependency.

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

### Stale native libraries in history tests

If native history tests fail at `ghostty_terminal_new` with `InvalidValue` on
both .NET targets, check the native artifacts before changing the bindings.
A loadable DLL can use an older C ABI and lack RoyalTerminal's history exports.
The November 2026 investigation found June DLLs in both Windows runtime folders;
the managed constructor already matched the pinned Ghostty source.

Rebuild the extension library for the architecture of the .NET test process.
The Windows build script defaults to x64; ARM64 hosts need `-Arch arm64`:

```powershell
pwsh -NoProfile -File scripts/build-native.ps1 -Arch arm64 -Release
dotnet build RoyalTerminal.slnx -c Release
dotnet test tests/RoyalTerminal.HistoryTests/RoyalTerminal.HistoryTests.csproj -c Release --no-build
```

Use `-Arch x64` for an x64 process. Rebuild the managed projects after staging
native artifacts so both framework output directories receive the current DLL.
With LLVM installed, `python scripts/verify-history-exports.py
src/RoyalTerminal.GhosttySharp.Native.Win64/runtimes/win-arm64/native` verifies
the required history exports. A plain upstream Ghostty build does not contain
RoyalTerminal's extensions; retain the pinned ABI and use the extension build.

Run the demo:

```bash
dotnet run -f net11.0 --project samples/RoyalTerminal.Demo
```

Run the demo with rendering diagnostics:

```bash
ROYALTERMINAL_DISABLE_TEXT_SHAPING=1 \
ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS=1 \
dotnet run -f net11.0 --project samples/RoyalTerminal.Demo
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

The workflow uses GitHub Actions OIDC and `NuGet/login@v1` to obtain a temporary
NuGet API key immediately before publishing. Only the `pack-and-publish` job has
`id-token: write` permission. The existing push script receives the temporary key
through `NUGET_API_KEY`; no long-lived API key secret is required. Re-runs continue
past an existing package only after verifying that the exact package ID and
version are already visible in the NuGet.org feed.

The package version comes from the tag without the leading `v`; the repository `VersionPrefix` remains the local/default development version.

### nuget.org trusted publishing setup

1. Sign in to [nuget.org](https://www.nuget.org/) with the account that will create
   the policy. This user must own the packages or be an active member of the
   nuget.org organization that owns them.
2. Open your account menu, select **Trusted Publishing**, and add a GitHub policy:

   | Field | Value |
   | --- | --- |
   | Policy name | `RoyalTerminal releases` |
   | Package owner | The nuget.org user or organization that owns the packages |
   | Repository owner | `royalapplications` |
   | Repository | `RoyalTerminal` |
   | Workflow file | `release.yml` (filename only) |
   | Environment | Leave empty; the publishing job does not use a GitHub environment |
   | Package glob | `RoyalApps.RoyalTerminal.*` |
   | Scopes | Push new packages and package versions |

   Allow new packages as well as versions so future packable projects can be
   published under the same prefix. All current package IDs match this glob.
3. In the repository's
   [Actions variables settings](https://github.com/royalapplications/RoyalTerminal/settings/variables/actions),
   create the repository variable `NUGET_USER`. Set it to the **nuget.org username
   of the user who created the policy**, including when the policy belongs to an
   organization. Use the profile username, not an email address, GitHub username,
   or organization name. A username is not a credential, so this is an Actions
   variable rather than a secret.
4. Commit and push the workflow change before creating the next release tag. The
   tagged commit must contain the trusted publishing workflow; re-running a tag
   created before this change still uses the old workflow.
5. Publish the next intended release using a fresh `vMAJOR.MINOR.PATCH` or
   prerelease tag. In the workflow, confirm **NuGet login (trusted publishing)**
   and **Push to NuGet.org** succeed. If nuget.org shows the policy as pending
   activation, complete the first publish within its seven-day activation window
   or reactivate the policy before publishing.
6. After the first successful trusted publish, remove the old `NUGET_API_KEY`
   repository secret if present. Revoke its long-lived key on nuget.org if no
   other publishing workflow uses it.

For troubleshooting, verify the policy owner has access to every package being
pushed, the workflow filename matches, and the environment is empty. If a GitHub
environment is added later, set the same environment in the nuget.org policy.
The temporary key is valid for one hour, so keep login directly before the push.

See [NuGet trusted publishing documentation](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
and the [NuGet login action](https://github.com/NuGet/login).

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
dotnet run -f net11.0 --project tests/RoyalTerminal.Benchmarks/RoyalTerminal.Benchmarks.csproj -c Release -- --output /tmp/royalterminal-render-baseline.md
```

Focused update benchmarks accept `--terminal-update` (managed parser, native
adapter and native core), `--program-status` (OSC 7501 and application
inheritance), and `--history-compression` (row codec and snapshot restoration).
Build first, then run separate revisions serially with identical JIT settings
and alternating order. See the [October performance report](../specs/ghostty-performance-2026-10-09.md)
for workloads, allocation accounting and measured tradeoffs.

## Documentation site

This docs site lives under `docs/` and uses VitePress. Local commands:

```bash
npm install
npm run docs:dev
npm run docs:build
npm run docs:preview
```
