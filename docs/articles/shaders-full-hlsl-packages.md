---
title: Compiler-Backed HLSL Shader Packages
---

# Compiler-Backed HLSL Shader Packages

RoyalTerminal now has a compiler-backed package model for full HLSL shader work. This model is separate from the lightweight Windows Terminal compatibility adapter described in [Windows Terminal HLSL Shader Compatibility](/articles/shaders-windows-terminal-hlsl).

The package model, compiler contracts, include resolver, runtime contracts, and diagnostics live in `RoyalTerminal.Shaders`. The project is dependency-free, so hosts can validate, translate, and compile shader packages without taking an Avalonia, SkiaSharp, terminal model, or native renderer dependency.

Use shader packages when you need real HLSL concepts:

- multiple source files and includes
- explicit passes
- pixel and compute stages
- additional textures, samplers, constant buffers, UAVs, and pass outputs
- compiler diagnostics against original source files
- backend capability validation before runtime execution

## Current implementation status

The implemented foundation includes:

| Area | Status |
| --- | --- |
| Package model | Implemented with `TerminalShaderPackage`, files, passes, resources, and options. |
| Include resolution | Implemented with virtual paths, external include providers, cycle detection, and path safety diagnostics. |
| Package validation | Implemented for file/pass/resource invariants, register conflicts, pass ordering, compute dispatch, and target profile mismatches. |
| Compiler pipeline | Implemented through `TerminalShaderCompilationPipeline` and `ITerminalShaderCompiler`. |
| DXC CLI compiler | Implemented through `TerminalShaderDxcCliCompiler` when a `dxc` executable is available. |
| Slang CLI compiler | Implemented through `TerminalShaderSlangCliCompiler` when a `slangc` executable is available. |
| Compiler cache | Implemented through `TerminalShaderCachingCompiler` with deterministic cache keys. |
| Source-side reflection preflight | Implemented through `TerminalShaderHlslReflectionScanner` for resources, entry-point semantics, compute thread groups, and basic constant-buffer packing. |
| Runtime contracts | Implemented through `ITerminalShaderRuntime`, runtime program/frame models, capabilities, and unavailable-backend diagnostics. |
| Runtime frame validation | Implemented through `TerminalShaderRuntimeValidator.ValidateFrameResources`. |
| Runtime orchestration | Implemented through `TerminalShaderRuntimePipeline` for external resource resolution and validation-gated frame execution. |
| Backend preference and diagnostics | Implemented through `TerminalShaderBackendPreference`, `TerminalShaderBackendSelector`, and `ITerminalShaderDiagnosticsSink`. |
| Avalonia package configuration | Implemented through `TerminalControl.ShaderPackage`, `ShaderBackendPreference`, `ShaderResourceProvider`, and `ShaderDiagnosticsSink`. |
| Native GPU execution | Not implemented yet. D3D11/D3D12/Vulkan/Metal runtime backends still need native execution code. |

The important distinction is that HLSL compilation can now be represented and executed through a real compiler backend, but applying compiled DXIL/SPIR-V/MSL to the terminal frame still requires a native runtime backend.

## Package shape

```csharp
TerminalShaderPackage package = new(
    "crt-full",
    [
        new TerminalShaderFile(
            "main.hlsl",
            """
            #include "common/color.hlsl"

            struct PSInput
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Texture2D terminalFrame : register(t0);
            SamplerState linearSampler : register(s0);

            float4 Main(PSInput input) : SV_TARGET
            {
                float4 color = terminalFrame.Sample(linearSampler, input.uv);
                return ApplyCrt(color);
            }
            """),
    ],
    [
        new TerminalShaderPass(
            "main",
            TerminalShaderStage.Pixel,
            "main.hlsl",
            "Main",
            TerminalShaderTargetProfile.PixelShader60,
            inputs: [new TerminalShaderPassInput("terminalFrame")],
            outputs: [new TerminalShaderPassOutput("final")]),
    ],
    [
        new TerminalShaderResourceBinding(
            "terminalFrame",
            TerminalShaderResourceKind.TerminalFramebuffer,
            TerminalShaderResourceSource.BuiltIn,
            TerminalShaderValueType.Texture2D,
            registerIndex: 0),
    ],
    new TerminalShaderPackageOptions(allowExternalIncludes: true));
```

## Include providers

External includes are opt-in. Hosts provide includes through `ITerminalShaderIncludeProvider`:

```csharp
TerminalShaderInMemoryIncludeProvider includeProvider = new(
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["common/color.hlsl"] = "float4 ApplyCrt(float4 color) { return color; }",
    });
```

The resolver rejects traversal paths, detects cycles, and requires providers to return the requested virtual path.

## Validation

Validate packages before compiling:

```csharp
TerminalShaderPackageValidationResult validation =
    TerminalShaderPackageValidator.Validate(package);

if (!validation.IsValid)
{
    foreach (TerminalShaderDiagnostic diagnostic in validation.Diagnostics)
    {
        // Show diagnostics in settings UI or logs.
    }
}
```

Runtime capability checks are available separately:

```csharp
TerminalShaderPackageValidationResult capabilityValidation =
    TerminalShaderRuntimeValidator.ValidateCapabilities(package, capabilities);
```

