# Shader Support Analysis And Implementation Plan

Date: 2026-04-22

## Goal

Enable terminal framebuffer shader effects across the managed renderer, Ghostty VT
mode, and future native Ghostty renderer interop without binding shader behavior to
one VT processor. The feature must support the same post-process mental model as:

- Windows Terminal `experimental.pixelShaderPath` HLSL shaders.
- Ghostty `custom-shader` GLSL/Shadertoy-style shaders.
- RoyalTerminal's current managed Skia renderer.

## External Compatibility Targets

Windows Terminal applies a user HLSL pixel shader to the terminal output through
`experimental.pixelShaderPath`. The shader receives the terminal framebuffer as a
texture plus a settings constant buffer containing time, scale, resolution, and
background color.

Reference:
- https://learn.microsoft.com/en-us/windows/terminal/samples
- https://github.com/microsoft/terminal/tree/main/samples/PixelShaders
- https://github.com/Hammster/windows-terminal-shaders

Ghostty applies `custom-shader` files after the default terminal shaders. The file
format is GLSL/Shadertoy-like, with `mainImage`, `iChannel0`, `iResolution`,
`iTime`, `iTimeDelta`, `iFrame`, and Ghostty-specific cursor, palette, and color
uniforms.

Reference:
- https://ghostty.org/docs/config/reference#custom-shader

## Current RoyalTerminal Rendering Findings

### Managed renderer

Relevant files:

- `src/RoyalTerminal.Rendering.Skia/Rendering/SkiaTerminalRenderer.cs`
- `src/RoyalTerminal.Avalonia/Rendering/TerminalDrawHandler.cs`
- `src/RoyalTerminal.Avalonia/Controls/TerminalPresenter.cs`
- `src/RoyalTerminal.Avalonia/Controls/TerminalControl.cs`

The active managed path is:

1. `TerminalControl` owns `TerminalScreen` and `SkiaTerminalRenderer`.
2. `TerminalPresenter` hosts an Avalonia `CompositionCustomVisual`.
3. `TerminalDrawHandler.OnRender` leases Avalonia's `SKCanvas`.
4. The handler clears the canvas and calls `SkiaTerminalRenderer.RenderFull`.

This is the correct insertion point for shader support because shaders must run
after all terminal content has been composed: background, text, cursor,
selection/search highlights, and Kitty Graphics placements.

### Ghostty native terminal bindings

Relevant files:

- `src/RoyalTerminal.Terminal.Vt.Ghostty/Terminal/GhosttyVtProcessor.cs`
- `src/RoyalTerminal.GhosttySharp/Native/*`
- `src/RoyalTerminal.GhosttySharp/GhosttyRenderState.cs`

The Ghostty binding currently provides VT parsing/state services and exposes
screen/cursor/image state into RoyalTerminal's managed `TerminalScreen`. It does
not own the final framebuffer in the active demo path, so shader support should
remain renderer-level and not be implemented inside the VT binding.

### Ghostty renderer interop

Relevant files:

- `src/RoyalTerminal.Rendering.Interop.Ghostty/*`
- `src/RoyalTerminal.Rendering.Interop.Ghostty.Skia/*`
- `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/*`
- `native/ghostty-renderer-capi/src/main.zig`

The native C API currently validates external targets and has a Metal smoke path,
but most backends still return a generic/synchronization placeholder. This makes
it the wrong first implementation point for shader support. The abstraction should
eventually grow a native post-process hook, but the first usable implementation
should sit at the shared Skia framebuffer stage and work regardless of whether VT
state came from managed or Ghostty parsing.

## Architecture Decision

Implement shaders as a framebuffer post-process pipeline:

1. Draw the complete terminal frame into an offscreen Skia surface.
2. Feed that snapshot into one or more `SKRuntimeEffect` shaders.
3. Draw the final shader output to the Avalonia Skia canvas.

This matches both target terminals:

- Windows Terminal shader input is the terminal framebuffer texture.
- Ghostty shader input is `iChannel0`, the current terminal screen texture.

The canonical runtime language inside RoyalTerminal is Skia Runtime Effect SkSL.
Compatibility adapters translate simple Ghostty Shadertoy GLSL and common Windows
Terminal HLSL post-process patterns into SkSL. Full arbitrary HLSL parity still
requires a future DirectX/DXC or Slang-backed native path because Skia cannot
execute DXIL/HLSL bytecode directly.

## Public Surface

Add rendering-layer types:

- `TerminalShaderLanguage`
  - `SkiaRuntimeEffect`
  - `GhosttyShadertoy`
  - `WindowsTerminalHlsl`

- `TerminalShaderSource`
  - stable name
  - source text
  - language
  - animation hint

- `TerminalShaderFrameContext`
  - framebuffer dimensions
  - time, delta, frame
  - scale
  - foreground/background/cursor color
  - cursor rectangle, style, visibility

- `TerminalShaderPostProcessor`
  - compiles source list to runtime effects
  - applies effects as a chain
  - exposes compile errors for diagnostics/tests

Add control-level properties:

- `TerminalControl.ShaderSources`
- `TerminalControl.ShaderAnimationEnabled`

These are control properties, not ViewModel-only demo state, so host apps can bind
or set shaders directly.

## Uniform Compatibility

RoyalTerminal should provide these uniforms when a shader declares them:

Windows Terminal-compatible names:

- `Time`
- `Scale`
- `Resolution`
- `Background`
- `shaderTexture`

Ghostty/Shadertoy-compatible names:

- `iChannel0`
- `iResolution`
- `iTime`
- `iTimeDelta`
- `iFrame`
- `iChannelResolution`
- `iBackgroundColor`
- `iForegroundColor`
- `iCursorColor`
- `iCurrentCursor`
- `iCurrentCursorColor`
- `iCurrentCursorStyle`
- `iCursorVisible`

The first implementation will set palette/cursor history extension points later
because the active renderer currently tracks current cursor data only.

## Demo Samples

Add built-in demo shader selections based on the README-highlighted
`Hammster/windows-terminal-shaders` effects:

1. Off
2. CRT Amber
3. Hue Shift
4. Transparent Key
5. Retro Scanlines

The demo uses SkSL ports so the samples run cross-platform on Avalonia/Skia while
preserving the terminal-framebuffer effect and uniform model from the original
Windows Terminal shaders.

## Implementation Steps

1. Add shader model/compiler/post-processor classes to
   `RoyalTerminal.Rendering.Skia`.
2. Update `TerminalDrawHandler` to render through the shader pipeline when
   shader sources are configured.
3. Update `TerminalPresenter` and `TerminalControl` to pass shader state into the
   composition handler.
4. Add demo sample catalog and ViewModel/controller commands for selecting samples.
5. Add tests that compile sample shaders and verify post-processing changes output
   pixels while empty/invalid shader state falls back cleanly.

## Known Limits And Follow-Up Work

- The implemented HLSL adapter is source-level compatibility for common
  post-process shader patterns. It is not a replacement for a real HLSL compiler.
- True arbitrary Windows Terminal HLSL compatibility needs a DirectX/DXC or Slang
  pipeline and backend-specific drawing.
- True native Ghostty renderer shader support should be added later to the
  `ghostty-renderer-capi` once the native renderer owns real target rendering for
  all backends.
- Multiple Ghostty-style shader chains are supported conceptually by the Skia
  pipeline, but cursor history, palette arrays, and focus-time uniforms should be
  expanded as the renderer model exposes those values.
