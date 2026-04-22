---
title: Rendering, Text, And Graphics
---

# Rendering, Text, And Graphics

RoyalTerminal splits rendering into five layers: backend-neutral contracts, text shaping and fallback, the default Skia renderer, the managed framebuffer shader pipeline, and optional Ghostty renderer interop. That separation keeps the default path simple while still giving advanced hosts access to GPU-level integration.

## The default render path

The normal path for an Avalonia application is:

1. a VT processor updates `TerminalScreen`
2. shaping and font fallback resolve how text should be drawn
3. `SkiaTerminalRenderer` paints the grid, cursor, selection, and overlays
4. optional framebuffer shaders post-process the completed terminal frame
5. the Avalonia control presents the result

For most applications, this is the only rendering path you need.

### Default renderer types

| Type | Purpose |
| --- | --- |
| `GlyphCache` | Glyph and font resource cache for the renderer. |
| `SkiaTerminalRenderer` | High-performance CPU renderer for the terminal screen. |
| `CursorStyle` | Renderer cursor style enum. |

## Framebuffer shaders

The managed shader path runs after the terminal has been rendered into a Skia surface. It treats the completed terminal frame as a texture and applies one or more `TerminalShaderSource` entries through `TerminalShaderPostProcessor`.

The main host-facing properties live on `TerminalControl`:

| Member | Purpose |
| --- | --- |
| `ShaderSources` | Ordered shader chain applied to the completed frame. |
| `ShaderAnimationEnabled` | Allows animated shaders to keep requesting frames while terminal output is idle. |

Supported source families:

| Source family | Article |
| --- | --- |
| Direct Skia Runtime Effect source | [Skia Runtime Effect Shaders](/articles/shaders-skia-runtime-effect) |
| Ghostty/Shadertoy `mainImage` GLSL | [Ghostty/Shadertoy Shader Compatibility](/articles/shaders-ghostty-shadertoy) |
| Windows Terminal-style HLSL pixel shaders | [Windows Terminal HLSL Shader Compatibility](/articles/shaders-windows-terminal-hlsl) |

Start with [Shader Support](/articles/shaders) for the architecture and [Applying Shaders](/articles/shaders-applying) for the host API.

## Text shaping and font fallback

Terminal rendering is not just "draw a codepoint". Ligatures, bidirectional text, fallback fonts, emoji presentation, and diagnostics all matter if the renderer is going to remain believable across languages and shells.

### Text stack

| Type | Purpose |
| --- | --- |
| `TextDirectionMode` | Shaping direction mode. |
| `TextShapingOptions` | Shaping options payload. |
| `ShapedGlyph` | One shaped glyph with cluster and positional data. |
| `ShapedTextRun` | Immutable shaped glyph run. |
| `ITextShaper` | Shaping contract. |
| `HarfBuzzTextShaper` | HarfBuzz-based shaping implementation. |
| `HarfBuzzTypefaceCache` | Cache for HarfBuzz typeface resources. |
| `HarfBuzzTypefaceEntry` | One cached HarfBuzz typeface resource. |
| `TerminalFontResolution` | Result of resolving a primary or fallback typeface. |
| `TerminalFontResolver` | Fallback typeface resolver. |
| `TextRenderDiagnostics` | Diagnostics snapshot for shaping and fallback behavior. |

The shaping layer is deliberately terminal-focused. It exists to preserve the cell grid while still handling the text cases that real terminal applications produce.

## Backend-neutral render contracts

When you need to render outside the default Avalonia control, the public contract surface lives in `RoyalTerminal.Rendering.Contracts`.

| Type | Purpose |
| --- | --- |
| `IRenderBackend` | Capability contract for a backend implementation. |
| `IRenderSurface` | Surface contract for rendering into external targets. |
| `RenderBackendKind` | Backend kind enum. |
| `RenderTargetKind` | Render target kind enum. |
| `RenderPixelFormat` | Pixel-format enum. |
| `RenderFeatureFlags` | Optional backend feature flags. |
| `RenderBackendCapabilities` | Capability snapshot for a backend or surface. |
| `RenderTargetDescriptor` | External target descriptor passed into validation or rendering. |
| `RenderValidationResult` | Validation result model. |
| `RenderFrameResult` | Render result model. |
| `RenderTargetDescriptorValidator` | Static validator for render target invariants. |

This contract layer is what makes the renderer interop packages possible without baking one graphics stack into the rest of the terminal model.

## Ghostty renderer interop

The Ghostty renderer is optional. RoyalTerminal keeps it in separate packages so hosts can opt in only when they actually want GPU-level interop.

### Core interop types

| Type | Purpose |
| --- | --- |
| `GhosttyRenderContext` | Owner of the native Ghostty renderer context. |
| `GhosttyRenderSurface` | Managed render surface over the native Ghostty renderer surface. |
| `GhosttyRenderInteropException` | Interop-specific exception type. |
| `GhosttyRenderInteropResult` | Native result code enum. |
| `GhosttyRenderInteropResultMapper` | Static mapper from native result codes to readable messages. |
| `RenderTheme` | Neutral render theme structure for Ghostty surfaces. |
| `GhosttyRendererNativeLibraryLoader` | Native loader for `ghostty-renderer-capi`. |

### Skia bridge types

| Type | Purpose |
| --- | --- |
| `ISkiaRgbaFallbackRenderer` | Contract for CPU fallback drawing into Skia. |
| `GhosttyRenderSurfaceRgbaFallbackRenderer` | Default RGBA fallback implementation. |
| `SkiaInteropRenderRequest` | Request passed into the Skia bridge. |
| `SkiaInteropRenderResult` | Result returned by the Skia bridge. |
| `SkiaInteropRenderer` | Bridge that renders Ghostty surfaces through GPU interop or CPU fallback. |

If you are using the Avalonia package, the host-specific handle acquisition layer is documented separately in [Embedding In Avalonia](/articles/avalonia-control).

## Choosing the right rendering level

| If you need | Start with |
| --- | --- |
| A normal Avalonia terminal | `SkiaTerminalRenderer` through `TerminalControl` |
| Terminal post-process effects | `TerminalControl.ShaderSources` and `TerminalShaderSource` |
| Font shaping and fallback insight | `HarfBuzzTextShaper` and `TerminalFontResolver` |
| A custom render host | `RenderTargetDescriptor`, `IRenderSurface`, and the contracts package |
| Ghostty renderer integration | `GhosttyRenderContext`, `GhosttyRenderSurface`, and `SkiaInteropRenderer` |

The rule of thumb is simple: stay on the default Skia path until your host architecture gives you a concrete reason to do otherwise.
