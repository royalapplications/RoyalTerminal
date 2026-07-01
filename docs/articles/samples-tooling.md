---
title: Samples And Tooling
---

# Samples And Tooling

The repository includes multiple samples and validation tools. They are part of the product story, not just incidental demos.

## `RoyalTerminal.Demo`

The main Avalonia demo is the most complete integration example. It includes:

- runtime tabs
- interactive split panes with keyboard focus and resize commands
- workspace restore/save for windows, tabs, pane trees, render mode, working
  directory, profile id, and transport id
- mode switching across `Native VT`, `Managed VT`, and `Rendered (Auto VT)`
- transport forms for PTY, pipe, raw TCP, Telnet, serial, and SSH
- searchable profile launcher backed by persisted session profiles and shell
  discovery
- shell-integrated command history capture and command suggestions
- a tabbed settings flyout
- profile CRUD and default-profile handling
- settings profile actions exposed through the window menu
- session logging and event logging
- capture and replay with selectable RoyalTerminal JSON or asciicast v3 files
- search
- theme presets and generated themes
- framebuffer shader samples
- snapshot copy actions
- hyperlink and Kitty graphics showcase actions
- macOS application menu replacement for About, Preferences, and Quit
- window native menus for shell, edit, view, session, window, and help commands
- compact extended-titlebar command bar with native titlebar hit-test roles

Settings categories in the demo:

- `Session`
- `Connection`
- `Terminal`
- `Appearance`
- `SSH`
- `Logging`

If you want a realistic host application launcher, start here. The reusable shell
implementation lives in `RoyalApps.RoyalTerminal.Avalonia.App`.

The shared shell View menu includes a shader sample action that cycles through built-in post-process effects:

- `Off`
- `CRT Amber`
- `Hue Shift`
- `Transparent Key`
- `Retro Scanlines`
- `Windows Terminal CRT`
- `Ghostty Shadertoy`

The capture persistence behavior is covered in [Capture Formats](/articles/capture-formats). The sample catalog is documented in [Applying Shaders](/articles/shaders-applying#shared-shell-samples). The compatibility details are covered in [Shader Support](/articles/shaders).
Product workflow topics are split into dedicated articles:

- [Workspace Restore](/articles/workspace-restore)
- [Split Panes](/articles/split-panes)
- [Shell Integration](/articles/shell-integration)
- [Command History And Suggestions](/articles/command-history-and-suggestions)
- [Demo Product Shell](/articles/demo-product-shell)

Pane shortcuts in the demo:

| Command | Shortcut |
| --- | --- |
| Split pane right | `Alt+Shift+OemPlus` |
| Split pane down | `Alt+Shift+OemMinus` |
| Focus pane left/right/up/down | `Alt+Arrow` |
| Resize pane left/right/up/down | `Alt+Shift+Arrow` |

Command-history shortcuts in the demo:

| Command | Shortcut |
| --- | --- |
| Open command-history overlay | `Ctrl+Space` |
| Accept selected suggestion | `Ctrl+Enter` |

## `RoyalTerminal.ControlCatalog`

The control catalog is a validation and inspection tool rather than a simple showcase. It groups scenarios into:

- validation scenarios
- visual scenarios
- interactive scenarios

It can run as:

- an interactive CLI menu for manual inspection
- a redirected-output full sweep for automated result capture

Representative coverage areas include VT modes, queries, OSC behavior, Kitty features, rendering galleries, TUI compatibility, and PTY lifecycle validation.

## `RoyalTerminal.MacNativeTabbed`

This sample is intentionally separate from the managed RoyalTerminal surface. It hosts GhosttyKit directly through SwiftUI/AppKit and demonstrates native macOS tabbed hosting.

Use it when you need to understand:

- direct GhosttyKit surface hosting
- native macOS tab management patterns
- how the repo separates managed and native sample surfaces

## Tests

The repository includes several test layers:

| Project | Focus |
| --- | --- |
| `tests/RoyalTerminal.Tests` | unit tests, headless Avalonia tests, rendering tests, transport tests, package boundary tests |
| `tests/RoyalTerminal.IntegrationTests` | VT parser behavior, paste, key encoding, SSH integration |
| `tests/RoyalTerminal.Benchmarks` | performance baselines |
| `tests/RoyalTerminal.PtyHarness` | PTY harness support |

The test names are descriptive and double as subsystem maps. If you need to understand whether a behavior is intentional, read the relevant test file before changing the implementation.

## Internal reference material

The repository also contains a rich internal reference set under `skills/royalterminal-development/references/`. Those files are not the public docs site, but they are extremely useful when you need source-grounded details about:

- architecture guardrails
- transport mappings
- VT behavior
- native packaging
- rendering
- validation recipes

This public site distills that information into a consumer-facing guide while keeping the low-level repo references available for contributors.
