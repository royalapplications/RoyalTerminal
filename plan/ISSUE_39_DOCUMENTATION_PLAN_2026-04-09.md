# Issue 39 Documentation Plan

Date: 2026-04-09
Issue: https://github.com/royalapplications/RoyalTerminal/issues/39
Reference docs base:
- https://github.com/royalapplications/royalapps-community-externalapps/tree/main/docs
- https://royalapplications.github.io/royalapps-community-externalapps/

## Goal

Implement a professional project documentation site for RoyalTerminal that follows the same high-level pattern as the reference repository:

- `docs/` as the documentation root
- VitePress site with `.vitepress/config.mts`
- curated article-based guide
- branded home page
- local search
- GitHub Pages friendly static output

The documentation must cover the whole RoyalTerminal repository except the `external/ghostty` git submodule.

## Scope Analyzed

Analyzed areas:

- solution structure in `RoyalTerminal.sln`
- all `src/` projects
- all `samples/` projects
- test projects and validation surface in `tests/`
- build and release workflows in `.github/workflows/`
- native build scripts in `scripts/`
- existing public README
- internal architecture/reference material under `skills/royalterminal-development/references/`

Explicitly excluded:

- `external/ghostty/**`

## Project Inventory Summary

The repository is not a single package. It is a terminal platform composed of:

1. UI and integration packages
- `RoyalTerminal.Avalonia`
- `RoyalTerminal.Avalonia.Settings`
- `RoyalTerminal.Avalonia.Rendering.GhosttyInterop`

2. Core terminal/runtime packages
- `RoyalTerminal.Terminal`
- `RoyalTerminal.Terminal.Services.Contracts`
- `RoyalTerminal.Terminal.Services`
- `RoyalTerminal.Unicode`

3. VT processor packages
- `RoyalTerminal.Terminal.Vt.Managed`
- `RoyalTerminal.Terminal.Vt.Ghostty`
- `RoyalTerminal.Terminal.Vt.Default`
- `RoyalTerminal.GhosttySharp`

4. Transport and PTY packages
- `RoyalTerminal.Terminal.Pty.Unix`
- `RoyalTerminal.Terminal.Pty.Windows`
- `RoyalTerminal.Terminal.Pty.Platform`
- `RoyalTerminal.Terminal.Transport.Pty`
- `RoyalTerminal.Terminal.Transport.Pipe`
- `RoyalTerminal.Terminal.Transport.Raw`
- `RoyalTerminal.Terminal.Transport.Telnet`
- `RoyalTerminal.Terminal.Transport.Serial`
- `RoyalTerminal.Terminal.Transport.Ssh.Abstractions`
- `RoyalTerminal.Terminal.Transport.Ssh.SshNet`
- `RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent`

5. Rendering packages
- `RoyalTerminal.Rendering.Contracts`
- `RoyalTerminal.Rendering.Text`
- `RoyalTerminal.Rendering.Skia`
- `RoyalTerminal.Rendering.Interop.Ghostty`
- `RoyalTerminal.Rendering.Interop.Ghostty.Skia`

6. Native runtime asset packages
- `RoyalTerminal.GhosttySharp.Native.OSX`
- `RoyalTerminal.GhosttySharp.Native.Win64`
- `RoyalTerminal.GhosttySharp.Native.Linux64`

7. Samples and validation apps
- `samples/RoyalTerminal.Demo`
- `samples/RoyalTerminal.ControlCatalog`
- `samples/RoyalTerminal.MacNativeTabbed`
- `tests/RoyalTerminal.Tests`
- `tests/RoyalTerminal.IntegrationTests`
- `tests/RoyalTerminal.Benchmarks`
- `tests/RoyalTerminal.PtyHarness`

## Documentation Gaps Identified

The repository currently has a strong top-level `README.md`, but it does not yet provide:

- a dedicated documentation site
- a clear getting-started flow for package selection
- a structured explanation of architecture boundaries
- package-by-package guidance
- dedicated transport documentation
- dedicated VT mode and fallback documentation
- dedicated rendering/native runtime documentation
- sample-app walkthroughs
- build, validation, and troubleshooting guidance in a publishable docs format

## Documentation Information Architecture

The site should stay article-driven, similar to the reference repo, but expanded for RoyalTerminal’s broader surface area.

Planned articles:

1. `Getting Started`
- what RoyalTerminal is
- package selection
- minimal Avalonia setup
- first `TerminalControl`
- first PTY session

2. `Architecture`
- layer boundaries
- `TerminalControl`
- session service
- VT selection
- rendering split
- native asset packages

3. `Package Guide`
- package matrix
- when to use each package
- recommended package sets by scenario

4. `Avalonia Control`
- `TerminalControl`
- theming
- selection, search, capture, snapshot export
- settings panel package

5. `Transport Sessions`
- PTY, pipe, SSH, raw TCP, Telnet, serial
- options model
- security and host key validation
- profile persistence

6. `VT Processors And Modes`
- managed vs native VT
- `Auto`, `Managed`, `Native`
- demo mode mapping and fallback behavior

7. `Rendering And Native Runtime`
- Skia rendering path
- Ghostty renderer interop path
- native library packaging and RIDs
- text shaping and diagnostics

8. `Samples And Tooling`
- Avalonia demo
- control catalog
- macOS native tabbed sample
- benchmarks and harnesses

9. `Build, Test, And Release`
- prerequisites
- native build scripts
- managed build/test commands
- CI/release model

10. `Troubleshooting`
- native asset resolution
- VT availability/fallback confusion
- transport/session issues
- build environment issues

## Implementation Plan

1. Create the VitePress site shell under `docs/`
- mirror the reference repo pattern:
  - `docs/.vitepress/config.mts`
  - `docs/.vitepress/theme/index.ts`
  - `docs/.vitepress/theme/custom.css`
  - `docs/index.md`
  - `docs/articles/*.md`
  - `docs/public/assets/*`

2. Establish RoyalTerminal-specific branding
- reuse the organization brand treatment style from the reference repo
- adapt colors and hero copy to terminal/runtime positioning
- keep local search and GitHub links

3. Author the article set
- convert the current README into a more navigable set of pages
- integrate validated details from source, workflows, and internal repo references
- include concise code examples for common tasks

4. Add docs tooling
- create a minimal root `package.json` for VitePress scripts
- ensure local development and static build commands exist

5. Add GitHub Pages deployment workflow
- build the docs site on pushes to `main`
- publish the static output from VitePress

6. Validate locally
- install Node dependencies
- run the documentation build
- fix any broken links, front matter, or config issues

## Deliverables

- new `docs/` site structure
- article-based documentation set
- VitePress configuration and theme overrides
- Node package manifest for docs scripts
- GitHub Pages deployment workflow
- updated README link to the new docs site if useful

## Non-Goals

- generating a full API reference from XML docs in this pass
- documenting the `external/ghostty` submodule internals
- changing runtime behavior or product architecture

## Validation

Minimum validation for this work:

- VitePress dependency install succeeds
- `npm run docs:build` succeeds
- navigation and sidebar entries resolve to real pages
- written content matches actual package names, transport IDs, and mode names in source

