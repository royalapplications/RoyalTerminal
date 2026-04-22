---
title: Windows Terminal HLSL Shader Compatibility
---

# Windows Terminal HLSL Shader Compatibility

RoyalTerminal can load a supported subset of Windows Terminal-style HLSL pixel shaders and translate them into Skia Runtime Effect source. This is intended for common terminal post-process shaders such as CRT, scanline, hue-shift, and transparency-key effects.

This compatibility mode is source adaptation. RoyalTerminal does not currently invoke DXC, Direct2D, or the Windows Terminal rendering pipeline.

Full HLSL packages, compute shaders, multiple resources, and native DirectX execution are outside this path. Port those effects into the supported single-pass post-process shape before assigning them to `ShaderSources`.

## Apply a Windows Terminal-style shader

```csharp
using RoyalTerminal.Shaders;

Terminal.ShaderSources =
[
    new TerminalShaderSource(
        "Windows Terminal Compatible",
        """
        Texture2D shaderTexture : register(t0);
        SamplerState samplerState : register(s0);

        cbuffer PixelShaderSettings : register(b0)
        {
            float Time;
            float Scale;
            float2 Resolution;
            float4 Background;
        };

        struct PSInput
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD;
        };

        float4 main(PSInput pin) : SV_TARGET
        {
            float4 color = shaderTexture.Sample(samplerState, pin.uv);
            color.rgb = lerp(color.rgb, float3(1.0, 0.75, 0.25), 0.12);
            return color;
        }
        """,
        TerminalShaderLanguage.WindowsTerminalHlsl)
];
```

The adapter strips the Windows Terminal resource declarations, creates equivalent Skia uniforms, and rewrites terminal texture sampling to the managed framebuffer input.

## Supported source shape

| Pattern | Status |
| --- | --- |
| `Texture2D shaderTexture : register(t0);` | Supported and removed during translation. Generic forms such as `Texture2D<float4>` are also accepted, and any `Texture2D` name bound to `t0` is treated as the terminal frame. |
| `SamplerState samplerState : register(s0);` | Supported and removed during translation. The sampler variable name does not need to be `samplerState`. |
| `cbuffer PixelShaderSettings : register(b0)` | Supported and removed during translation. |
| `struct PSInput { float4 pos; float2 uv; }` | Supported and removed during translation. Input structs with `SV_POSITION` or `TEXCOORD` fields are also removed, including common aliases such as `position` and `texCoord`. |
| `float4 main(PSInput pin) : SV_TARGET` | Supported. The body becomes the Skia `main(float2 fragCoord)`, and direct `pin.pos`/`pin.uv` or semantic field aliases are mapped to generated `pos`/`uv` values. |
| `float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0)` | Supported. Parameter names are normalized to generated `pos` and `uv` values. |
| `shaderTexture.Sample(samplerState, uv)` | Rewritten to sample the terminal frame. |
| `shaderTexture.SampleLevel(samplerState, uv, lod)` | Rewritten to sample the terminal frame. The explicit LOD is ignored by the Skia post-process path. |
| `shaderTexture.SampleBias(...)`, `shaderTexture.SampleGrad(...)` | Rewritten to sample the terminal frame. Bias and gradient arguments are ignored by the Skia post-process path. |
| `shaderTexture.Load(int3(pos.xy, 0))` | Rewritten to sample the terminal frame with pixel coordinates. |
| `#include "SHADERed/..."` | Removed. |
| `register(...)` annotations | Removed. |
| HLSL float suffixes such as `1.0f` | Removed. |
| `static const` values | Normalized to Skia `const` declarations. |
| `min10float`, `min16float` vector types | Normalized to Skia `half` vector types. |

The generated Skia entry point provides:

```glsl
float4 pos = float4(fragCoord, 0.0, 1.0);
float2 uv = fragCoord / Resolution;
```

That matches the common Windows Terminal sample shape where `pin.pos` is pixel-space and `pin.uv` is normalized.

## Supported built-in replacements

| HLSL | Skia Runtime Effect |
| --- | --- |
| `lerp(a, b, t)` | `mix(a, b, t)` |
| `frac(x)` | `fract(x)` |
| `fmod(a, b)` | `mod(a, b)` |
| `saturate(x)` | `clamp(x, 0.0, 1.0)` |
| `atan2(y, x)` | `atan(y, x)` |
| `rsqrt(x)` | `inversesqrt(x)` |
| `mad(a, b, c)` | `(a * b + c)` |
| `mul(a, b)` | `(a * b)` |

The adapter also normalizes a simple bitwise-or expression pattern used by some shader samples.

## PixelShaderSettings uniforms

RoyalTerminal exposes the common Windows Terminal settings names:

| Uniform | Meaning |
| --- | --- |
| `Time` | Elapsed shader time in seconds. |
| `Scale` | Avalonia render scale. |
| `Resolution` | Framebuffer size in pixels. |
| `Background` | Terminal background color. |

If a source needs more terminal state, it can also declare the common RoyalTerminal uniforms listed in [Shader Support](/articles/shaders#common-uniforms).

## Limitations

This is not a full HLSL compiler. It covers common Windows Terminal pixel shader samples, including the patterns used by many community CRT and color effects, but it will not compile every HLSL file.

Not currently supported:

- arbitrary HLSL syntax outside the supported pixel-shader shape
- additional textures, samplers, UAVs, or constant buffers
- multi-pass effects
- compute shaders
- external include files after translation
- DirectX-only intrinsics with no Skia equivalent
- semantic-dependent code that cannot be reduced to `fragCoord`, `pos`, and `uv`

When a shader fails translation or compilation, `TerminalShaderPostProcessor.CompileLog` contains the Skia compiler diagnostics for the generated source.

## Demo sample relationship

The demo shader catalog includes Skia Runtime Effect ports inspired by common Windows Terminal shader samples and one Windows Terminal-style HLSL sample translated at runtime:

- CRT-style amber display
- hue shift
- transparent background keying
- retro scanlines
- Windows Terminal CRT

All samples remain cross-platform because execution happens through Skia Runtime Effect.
