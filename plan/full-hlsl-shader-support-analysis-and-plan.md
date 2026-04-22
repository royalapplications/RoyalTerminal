# Full HLSL Shader Support Analysis And Implementation Plan

Date: 2026-04-22

Branch context: `feature/shader-support`

## Goal

Implement full shader support for terminal post-processing beyond the current Windows Terminal sample adapter. The target is to support the limitations currently documented for `TerminalShaderLanguage.WindowsTerminalHlsl`:

- arbitrary HLSL syntax outside the current pixel-shader shape
- additional textures, samplers, UAVs, and constant buffers
- multi-pass effects
- compute shaders
- external include files after translation
- DirectX-only intrinsics with no Skia equivalent
- semantic-dependent code that cannot be reduced to `fragCoord`, `pos`, and `uv`

The current implementation should remain as a lightweight compatibility path. Full support requires a new compiler-backed shader pipeline and GPU execution backend.

## Current State

The current implementation is intentionally simple:

- `TerminalShaderSourceTranslator` rewrites known HLSL patterns into Skia Runtime Effect source.
- `TerminalShaderPostProcessor` compiles that generated SkSL with `SKRuntimeEffect.CreateShader`.
- `TerminalDrawHandler` renders the terminal into an offscreen Skia frame and applies one or more Skia shader passes.
- The shader input model is a single framebuffer texture with a fixed uniform prelude.

This supports common Windows Terminal shader samples but cannot become a full HLSL implementation by adding more regular expressions.

## Progress Update 2026-04-22

Implementation has moved beyond the original source-adapter-only state. The managed package execution bridge and first native backend package now exist, but cross-platform native backends and zero-copy presentation are still the main remaining gaps.

Completed:

- Split the non-terminal shader layer into the dependency-free `RoyalTerminal.Shaders` project.
- Implemented the full package model, files, passes, resources, diagnostics, include providers, include resolution, and package validation.
- Implemented compiler abstraction through `ITerminalShaderCompiler` and `TerminalShaderCompilationPipeline`.
- Implemented DXC command-line compilation through `TerminalShaderDxcCliCompiler`, including unavailable-compiler diagnostics and optional real compiler tests.
- Implemented Slang command-line compilation through `TerminalShaderSlangCliCompiler`, including unavailable-compiler diagnostics and optional real compiler tests.
- Implemented deterministic compiler cache keys and `TerminalShaderCachingCompiler`.
- Implemented source-side HLSL reflection preflight through `TerminalShaderHlslReflectionScanner` for common resource declarations, explicit registers, entry-point input/output semantics, compute `[numthreads]`, and basic constant-buffer packing.
- Implemented backend capability validation and frame resource validation through `TerminalShaderRuntimeValidator`.
- Implemented runtime contracts, runtime program/frame/result models, resource values, resource providers, and `TerminalShaderUnavailableRuntime`.
- Implemented runtime orchestration through `TerminalShaderRuntimePipeline`, including external resource resolution and validation-gated frame execution.
- Implemented built-in terminal framebuffer resource propagation for full shader package frames.
- Implemented `ITerminalShaderPackageExecutor` and `TerminalShaderCompilerRuntimePackageExecutor` so a package can compile once, cache its runtime program, and execute frames through a concrete runtime.
- Implemented backend preference selection through `TerminalShaderBackendPreference` and `TerminalShaderBackendSelector`.
- Implemented shader diagnostics sink contract through `ITerminalShaderDiagnosticsSink`.
- Implemented the `TerminalControl.ShaderPackage`, `ShaderBackendPreference`, `ShaderResourceProvider`, `ShaderDiagnosticsSink`, and `ShaderPackageExecutor` configuration surface.
- Implemented real `TerminalControl.ShaderPackage` propagation through `TerminalPresenter` and `TerminalDrawHandler`, including terminal framebuffer capture, runtime frame creation, package execution, diagnostic reporting, CPU pixel fallback drawing, and native-texture fallback diagnostics.
- Implemented the `RoyalTerminal.Shaders.D3D11` project with a Windows-only D3DCompiler DXBC compiler path and a Direct3D 11 runtime backend skeleton that creates native shaders, binds SRV/UAV/sampler/cbuffer resources, executes pixel and compute passes, and reads back the final frame for tests/presentation fallback.
- Added full HLSL package samples to the demo catalog: CRT bloom, two-pass bloom blur, and compute phosphor.
- Added tests for package validation, include resolution, compiler orchestration, DXC diagnostics, Slang diagnostics, compiler caching, reflection preflight, runtime capability validation, runtime frame validation, runtime pipeline gating, and unavailable runtime diagnostics.
- Added tests for package executor caching, built-in framebuffer resources, control behavior with an executor, demo package validation, D3D11 non-Windows gating, and an opt-in D3D11 GPU smoke test behind `ROYALTERMINAL_TEST_D3D11=1`.
- Implemented D3D11 compiler-native DXBC reflection extraction through D3DCompiler, including bound resources, constant-buffer sizes, input/output semantics, and compute thread-group metadata, with source-scanner fallback when reflection is unavailable.
- Added opt-in D3D11 DXBC reflection coverage behind `ROYALTERMINAL_TEST_D3D11=1`.
- Updated public docs and API package configuration for `RoyalTerminal.Shaders`.

