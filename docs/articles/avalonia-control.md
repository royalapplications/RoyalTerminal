---
title: Embedding In Avalonia
---

# Embedding In Avalonia

RoyalTerminal is easiest to understand from the top down: drop `TerminalControl` into an Avalonia view, start a session, and only then decide whether you need custom settings UI, capture tooling, or GPU interop. That is also how the public API is structured. The default path is small, and the lower layers only come into view when you need to customize behavior.

## The default host story

Most applications only need `RoyalTerminal.Avalonia`, a `TerminalControl`, and a session start call:

```xml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:rt="clr-namespace:RoyalTerminal.Avalonia.Controls;assembly=RoyalTerminal.Avalonia">
  <rt:TerminalControl
      x:Name="Terminal"
      Columns="120"
      Rows="36"
      ScrollbackLimit="10000"
      TerminalFontSize="14" />
</Window>
```

```csharp
PtyTransportOptions options = new(
    Command: null,
    WorkingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    Environment: null,
    Dimensions: new TerminalSessionDimensions(120, 36, 1200, 800));

await Terminal.StartSessionAsync(options);
```

That one control already owns the default session service, the active VT processor, the terminal renderer, selection and scrolling behavior, and the mode-aware input path.

## What the control actually owns

`TerminalControl` is the public host boundary. It is where Avalonia-facing configuration lives: font family, font size, grid size, scrollback size, colors, active theme, regex text highlighting, framebuffer shaders, and `VtProcessorPreference`. It also exposes the events most hosts care about, such as `DataReceived`, `TitleChanged`, `Bell`, `ProcessExited`, `CloseRequested`, and `TerminalResized`.

The rendering tree around the control is intentionally exposed instead of hidden:

| Type | Why it exists |
| --- | --- |
| `TerminalControl` | Main terminal host control and session entry point. |
| `TerminalPresenter` | The rendering host inside the control template. |
| `TerminalScrollData` | Shared scroll extent and viewport state. |
| `VirtualizedTerminalScrollViewer` | `ILogicalScrollable` implementation for long terminal histories. |
| `TerminalDrawHandler` | Composition visual handler for the default Skia render path. |
| `TerminalDrawHandler.UpdateMessage` | Swaps the active renderer and screen state source. |
| `TerminalDrawHandler.InvalidateMessage` | Requests a fresh frame. |
| `TerminalDrawHandler.ResizeMessage` | Forces redraw after size changes. |
| `TerminalDataEventArgs` | Carries terminal output bytes to the host. |
| `TerminalSizeEventArgs` | Carries resize notifications to the host. |

This is an important difference between RoyalTerminal and a monolithic widget: the control is high level, but it still exposes the moving pieces you need if you are building a specialized host.

## Applying framebuffer shaders

`TerminalControl` can apply one or more post-process shaders to the completed terminal frame:

```csharp
using RoyalTerminal.Shaders;

Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Scanline",
        shaderSource,
        TerminalShaderLanguage.SkiaRuntimeEffect,
        requiresContinuousAnimation: true)
];
```

Use `ShaderAnimationEnabled` to control whether animated shaders keep rendering between terminal output events. It defaults to `true`.

RoyalTerminal supports direct Skia Runtime Effect source plus compatibility adapters for Ghostty/Shadertoy-style `mainImage` GLSL and common Windows Terminal-style HLSL pixel shaders. See [Shader Support](/articles/shaders) and [Applying Shaders](/articles/shaders-applying) for the complete API and compatibility matrix.

## Applying regex text highlights

`TerminalControl.TextHighlightRules` applies ordered regex rules to rendered terminal rows. The feature is useful for log levels, hostnames, IP addresses, ticket ids, prompts, and other row-local tokens that should stand out without changing terminal output itself.

```csharp
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

Terminal.TextHighlightingMode = TerminalTextHighlightingMode.Static;
Terminal.TextHighlightRules =
[
    new TerminalTextHighlightRule
    {
        Name = "Warnings",
        Pattern = @"\b(WARN|WARNING)\b",
        Foreground = 0xFFFFF3C4,
        Background = 0xFF78350F,
    },
];
```

