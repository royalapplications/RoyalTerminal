---
title: Package Guide
---

# Package Guide

RoyalTerminal is published as a family of packages so you can compose the exact runtime you need.

## Recommended package sets

| Goal | Package set |
| --- | --- |
| Managed Avalonia terminal | `RoyalApps.RoyalTerminal.Avalonia` |
| Avalonia terminal plus reusable settings UI | `RoyalApps.RoyalTerminal.Avalonia`, `RoyalApps.RoyalTerminal.Avalonia.Settings` |
| Avalonia terminal with native Ghostty VT available | `RoyalApps.RoyalTerminal.Avalonia`, `RoyalApps.RoyalTerminal.Terminal.Vt.Ghostty`; RID-aware restore/publish selects the matching `RoyalApps.RoyalTerminal.GhosttySharp.Native.*` package |
| Product terminal workflow state | `RoyalApps.RoyalTerminal.Terminal` for workspace documents, split panes, shell integration events, bootstrap scripts, command history, stores, snippets, and suggestion providers |
| Custom transport/profile orchestration without Avalonia | `RoyalApps.RoyalTerminal.Terminal`, `RoyalApps.RoyalTerminal.Terminal.Services`, selected `RoyalApps.RoyalTerminal.Terminal.Transport.*` packages |
| Shader source models and compatibility translation without Avalonia or Skia | `RoyalApps.RoyalTerminal.Shaders` |
| Custom rendering integration | `RoyalApps.RoyalTerminal.Rendering.Contracts`, `RoyalApps.RoyalTerminal.Rendering.Skia`, optional `RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty*` |

## Deep dive guides

| Topic | Article |
| --- | --- |
| Hosting the control, input, selection, capture, and Avalonia GPU interop | [Embedding In Avalonia](/articles/avalonia-control) |
| Session documents, settings panels, themes, capture files, and profile stores | [Sessions, Profiles, And Settings](/articles/sessions-profiles-and-settings) |
| Workspace documents, stores, serializer normalization, and sample startup restore | [Workspace Restore](/articles/workspace-restore) |
| Pane document trees, split ratios, runtime focus/resize behavior, and active-pane features | [Split Panes](/articles/split-panes) |
| OSC 7/OSC 133 event parsing and control-level shell metadata relay | [Shell Integration](/articles/shell-integration) |
| Command history persistence, capture, retention, and suggestions | [Command History And Suggestions](/articles/command-history-and-suggestions) |
| Demo shell titlebar, native menus, settings overlay, and product startup behavior | [Demo Product Shell](/articles/demo-product-shell) |
| RoyalTerminal JSON, asciicast v3, and pluggable recording formats | [Capture Formats](/articles/capture-formats) |
| User-configurable regex text highlighting and persisted highlight rules | [Regex Text Highlighting](/articles/text-highlighting) |
| PTY, pipe, SSH, raw TCP, Telnet, serial, trust policy, and secret handling | [Transports And Remote Access](/articles/transports) |
| Screen state, endpoint contracts, VT processors, input encoding, and Unicode | [Terminal Engine And Screen State](/articles/vt-modes) |
| Rendering contracts, shaping, Skia, and Ghostty renderer interop | [Rendering, Text, And Graphics](/articles/rendering-native) |
| Framebuffer shader architecture, application, and source compatibility | [Shader Support](/articles/shaders) |
| High-level and low-level Ghostty wrapper layers | [Ghostty Integration](/articles/ghostty-integration) |

## API reference

Use [API Reference](/api/) when you need exact public types, members, and XML-commented contracts for the managed RoyalTerminal packages.

The API section is generated from the packable managed libraries under `src/` and grouped the same way the package family is organized in this guide. Native runtime asset packages remain documented here because they ship binaries and MSBuild targets rather than managed public APIs.

## UI and host packages

| Package | Responsibility |
| --- | --- |
| `RoyalApps.RoyalTerminal.Avalonia` | Backend-neutral Avalonia terminal control, presentation services, scrolling, input adaptation, regex text highlighting, and default session composition. |
| `RoyalApps.RoyalTerminal.Avalonia.Settings` | Reusable settings panel controls and state for session, connection, terminal, appearance, regex highlighting, SSH, and logging categories. |
| `RoyalApps.RoyalTerminal.Avalonia.Rendering.GhosttyInterop` | Avalonia-specific render-target acquisition and draw-loop adapters for Ghostty renderer interop. |

## Core model and orchestration packages

| Package | Responsibility |
| --- | --- |
| `RoyalApps.RoyalTerminal.Terminal` | Core contracts, terminal screen model, transport option records, themes, regex highlight profile settings, capture/snapshot contracts, pluggable capture formats, shell profiles, profile persistence, workspace/pane documents, shell integration events/bootstrap scripts, command history, profile snippets, suggestion providers, and SSH support contracts. |
| `RoyalApps.RoyalTerminal.Terminal.Services.Contracts` | Contracts for terminal session lifecycle services. |
| `RoyalApps.RoyalTerminal.Terminal.Services` | The default `TerminalSessionService` implementation. |
| `RoyalApps.RoyalTerminal.Unicode` | Deterministic Unicode width helpers used by the terminal stack. |
| `RoyalApps.RoyalTerminal.Sixel` | Reusable managed sixel decoder and image payload model used by managed VT graphics support. |

## VT packages

