---
title: Package Guide
---

# Package Guide

RoyalTerminal is published as a family of packages so you can compose the exact runtime you need.

## Recommended package sets

| Goal | Package set |
| --- | --- |
| Managed Avalonia terminal | `RoyalTerminal.Avalonia` |
| Avalonia terminal plus reusable settings UI | `RoyalTerminal.Avalonia`, `RoyalTerminal.Avalonia.Settings` |
| Avalonia terminal with native Ghostty VT available | `RoyalTerminal.Avalonia`, `RoyalTerminal.Terminal.Vt.Ghostty`; RID-aware restore/publish selects the matching `RoyalTerminal.GhosttySharp.Native.*` package |
| Custom transport/profile orchestration without Avalonia | `RoyalTerminal.Terminal`, `RoyalTerminal.Terminal.Services`, selected `RoyalTerminal.Terminal.Transport.*` packages |
| Shader source models and compatibility translation without Avalonia or Skia | `RoyalTerminal.Shaders` |
| Custom rendering integration | `RoyalTerminal.Rendering.Contracts`, `RoyalTerminal.Rendering.Skia`, optional `RoyalTerminal.Rendering.Interop.Ghostty*` |

## Deep dive guides

| Topic | Article |
| --- | --- |
| Hosting the control, input, selection, capture, and Avalonia GPU interop | [Embedding In Avalonia](/articles/avalonia-control) |
| Session documents, settings panels, themes, capture files, and profile stores | [Sessions, Profiles, And Settings](/articles/sessions-profiles-and-settings) |
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
| `RoyalTerminal.Avalonia` | Backend-neutral Avalonia terminal control, presentation services, scrolling, input adaptation, regex text highlighting, and default session composition. |
| `RoyalTerminal.Avalonia.Settings` | Reusable settings panel controls and state for session, connection, terminal, appearance, regex highlighting, SSH, and logging categories. |
| `RoyalTerminal.Avalonia.Rendering.GhosttyInterop` | Avalonia-specific render-target acquisition and draw-loop adapters for Ghostty renderer interop. |

## Core model and orchestration packages

| Package | Responsibility |
| --- | --- |
| `RoyalTerminal.Terminal` | Core contracts, terminal screen model, transport option records, themes, regex highlight profile settings, capture/snapshot contracts, shell profiles, profile persistence, and SSH support contracts. |
| `RoyalTerminal.Terminal.Services.Contracts` | Contracts for terminal session lifecycle services. |
| `RoyalTerminal.Terminal.Services` | The default `TerminalSessionService` implementation. |
| `RoyalTerminal.Unicode` | Deterministic Unicode width helpers used by the terminal stack. |

## VT packages

| Package | Responsibility |
| --- | --- |
| `RoyalTerminal.Terminal.Vt.Managed` | Managed `BasicVtProcessor` implementation. |
| `RoyalTerminal.Terminal.Vt.Ghostty` | Native `GhosttyVtProcessor` over the official `libghostty-vt` API. |
| `RoyalTerminal.Terminal.Vt.Default` | `DefaultVtProcessorFactory` with managed fallback and optional native providers. |
| `RoyalTerminal.GhosttySharp` | Managed Ghostty VT bindings and wrappers. Its package-level `runtime.json` selects the matching native asset package for RID-aware restores. |

## Transport and PTY packages

