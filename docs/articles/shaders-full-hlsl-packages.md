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
| Runtime contracts | Implemented through `ITerminalShaderRuntime`, runtime program/frame models, capabilities, and unavailable-backend diagnostics. |
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

For Vulkan targets, the DXC CLI backend emits SPIR-V by adding `-spirv`. Metal targets require a Slang-based compiler backend and are not handled by DXC.

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

`TerminalShaderUnavailableRuntime` is provided as a deterministic diagnostic runtime when a backend is requested but unavailable.

## Testing surface

The current tests cover:

- package validation
- include resolution
- include safety
- compiler orchestration
- unavailable compiler diagnostics
- optional DXC CLI compilation when `dxc` is available
- runtime capability validation
- unavailable runtime diagnostics

Native GPU runtime tests should be added with backend gates when D3D11, D3D12, Vulkan, or Metal execution backends are implemented.
