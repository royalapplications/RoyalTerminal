# Rendering Interop And Backends

## Table Of Contents

- [Scope And Primary Files](#scope-and-primary-files)
- [Pipeline Overview](#pipeline-overview)
- [Contracts And Models](#contracts-and-models)
- [Descriptor Validation Rules](#descriptor-validation-rules)
- [Backend Selection](#backend-selection)
- [Direct Interop Vs CPU Fallback](#direct-interop-vs-cpu-fallback)
- [Avalonia Handle Extraction](#avalonia-handle-extraction)
- [Control Integration](#control-integration)
- [Validation And Regression Tests](#validation-and-regression-tests)
- [Code Examples](#code-examples)

## Scope And Primary Files

Primary rendering/interop files:
- `src/RoyalTerminal.Rendering.Contracts/Contracts/IRenderSurface.cs`
- `src/RoyalTerminal.Rendering.Contracts/Contracts/IRenderBackend.cs`
- `src/RoyalTerminal.Rendering.Contracts/Models/RenderTargetDescriptor.cs`
- `src/RoyalTerminal.Rendering.Contracts/Validation/RenderTargetDescriptorValidator.cs`
- `src/RoyalTerminal.Rendering.Interop.Ghostty/Interop/GhosttyRenderContext.cs`
- `src/RoyalTerminal.Rendering.Interop.Ghostty/Interop/GhosttyRenderSurface.cs`
- `src/RoyalTerminal.Rendering.Interop.Ghostty.Skia/Interop/SkiaInteropRenderer.cs`
- `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/AvaloniaSkiaRenderTargetProvider.cs`
- `src/RoyalTerminal.Avalonia.Rendering.GhosttyInterop/Interop/TerminalTextureInteropDrawHandler.cs`
- `src/RoyalTerminal.Avalonia.Ghostty/Controls/GhosttyRenderedTerminalControl.cs`

Related managed rendering references:
- [`rendering-system-usage.md`](rendering-system-usage.md)
- [`rendering-managed-pipeline.md`](rendering-managed-pipeline.md)
- [`rendering-text-shaping-font-fallback-and-diagnostics.md`](rendering-text-shaping-font-fallback-and-diagnostics.md)

Native dependency for direct interop:
- `ghostty-renderer-capi` loaded via `GhosttyRendererNativeLibraryLoader`

Platform note:
- `GhosttyRenderedTerminalControl` is currently marked `[SupportedOSPlatform("macos")]` and creates macOS NSView/NSWindow hosts.
- backend-candidate logic includes Linux/Windows kinds, but this specific control path is presently macOS-focused.

## Pipeline Overview

Texture interop runtime flow:
1. `GhosttyRenderedTerminalControl` creates `GhosttyRenderContext` and `GhosttyRenderSurface`.
2. Control wraps surface in `SkiaInteropRenderer` with `GhosttyRenderSurfaceRgbaFallbackRenderer`.
3. `TerminalTextureInteropDrawHandler` receives update messages and runs each frame.
4. Handler asks `IAvaloniaSkiaRenderTargetProvider` for a `SkiaInteropRenderRequest`.
5. `SkiaInteropRenderer` attempts direct `IRenderSurface.Render(...)` when descriptor is valid.
6. On unsupported/invalid/failing direct path, renderer uses CPU RGBA fallback when allowed.

CPU-cell mode remains separate (`SkiaTerminalRenderer` rendering from terminal cell model).

## Contracts And Models

`IRenderSurface` is the central contract for interop surfaces:
- `BackendKind`
- `Capabilities`
- `SetSize(...)`
- `SetScale(...)`
- `ValidateTarget(...)`
- `Render(...)`

`GhosttyRenderSurface` implements `IRenderSurface` and adds native-specific APIs:
- `SetFocus(...)`
- `SetColorScheme(...)`
- `BeginFrame()` / `EndFrame(...)`
- `RenderToRgba(...)`

`RenderTargetDescriptor` describes one render submission:
- backend and target kinds
- format, dimensions, sample count
- backend handles (device/context/queue/list/target/view)
- optional debug name

## Descriptor Validation Rules

`RenderTargetDescriptorValidator.Validate(...)` enforces non-negotiable invariants before native calls:
- backend kind must not be `Unknown`
- target kind must not be `Unknown`
- width/height > 0
- sample count > 0
- target handle required
- texture targets require known pixel format

Backend-specific requirements:
- Metal: `DeviceHandle`
- Vulkan: `DeviceHandle` + `CommandQueueHandle`; texture also needs `TargetViewHandle`
- D3D11: `DeviceHandle`; texture also needs `TargetViewHandle`
- D3D12: `DeviceHandle` + `CommandQueueHandle` + `CommandBufferHandle`; texture also needs `TargetViewHandle`
- OpenGL: `ContextHandle`

Special case:
- OpenGL default framebuffer (`TargetKind=Framebuffer`) allows `TargetHandle=0`.

## Backend Selection

`GhosttyRenderedTerminalControl` surface creation candidates:
- macOS: `Metal`, then `Software`
- Linux: `Vulkan`, then `Software`
- Windows: `D3D11`, `D3D12`, then `Software`

`AvaloniaSkiaRenderTargetProvider` descriptor backend candidates:
- preferred explicit backend when `BackendPreference` is set
- otherwise inferred from Avalonia graphics context and host OS
- unsupported/missing-handle-provider paths emit diagnostics and return software fallback descriptor

## Direct Interop Vs CPU Fallback

`SkiaInteropRenderer.Render(...)` decision sequence:
1. validate descriptor with `RenderTargetDescriptorValidator`
2. check feature flags (`ExternalTextureTargets` / `ExternalFramebufferTargets`)
3. validate descriptor against surface (`IRenderSurface.ValidateTarget`)
4. execute direct render (`IRenderSurface.Render`)
5. if any direct step fails and fallback is allowed, render to RGBA and blit to `SKCanvas`

Fallback details:
- rents pooled buffer via `ArrayPool<byte>`
- calls fallback renderer (`ISkiaRgbaFallbackRenderer.RenderToRgba`)
- creates `SKImage` from pixels and draws to destination rect
- returns combined error when both direct and fallback fail

## Avalonia Handle Extraction

`AvaloniaSkiaRenderTargetProvider` depends on handle providers for each backend:
- `IAvaloniaMetalTextureHandleProvider`
- `IAvaloniaVulkanTextureHandleProvider`
- `IAvaloniaD3D11TextureHandleProvider`
- `IAvaloniaD3D12TextureHandleProvider`
- `IAvaloniaOpenGlRenderTargetHandleProvider`

`AvaloniaInteropHandleExtraction` performs reflective member probing against Avalonia internals to locate current Skia session and native handles.

Maintenance note:
- this reflective extraction is intentionally isolated to the interop adapter layer.
- if Avalonia internal member names change, provider diagnostics should report fallback reasons instead of crashing rendering.

## Control Integration

`GhosttyRenderedTerminalControl` texture interop integration points:
- `RenderingMode = TextureInterop` creates interop context/surface/renderer
- scale and size updates call both Ghostty surface and interop surface setters
- `SyncTextureInteropFrame()` pushes `UpdateMessage` to `TerminalTextureInteropDrawHandler`
- `InteropRenderTargetProvider.DiagnosticReported` is logged through control logger

`TerminalTextureInteropDrawHandler` behavior:
- stores renderer/provider/pixel-size from update messages
- requests next animation frame when invalidated
- uses current `ISkiaSharpApiLease` to build a render request
- executes interop render and optionally overlays `SkiaTerminalRenderer` output

## Validation And Regression Tests

Run these tests after interop/backend changes:
- `tests/RoyalTerminal.Tests/RenderingContractsTests.cs`
- `tests/RoyalTerminal.Tests/RenderingInteropTests.cs`
- `tests/RoyalTerminal.Tests/RenderingSkiaInteropTests.cs`
- `tests/RoyalTerminal.Tests/RenderingAvaloniaAdapterTests.cs`
- `tests/RoyalTerminal.Tests/HeadlessSkiaRenderingTests.cs`
- `tests/RoyalTerminal.Tests/PackageBoundaryTests.cs`
- `tests/RoyalTerminal.Tests/WindowsArm64NativePackagingTests.cs`

## Code Examples

### Create surface and render via software backend

```csharp
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Rendering.Interop.Ghostty;

using GhosttyRenderContext context = new();
using GhosttyRenderSurface surface = context.CreateSurface(RenderBackendKind.Software);

surface.SetSize(1280, 720);
surface.SetScale(1.0, 1.0);

RenderTargetDescriptor descriptor = new()
{
    BackendKind = RenderBackendKind.Software,
    TargetKind = RenderTargetKind.Framebuffer,
    PixelFormat = RenderPixelFormat.Unknown,
    Width = 1280,
    Height = 720,
    SampleCount = 1,
    TargetHandle = (nint)1,
    DebugName = "software-framebuffer",
};

RenderValidationResult validation = surface.ValidateTarget(descriptor);
if (!validation.IsValid)
{
    throw new InvalidOperationException(validation.ErrorMessage);
}

RenderFrameResult frame = surface.Render(descriptor);
if (!frame.Succeeded)
{
    throw new InvalidOperationException(frame.ErrorMessage);
}
```

### CPU RGBA fallback render for diagnostics

```csharp
using RoyalTerminal.Rendering.Interop.Ghostty;

using GhosttyRenderContext context = new();
using GhosttyRenderSurface surface = context.CreateSurface(RenderBackendKind.Software);

int width = 640;
int height = 480;
int stride = width * 4;
byte[] rgba = new byte[stride * height];

RenderFrameResult result = surface.RenderToRgba(rgba, width, height, stride);
if (!result.Succeeded)
{
    Console.WriteLine($"Fallback render failed: {result.ErrorMessage}");
}
```

### Configure explicit backend preference for texture interop mode

```csharp
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

GhosttyRenderedTerminalControl control = new()
{
    RenderingMode = GhosttyRenderedTerminalRenderingMode.TextureInterop,
    InteropRenderTargetProvider = new AvaloniaSkiaRenderTargetProvider(
        backendPreference: AvaloniaRenderBackendPreference.OpenGL),
};

control.InteropRenderTargetProvider.DiagnosticReported += (_, message) =>
{
    Console.WriteLine($"Interop diagnostic: {message}");
};
```

### Defensive descriptor validation before native call

```csharp
using RoyalTerminal.Rendering.Contracts;

RenderTargetDescriptor descriptor = new()
{
    BackendKind = RenderBackendKind.Vulkan,
    TargetKind = RenderTargetKind.Texture2D,
    PixelFormat = RenderPixelFormat.Bgra8Unorm,
    Width = 1920,
    Height = 1080,
    SampleCount = 1,
    DeviceHandle = device,
    CommandQueueHandle = queue,
    TargetHandle = image,
    TargetViewHandle = imageView,
};

RenderTargetDescriptorValidator.ThrowIfInvalid(descriptor);
```
