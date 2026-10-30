# Experimental workspace validation

This records the Linux workspace checks performed on 2026-10-30 with .NET SDK
10.0.403, alongside commands for reproducing them. Read it with the
[implementation specification](rex-experimental-implementation.md). Results are
local observations; the [experiment workflow](../../.github/workflows/multiplexing-experiment.yml)
defines additional CI coverage whose execution must be checked separately.

## Observed results

| Suite | Passed | Skipped | Failed | Interpretation |
| --- | ---: | ---: | ---: | --- |
| Existing `RoyalTerminal.Tests` | 7,552 | 728 | 0 | All executed cases passed; native and platform guards account for skips |
| Existing `RoyalTerminal.IntegrationTests` | 22 | 280 | 0 | All 280 skipped cases report that `libghostty-vt` is unavailable |
| Existing `RoyalTerminal.HistoryTests` | 25 | 0 | 20 | Managed cases passed; every failure requires the unavailable native VT backend |
| Experiment broker | 30 | 0 | 0 | Includes an actual child process surviving client detachment |
| Experiment inspection | 17 | 0 | 0 | Canonical capture, resize, lifecycle, and response ownership |
| Experiment automation | 87 | 0 | 0 | Actions, schemas, configuration, keys, and target resolution |
| Experiment HTTP/WebSocket client and CLI | 56 | 0 | 0 | Protocol, authentication, selectors, configuration, and actions |
| Experiment Avalonia desktop | 8 | 0 | 0 | Headless controls and view models using the managed VT engine |
| Experiment host | 37 | 0 | 0 | Authenticated HTTP/WebSocket flows, revocation races, strict schemas, actions, persistence, and request bounds |
| **Experiment .NET total** | **235** | **0** | **0** | All six experiment suites passed |
| Browser end-to-end | 5 | 0 | 0 | Real Chromium, PTY processes, input, sharing, geometry, navigation, and custom key actions |
| Browser Node cases | 8 | 0 | 0 | Protocol, layout, Unicode input, key modes, chords, and filename safety |

The final host build completed with zero warnings and errors. All eight Node
cases also passed with `node --test --test-isolation=none --test-reporter=tap
browser-tests/*.test.mjs` on Node 24.22.1; the default runner in this environment
reported the two passing test files as its summary units. Browser flows ran
against the built host with Chromium for Testing 156.0.8078.80 supplied through
`PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH`; the pinned Playwright runner was 1.64.1.
The default Playwright browser download failed in this environment, so the local
run used the official Chrome for Testing distribution. CI is configured to use
Playwright's normal Chromium installation. Browser coverage includes real shell
input, split/rename/zoom, explicit detach and fresh-client replay, read-only
mobile sharing and revocation, deferred IME input with parser-response suppression,
held authoritative resize events, preserved live views across window navigation,
and configured context-aware key actions.

### Native prerequisite failures

Ghostty source fixtures were initialized at the repository's pinned submodule
revision. The `libghostty-vt` runtime was not built, and no environment override
was used to disable native availability checks. The history failures distinguish
an unavailable prerequisite from a managed implementation regression:

- 19 native history cases fail with: `Build the native library before running history acceptance tests.`
- `TerminalHistoryControlTests.ControlForwardsCaptureUnderItsLock(preference: Native)`
  fails with: `Native VT processor was requested but no native VT provider is available.`

These failures do not establish whether the native acceptance tests would pass
after the runtime is built. Native backend sign-off remains with the existing
native build/test pipeline. Avalonia Headless tests do not establish desktop
windowing, GPU rendering, or interactive platform UI coverage.

### Initial run and correction

The first full regression attempt had 16 subprocess-launch failures because
nested tests could not find `dotnet` on `PATH`, plus one PTY ready-marker timeout.
After adding the SDK directory to `PATH`, the complete rerun passed all 7,552
executed cases in approximately 2 minutes 38 seconds. The PTY timeout did not
recur. No repository assertions were changed for the rerun.