Foreground and background are optional independently. Unset colors preserve the cell's current foreground or background. See [Regex Text Highlighting](/articles/text-highlighting) for mode selection, persisted profile settings, dark-theme overrides, regex behavior, and performance notes.

## Input, selection, paste, and shortcuts

The control routes all user input through public service abstractions rather than hard-wiring it in code-behind. That is what lets the demo, tests, and custom hosts all exercise the same input pipeline.

### Input and viewport services

| Type | Purpose |
| --- | --- |
| `ITerminalInputAdapter` | Converts Avalonia key, text, pointer, and wheel input into terminal protocol sequences. |
| `DefaultTerminalInputAdapter` | Default implementation used by `TerminalControl`. |
| `ITerminalScrollService` | Scroll contract for viewport movement and bottom-follow behavior. |
| `DefaultTerminalScrollService` | Default viewport scroll implementation. |
| `ITerminalSelectionService` | Selection, clipboard export, and paste contract. |
| `DefaultTerminalSelectionService` | Default selection and clipboard implementation. |

### Paste safety

RoyalTerminal treats paste as a terminal concern rather than a plain text-box concern. The paste safety surface exists so hosts can choose whether multiline content or control characters should flow straight through, be sanitized, or require confirmation.

| Type | Purpose |
| --- | --- |
| `TerminalPasteSafetyPolicy` | Host policy: allow, confirm, sanitize, or block. |
| `TerminalPasteRisk` | Risk flags detected in the paste payload. |
| `TerminalPasteSafetyDecision` | Result returned by the host when confirmation is required. |
| `TerminalPasteContext` | Paste payload plus the detected risk flags. |
| `TerminalUnsafePasteHandler` | Async callback for confirmation flows. |
| `TerminalPasteRequest` | Full paste request: bracketed paste plus safety behavior. |

### Shortcut dispatch

RoyalTerminal keeps common clipboard-style shortcuts configurable instead of hard-coded.

| Type | Purpose |
| --- | --- |
| `TerminalShortcutGesture` | One key and modifier combination. |
| `TerminalShortcutConfiguration` | Copy, paste, cut, and select-all gesture sets. |
| `TerminalShortcutDispatcher` | Static helper that dispatches common terminal shortcuts using a configuration. |

## Capture and replay in the UI layer

Capture and replay are designed like Ghostty's higher-level feature docs: the public surface is small, but it sits on top of a more detailed runtime. In Avalonia, the entry point is `TerminalCaptureRuntime`. It subscribes to `TerminalControl` events and the session service, records output, input, resizes, and process exits, and can play the session back into the same control on a timeline.

If you are building a debugging, support, or teaching workflow, this is the feature to start with. The recorder types are covered in [Sessions, Profiles, And Settings](/articles/sessions-profiles-and-settings), and the RoyalTerminal JSON/asciicast v3 persistence layer is covered in [Capture Formats](/articles/capture-formats).

## Adding a settings surface

The `RoyalTerminal.Avalonia.Settings` package is not just a demo convenience. It is the reusable configuration UI for editing the same session document model used by the runtime.

### Settings controls

| Type | Purpose |
| --- | --- |
| `TerminalSettingsPanel` | Root settings host control. |
| `TerminalSettingsSessionPanel` | Session identity and transport selection UI. |
| `TerminalSettingsConnectionPanel` | Connection and endpoint details UI. |
| `TerminalSettingsTerminalPanel` | Terminal behavior UI. |
| `TerminalSettingsAppearancePanel` | Font, scroll, opacity, and regex text highlighting UI. |
| `TerminalSettingsSshPanel` | SSH auth, proxy, forwarding, and X11 UI. |
| `TerminalSettingsLoggingPanel` | Session and event logging UI. |

### Settings state

The state model is intentionally explicit: one owner object plus typed slices per category.

