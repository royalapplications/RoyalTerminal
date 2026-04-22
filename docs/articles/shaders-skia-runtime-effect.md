---
title: Skia Runtime Effect Shaders
---

# Skia Runtime Effect Shaders

Skia Runtime Effect source is the canonical RoyalTerminal shader format. It compiles directly with `SKRuntimeEffect.CreateShader` and avoids the compatibility translation needed for Ghostty/Shadertoy and Windows Terminal sources.

Use this format for new effects.

## Minimal shader

```csharp
Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Invert",
        """
        uniform shader shaderTexture;

        half4 main(float2 fragCoord) {
            float4 color = shaderTexture.eval(fragCoord);
            return half4(1.0 - color.rgb, color.a);
        }
        """)
];
```

`fragCoord` is in framebuffer pixels with the origin at the top-left corner of the terminal frame.

## Sampling the terminal frame

Declare the terminal frame as a child shader:

```glsl
uniform shader shaderTexture;
```

Then sample with framebuffer coordinates:

```glsl
float4 color = shaderTexture.eval(fragCoord);
```

The aliases `inputTexture` and `iChannel0` are also bound by the post processor, but `shaderTexture` is the preferred name for new Skia Runtime Effect shaders.

## Static effect example

```glsl
uniform shader shaderTexture;
uniform float4 Background;

half4 main(float2 fragCoord) {
    float4 color = shaderTexture.eval(fragCoord);
    float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
    float3 phosphor = mix(Background.rgb, float3(0.64, 1.0, 0.48), luminance);
    return half4(phosphor, color.a);
}
```

This shader only needs new frames when terminal content changes, so `requiresContinuousAnimation` can stay `false`.

## Animated effect example

```csharp
Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Moving Scanline",
        """
        uniform shader shaderTexture;
        uniform float Time;
        uniform float2 Resolution;

        half4 main(float2 fragCoord) {
            float4 color = shaderTexture.eval(fragCoord);
            float scan = smoothstep(0.02, 0.0, abs(fragCoord.y / Resolution.y - fract(Time * 0.25)));
            color.rgb += scan * 0.12;
            return half4(color);
        }
        """,
        requiresContinuousAnimation: true)
];
```

Animated sources should set `requiresContinuousAnimation: true`. The control will keep presenting frames while `ShaderAnimationEnabled` is enabled.

## Available uniforms

Direct Skia shaders can declare any of the common uniforms from [Shader Support](/articles/shaders#common-uniforms). For new Skia effects, the most useful set is:

| Uniform | Meaning |
| --- | --- |
| `Resolution` | Framebuffer size in pixels. |
| `Scale` | Avalonia render scale. |
| `Time` | Elapsed shader time in seconds. |
| `Background` | Terminal background color. |
| `iForegroundColor` | Terminal foreground RGB. |
| `iCurrentCursor` | Cursor rectangle in framebuffer coordinates. |

Only declared uniforms are set.

## Compilation behavior

`TerminalShaderPostProcessor.Create` compiles all sources in the chain. Sources that fail to compile are skipped and written to `CompileLog`.

Use this behavior in settings surfaces:

```csharp
using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create(candidateSources);

if (!processor.HasShaders && !string.IsNullOrWhiteSpace(processor.CompileLog))
{
    // The user selected only invalid shader source.
}
```

## Porting advice

When porting from GLSL or HLSL, start by converting only the final post-process pass:

- replace texture samplers with `uniform shader shaderTexture`
- sample with pixel coordinates through `shaderTexture.eval(fragCoord)`
- replace unsupported language built-ins manually
- declare only the uniforms the effect actually needs

If the source already matches a supported compatibility shape, you can use [Ghostty/Shadertoy Compatibility](/articles/shaders-ghostty-shadertoy) or [Windows Terminal HLSL Compatibility](/articles/shaders-windows-terminal-hlsl) first, then port to direct SkSL if you need tighter control.