Partially complete:

- Reflection now has D3D11 compiler-native DXBC extraction. DXIL, SPIR-V, and Slang reflection metadata extraction remains to be implemented for the other compiler/runtime paths.
- Compiler support is CLI-based. Native `dxcompiler` and `slang` hosting remain future improvements.
- Runtime validation detects missing external resources, resource-kind mismatches, UAV capability issues, unsupported stages, and texture-size limit violations.
- Direct3D 11 execution is implemented as the first native backend path, and the compiler now reflects DXBC bindings natively. It still needs Windows GPU validation for the opt-in smoke test and broader resource-binding/corpus coverage before it should be treated as production complete.
- `TerminalControl` can execute packages through an injected executor today. The demo app ships package samples, but it does not yet auto-create a platform runtime/compiler because native runtime registration and trust policy need composition-root wiring.

Not complete:

- D3D12/Vulkan/Metal native GPU runtime backends.
- Compiler-native DXIL, SPIR-V, and Slang reflection extraction.
- Zero-copy Skia/Avalonia GPU texture import for full HLSL output.
- Runtime registration/composition root wiring for demo/application opt-in execution.
- Golden image tests, full corpus tests, and performance test suites.

Current next implementation priority:

1. Validate and harden the D3D11 backend on Windows with `ROYALTERMINAL_TEST_D3D11=1`.
2. Add compiler-native DXIL, SPIR-V, and Slang reflection extraction so non-D3D11 paths no longer rely on source preflight.
3. Add platform runtime registration and zero-copy import adapters.
4. Implement Vulkan and Metal runtimes after D3D11 validation is stable.

### Remaining Phase Status

| Phase | Status | Notes |
| --- | --- | --- |
| Phase 0: Design lock | Mostly complete | Public names and backend strategy are represented in the plan; final native dependency packaging is still open. |
| Phase 1: Package model and validation | Complete | Package/files/passes/resources/options/diagnostics/include validation are implemented and tested. |
| Phase 2: Compiler abstraction | Mostly complete | DXC and Slang CLI paths, cache keys, and compiler tests exist. Native compiler hosting and compiler version keys remain open. |
| Phase 3: Reflection and binding | Partially complete | Source-side reflection preflight exists, and D3D11 now consumes compiler-native DXBC reflection for resources, semantics, cbuffer sizes, and compute thread groups. Compiler-native DXIL/SPIR-V/Slang reflection and full binding maps remain open. |
| Phase 4: First runtime backend | Partially complete | `RoyalTerminal.Shaders.D3D11` now contains DXBC compilation, native DXBC reflection, native shader creation, SRV/UAV/sampler/cbuffer binding, pixel/compute pass execution, and readback. Windows GPU validation and hardening remain. |
| Phase 5: Avalonia integration | Mostly complete | Control properties, backend preference, resource provider, diagnostics sink, package executor, runtime frame creation, and CPU fallback drawing exist. Runtime registration and zero-copy import remain. |
| Phase 6: Cross-platform backends | Not complete | Vulkan and Metal runtime packages remain future work after D3D11 MVP. |
| Phase 7: Corpus, docs, and demo | Partially complete | Docs, simple shader samples, and full HLSL package samples exist. Full corpus, GPU golden images, package runtime registration UI, and performance suites remain open. |