| Type | Purpose |
| --- | --- |
| `TerminalSettingsCategoryStateBase` | Observable base type for a category slice. |
| `TerminalSettingsPanelState` | Central state owner for profile CRUD, property editing, and dirty tracking. |
| `TerminalSettingsProfileItem` | Lightweight profile list item. |
| `TerminalSettingsTransportModeOption` | Lightweight transport picker option. |
| `TerminalSettingsSshAuthModeOption` | SSH auth mode option with stable built-in ids. |
| `TerminalSettingsSessionState` | Session name and transport mode slice. |
| `TerminalSettingsConnectionState` | Working directory, shell, raw TCP, Telnet, serial, and base SSH endpoint slice. |
| `TerminalSettingsTerminalBehaviorState` | Copy, bell, shaping, ligature, paste, and terminal-type slice. |
| `TerminalSettingsAppearanceState` | Font, opacity, and regex text highlighting appearance slice. |
| `TerminalSettingsSshState` | SSH auth, trust, proxy, forwarding, and timeout slice. |
| `TerminalSettingsLoggingState` | Log file, log format, flush, and event-log slice. |

The important idea is that the settings package edits durable session documents instead of directly mutating transports or controls. That keeps the UI reusable and testable.

## When to step into Avalonia GPU interop

Most hosts should stay on the default Skia path. The Avalonia Ghostty interop package exists for applications that explicitly want Ghostty renderer surfaces and need to bridge Avalonia's graphics context into that renderer.

### High-level interop types

| Type | Purpose |
| --- | --- |
| `AvaloniaRenderBackendPreference` | Backend selection policy for GPU interop. |
| `IAvaloniaSkiaRenderTargetProvider` | Creates render requests from Avalonia Skia leases. |
| `AvaloniaSkiaRenderTargetProvider` | Default backend and handle resolver with CPU fallback diagnostics. |
| `TerminalTextureInteropDrawHandler` | Composition visual handler for Ghostty renderer interop. |
| `TerminalTextureInteropDrawHandler.UpdateMessage` | Swaps renderer, provider, and optional overlay renderer/screen. |
| `TerminalTextureInteropDrawHandler.ResizeMessage` | Updates only the target size. |
| `TerminalTextureInteropDrawHandler.InvalidateMessage` | Requests another render pass. |

### Platform handle providers

The package separates handle acquisition by backend so hosts can replace only the pieces they need:

| Backend | Contract | Default | Null/fallback |
| --- | --- | --- | --- |
| D3D11 | `IAvaloniaD3D11TextureHandleProvider` | `DefaultAvaloniaD3D11TextureHandleProvider` | `NullAvaloniaD3D11TextureHandleProvider` |
| D3D12 | `IAvaloniaD3D12TextureHandleProvider` | `DefaultAvaloniaD3D12TextureHandleProvider` | `NullAvaloniaD3D12TextureHandleProvider` |
| Metal | `IAvaloniaMetalTextureHandleProvider` | `DefaultAvaloniaMetalTextureHandleProvider` | `NullAvaloniaMetalTextureHandleProvider` |
| Vulkan | `IAvaloniaVulkanTextureHandleProvider` | `DefaultAvaloniaVulkanTextureHandleProvider` | `NullAvaloniaVulkanTextureHandleProvider` |
| OpenGL | `IAvaloniaOpenGlRenderTargetHandleProvider` | `DefaultAvaloniaOpenGlRenderTargetHandleProvider` | `NullAvaloniaOpenGlRenderTargetHandleProvider` |

## Choosing the right level

If you are embedding RoyalTerminal into an Avalonia application, the normal progression is:

1. Start with `TerminalControl`.
2. Add `TerminalCaptureRuntime` if you need capture or replay.
3. Add `RoyalTerminal.Avalonia.Settings` if your application edits saved session documents.
4. Add `RoyalTerminal.Avalonia.Rendering.GhosttyInterop` only when your host architecture specifically needs Ghostty renderer interop.