This catches cases such as compute passes on a non-compute backend or UAV resources on a backend that cannot bind writable resources.

## Compiling with DXC

`TerminalShaderDxcCliCompiler` invokes a `dxc` executable and returns compiled pass bytes:

```csharp
ITerminalShaderCompiler compiler = new TerminalShaderDxcCliCompiler("dxc");
TerminalShaderCompilationOptions options = new(
    TerminalShaderBackendKind.D3D11,
    TerminalShaderCompilerKind.Dxc,
    new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ENABLE_SCANLINES"] = "1",
    });

TerminalShaderCompilationResult result =
    await TerminalShaderCompilationPipeline.CompileAsync(
        package,
        compiler,
        options,
        includeProvider);
```

For Vulkan targets, the DXC CLI backend emits SPIR-V by adding `-spirv`. Metal targets require the Slang CLI backend.

## Compiling with Slang

`TerminalShaderSlangCliCompiler` invokes a `slangc` executable and can target DXIL, SPIR-V, or MSL depending on `TerminalShaderBackendKind`:

```csharp
ITerminalShaderCompiler compiler = new TerminalShaderSlangCliCompiler("slangc");
TerminalShaderCompilationOptions options = new(
    TerminalShaderBackendKind.Vulkan,
    TerminalShaderCompilerKind.Slang);

TerminalShaderCompilationResult result =
    await TerminalShaderCompilationPipeline.CompileAsync(
        package,
        new TerminalShaderCachingCompiler(compiler),
        options,
        includeProvider);
```

Use `TerminalShaderCachingCompiler` around DXC or Slang when packages are recompiled from settings or profile state. The cache key includes resolved source files, pass graph data, resources, target backend, compiler kind, defines, and debug name.

## Reflection preflight

`TerminalShaderHlslReflectionScanner` provides deterministic source-side reflection before a native compiler or GPU backend is available:

```csharp
TerminalShaderReflectionResult reflection =
    TerminalShaderHlslReflectionScanner.ScanPackage(package);
```

It identifies common HLSL resources, explicit registers, entry-point input/output semantics, compute `[numthreads]`, and basic constant-buffer packing. Native compiler reflection should still be preferred by runtime backends because HLSL source scanning cannot fully resolve macros, overloads, templates, or compiler-lowered resource layouts.

## Runtime boundary

The runtime contract is intentionally backend-neutral:

```csharp
public interface ITerminalShaderRuntime : IDisposable
{
    TerminalShaderBackendCapabilities Capabilities { get; }

    ValueTask<TerminalShaderRuntimeProgram> CreateProgramAsync(
        TerminalShaderCompilationResult compilation,
        CancellationToken cancellationToken = default);

    ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
        TerminalShaderRuntimeProgram program,
        TerminalShaderFrameRequest frame,
        CancellationToken cancellationToken = default);
}
```

`TerminalShaderFrameRequest` carries primitive frame dimensions, timing, scale, and resource values. Renderer-specific adapters should map their own richer frame context into this contract before invoking a runtime backend.

`TerminalShaderRuntimePipeline` resolves external resources through `ITerminalShaderResourceProvider`, validates backend capabilities, validates frame resources and texture-size limits, rejects backend/program mismatches, and only then invokes the runtime:

```csharp
TerminalShaderFrameRequest frame =
    await TerminalShaderRuntimePipeline.CreateFrameRequestAsync(
        package,
        width,
        height,
        time,
        timeDelta,
        frameIndex,
        scale,
        resourceProvider);

TerminalShaderFrameResult result =
    await TerminalShaderRuntimePipeline.RenderFrameAsync(
        package,
        runtime,
        program,
        frame);
```

`TerminalShaderUnavailableRuntime` is provided as a deterministic diagnostic runtime when a backend is requested but unavailable.

## Avalonia control surface

`TerminalControl` exposes the full-package configuration surface separately from `ShaderSources`:

```csharp
terminal.ShaderPackage = package;
terminal.ShaderBackendPreference = TerminalShaderBackendPreference.D3D11;
terminal.ShaderResourceProvider = resourceProvider;
terminal.ShaderDiagnosticsSink = diagnosticsSink;
```

The control validates assigned packages and reports backend availability through `ShaderDiagnosticsSink`. Native execution is still pending; until a D3D/Vulkan/Metal runtime is registered and wired into the renderer, the control emits `RTSHADERCONTROL001` and continues rendering without package shaders.

## Testing surface

The current tests cover:

- package validation
- include resolution
- include safety
- compiler orchestration
- unavailable compiler diagnostics
- optional DXC CLI compilation when `dxc` is available
- Slang CLI unavailable diagnostics and optional compilation when `slangc` is available
- deterministic compiler caching
- source-side HLSL reflection for resources, semantics, thread groups, and constant buffers
- runtime capability validation
- runtime frame resource validation
- runtime pipeline resource resolution and validation-gated execution
- backend preference selection and deterministic unavailable-runtime creation
- `TerminalControl` full-package properties and unavailable-backend diagnostics
- unavailable runtime diagnostics

Native GPU runtime tests should be added with backend gates when D3D11, D3D12, Vulkan, or Metal execution backends are implemented.