## Source-Grounded Constraints

### HLSL is a real language, not a pattern format

The HLSL specification covers grammar, semantics, and standard shader-library data types. It explicitly describes HLSL as a GPU programming language influenced by C/C++ and implemented by FXC and DXC. Full language support therefore needs a real parser/compiler, not string rewriting.

Primary reference: <https://microsoft.github.io/hlsl-specs/specs/hlsl.html>

### DXC is the correct Microsoft compiler for modern HLSL

The DirectX Shader Compiler project provides `dxc.exe` and `dxcompiler.dll`, compiles HLSL to DXIL, and also includes SPIR-V code generation. Microsoft Windows SDK releases include the supported compiler and validator.

Primary reference: <https://github.com/microsoft/DirectXShaderCompiler>

### Slang is the best cross-platform compiler candidate

Slang is HLSL-like, compiles most existing HLSL with little or no modification, supports Windows, Linux, and macOS, and can target D3D11, D3D12, Vulkan/SPIR-V, Metal/MSL, WGSL, CUDA, and CPU-oriented outputs. It also has a module system and reflection-oriented workflows.

Primary reference: <https://github.com/shader-slang/slang>

### Skia Runtime Effects are not a complete GPU pipeline

Skia Runtime Effects bind child shaders and sample them with `.eval()`. They are excellent for single-pass color shaders, but they do not expose the full HLSL resource model, compute dispatch, UAV writes, descriptor sets, or shader-stage semantics.

Primary reference: <https://skia.org/docs/user/sksl/>

### Resource binding and semantics matter

HLSL uses semantics to identify values crossing shader stages, and resources are bound through registers such as `b#`, `t#`, and `s#`. Full support must preserve that binding model instead of collapsing it into `pos` and `uv`.

Primary references:

- <https://learn.microsoft.com/en-us/windows/win32/direct3dgetstarted/work-with-shaders-and-shader-resources>
- <https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-semantics>

### Compute support requires dispatch, thread-group metadata, and writable resources

HLSL compute shaders use `[numthreads(x, y, z)]` and dispatch grids. Thread IDs such as `SV_DispatchThreadID`, `SV_GroupThreadID`, `SV_GroupID`, and `SV_GroupIndex` must be provided by a compute-capable GPU backend.

Primary reference: <https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/sm5-attributes-numthreads>

### Avalonia can host custom GPU interop, but that is a separate path

Avalonia supports custom rendering through Skia leases and GPU interop with composition surfaces. Full HLSL support should not be forced through the existing Skia Runtime Effect path; it needs a backend that owns GPU textures and synchronization, then imports the final texture into Avalonia.

Primary reference: <https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering>

## Architectural Decision

Do not attempt to make `TerminalShaderSourceTranslator` a full compiler.

Instead, split shader execution into two pipelines:

1. **Skia Runtime Effect Pipeline**
   - Existing pipeline.
   - Supports direct SkSL, Ghostty/Shadertoy single-pass compatibility, and Windows Terminal sample compatibility.
   - Runs everywhere Skia rendering works.
   - Remains the default for simple effects.

2. **Compiler-Backed GPU Shader Pipeline**
   - New pipeline.
   - Uses DXC and/or Slang for real HLSL compilation.
   - Uses reflection to discover resources, semantics, entry points, stages, and pass requirements.
   - Executes passes through a real GPU backend.
   - Imports the final texture into Avalonia for composition.

This is the only path that can fully support arbitrary HLSL syntax, resource binding, UAVs, compute dispatch, semantic-dependent code, and DirectX-specific functionality.

## Proposed Public Model

### Shader Package

Add a package-level model instead of treating a shader as one source string:

```csharp
public sealed class TerminalShaderPackage
{
    public string Name { get; }
    public IReadOnlyList<TerminalShaderFile> Files { get; }
    public IReadOnlyList<TerminalShaderPass> Passes { get; }
    public IReadOnlyList<TerminalShaderResourceBinding> Resources { get; }
    public TerminalShaderPackageOptions Options { get; }
}
```

