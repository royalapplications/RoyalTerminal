---
title: Ghostty/Shadertoy Shader Compatibility
---

# Ghostty/Shadertoy Shader Compatibility

RoyalTerminal can load Ghostty-style or Shadertoy-style fragment shaders that expose a `mainImage` entry point. The source is translated into Skia Runtime Effect source and applied through the managed terminal framebuffer shader pipeline.

This is source compatibility for post-process effects. It does not inject shader source into the native Ghostty renderer.

## Apply a `mainImage` shader

```csharp
using RoyalTerminal.Shaders;

Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Ghostty Compatible Scanline",
        """
        void mainImage(out vec4 fragColor, in vec2 fragCoord) {
            vec2 uv = fragCoord / iResolution.xy;
            vec4 color = texture(iChannel0, uv);
            float scanline = 1.0 - mod(floor(fragCoord.y), 2.0) * 0.18;
            fragColor = vec4(color.rgb * scanline, color.a);
        }
        """,
        TerminalShaderLanguage.GhosttyShadertoy)
];
```

The translated shader samples the rendered terminal frame through `iChannel0`.

## Supported source shape

| Pattern | Status |
| --- | --- |
| `void mainImage(out vec4 fragColor, in vec2 fragCoord)` | Supported. |
| `iResolution`, `iTime`, `iTimeDelta`, `iFrame` | Supported as optional uniforms. |
| `iChannelResolution` | Supported. Channel 0 matches the terminal frame size. |
| `iChannel0` | Supported and bound to the terminal frame. |
| `texture(iChannel0, uv)` | Rewritten to the Skia child shader sampler, including nested coordinate expressions. |
| `texture2D(iChannel0, uv)` | Rewritten to the Skia child shader sampler, including nested coordinate expressions. |
| GLSL `vec2`, `vec3`, `vec4` | Rewritten to Skia `float2`, `float3`, `float4`. |
| GLSL `ivec2`, `ivec3`, `ivec4` | Rewritten to Skia `int2`, `int3`, `int4`. |
| GLSL `mat2`, `mat3`, `mat4` | Rewritten to Skia `float2x2`, `float3x3`, `float4x4`. |
| `#version`, `#ifdef GL_ES`, `precision mediump float;` | Removed for common portable GLSL/Shadertoy sources. |

Known Ghostty/Shadertoy uniforms declared in the source are removed before the RoyalTerminal prelude is added. This includes common cursor and channel-resolution uniforms, so portable Ghostty sources can keep their original declarations.

## RoyalTerminal-specific uniforms

In addition to Shadertoy-style uniforms, the runtime exposes terminal state:

| Uniform | Meaning |
| --- | --- |
| `iBackgroundColor` | Terminal background RGB. |
| `iForegroundColor` | Terminal foreground RGB. |
| `iCursorColor` | Cursor color RGB. |
| `iCurrentCursor` | Cursor rectangle as left, top, width, height. |
| `iCurrentCursorColor` | Cursor color RGBA. |
| `iCurrentCursorStyle` | Cursor style id in the first component. |
| `iCursorVisible` | Cursor visibility flag in the first component. |

These uniforms are useful for effects that treat text, background, or cursor regions differently.

## Coordinate rules

`fragCoord` is a framebuffer pixel coordinate. For normalized texture coordinates, divide by `iResolution.xy`:

```glsl
vec2 uv = fragCoord / iResolution.xy;
vec4 color = texture(iChannel0, uv);
```

The compatibility sampler clamps UV values to the terminal frame.

## Limitations

This compatibility mode is designed for single-pass framebuffer effects. It does not provide a full Shadertoy runtime or a full Ghostty renderer integration.

Not currently supported:

- multi-pass buffers
- additional image or cube-map channels
- audio channels
- arbitrary GLSL preprocessor/include flows
- custom sampler states
- native Ghostty renderer `custom-shader` injection

For effects that need those capabilities, port the final pass to [Skia Runtime Effect Shaders](/articles/shaders-skia-runtime-effect) or use a future native renderer integration.

## Relationship to Ghostty VT

The compatibility mode is independent from the selected VT processor. You can use a Ghostty-compatible shader while running the managed VT engine, and you can use direct Skia shaders while running the Ghostty-backed VT engine.

The reason is architectural: RoyalTerminal's Ghostty VT binding produces terminal state, while framebuffer shaders are applied after RoyalTerminal has rendered that state into a Skia surface.
