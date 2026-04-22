---
title: Applying Shaders
---

# Applying Shaders

RoyalTerminal exposes shaders at three levels:

- `TerminalControl.ShaderSources` for Avalonia hosts
- `TerminalControl.ShaderPackage` for compiler-backed full HLSL packages
- `TerminalShaderPostProcessor` for lower-level Skia render integration and tests

Most applications should use `TerminalControl`.

`TerminalShaderSource`, `TerminalShaderLanguage`, and package/compiler/runtime contracts are defined in `RoyalTerminal.Shaders`. `TerminalShaderPostProcessor` and `TerminalShaderFrameContext` are Skia adapter types from `RoyalTerminal.Rendering.Skia`.

## Apply a shader to `TerminalControl`

Create one or more `TerminalShaderSource` instances and assign them to `ShaderSources`:

```csharp
using RoyalTerminal.Shaders;

Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Green Tint",
        """
        uniform shader shaderTexture;

        half4 main(float2 fragCoord) {
            float4 color = shaderTexture.eval(fragCoord);
            return half4(color.r * 0.72, color.g, color.b * 0.72, color.a);
        }
        """)
];
```

Assign `null` or an empty list to remove shaders:

```csharp
Terminal.ShaderSources = null;
```

## Enable animated shaders

Shaders that depend on `Time`, `iTime`, `iTimeDelta`, or `iFrame` should mark the source as animated:

```csharp
Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Animated Scanline",
        shaderSource,
        TerminalShaderLanguage.SkiaRuntimeEffect,
        requiresContinuousAnimation: true)
];

Terminal.ShaderAnimationEnabled = true;
```

`ShaderAnimationEnabled` defaults to `true`. Set it to `false` when your host wants shader effects to update only when the terminal itself redraws.

## Bind from MVVM

`ShaderSources` and `ShaderAnimationEnabled` are Avalonia direct properties, so a host can bind them from a ViewModel:

```xml
<rt:TerminalControl
    ShaderSources="{Binding ActiveShaderSources}"
    ShaderAnimationEnabled="{Binding ShaderAnimationEnabled}" />
```

Keep the ViewModel framework-agnostic by storing shader selections as application state and creating `TerminalShaderSource` values in a presentation service or adapter.

## Bind compiler-backed packages

Full HLSL packages use a separate control surface:

```xml
<rt:TerminalControl
    ShaderPackage="{Binding ActiveShaderPackage}"
    ShaderBackendPreference="{Binding ShaderBackendPreference}"
    ShaderResourceProvider="{Binding ShaderResourceProvider}"
    ShaderDiagnosticsSink="{Binding ShaderDiagnosticsSink}" />
```

`ShaderPackage` is the configuration entry point for compiler-backed HLSL packages. `ShaderBackendPreference` selects the requested native backend, `ShaderResourceProvider` supplies external textures and buffers, and `ShaderDiagnosticsSink` receives validation or backend availability diagnostics.

Native full-HLSL execution backends are not implemented yet. When a package is assigned today, `TerminalControl` reports `RTSHADERCONTROL001` through the diagnostics sink and renders without package shaders. Keep using `ShaderSources` for currently executable Skia Runtime Effect, Ghostty/Shadertoy, and Windows Terminal sample-compatible shaders.

## Chain multiple shaders

`ShaderSources` is ordered. Each shader receives the previous shader's output as its input texture:

```csharp
Terminal.ShaderSources =
[
    new TerminalShaderSource("Bloom", bloomSource),
    new TerminalShaderSource("Scanlines", scanlineSource, requiresContinuousAnimation: true),
];
```

Chaining is useful for small composable effects, but each pass creates another framebuffer step. Prefer one combined shader for hot paths.

## Use compatibility modes

Select the source language on `TerminalShaderSource`:

```csharp
Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Ghostty MainImage",
        ghosttyStyleSource,
        TerminalShaderLanguage.GhosttyShadertoy,
        requiresContinuousAnimation: true),
    new TerminalShaderSource(
        "Windows Terminal HLSL",
        windowsTerminalSource,
        TerminalShaderLanguage.WindowsTerminalHlsl)
];
```

Compatibility modes are translated into Skia Runtime Effect source before compilation. See [Ghostty/Shadertoy Compatibility](/articles/shaders-ghostty-shadertoy) and [Windows Terminal HLSL Compatibility](/articles/shaders-windows-terminal-hlsl) for the supported source patterns.

## Use the low-level post processor

Use `TerminalShaderPostProcessor` when you are testing shader behavior or rendering outside `TerminalControl`:

```csharp
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Shaders;

using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create(sources);

if (!string.IsNullOrWhiteSpace(processor.CompileLog))
{
    // Surface diagnostics in your settings UI or logs.
}

TerminalShaderFrameContext frameContext = new(
    width,
    height,
    time,
    timeDelta,
    frame,
    scale,
    backgroundColor,
    foregroundColor,
    cursorColor,
    cursorRect,
    cursorStyle,
    cursorVisible);

bool applied = processor.TryApply(canvas, inputFrame, destinationRect, frameContext);
```

Invalid shader sources are skipped and recorded in `CompileLog`. `TryApply` returns `false` when no shader could be applied, letting callers fall back to drawing the unmodified terminal frame.

## Demo app samples

`RoyalTerminal.Demo` includes a toolbar shader button that cycles through built-in samples:

- `Off`
- `CRT Amber`
- `Hue Shift`
- `Transparent Key`
- `Retro Scanlines`

The samples live in `TerminalShaderSampleCatalog` and are intentionally small Skia Runtime Effect ports. They are useful as implementation examples and quick visual validation, not as a replacement for application-specific shader design.

## Performance guidance

Framebuffer shaders run every time the terminal presenter redraws. Animated shaders can redraw even when terminal output is idle.

Use these rules for production hosts:

- prefer direct Skia Runtime Effect source for effects you own
- avoid long shader chains in latency-sensitive terminals
- disable continuous animation for static effects
- compile shaders when settings change, not during every frame
- keep compatibility-mode shaders small and inspect `CompileLog` before enabling them