`TerminalShaderSource` can continue to exist for single-file/simple shaders. `TerminalShaderPackage` becomes the full-feature entry point.

### Shader Files And Include Roots

```csharp
public sealed class TerminalShaderFile
{
    public string VirtualPath { get; }
    public string Source { get; }
}

public interface ITerminalShaderIncludeProvider
{
    ValueTask<TerminalShaderFile?> TryLoadAsync(string includePath, string? includingFile, CancellationToken cancellationToken);
}
```

Requirements:

- include paths are virtualized
- relative includes resolve from the including file
- hosts can provide file-system, embedded, or in-memory include providers
- include resolution is deterministic and sandboxed
- include dependency graphs are cached and invalidated

### Shader Pass Graph

```csharp
public sealed class TerminalShaderPass
{
    public string Name { get; }
    public TerminalShaderStage Stage { get; }
    public string EntryPoint { get; }
    public string SourcePath { get; }
    public TerminalShaderTargetProfile TargetProfile { get; }
    public TerminalShaderDispatch? Dispatch { get; }
    public IReadOnlyList<TerminalShaderPassInput> Inputs { get; }
    public IReadOnlyList<TerminalShaderPassOutput> Outputs { get; }
}
```

Supported pass types:

- pixel pass
- compute pass
- copy/blit pass
- clear pass

Supported graph features:

- named intermediate render targets
- ping-pong buffers
- frame history buffers
- previous frame input
- pass-level viewport size
- scale-aware resizing
- explicit pass ordering
- future dependency sorting for declarative graphs

### Resource Binding

```csharp
public sealed class TerminalShaderResourceBinding
{
    public string Name { get; }
    public TerminalShaderResourceKind Kind { get; }
    public int RegisterIndex { get; }
    public int RegisterSpace { get; }
    public TerminalShaderValueType ValueType { get; }
    public TerminalShaderResourceSource Source { get; }
}
```

Resource kinds:

- terminal framebuffer SRV
- external texture SRV
- sampler
- constant buffer
- structured buffer
- byte-address buffer
- UAV texture
- UAV buffer
- render-target attachment
- history attachment

### Host-Facing Control API

Add to `TerminalControl`:

```csharp
public TerminalShaderPackage? ShaderPackage { get; set; }
public TerminalShaderBackendPreference ShaderBackendPreference { get; set; }
public ITerminalShaderResourceProvider? ShaderResourceProvider { get; set; }
public ITerminalShaderDiagnosticsSink? ShaderDiagnosticsSink { get; set; }
```

Keep `ShaderSources` for simple effects and current docs compatibility.

## Compiler Layer

### Contracts

```csharp
public interface ITerminalShaderCompiler
{
    ValueTask<TerminalShaderCompilationResult> CompileAsync(
        TerminalShaderCompilationRequest request,
        CancellationToken cancellationToken);
}
```

Compilation result:

- compiled bytecode per pass
- backend target format
- reflection data
- diagnostics
- dependency list
- content hash

### Compiler Backends

#### DXC Backend

Use for:

- Windows D3D12/D3D11 DXIL compilation
- strict Microsoft HLSL validation
- optional SPIR-V output for Vulkan when Slang is not selected

Deliverables:

- native loader for `dxcompiler`
- command-line fallback for dev machines
- managed wrapper around `IDxcCompiler3` if native hosting is selected
- diagnostic normalization
- include handler
- shader profile selection
- caching by source hash, defines, include graph, target profile, and compiler version

#### Slang Backend

Use for:

- cross-platform HLSL/Slang compilation
- Vulkan/SPIR-V output
- Metal/MSL output
- optional D3D HLSL/DXIL flows if Slang proves sufficient

Deliverables:

- native loader for `slang`
- session/module setup
- target configuration
- include provider bridge
- reflection extraction
- diagnostic normalization
- compilation cache

### Compiler Selection

Default policy:

| Platform/backend | Preferred compiler |
| --- | --- |
| Windows D3D12 | DXC first, Slang optional |
| Windows D3D11 | DXC first, Slang optional |
| Linux Vulkan | Slang first, DXC SPIR-V optional |
| macOS Metal | Slang MSL first |
| Software/Skia-only | unsupported for full HLSL; use SkSL path |

