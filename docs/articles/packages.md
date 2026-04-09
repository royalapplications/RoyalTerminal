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
| Avalonia terminal with native Ghostty VT available | `RoyalTerminal.Avalonia`, `RoyalTerminal.Terminal.Vt.Ghostty`, matching `RoyalTerminal.GhosttySharp.Native.*` |
| Custom transport/profile orchestration without Avalonia | `RoyalTerminal.Terminal`, `RoyalTerminal.Terminal.Services`, selected `RoyalTerminal.Terminal.Transport.*` packages |
| Custom rendering integration | `RoyalTerminal.Rendering.Contracts`, `RoyalTerminal.Rendering.Skia`, optional `RoyalTerminal.Rendering.Interop.Ghostty*` |

## UI and host packages

| Package | Responsibility |
| --- | --- |
| `RoyalTerminal.Avalonia` | Backend-neutral Avalonia terminal control, presentation services, scrolling, input adaptation, and default session composition. |
| `RoyalTerminal.Avalonia.Settings` | Reusable settings panel controls and state for session, connection, terminal, appearance, SSH, and logging categories. |
| `RoyalTerminal.Avalonia.Rendering.GhosttyInterop` | Avalonia-specific render-target acquisition and draw-loop adapters for Ghostty renderer interop. |

## Core model and orchestration packages

| Package | Responsibility |
| --- | --- |
| `RoyalTerminal.Terminal` | Core contracts, terminal screen model, transport option records, themes, capture/snapshot contracts, shell profiles, profile persistence, and SSH support contracts. |
| `RoyalTerminal.Terminal.Services.Contracts` | Contracts for terminal session lifecycle services. |
| `RoyalTerminal.Terminal.Services` | The default `TerminalSessionService` implementation. |
| `RoyalTerminal.Unicode` | Deterministic Unicode width helpers used by the terminal stack. |

## VT packages

| Package | Responsibility |
| --- | --- |
| `RoyalTerminal.Terminal.Vt.Managed` | Managed `BasicVtProcessor` implementation. |
| `RoyalTerminal.Terminal.Vt.Ghostty` | Native `GhosttyVtProcessor` over the official `libghostty-vt` API. |
| `RoyalTerminal.Terminal.Vt.Default` | `DefaultVtProcessorFactory` with managed fallback and optional native providers. |
| `RoyalTerminal.GhosttySharp` | Managed Ghostty VT bindings and wrappers. |

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
| `RoyalTerminal.Rendering.Skia` | CPU Skia terminal renderer and glyph cache. |
| `RoyalTerminal.Rendering.Interop.Ghostty` | Managed interop wrappers for `ghostty-renderer-capi`. |
| `RoyalTerminal.Rendering.Interop.Ghostty.Skia` | Skia bridge around Ghostty renderer interop with fallback support. |

## Native asset packages

| Package | Runtime payload |
| --- | --- |
| `RoyalTerminal.GhosttySharp.Native.OSX` | `libghostty-vt.dylib` and `libghostty-renderer-capi.dylib` for macOS x64 and arm64 |
| `RoyalTerminal.GhosttySharp.Native.Win64` | `ghostty-vt.dll` and `ghostty-renderer-capi.dll` for Windows x64 and arm64 |
| `RoyalTerminal.GhosttySharp.Native.Linux64` | `libghostty-vt.so` and `libghostty-renderer-capi.so` for Linux x64 and arm64 |

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