| Package | Responsibility |
| --- | --- |
| `RoyalApps.RoyalTerminal.Terminal.Vt.Managed` | Managed `BasicVtProcessor` implementation. |
| `RoyalApps.RoyalTerminal.Terminal.Vt.Ghostty` | Native `GhosttyVtProcessor` over the official `libghostty-vt` API. |
| `RoyalApps.RoyalTerminal.Terminal.Vt.Default` | `DefaultVtProcessorFactory` with managed fallback and optional native providers. |
| `RoyalApps.RoyalTerminal.GhosttySharp` | Managed Ghostty VT bindings and wrappers. Its package-level `runtime.json` selects the matching native asset package for RID-aware restores. |

## Transport and PTY packages

| Package | Responsibility |
| --- | --- |
| `RoyalApps.RoyalTerminal.Terminal.Pty.Unix` | Unix `forkpty` implementation. |
| `RoyalApps.RoyalTerminal.Terminal.Pty.Windows` | Windows ConPTY implementation. |
| `RoyalApps.RoyalTerminal.Terminal.Pty.Platform` | Platform-selecting PTY factory over Unix and Windows implementations. |
| `RoyalApps.RoyalTerminal.Terminal.Transport.Pty` | PTY transport provider and wrapper. |
| `RoyalApps.RoyalTerminal.Terminal.Transport.Pipe` | Process pipe transport provider. |
| `RoyalApps.RoyalTerminal.Terminal.Transport.Raw` | Raw TCP transport provider. |
| `RoyalApps.RoyalTerminal.Terminal.Transport.Telnet` | Telnet transport provider with negotiation support. |
| `RoyalApps.RoyalTerminal.Terminal.Transport.Serial` | Serial line transport provider. |
| `RoyalApps.RoyalTerminal.Terminal.Transport.Ssh.Abstractions` | SSH host-key validation abstractions. |
| `RoyalApps.RoyalTerminal.Terminal.Transport.Ssh.SshNet` | SSH transport provider implemented with SSH.NET. |
| `RoyalApps.RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent` | Optional SSH.NET agent authentication contributor. |

## Rendering packages

| Package | Responsibility |
| --- | --- |
| `RoyalApps.RoyalTerminal.Rendering.Contracts` | Backend-agnostic GPU rendering contracts and validation helpers. |
| `RoyalApps.RoyalTerminal.Rendering.Text` | HarfBuzz-backed shaping, font fallback, and diagnostics primitives. |
| `RoyalApps.RoyalTerminal.Shaders` | Dependency-free shader source models plus Ghostty/Shadertoy and Windows Terminal translation into Skia Runtime Effect source. |
| `RoyalApps.RoyalTerminal.Rendering.Skia` | CPU Skia terminal renderer, regex text highlighting engine, glyph cache, and framebuffer shader post-processing. |
| `RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty` | Managed interop wrappers for `ghostty-renderer-capi`. Its package-level `runtime.json` selects the matching native asset package for RID-aware restores. |
| `RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty.Skia` | Skia bridge around Ghostty renderer interop with fallback support. |

## Native asset packages

| Package | Runtime payload |
| --- | --- |
| `RoyalApps.RoyalTerminal.GhosttySharp.Native.OSX` | `libghostty-vt.dylib` and `libghostty-renderer-capi.dylib` for macOS x64 and arm64 |
| `RoyalApps.RoyalTerminal.GhosttySharp.Native.Win64` | `ghostty-vt.dll` and `ghostty-renderer-capi.dll` for Windows x64 and arm64 |
| `RoyalApps.RoyalTerminal.GhosttySharp.Native.Linux64` | `libghostty-vt.so` and `libghostty-renderer-capi.so` for Linux x64 and arm64 |

These packages are normally selected through the `runtime.json` files in
`RoyalApps.RoyalTerminal.GhosttySharp` and `RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty`.
Restore or publish with a concrete RID, for example `dotnet publish -r osx-arm64`,
to let NuGet resolve only the native package for that target.

## Sample and validation projects

| Project | Purpose |
| --- | --- |
| `samples/RoyalTerminal.Demo` | End-user style Avalonia sample with tabs, split panes, workspace restore, command-history suggestions, settings, profiles, logging, selectable-format capture/replay, search, titlebar/native-menu integration, and diagnostics. |
| `samples/RoyalTerminal.WinFormsHost` | Windows Forms interop sample using `Avalonia.Win32.Interoperability` and `TerminalControl.Padding`. |
| `samples/RoyalTerminal.ControlCatalog` | Terminal validation, rendering gallery, TUI parity, and interactive scenario catalog. |
| `samples/RoyalTerminal.MacNativeTabbed` | Native macOS SwiftUI/GhosttyKit sample outside the managed RoyalTerminal surface. |
| `tests/RoyalTerminal.Tests` | Unit, headless UI, renderer, packaging, and integration boundary tests. |
| `tests/RoyalTerminal.IntegrationTests` | VT, parser, paste, and SSH integration tests. |
| `tests/RoyalTerminal.Benchmarks` | Benchmark harness for performance baselines. |
| `tests/RoyalTerminal.PtyHarness` | PTY harness support tooling. |

## Package selection advice

Use `RoyalApps.RoyalTerminal.Avalonia` as the entry package unless you have a clear reason to build lower in the stack.

Add packages incrementally when you need:

- native Ghostty VT fidelity
- richer settings/profile UI
- explicit SSH agent auth
- custom renderer integration
- standalone transport or profile orchestration without Avalonia

For most applications, the wrong direction is over-assembling the full package graph up front. Start with the control and add only the specialized packages you actually need.