## Reflection Layer

Create a normalized reflection model independent of DXC/Slang/SPIR-V:

```csharp
public sealed class TerminalShaderReflection
{
    public IReadOnlyList<TerminalShaderEntryPointReflection> EntryPoints { get; }
    public IReadOnlyList<TerminalShaderResourceReflection> Resources { get; }
    public IReadOnlyList<TerminalShaderSemanticReflection> Inputs { get; }
    public IReadOnlyList<TerminalShaderSemanticReflection> Outputs { get; }
}
```

Responsibilities:

- map HLSL registers to backend descriptors
- validate required terminal resources
- validate user-provided external resources
- discover cbuffer layout and packing
- map semantic inputs for pixel stages
- map compute thread-group sizes
- detect unsupported intrinsic/backend combinations
- produce actionable diagnostics before runtime execution

For SPIR-V paths, use a reflection library such as SPIRV-Cross or SPIRV-Reflect to extract descriptor sets, bindings, push constants, stage inputs, stage outputs, storage images, and buffers.

## GPU Runtime Layer

### Contracts

```csharp
public interface ITerminalShaderRuntime : IDisposable
{
    TerminalShaderBackendKind BackendKind { get; }

    ValueTask<TerminalShaderRuntimeProgram> CreateProgramAsync(
        TerminalShaderCompilationResult compilation,
        CancellationToken cancellationToken);

    ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
        TerminalShaderRuntimeProgram program,
        TerminalShaderFrameRequest frame,
        CancellationToken cancellationToken);
}
```

Frame request:

- terminal framebuffer texture
- target size
- render scale
- time/frame counters
- cursor metadata
- theme colors
- external resources
- previous frame attachments

Frame result:

- final GPU texture handle
- synchronization primitives
- backend metadata needed by Avalonia import
- fallback bitmap if GPU import is unavailable

### Backends

#### D3D11 Backend

Purpose:

- high Windows compatibility
- natural fit for many Windows Terminal-era HLSL samples
- simpler than D3D12 for resource management

Needed components:

- device/context lifecycle
- render target texture pool
- SRV/UAV/RTV creation
- sampler creation
- constant buffer upload
- pixel shader fullscreen triangle
- compute dispatch
- readback for tests
- shared texture handle export/import into Avalonia when available

#### D3D12 Backend

Purpose:

- modern DXIL pipeline
- better fit for Shader Model 6 features and wave intrinsics

Needed components:

- device/queue/command-list lifecycle
- descriptor heaps
- root signatures from reflection
- pipeline state objects
- resource state transitions
- fences
- render target and UAV pools
- shared handle export/import
- readback for tests

#### Vulkan Backend

Purpose:

- Linux and optional Windows cross-platform GPU execution
- SPIR-V target path

Needed components:

- instance/device/queue lifecycle
- descriptor set layout from reflection
- pipeline layouts
- graphics and compute pipelines
- render passes or dynamic rendering
- image layout transitions
- storage image support
- sampler and uniform buffer upload
- external memory/semaphore interop where Avalonia supports it
- readback for tests

#### Metal Backend

Purpose:

- macOS full shader execution

Needed components:

- MSL compilation path through Slang
- device/command queue lifecycle
- render and compute pipeline states
- texture and buffer pools
- sampler state creation
- IOSurface or other import path for Avalonia composition
- readback for tests

### Skia Interop

The full GPU runtime should not rely on `SKRuntimeEffect`, but it still needs to interoperate with the current terminal renderer.

Phase 1:

- render terminal frame into an `SKSurface`
- snapshot/read or export it into the GPU runtime input texture
- execute full shader graph
- import final GPU texture into Avalonia or copy back to Skia as fallback

Phase 2:

- avoid CPU readback by sharing GPU-backed Skia textures where available
- integrate with Avalonia GPU leases and external image import
- use synchronization primitives instead of blocking copies

## Addressing Each Limitation

### Arbitrary HLSL Syntax

Plan:

- route `TerminalShaderLanguage.WindowsTerminalHlsl` full-mode packages through DXC or Slang
- stop regex translation for full packages
- support defines, profiles, entry points, and include paths
- preserve compiler diagnostics with file/line mapping

