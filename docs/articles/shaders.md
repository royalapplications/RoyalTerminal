---
title: Shader Support
---

# Shader Support

RoyalTerminal supports terminal framebuffer shaders in the managed Skia render path. A shader receives the fully rendered terminal frame as a texture, applies a post-process effect, and draws the result back into the Avalonia composition target.

This keeps shader support independent from the VT processor. The same shader pipeline works when the terminal state came from the managed VT engine, the Ghostty-backed VT engine, replay data, or any transport that updates `TerminalScreen`.

The non-terminal shader surface lives in `RoyalTerminal.Shaders`. That package contains source descriptions and compatibility translation without referencing Avalonia, SkiaSharp, terminal internals, or native renderer packages. The Skia-specific post-processing adapter remains in `RoyalTerminal.Rendering.Skia`.

## Supported shader paths

| Path | Use when |
| --- | --- |
| [Applying Shaders](/articles/shaders-applying) | You want to set shaders on `TerminalControl`, bind them from a ViewModel, or call the lower-level post processor. |
| [Skia Runtime Effect Shaders](/articles/shaders-skia-runtime-effect) | You can write or port the shader as Skia Runtime Effect source. This is the canonical runtime format. |
| [Ghostty/Shadertoy Compatibility](/articles/shaders-ghostty-shadertoy) | You have a Ghostty-style or Shadertoy-style GLSL fragment shader with a `mainImage` entry point. |
| [Windows Terminal HLSL Compatibility](/articles/shaders-windows-terminal-hlsl) | You have a Windows Terminal-style HLSL pixel shader that samples `shaderTexture` through `PixelShaderSettings`. |

## Render architecture

The managed shader path is deliberately late in the frame:

1. the active VT processor updates `TerminalScreen`
2. `SkiaTerminalRenderer` renders the terminal cells, selection, cursor, overlays, and graphics into an offscreen Skia surface
3. `TerminalShaderPostProcessor` compiles and applies one or more `TerminalShaderSource` entries
4. the final image is drawn into the `TerminalControl` presenter

Because the shader runs after terminal drawing, it affects everything in the frame: text, cursor, selection, decorations, and any image content already drawn by the renderer.

## Public API surface

| Type or member | Package | Purpose |
| --- | --- | --- |
| `TerminalControl.ShaderSources` | `RoyalTerminal.Avalonia` | Optional shader chain applied to the completed terminal frame. |
| `TerminalControl.ShaderAnimationEnabled` | `RoyalTerminal.Avalonia` | Allows shaders that request animation to keep the render loop active between terminal updates. |
| `TerminalShaderSource` | `RoyalTerminal.Shaders` | One named shader source plus language and animation metadata. |
| `TerminalShaderLanguage` | `RoyalTerminal.Shaders` | Selects direct SkSL, Ghostty/Shadertoy compatibility, or Windows Terminal HLSL compatibility. |
| `TerminalShaderSourceTranslator` | `RoyalTerminal.Shaders` | Dependency-free source translator used by the Skia adapter. |
| `TerminalShaderPostProcessor` | `RoyalTerminal.Rendering.Skia` | Lower-level compiler and post-processor used by the renderer. |
| `TerminalShaderFrameContext` | `RoyalTerminal.Rendering.Skia` | Skia/terminal frame data passed to post-process uniforms. |

## Frame inputs

The post processor exposes the terminal frame under several names so compatibility modes can share the same runtime:

| Input | Meaning |
| --- | --- |
| `shaderTexture` | Canonical Skia child shader for the rendered terminal frame. |
| `inputTexture` | Alias for custom SkSL effects that prefer a neutral name. |
| `iChannel0` | Ghostty/Shadertoy-compatible alias for the rendered terminal frame. |

## Common uniforms

| Uniform | Shape | Meaning |
| --- | --- | --- |
| `Resolution` | `float2` | Framebuffer width and height in pixels. |
| `Scale` | `float` | Current UI render scale. |
| `Time` | `float` | Elapsed shader time in seconds. |
| `Background` | `float4` | Current terminal background color. |
| `iResolution` | `float3` | Ghostty/Shadertoy-style framebuffer size. |
| `iTime` | `float` | Ghostty/Shadertoy-style elapsed time. |
| `iTimeDelta` | `float` | Seconds since the previous shader frame. |
| `iFrame` | `int` | Shader frame index. |
| `iChannelResolution` | `float3[4]` | Channel sizes. Channel 0 matches the terminal frame. |
| `iBackgroundColor` | `float3` | Terminal background RGB. |
| `iForegroundColor` | `float3` | Terminal foreground RGB. |
| `iCursorColor` | `float3` | Cursor color RGB. |
| `iCurrentCursor` | `float4` | Cursor rectangle as left, top, width, height. |
| `iCurrentCursorColor` | `float4` | Cursor color RGBA. |
| `iCurrentCursorStyle` | `float4` | Cursor style id in the first component. |
| `iCursorVisible` | `float4` | Cursor visibility flag in the first component. |

Uniforms are optional. A shader only needs to declare the values it uses.

## Compatibility scope

The current compatibility layer is source-level adaptation into Skia Runtime Effect source. It is not a full GLSL or HLSL compiler.

| Source family | Current status |
| --- | --- |
| Skia Runtime Effect | Native support. This is the preferred production format. |
| Ghostty/Shadertoy `mainImage` GLSL | Supported for single-pass post-process shaders that sample the terminal frame through `iChannel0`. |
| Windows Terminal HLSL | Supported for common Windows Terminal pixel shader samples that use `shaderTexture`, `samplerState`, `PSInput`, and `PixelShaderSettings`. |
| Full HLSL packages, compute shaders, and native GPU runtimes | Not supported. RoyalTerminal does not ship a DXC/Slang/D3D/Vulkan/Metal shader runtime. |
| Arbitrary HLSL/GLSL projects | Not supported without manual porting into the supported Skia post-process shape. |
| Native Ghostty renderer custom shaders | Not injected into the native renderer yet. RoyalTerminal applies Ghostty-compatible shader source through the managed Skia post-process path. |

## Choosing a format

Use direct Skia Runtime Effect source for new RoyalTerminal shaders. Use Ghostty/Shadertoy or Windows Terminal compatibility when you are porting existing shader libraries and want to preserve their original entry-point shape.

For production applications that ship user-provided shaders, compile them during settings validation and surface `TerminalShaderPostProcessor.CompileLog` to the user. Invalid shaders are skipped so a bad effect does not prevent terminal rendering.