## Reproduce the managed checks

On a normal development machine, install .NET 10 and ensure `dotnet` is on
`PATH`. The validation workspace used this explicit setup; setting only the
absolute path of the outer command is insufficient for tests that spawn another
`dotnet` process:

```sh
export PATH="/tmp/royalterminal-dotnet:$PATH"
export DOTNET_CLI_HOME=/tmp/royalterminal-dotnet-home
export NUGET_PACKAGES=/tmp/royalterminal-nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1

git submodule update --init --recursive
dotnet --version
```

From the repository root, run the five focused non-server suites:

```sh
for suite in RoyalTerminal.Multiplexing.Tests \
  RoyalTerminal.Multiplexing.InspectionTests \
  RoyalTerminal.Multiplexing.AutomationTests \
  RoyalTerminal.Multiplexing.ClientTests \
  RoyalTerminal.Multiplexing.DesktopTests; do
  dotnet test "tests/${suite}/${suite}.csproj" -c Release \
    -m:1 -nr:false -p:UseSharedCompilation=false \
    --logger "trx;LogFileName=${suite}.trx" \
    --results-directory /tmp/royalterminal-regression-results
done
```

The existing regression checks use the same SDK environment and build settings:

```sh
for suite in RoyalTerminal.Tests RoyalTerminal.IntegrationTests RoyalTerminal.HistoryTests; do
  dotnet test "tests/${suite}/${suite}.csproj" -c Release \
    -m:1 -nr:false -p:UseSharedCompilation=false \
    --logger "trx;LogFileName=${suite}.trx" \
    --results-directory /tmp/royalterminal-regression-results
done
```

The final successful main-suite run reused the compiled Release output with
`--no-build` and added `--blame-hang`, `--blame-hang-timeout 3m`, and
`--blame-hang-dump-type none`. The history command exits nonzero until its native
prerequisite is supplied; the result table above preserves that failure.
TRX files and captured command logs were retained under
`/tmp/royalterminal-regression-results` and `/tmp/royalterminal-*.log` for this run.

## Host and browser commands

The sample host and desktop use managed VT. Build the pinned browser assets
before building the host, so its output directory contains the matching assets:

```sh
npm --prefix samples/RoyalTerminal.Multiplexing.Server ci
npm --prefix samples/RoyalTerminal.Multiplexing.Server run build
dotnet build samples/RoyalTerminal.Multiplexing.Server -c Release -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/RoyalTerminal.Multiplexing.ServerTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false
npm --prefix samples/RoyalTerminal.Multiplexing.Server test
```

Then run Chromium flows from the server sample directory:

```sh
cd samples/RoyalTerminal.Multiplexing.Server
npx playwright install --with-deps chromium
npx playwright test --reporter=line,html
```

Playwright starts an isolated loopback host on port 7331 with the explicit test
credential in its configuration. Keep that port free so the tests can start
their own isolated host. Linux headless execution needs fontconfig/freetype/fonts;
macOS PTY tests require command-line developer tools for the existing spawn helper.
The [server README](../../samples/RoyalTerminal.Multiplexing.Server/README.md)
describes intentionally selected external-host and browser-executable options.

## CI and evidence boundaries

The dedicated experiment workflow is **configured**, not a record of local
cross-platform execution. It defines all six experiment suites on Ubuntu, macOS,
and Windows, plus an Ubuntu browser job with `npm ci`, asset build, browser unit
tests, Chromium installation with system dependencies, Playwright flows, and
retained test artifacts. Existing release workflows were not changed.

The workflow passed actionlint 1.7.12. The .NET solution parser listed all 13
experiment projects in the intended source, sample, and test folders. No macOS
or Windows run, native Ghostty acceptance run, production deployment, or server
restart recovery is established by these local results. Review the actual CI
runs and the implementation specification's remaining product limits before
expanding those claims.