Acceptance:

- shader code with structs, helper functions, macros, loops, branches, overloaded functions, and user-defined types compiles
- compiler errors point to original source paths and line numbers

### Additional Textures, Samplers, UAVs, And Constant Buffers

Plan:

- add a normalized resource-binding model
- reflect resources from compiled shader output
- let hosts bind resources by name or register
- provide built-in bindings for terminal framebuffer, history buffers, and generated noise/utility textures
- support immutable and dynamic constant buffers
- support writable UAV textures/buffers in compute and pixel stages where backend allows them

Acceptance:

- multiple `Texture2D` and `SamplerState` declarations work
- `RWTexture2D` and `RWStructuredBuffer` work in compute passes
- multiple cbuffers bind with correct 16-byte packing

### Multi-Pass Effects

Plan:

- introduce `TerminalShaderPackage.Passes`
- allocate named intermediate render targets
- add pass graph validation
- support ping-pong and history buffers
- support pass-level output sizes
- support both pixel and compute passes in one graph

Acceptance:

- blur, bloom, CRT phosphor, feedback, and temporal persistence effects run as package graphs
- resizing invalidates and recreates dependent attachments correctly

### Compute Shaders

Plan:

- add `TerminalShaderStage.Compute`
- parse/reflect `[numthreads]`
- expose dispatch dimensions as explicit settings or automatic sizing expressions
- support UAV writes and SRV reads
- add memory barrier/resource transition handling per backend

Acceptance:

- compute shader can write an output texture consumed by a later pixel pass
- compute shader can run at full frame size and at downsampled sizes
- thread ID semantics produce expected pixel mapping

### External Include Files

Plan:

- implement `ITerminalShaderIncludeProvider`
- support in-memory packages, file-system roots, and embedded resources
- preserve include dependency graph for caching and reload
- block path traversal outside configured roots
- preserve compiler diagnostics with virtual file names

Acceptance:

- nested relative includes work
- shared include libraries compile across multiple packages
- missing include diagnostics identify the requesting file and include path

### DirectX-Only Intrinsics With No Skia Equivalent

Plan:

- classify intrinsics by execution requirement:
  - portable math intrinsic
  - derivative/sample intrinsic
  - wave/subgroup intrinsic
  - resource/descriptor intrinsic
  - DirectX-only intrinsic
  - vendor extension intrinsic
- use real GPU backends for intrinsic execution
- map portable intrinsics via compiler target where possible
- reject unsupported backend/intrinsic combinations during reflection/validation
- provide backend capability diagnostics

Acceptance:

- DirectX-specific shader model features run on D3D backends when supported
- unsupported features fail before runtime with a clear backend capability error
- portable features compile on Vulkan/Metal through Slang where supported

### Semantic-Dependent Code

Plan:

- preserve entry-point signatures instead of replacing them with `pos` and `uv`
- support reflected pixel shader inputs:
  - `SV_Position`
  - `TEXCOORDn`
  - `COLORn`
  - custom user semantics
- generate fullscreen vertex inputs for pixel passes
- define RoyalTerminal-specific semantic conventions for terminal metadata when needed
- support Direct3D 9 legacy aliases only through an explicit compatibility flag

Acceptance:

- shaders using multiple `TEXCOORD` inputs compile and receive expected values
- shaders using `SV_Position` receive pixel coordinates
- shaders using custom semantics receive configured host data or fail with clear missing-binding diagnostics

## Security And Trust

Full shader support executes user-provided GPU programs. Treat it as trusted-code execution unless the host explicitly sandboxed it.

Requirements:

- disabled by default for untrusted profile files
- host opt-in for file-system include providers
- no implicit network include loading
- configurable maximum passes, texture sizes, and dispatch dimensions
- timeout/watchdog where backend permits it
- diagnostic-only validation path before enabling a package
- no automatic execution from downloaded profiles without user confirmation

## Testing Strategy

### Unit Tests

Add tests for:

- shader package validation
- include resolution and sandboxing
- define/profile/entry-point options
- pass graph validation
- resource binding normalization
- constant buffer packing
- semantic mapping
- diagnostics normalization
- cache-key generation

