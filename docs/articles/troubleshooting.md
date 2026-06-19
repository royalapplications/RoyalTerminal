---
title: Troubleshooting
---

# Troubleshooting

This page focuses on the most common setup and runtime failures in the repository outside the `external/ghostty` submodule.

## Native VT was requested but startup failed

Typical causes:

- `RoyalTerminal.Terminal.Vt.Ghostty` was not referenced
- restore or publish did not use a concrete RID, so `runtime.json` could not select the matching `RoyalTerminal.GhosttySharp.Native.*` package
- native runtime files were not copied or restored correctly
- the current machine architecture does not match the available native binaries

First check:

- package references
- OS/RID match, for example `dotnet publish -r osx-arm64`
- whether `VtProcessorPreference` is `Native` instead of `Auto`

If your host should remain resilient, switch to `Auto` so the managed VT processor can take over when native VT is unavailable.

## Native library load failures

When `libghostty-vt` or `ghostty-renderer-capi` cannot be found:

- run the native build scripts again
- verify the runtime package layout under the expected `runtimes/<rid>/native` paths
- verify CI or local staging copied native files into the interop/runtime locations

The repository treats missing native libraries as a first-class failure mode, not an edge case.

## SSH host-key validation failures

If SSH startup fails immediately:

- verify `ExpectedHostKeyFingerprintSha256`
- verify your OpenSSH `known_hosts` entry if fingerprint pinning is not used
- verify proxy configuration and endpoint reachability
- verify credentials or secret store resolution

If you enable X11 forwarding, the current SSH.NET backend will throw because X11 is not yet supported there.

## PTY startup failures

Common causes:

- ConPTY is unavailable or blocked on the current Windows environment
- shell path or working directory is invalid
- local environment variables are malformed
- the current platform requires different shell profile defaults

When PTY behavior is unclear, compare:

- `DefaultShellProfileCatalog`
- `PtyTransportOptions`
- `DefaultPtyFactory`

## Rendering issues

If glyph shaping or cell rendering looks wrong:

- disable shaping temporarily with `ROYALTERMINAL_DISABLE_TEXT_SHAPING=1`
- enable diagnostics with `ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS=1`
- verify the selected font actually covers the codepoints you are testing
- compare the behavior between managed VT and native VT modes

This helps separate parser issues from shaping or rendering issues.

## Session profile problems

If the settings panel or persisted profiles behave unexpectedly:

- inspect the serialized `TerminalSessionProfilesDocument`
- verify the transport-specific settings block under the profile
- verify secret IDs referenced by SSH profiles still exist in the configured secret store

The demo uses the same persistence model as the shared library surface, so reproducing the issue in the sample app is often a fast debugging step.

## Docs deployment problems

If the documentation site does not publish:

- ensure GitHub Pages is configured to use `GitHub Actions`
- ensure the repo has permission to deploy Pages
- ensure repository variable `DOCS_DEPLOY_ENABLED` is set to `true`
- verify the `base` option stays `/RoyalTerminal/`
- run `npm run docs:build` locally before pushing

If the site builds locally but routes fail in production, the most common cause is an incorrect `base` path for the GitHub Pages repository URL.