| Package | Responsibility |
| --- | --- |
| `RoyalTerminal.Terminal.Pty.Unix` | Unix `forkpty` implementation. |
| `RoyalTerminal.Terminal.Pty.Windows` | Windows ConPTY implementation. |
| `RoyalTerminal.Terminal.Pty.Platform` | Platform-selecting PTY factory over Unix and Windows implementations. |
| `RoyalTerminal.Terminal.Transport.Pty` | PTY transport provider and wrapper. |
| `RoyalTerminal.Terminal.Transport.Pipe` | Process pipe transport provider. |
| `RoyalTerminal.Terminal.Transport.Raw` | Raw TCP transport provider. |
| `RoyalTerminal.Terminal.Transport.Telnet` | Telnet transport provider with negotiation support. |
| `RoyalTerminal.Terminal.Transport.Serial` | Serial line transport provider. |
| `RoyalTerminal.Terminal.Transport.Ssh.Abstractions` | SSH host-key validation abstractions. |
| `RoyalTerminal.Terminal.Transport.Ssh.SshNet` | SSH transport provider implemented with SSH.NET. |
| `RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent` | Optional SSH.NET agent authentication contributor. |

## Rendering packages

| Package | Responsibility |
| --- | --- |
| `RoyalTerminal.Rendering.Contracts` | Backend-agnostic GPU rendering contracts and validation helpers. |
| `RoyalTerminal.Rendering.Text` | HarfBuzz-backed shaping, font fallback, and diagnostics primitives. |
| `RoyalTerminal.Shaders` | Dependency-free shader source models plus Ghostty/Shadertoy and Windows Terminal translation into Skia Runtime Effect source. |
| `RoyalTerminal.Rendering.Skia` | CPU Skia terminal renderer, regex text highlighting engine, glyph cache, and framebuffer shader post-processing. |
| `RoyalTerminal.Rendering.Interop.Ghostty` | Managed interop wrappers for `ghostty-renderer-capi`. Its package-level `runtime.json` selects the matching native asset package for RID-aware restores. |
| `RoyalTerminal.Rendering.Interop.Ghostty.Skia` | Skia bridge around Ghostty renderer interop with fallback support. |

## Native asset packages

| Package | Runtime payload |
| --- | --- |
| `RoyalTerminal.GhosttySharp.Native.OSX` | `libghostty-vt.dylib` and `libghostty-renderer-capi.dylib` for macOS x64 and arm64 |
| `RoyalTerminal.GhosttySharp.Native.Win64` | `ghostty-vt.dll` and `ghostty-renderer-capi.dll` for Windows x64 and arm64 |
| `RoyalTerminal.GhosttySharp.Native.Linux64` | `libghostty-vt.so` and `libghostty-renderer-capi.so` for Linux x64 and arm64 |

These packages are normally selected through the `runtime.json` files in
`RoyalTerminal.GhosttySharp` and `RoyalTerminal.Rendering.Interop.Ghostty`.
Restore or publish with a concrete RID, for example `dotnet publish -r osx-arm64`,
to let NuGet resolve only the native package for that target.

## Sample and validation projects

| Project | Purpose |
| --- | --- |
| `samples/RoyalTerminal.Demo` | End-user style Avalonia sample with tabs, settings, profiles, logging, capture/replay, search, and diagnostics. |
| `samples/RoyalTerminal.ControlCatalog` | Terminal validation, rendering gallery, TUI parity, and interactive scenario catalog. |
| `samples/RoyalTerminal.MacNativeTabbed` | Native macOS SwiftUI/GhosttyKit sample outside the managed RoyalTerminal surface. |
| `tests/RoyalTerminal.Tests` | Unit, headless UI, renderer, packaging, and integration boundary tests. |
| `tests/RoyalTerminal.IntegrationTests` | VT, parser, paste, and SSH integration tests. |
| `tests/RoyalTerminal.Benchmarks` | Benchmark harness for performance baselines. |
| `tests/RoyalTerminal.PtyHarness` | PTY harness support tooling. |

## Package selection advice

Use `RoyalTerminal.Avalonia` as the entry package unless you have a clear reason to build lower in the stack.

Add packages incrementally when you need:

- native Ghostty VT fidelity
- richer settings/profile UI
- explicit SSH agent auth
- custom renderer integration
- standalone transport or profile orchestration without Avalonia

For most applications, the wrong direction is over-assembling the full package graph up front. Start with the control and add only the specialized packages you actually need.