### Compiler Tests

Use small fixture shaders for:

- arbitrary HLSL syntax
- macro-heavy HLSL
- nested includes
- multiple cbuffers
- multiple textures/samplers
- UAV declarations
- pixel shader semantics
- compute shader `[numthreads]`
- unsupported intrinsic diagnostics

Run each fixture through:

- DXC compile path on Windows
- Slang compile path on Windows/Linux/macOS where native binaries are available
- SPIR-V reflection path for Vulkan-target fixtures

### Runtime GPU Tests

Add a new test category, for example `RoyalTerminal.Tests.Shaders.Gpu`, and gate it with environment variables:

- `ROYALTERMINAL_TEST_D3D11=1`
- `ROYALTERMINAL_TEST_D3D12=1`
- `ROYALTERMINAL_TEST_VULKAN=1`
- `ROYALTERMINAL_TEST_METAL=1`
- `ROYALTERMINAL_TEST_SHADER_COMPILERS=1`

Runtime tests:

- single-pass HLSL pixel shader output
- multi-texture sampling
- sampler filtering/address modes
- constant buffer values
- UAV write/readback
- compute pass followed by pixel pass
- multi-pass blur/bloom
- resize and attachment recreation
- frame history feedback
- invalid package fallback
- renderer disposal and resource release

### Golden Image Tests

Create deterministic frame fixtures:

- simple checkerboard terminal frame
- text/cursor frame
- alpha/transparency frame
- color palette frame

Compare output with tolerances:

- exact byte compare for simple integer-color shaders
- per-channel tolerance for filtered/float effects
- backend-specific baselines only when unavoidable

### Avalonia Integration Tests

Use headless tests for the existing Skia path.

Use GPU integration tests only on agents with the relevant backend:

- D3D on Windows runners
- Vulkan on Linux runners with software Vulkan or GPU runner
- Metal on macOS runners if available

Validate:

- `TerminalControl.ShaderPackage` property propagation
- renderer fallback when backend unavailable
- animated graph invalidation
- composition target import path
- screenshot output is nonblank and matches expected broad visual properties

### Corpus Tests

Create a curated corpus:

- Windows Terminal sample shaders
- selected Hammster `windows-terminal-shaders` samples
- synthetic shaders for each resource/semantic/compute case
- negative shaders for diagnostics

Rules:

- store third-party shaders only if license-compatible
- otherwise fetch in CI only when explicitly allowed, or keep minimal locally-authored equivalents
- record expected compiler/backend support per fixture

### Performance Tests

Add benchmarks for:

- compile time cold/warm cache
- per-frame single-pass runtime
- per-frame multi-pass runtime
- CPU readback fallback cost
- GPU interop path cost
- allocation count per frame

Track:

- frame time
- GPU/CPU synchronization stalls
- texture allocation churn
- memory pressure during resize

## CI Plan

### Default CI

Run always:

- existing unit tests
- Skia Runtime Effect shader tests
- package validation tests that do not need native compilers
- docs build

### Extended Shader CI

Run on scheduled/manual workflows:

- compiler availability checks
- DXC tests on Windows
- Slang tests on Windows/Linux/macOS
- Vulkan runtime tests where available
- D3D11/D3D12 runtime tests on Windows GPU-capable runner
- Metal runtime tests on macOS where supported

### Artifact Collection

Collect:

- compiler diagnostics
- generated reflection JSON
- golden output images
- failure screenshots
- backend capability reports
- performance summaries

## Implementation Phases

### Phase 0: Design Lock And Issue Split

Deliverables:

- finalize public API names
- choose native dependency packaging approach
- decide DXC-only, Slang-only, or dual-compiler strategy for v1
- define minimal backend support matrix
- create tracking issues for compiler, reflection, runtime, docs, and tests

Exit criteria:

- no API ambiguity
- backend support matrix approved
- security posture approved

### Phase 1: Shader Package Model And Validation

Deliverables:

- `TerminalShaderPackage`
- pass graph model
- include provider contracts
- resource-binding model
- diagnostics model
- validation-only tests

Exit criteria:

- complex packages can be represented without compiling
- invalid packages produce stable diagnostics

### Phase 2: Compiler Abstraction

