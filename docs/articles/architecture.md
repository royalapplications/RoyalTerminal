---
title: Architecture
---

# Architecture

RoyalTerminal is organized as layered packages that keep UI concerns, terminal contracts, transport runtime, VT processing, rendering, and native asset distribution separate.

## Layer map

| Layer | Responsibility | Representative packages |
| --- | --- | --- |
| UI | Avalonia control surface, input adaptation, scrolling, selection, capture/replay | `RoyalApps.RoyalTerminal.Avalonia`, `RoyalApps.RoyalTerminal.Avalonia.Settings` |
| Presentation and orchestration | Sample app view model/controller coordination | `samples/RoyalTerminal.Demo` |
| Domain contracts | Terminal model, transport abstractions, profiles, themes, snapshot/capture contracts | `RoyalApps.RoyalTerminal.Terminal`, `RoyalApps.RoyalTerminal.Terminal.Services.Contracts` |
| Runtime implementations | Session service, PTY, transports, VT engines, rendering, SSH adapters | `RoyalApps.RoyalTerminal.Terminal.Services`, `RoyalApps.RoyalTerminal.Terminal.Transport.*`, `RoyalApps.RoyalTerminal.Terminal.Vt.*`, `RoyalApps.RoyalTerminal.Rendering.*` |
| Native interop and assets | Ghostty VT bindings, renderer interop, OS-specific runtime binaries | `RoyalApps.RoyalTerminal.GhosttySharp`, `RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty*`, `RoyalApps.RoyalTerminal.GhosttySharp.Native.*` |

## Core runtime flow

The normal Avalonia-hosted flow is:

1. `TerminalControl` creates or updates its `IVtProcessor` according to `VtProcessorPreference`.
2. `TerminalControl.StartSessionAsync(...)` receives an `ITerminalTransportOptions` instance.
3. `ITerminalTransportFactory` resolves a concrete transport from `TransportId`.
4. `TerminalSessionService` wires VT callbacks before the transport starts.
5. Transport output feeds the active VT processor, which mutates `TerminalScreen`.
6. The Skia renderer reads terminal state and paints the Avalonia surface.

This separation is why the control can stay backend-neutral while transports, VT engines, and renderers remain replaceable.

## Key boundaries

The repository guardrails are consistent across code and tests:

- UI packages do not own transport policy.
- Domain packages do not depend on Avalonia types.
- Native interop stays isolated to explicit Ghostty and renderer packages.
- Demo-specific orchestration stays in `samples/`, not in reusable packages.

Important anchor types:

- `TerminalControl`
- `TerminalSessionService`
- `CompositeTerminalTransportFactory`
- `DefaultVtProcessorFactory`
- `BasicVtProcessor`
- `GhosttyVtProcessor`
- `SkiaTerminalRenderer`

## Session architecture

`TerminalSessionService` is the session coordinator for:

- active endpoint attachment
- active transport lifetime
- VT callback wiring
- input routing
- mode-source selection
- PTY compatibility paths

Input routing order is deterministic:

1. endpoint input sink
2. active transport
3. legacy direct PTY fallback

Mode-source routing is also deterministic:

1. endpoint mode source
2. VT processor mode bridge

That consistency matters for keyboard encoding, bracketed paste, mouse reporting, and session teardown.

## VT selection architecture

`DefaultVtProcessorFactory` implements the processor selection policy:

- `Managed`: always create `BasicVtProcessor`
- `Auto`: use the first working native provider, otherwise fall back to `BasicVtProcessor`
- `Native`: require a native provider and throw if none can create a processor

The default native provider in this repository is `GhosttyVtProcessorProvider`, which wraps the official `libghostty-vt` surface through `RoyalTerminal.GhosttySharp`.

## Rendering architecture

The rendering stack is intentionally split:

- `RoyalApps.RoyalTerminal.Rendering.Text` handles shaping and font fallback
- `RoyalApps.RoyalTerminal.Rendering.Skia` handles the CPU terminal renderer
- `RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty` and `RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty.Skia` provide optional Ghostty renderer integration
- `RoyalApps.RoyalTerminal.Avalonia.Rendering.GhosttyInterop` acquires Avalonia render targets and GPU texture handles

This lets consumers adopt only the pieces they need instead of taking a single monolithic renderer package.

## Sample architecture

The main sample app keeps product-style orchestration outside the reusable packages:

- `MainWindowViewModel` exposes commands, interactions, transport settings, logging, capture, replay, and search state
- `MainWindowController` performs runtime wiring, mode fallback, sample tabs, file pickers, clipboard interaction, and settings/profile synchronization

The separate `RoyalTerminal.MacNativeTabbed` sample demonstrates a different boundary: it hosts GhosttyKit directly and intentionally stays outside the managed `RoyalTerminal.GhosttySharp` surface.

## What is intentionally out of scope here

This documentation site covers the repository outside the `external/ghostty` submodule. RoyalTerminal consumes that submodule for native builds, but this guide does not document Ghostty internals.
