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
| Compiler-assisted reflection | Implemented through D3D11 DXBC reflection, SPIR-V binary reflection, DXC listing reflection, and Slang JSON reflection, with source-scanner fallback. |
| Runtime contracts | Implemented through `ITerminalShaderRuntime`, runtime program/frame models, capabilities, and unavailable-backend diagnostics. |
| Runtime frame validation | Implemented through `TerminalShaderRuntimeValidator.ValidateFrameResources`. |
| Runtime orchestration | Implemented through `TerminalShaderRuntimePipeline` for external resource resolution and validation-gated frame execution. |
| Backend preference and diagnostics | Implemented through `TerminalShaderBackendPreference`, `TerminalShaderBackendSelector`, and `ITerminalShaderDiagnosticsSink`. |
| Runtime registration | Implemented through `TerminalShaderPackageExecutorRegistry` and `TerminalShaderPackageExecutorRegistration`. Hosts register concrete compiler/runtime pairs in the composition root. |
| Avalonia package configuration | Implemented through `TerminalControl.ShaderPackage`, `ShaderBackendPreference`, `ShaderResourceProvider`, `ShaderDiagnosticsSink`, `ShaderPackageExecutor`, and `ShaderNativeTexturePresenter`. |
| Native GPU execution | Implemented first for D3D11 through `RoyalTerminal.Shaders.D3D11`, including pixel/compute execution, SRV/UAV/sampler/cbuffer binding, external cbuffer binding, pass chaining, and CPU readback. D3D12/Vulkan/Metal runtime backends remain future work. |
| Native texture presentation | Implemented as a descriptor and presenter boundary. The default Avalonia Skia presenter imports compatible Metal, Vulkan, and D3D12 textures. The D3D11 runtime currently presents through CPU readback until a safe shared-texture lifetime bridge exists. |

The important distinction is that HLSL compilation, reflection, package execution, and the first native runtime are now represented end to end. Backends are still explicit opt-in dependencies; hosts must provide or create a compatible compiler/runtime executor before packages render.

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

For Vulkan targets, the DXC CLI backend emits SPIR-V by adding `-spirv` and reflection-friendly SPIR-V metadata with `-fspv-reflect`. DXIL targets also request a DXC listing so resource bindings and signatures can be reflected from compiler output. Metal targets require the Slang CLI backend.

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

The Slang CLI path requests `-reflection-json` and uses that output when it contains usable package metadata. Vulkan targets also request `-fspv-reflect` so SPIR-V output can be reflected directly.

Use `TerminalShaderCachingCompiler` around DXC or Slang when packages are recompiled from settings or profile state. The cache key includes resolved source files, pass graph data, resources, target backend, compiler kind, defines, and debug name.

## Reflection

RoyalTerminal uses compiler-generated reflection first where possible:

- D3D11 DXBC reflection through `RoyalTerminal.Shaders.D3D11`
- SPIR-V binary reflection through `TerminalShaderSpirVReflectionReader`
- DXC text-listing reflection through `TerminalShaderDxcReflectionListingReader`
- Slang JSON reflection through `TerminalShaderSlangReflectionJsonReader`

`TerminalShaderHlslReflectionScanner` remains as deterministic source-side fallback when compiler metadata is unavailable or incomplete:

```csharp
TerminalShaderReflectionResult reflection =
    TerminalShaderHlslReflectionScanner.ScanPackage(package);
```

It identifies common HLSL resources, explicit registers, entry-point input/output semantics, compute `[numthreads]`, and basic constant-buffer packing. Compiler metadata is preferred because source scanning cannot fully resolve macros, overloads, templates, or compiler-lowered resource layouts.

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

Register native backends at the host composition root:

```csharp
TerminalShaderPackageExecutorRegistry registry = new();
registry.Register(TerminalShaderD3D11PackageExecutorRegistration.Create());

TerminalShaderPackageExecutorCreationResult creation =
    registry.TryCreate(TerminalShaderBackendPreference.D3D11);

terminal.ShaderPackageExecutor = creation.Executor;
```

The registry keeps native dependencies explicit and lets hosts report `creation.Diagnostics` through their own settings or diagnostics UI. `TerminalShaderCompilerKind.D3DCompiler` identifies the D3D11 DXBC path; DXC and Slang remain available for compiler-backed package workflows where those tools are installed.

## Avalonia control surface

`TerminalControl` exposes the full-package configuration surface separately from `ShaderSources`:

```csharp
terminal.ShaderPackage = package;
terminal.ShaderBackendPreference = TerminalShaderBackendPreference.D3D11;
terminal.ShaderResourceProvider = resourceProvider;
terminal.ShaderDiagnosticsSink = diagnosticsSink;
terminal.ShaderPackageExecutor = executor;
```

The control validates assigned packages and reports backend availability through `ShaderDiagnosticsSink`. If `ShaderPackage` is set without `ShaderPackageExecutor`, the control emits `RTSHADERCONTROL001` and continues rendering without package shaders.

The demo app registers the D3D11 package executor and creates it on Windows when `TerminalShaderD3D11Compiler` and `TerminalShaderD3D11Runtime` are supported. Other hosts should make the same decision in their composition root so native compiler/runtime dependencies remain explicit.

## Native texture presentation

Runtime backends can return CPU pixel data, a native texture descriptor, or both in `TerminalShaderFrameResult`. The renderer draws CPU pixels first because that is the most portable presentation path. If no CPU pixels are available, `ShaderNativeTexturePresenter` can import the native texture into the active Avalonia Skia GPU context.

`TerminalShaderSkiaNativeTexturePresenter` supports compatible Metal, Vulkan, and D3D12 descriptors. D3D11 package execution currently returns CPU readback frames; exposing D3D11 native textures safely requires shared-handle/lifetime management that is separate from the runtime's per-frame graph.

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
- SPIR-V binary reflection
- DXC listing reflection
- Slang JSON reflection
- runtime capability validation
- runtime frame resource validation
- runtime pipeline resource resolution and validation-gated execution
- backend preference selection, runtime registration, and deterministic unavailable-runtime creation
- `TerminalControl` full-package properties and unavailable-backend diagnostics
- `TerminalControl` package execution with an injected executor
- native texture frame result and presenter wiring
- unavailable runtime diagnostics
- D3D11 non-Windows availability gates
- opt-in D3D11 GPU tests for pixel output, external constant buffers, multipass output chaining, compute/UAV output, and DXBC reflection behind `ROYALTERMINAL_TEST_D3D11=1`
- Skia shader golden-pixel tests
- curated demo and synthetic package corpus tests
- shader package validation/reflection/cache-key benchmarks in `RoyalTerminal.Benchmarks`

Native GPU runtime tests should be expanded with backend gates as D3D11 hardening continues and D3D12, Vulkan, or Metal execution backends are implemented.