Deliverables:

- `ITerminalShaderCompiler`
- DXC compiler backend prototype
- Slang compiler backend prototype
- compiler cache
- include bridge
- diagnostics normalization
- compiler fixture tests

Exit criteria:

- arbitrary HLSL fixture compiles through at least one real compiler
- include and diagnostic tests pass

### Phase 3: Reflection And Binding

Deliverables:

- normalized reflection model
- cbuffer layout mapping
- resource binding mapper
- semantic mapper
- compute dispatch metadata extraction
- reflection fixture tests

Exit criteria:

- package resources can be matched to host resources before runtime
- missing/unsupported resources are diagnosed before drawing

### Phase 4: First Runtime Backend

Recommended first backend: D3D11 on Windows.

Reasons:

- direct fit for many Windows Terminal-era shaders
- less ceremony than D3D12
- enough support for pixel, compute, SRV, UAV, sampler, cbuffer, and readback tests

Deliverables:

- D3D11 runtime backend
- fullscreen triangle pixel pass
- compute dispatch
- texture/sampler/cbuffer/UAV binding
- readback test helper
- final-frame copy/import fallback

Exit criteria:

- single-pass, multi-pass, compute, UAV, and cbuffer tests pass on Windows

### Phase 5: Avalonia Integration

Deliverables:

- `TerminalControl.ShaderPackage`
- backend preference/property model
- runtime selection
- fallback behavior
- diagnostics sink
- Avalonia composition/import path
- integration tests

Exit criteria:

- demo can run a full HLSL package through D3D backend
- fallback path remains stable when GPU backend is unavailable

### Phase 6: Cross-Platform Backends

Deliverables:

- Vulkan backend with SPIR-V
- Metal backend with MSL
- Slang-driven cross-platform compiler path
- backend capability reporting
- cross-platform CI coverage where practical

Exit criteria:

- same portable shader package runs on at least Windows and Linux
- macOS support is validated or explicitly marked experimental

### Phase 7: Full Corpus, Docs, And Demo

Deliverables:

- curated shader corpus
- full docs for packages, resources, includes, passes, compute, and backend support
- demo package loader
- diagnostics UI
- performance notes

Exit criteria:

- public docs explain supported and unsupported backend combinations
- demo exercises pixel, multi-pass, and compute packages
- corpus tests provide broad regression coverage

## Risk Register

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Native compiler packaging increases install complexity | Medium | Keep Skia path dependency-free; package native compilers separately. |
| Cross-platform GPU interop differs by Avalonia backend | High | Start with copy/readback fallback; add zero-copy import backend by backend. |
| D3D12/Vulkan/Metal backends are large implementations | High | Ship one backend first; keep runtime contracts stable. |
| User shaders can hang GPU or allocate huge resources | High | Add trust model, limits, validation, and opt-in execution. |
| Golden images vary across GPUs | Medium | Use deterministic shaders and tolerance-based comparisons. |
| Slang/DXC behavior differs by version | Medium | Record compiler version in cache keys and test artifacts. |
| Vendor-specific intrinsics are not portable | Medium | Capability-gate and fail early with diagnostics. |

## Recommended MVP

The smallest meaningful "full HLSL" milestone is:

- `TerminalShaderPackage`
- external includes
- DXC compile path
- D3D11 runtime backend
- pixel shaders with arbitrary HLSL syntax
- multiple textures/samplers/cbuffers
- multi-pass render targets
- compute shaders with UAV output
- runtime readback tests
- demo package loader for full HLSL packages

Do not attempt Vulkan/Metal before the D3D11 MVP is stable.

## Definition Of Done

The full feature is complete when:

- HLSL source is compiled by a real compiler, not regex-translated
- shaders can use multiple files and includes
- shaders can declare and bind multiple cbuffers, textures, samplers, UAVs, and buffers
- pixel shaders can use reflected semantics instead of only `pos`/`uv`
- compute shaders can dispatch and write resources
- multi-pass effects can allocate and chain intermediate resources
- backend capability failures are reported before draw
- there are unit, compiler, reflection, GPU runtime, integration, golden image, corpus, and performance tests
- documentation clearly separates simple Skia shader support from full GPU HLSL support
