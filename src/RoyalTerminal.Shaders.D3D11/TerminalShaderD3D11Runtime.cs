// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders.D3D11 - Direct3D 11 shader runtime backend.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RoyalTerminal.Shaders;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace RoyalTerminal.Shaders.D3D11;

/// <summary>
/// Executes compiler-backed shader packages on a Direct3D 11 immediate context.
/// </summary>
public sealed class TerminalShaderD3D11Runtime : ITerminalShaderRuntime
{
    private const string FinalFrameResourceName = "__RoyalTerminalFinalFrame";
    private readonly object _syncRoot = new();
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11SamplerState _linearClampSampler;
    private readonly ConditionalWeakTable<TerminalShaderRuntimeProgram, D3D11Program> _programs = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a Direct3D 11 runtime using the default hardware adapter.
    /// </summary>
    public TerminalShaderD3D11Runtime()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Direct3D 11 shader runtime is available only on Windows.");
        }

        FeatureLevel[] featureLevels =
        [
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0,
        ];

        Result result = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out _device,
            out _,
            out _context);
        result.CheckError();

        _linearClampSampler = _device.CreateSamplerState(new SamplerDescription(
            Filter.MinMagMipLinear,
            TextureAddressMode.Clamp,
            mipLODBias: 0f,
            maxAnisotropy: 1,
            comparisonFunc: ComparisonFunction.Always,
            minLOD: 0f,
            maxLOD: float.MaxValue));
    }

    /// <summary>
    /// Gets whether Direct3D 11 can be used on the current platform.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public TerminalShaderBackendCapabilities Capabilities { get; } = new(
        TerminalShaderBackendKind.D3D11,
        supportsPixelShaders: true,
        supportsComputeShaders: true,
        supportsUavResources: true,
        supportsTextureInterop: true,
        maxTextureSize: 16384);

    /// <inheritdoc />
    public ValueTask<TerminalShaderRuntimeProgram> CreateProgramAsync(
        TerminalShaderCompilationResult compilation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return ValueTask.FromResult(CreateFailedProgram(
            compilation,
            [
                new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERD3D11000",
                    "Direct3D 11 runtime program creation requires the shader package pass graph."),
            ]));
    }

    /// <inheritdoc />
    public ValueTask<TerminalShaderRuntimeProgram> CreateProgramAsync(
        TerminalShaderPackage package,
        TerminalShaderCompilationResult compilation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(compilation);
        _ = cancellationToken;

        if (!compilation.IsSuccess)
        {
            return ValueTask.FromResult(CreateFailedProgram(compilation, compilation.Diagnostics));
        }

        List<TerminalShaderDiagnostic> diagnostics = [];
        Dictionary<string, TerminalShaderCompiledPass> compiledByName = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < compilation.Passes.Count; i++)
        {
            TerminalShaderCompiledPass compiledPass = compilation.Passes[i];
            if (compiledPass.Format != TerminalShaderCompiledCodeFormat.Dxbc)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERD3D11001",
                    $"Pass '{compiledPass.PassName}' uses compiled format '{compiledPass.Format}'. Direct3D 11 requires DXBC bytecode."));
                continue;
            }

            compiledByName[compiledPass.PassName] = compiledPass;
        }

        if (diagnostics.Count > 0)
        {
            return ValueTask.FromResult(CreateFailedProgram(compilation, diagnostics));
        }

        D3D11Program? nativeProgram = null;
        try
        {
            nativeProgram = CreateNativeProgram(package, compilation, compiledByName);
            TerminalShaderRuntimeProgram program = new(
                TerminalShaderBackendKind.D3D11,
                compilation,
                Capabilities,
                package,
                nativeHandle: nativeProgram.NativeHandle,
                dispose: nativeProgram.Dispose);
            _programs.Add(program, nativeProgram);
            return ValueTask.FromResult(program);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SharpGenException)
        {
            nativeProgram?.Dispose();
            return ValueTask.FromResult(CreateFailedProgram(
                compilation,
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERD3D11002",
                        $"Direct3D 11 program creation failed: {ex.Message}"),
                ]));
        }
    }

    /// <inheritdoc />
    public ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
        TerminalShaderRuntimeProgram program,
        TerminalShaderFrameRequest frame,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(frame);
        _ = cancellationToken;

        if (!_programs.TryGetValue(program, out D3D11Program? nativeProgram))
        {
            return ValueTask.FromResult(TerminalShaderFrameResult.Failed(
                TerminalShaderBackendKind.D3D11,
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERD3D11003",
                        "Direct3D 11 runtime program was not created by this runtime."),
                ]));
        }

        lock (_syncRoot)
        {
            using D3D11FrameGraph graph = new(_device, _context, frame);
            graph.AddFrameResources();
            UploadBuiltInFrameConstantBuffer(graph, frame);

            try
            {
                for (int i = 0; i < nativeProgram.Passes.Count; i++)
                {
                    D3D11Pass pass = nativeProgram.Passes[i];
                    if (pass.Stage == TerminalShaderStage.Compute)
                    {
                        ExecuteComputePass(nativeProgram.Package, pass, graph, frame);
                    }
                    else
                    {
                        ExecutePixelPass(nativeProgram.Package, nativeProgram.VertexShader, pass, graph, frame);
                    }
                }

                D3D11TextureResource finalResource = graph.GetRequired(FinalFrameResourceName);
                byte[] pixels = ReadBack(finalResource.Texture, finalResource.Width, finalResource.Height);
                TerminalShaderFrameResult result = new(
                    TerminalShaderBackendKind.D3D11,
                    pixelData: pixels,
                    width: finalResource.Width,
                    height: finalResource.Height);
                return ValueTask.FromResult(result);
            }
            catch (Exception ex) when (ex is InvalidOperationException or SharpGenException)
            {
                return ValueTask.FromResult(TerminalShaderFrameResult.Failed(
                    TerminalShaderBackendKind.D3D11,
                    [
                        new TerminalShaderDiagnostic(
                            TerminalShaderDiagnosticSeverity.Error,
                            "RTSHADERD3D11004",
                            $"Direct3D 11 frame execution failed: {ex.Message}"),
                    ]));
            }
            finally
            {
                _context.ClearState();
                _context.Flush();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _linearClampSampler.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private D3D11Program CreateNativeProgram(
        TerminalShaderPackage package,
        TerminalShaderCompilationResult compilation,
        Dictionary<string, TerminalShaderCompiledPass> compiledByName)
    {
        ReadOnlyMemory<byte> vertexCode = Compiler.Compile(
            FullscreenVertexShaderSource,
            "Main",
            "RoyalTerminalFullscreenTriangle.hlsl",
            "vs_5_0",
            ShaderFlags.OptimizationLevel3,
            EffectFlags.None);
        ID3D11VertexShader vertexShader = _device.CreateVertexShader(vertexCode.Span, null);
        List<D3D11Pass> passes = new(package.Passes.Count);

        for (int i = 0; i < package.Passes.Count; i++)
        {
            TerminalShaderPass pass = package.Passes[i];
            if (!compiledByName.TryGetValue(pass.Name, out TerminalShaderCompiledPass? compiledPass))
            {
                throw new InvalidOperationException($"Package pass '{pass.Name}' does not have compiled DXBC bytecode.");
            }

            if (pass.Stage == TerminalShaderStage.Compute)
            {
                ID3D11ComputeShader computeShader = _device.CreateComputeShader(compiledPass.Code.Span, null);
                passes.Add(new D3D11Pass(pass, compiledPass, pixelShader: null, computeShader));
            }
            else if (pass.Stage == TerminalShaderStage.Pixel)
            {
                ID3D11PixelShader pixelShader = _device.CreatePixelShader(compiledPass.Code.Span, null);
                passes.Add(new D3D11Pass(pass, compiledPass, pixelShader, computeShader: null));
            }
        }

        return new D3D11Program(package, compilation, vertexShader, passes);
    }

    private void ExecutePixelPass(
        TerminalShaderPackage package,
        ID3D11VertexShader vertexShader,
        D3D11Pass pass,
        D3D11FrameGraph graph,
        TerminalShaderFrameRequest frame)
    {
        D3D11TextureResource output = graph.GetOrCreateOutput(GetPassOutputName(pass.PackagePass), pass.PackagePass, frame);

        BindPixelResources(package, pass, graph);
        _context.OMSetRenderTargets(
            output.RenderTargetView ?? throw new InvalidOperationException($"Pixel pass '{pass.PackagePass.Name}' did not create a render target view."),
            null);
        _context.RSSetViewports([new Viewport(output.Width, output.Height)]);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(vertexShader, null!, 0);
        _context.PSSetShader(pass.PixelShader, null!, 0);
        _context.Draw(3, 0);
        graph.SetFinal(output);
    }

    private void ExecuteComputePass(
        TerminalShaderPackage package,
        D3D11Pass pass,
        D3D11FrameGraph graph,
        TerminalShaderFrameRequest frame)
    {
        if (pass.PackagePass.Dispatch is null)
        {
            throw new InvalidOperationException($"Compute pass '{pass.PackagePass.Name}' has no dispatch dimensions.");
        }

        D3D11TextureResource output = graph.GetOrCreateOutput(GetPassOutputName(pass.PackagePass), pass.PackagePass, frame);
        BindComputeResources(package, pass, graph, output);
        _context.CSSetShader(pass.ComputeShader, null!, 0);

        TerminalShaderDispatch dispatch = pass.PackagePass.Dispatch.Value;
        uint x = (uint)dispatch.X;
        uint y = (uint)dispatch.Y;
        uint z = (uint)dispatch.Z;
        if (dispatch.Kind == TerminalShaderDispatchKind.CoverOutput)
        {
            x = (uint)Math.Max(1, (output.Width + dispatch.X - 1) / dispatch.X);
            y = (uint)Math.Max(1, (output.Height + dispatch.Y - 1) / dispatch.Y);
        }

        _context.Dispatch(x, y, z);
        graph.SetFinal(output);
    }

    private void BindPixelResources(
        TerminalShaderPackage package,
        D3D11Pass pass,
        D3D11FrameGraph graph)
    {
        ID3D11ShaderResourceView?[] shaderResourceViews = BuildShaderResourceViewSlots(package, pass, graph);
        if (shaderResourceViews.Length > 0)
        {
            _context.PSSetShaderResources(0, shaderResourceViews!);
        }

        _context.PSSetSamplers(0, [_linearClampSampler]);
        _context.PSSetConstantBuffers(0, [graph.FrameConstantBuffer]);
    }

    private void BindComputeResources(
        TerminalShaderPackage package,
        D3D11Pass pass,
        D3D11FrameGraph graph,
        D3D11TextureResource output)
    {
        ID3D11ShaderResourceView?[] shaderResourceViews = BuildShaderResourceViewSlots(package, pass, graph);
        if (shaderResourceViews.Length > 0)
        {
            _context.CSSetShaderResources(0, shaderResourceViews!);
        }

        int uavSlot = FindResourceRegister(pass.CompiledPass.Reflection, output.Name, TerminalShaderResourceKind.UavTexture2D);
        ID3D11UnorderedAccessView?[] uavs = new ID3D11UnorderedAccessView?[Math.Max(1, uavSlot + 1)];
        uavs[uavSlot < 0 ? 0 : uavSlot] = output.UnorderedAccessView;
        _context.CSSetUnorderedAccessViews(0, uavs!);
        _context.CSSetSamplers(0, [_linearClampSampler]);
        _context.CSSetConstantBuffers(0, [graph.FrameConstantBuffer]);
    }

    private ID3D11ShaderResourceView?[] BuildShaderResourceViewSlots(
        TerminalShaderPackage package,
        D3D11Pass pass,
        D3D11FrameGraph graph)
    {
        List<(int Slot, ID3D11ShaderResourceView View)> bindings = [];
        for (int i = 0; i < pass.PackagePass.Inputs.Count; i++)
        {
            TerminalShaderPassInput input = pass.PackagePass.Inputs[i];
            D3D11TextureResource resource = graph.GetRequired(input.ResourceName);
            int slot = FindDeclaredResourceRegister(package, input.BindingName, input.ResourceName);
            if (slot < 0)
            {
                slot = FindResourceRegister(pass.CompiledPass.Reflection, input.BindingName, TerminalShaderResourceKind.Texture2D);
            }

            bindings.Add((slot < 0 ? i : slot, resource.ShaderResourceView));
        }

        if (bindings.Count == 0)
        {
            return [];
        }

        int maxSlot = bindings.Max(static binding => binding.Slot);
        ID3D11ShaderResourceView?[] views = new ID3D11ShaderResourceView?[maxSlot + 1];
        for (int i = 0; i < bindings.Count; i++)
        {
            views[bindings[i].Slot] = bindings[i].View;
        }

        return views;
    }

    private void UploadBuiltInFrameConstantBuffer(D3D11FrameGraph graph, TerminalShaderFrameRequest frame)
    {
        FrameConstants constants = new()
        {
            ResolutionX = frame.Width,
            ResolutionY = frame.Height,
            Time = frame.Time,
            TimeDelta = frame.TimeDelta,
            Scale = frame.Scale,
            BackgroundA = 1f,
        };

        graph.FrameConstantBuffer = _device.CreateBuffer(
            new BufferDescription(
                (uint)Marshal.SizeOf<FrameConstants>(),
                BindFlags.ConstantBuffer,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                ResourceOptionFlags.None,
                structureByteStride: 0),
            (SubresourceData?)null);

        unsafe
        {
            _context.UpdateSubresource(
                graph.FrameConstantBuffer,
                0,
                null,
                (IntPtr)(&constants),
                (uint)Marshal.SizeOf<FrameConstants>(),
                0);
        }
    }

    private byte[] ReadBack(ID3D11Texture2D texture, int width, int height)
    {
        Texture2DDescription description = new(
            Format.R8G8B8A8_UNorm,
            (uint)width,
            (uint)height,
            arraySize: 1,
            mipLevels: 1,
            BindFlags.None,
            ResourceUsage.Staging,
            CpuAccessFlags.Read,
            sampleCount: 1,
            sampleQuality: 0,
            ResourceOptionFlags.None);
        using ID3D11Texture2D staging = _device.CreateTexture2D(in description);
        _context.CopyResource(staging, texture);
        MappedSubresource mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            int rowBytes = checked(width * 4);
            byte[] pixels = new byte[checked(rowBytes * height)];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(
                    IntPtr.Add(mapped.DataPointer, checked(y * (int)mapped.RowPitch)),
                    pixels,
                    y * rowBytes,
                    rowBytes);
            }

            return pixels;
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    private static TerminalShaderRuntimeProgram CreateFailedProgram(
        TerminalShaderCompilationResult sourceCompilation,
        IReadOnlyList<TerminalShaderDiagnostic> diagnostics)
    {
        TerminalShaderCompilationResult failed = TerminalShaderCompilationResult.Failed(diagnostics);
        return new TerminalShaderRuntimeProgram(
            TerminalShaderBackendKind.D3D11,
            sourceCompilation.IsSuccess ? failed : sourceCompilation,
            new TerminalShaderBackendCapabilities(
                TerminalShaderBackendKind.D3D11,
                supportsPixelShaders: false,
                supportsComputeShaders: false,
                supportsUavResources: false,
                supportsTextureInterop: false,
                maxTextureSize: 1));
    }

    private static string GetPassOutputName(TerminalShaderPass pass)
    {
        return pass.Outputs.Count > 0 ? pass.Outputs[0].Name : FinalFrameResourceName;
    }

    private static int FindDeclaredResourceRegister(
        TerminalShaderPackage package,
        string bindingName,
        string resourceName)
    {
        for (int i = 0; i < package.Resources.Count; i++)
        {
            TerminalShaderResourceBinding resource = package.Resources[i];
            if (resource.RegisterIndex >= 0 &&
                (string.Equals(resource.Name, bindingName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(resource.Name, resourceName, StringComparison.OrdinalIgnoreCase)))
            {
                return resource.RegisterIndex;
            }
        }

        return -1;
    }

    private static int FindResourceRegister(
        TerminalShaderReflection reflection,
        string name,
        TerminalShaderResourceKind kind)
    {
        for (int i = 0; i < reflection.Resources.Count; i++)
        {
            TerminalShaderResourceReflection resource = reflection.Resources[i];
            if (resource.RegisterIndex >= 0 &&
                string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase) &&
                (resource.Kind == kind || resource.Kind == TerminalShaderResourceKind.Texture2D))
            {
                return resource.RegisterIndex;
            }
        }

        return -1;
    }

    private const string FullscreenVertexShaderSource = """
        struct VertexOutput
        {
            float4 Position : SV_Position;
            float2 TexCoord : TEXCOORD0;
        };

        VertexOutput Main(uint vertexId : SV_VertexID)
        {
            float2 positions[3] =
            {
                float2(-1.0, -1.0),
                float2(-1.0, 3.0),
                float2(3.0, -1.0)
            };

            float2 texcoords[3] =
            {
                float2(0.0, 1.0),
                float2(0.0, -1.0),
                float2(2.0, 1.0)
            };

            VertexOutput output;
            output.Position = float4(positions[vertexId], 0.0, 1.0);
            output.TexCoord = texcoords[vertexId];
            return output;
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public float ResolutionX;
        public float ResolutionY;
        public float Time;
        public float TimeDelta;
        public float Scale;
        public float Padding0;
        public float Padding1;
        public float Padding2;
        public float BackgroundR;
        public float BackgroundG;
        public float BackgroundB;
        public float BackgroundA;
    }

    private sealed class D3D11Program : IDisposable
    {
        public D3D11Program(
            TerminalShaderPackage package,
            TerminalShaderCompilationResult compilation,
            ID3D11VertexShader vertexShader,
            IReadOnlyList<D3D11Pass> passes)
        {
            Package = package;
            Compilation = compilation;
            VertexShader = vertexShader;
            Passes = passes.ToArray();
        }

        public TerminalShaderPackage Package { get; }

        public TerminalShaderCompilationResult Compilation { get; }

        public ID3D11VertexShader VertexShader { get; }

        public IReadOnlyList<D3D11Pass> Passes { get; }

        public nint NativeHandle => VertexShader.NativePointer;

        public void Dispose()
        {
            for (int i = 0; i < Passes.Count; i++)
            {
                Passes[i].Dispose();
            }

            VertexShader.Dispose();
        }
    }

    private sealed class D3D11Pass : IDisposable
    {
        public D3D11Pass(
            TerminalShaderPass packagePass,
            TerminalShaderCompiledPass compiledPass,
            ID3D11PixelShader? pixelShader,
            ID3D11ComputeShader? computeShader)
        {
            PackagePass = packagePass;
            CompiledPass = compiledPass;
            PixelShader = pixelShader;
            ComputeShader = computeShader;
        }

        public TerminalShaderPass PackagePass { get; }

        public TerminalShaderCompiledPass CompiledPass { get; }

        public TerminalShaderStage Stage => PackagePass.Stage;

        public ID3D11PixelShader? PixelShader { get; }

        public ID3D11ComputeShader? ComputeShader { get; }

        public void Dispose()
        {
            PixelShader?.Dispose();
            ComputeShader?.Dispose();
        }
    }

    private sealed class D3D11FrameGraph : IDisposable
    {
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private readonly TerminalShaderFrameRequest _frame;
        private readonly Dictionary<string, D3D11TextureResource> _textures = new(StringComparer.OrdinalIgnoreCase);

        public D3D11FrameGraph(
            ID3D11Device device,
            ID3D11DeviceContext context,
            TerminalShaderFrameRequest frame)
        {
            _device = device;
            _context = context;
            _frame = frame;
        }

        public ID3D11Buffer FrameConstantBuffer { get; set; } = null!;

        public void AddFrameResources()
        {
            for (int i = 0; i < _frame.Resources.Count; i++)
            {
                TerminalShaderResourceValue resource = _frame.Resources[i];
                if (resource.Kind is TerminalShaderResourceKind.Texture2D or
                    TerminalShaderResourceKind.TerminalFramebuffer or
                    TerminalShaderResourceKind.RenderTarget or
                    TerminalShaderResourceKind.HistoryTexture)
                {
                    AddTexture(resource);
                }
            }
        }

        public D3D11TextureResource GetRequired(string name)
        {
            if (_textures.TryGetValue(name, out D3D11TextureResource? resource))
            {
                return resource;
            }

            throw new InvalidOperationException($"Shader resource '{name}' is not available for the Direct3D 11 frame.");
        }

        public D3D11TextureResource GetOrCreateOutput(
            string name,
            TerminalShaderPass pass,
            TerminalShaderFrameRequest frame)
        {
            if (_textures.TryGetValue(name, out D3D11TextureResource? existing))
            {
                return existing;
            }

            TerminalShaderPassOutput? output = pass.Outputs.FirstOrDefault(
                item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            int width = output is null ? frame.Width : Math.Max(1, (int)MathF.Ceiling(frame.Width * output.WidthScale));
            int height = output is null ? frame.Height : Math.Max(1, (int)MathF.Ceiling(frame.Height * output.HeightScale));
            D3D11TextureResource resource = CreateTextureResource(
                name,
                width,
                height,
                BindFlags.RenderTarget | BindFlags.ShaderResource | BindFlags.UnorderedAccess);
            _textures.Add(name, resource);
            return resource;
        }

        public void SetFinal(D3D11TextureResource resource)
        {
            if (string.Equals(resource.Name, FinalFrameResourceName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_textures.TryGetValue(FinalFrameResourceName, out D3D11TextureResource? existing))
            {
                existing.Dispose();
            }

            _textures[FinalFrameResourceName] = resource;
        }

        public void Dispose()
        {
            HashSet<D3D11TextureResource> disposed = [];
            foreach (D3D11TextureResource resource in _textures.Values)
            {
                if (disposed.Add(resource))
                {
                    resource.Dispose();
                }
            }

            FrameConstantBuffer?.Dispose();
        }

        private void AddTexture(TerminalShaderResourceValue value)
        {
            if (value.Width <= 0 || value.Height <= 0 || value.Data.Length == 0)
            {
                throw new InvalidOperationException($"Direct3D 11 CPU texture resource '{value.Name}' requires RGBA pixel data and non-zero dimensions.");
            }

            D3D11TextureResource resource = CreateTextureResource(
                value.Name,
                value.Width,
                value.Height,
                BindFlags.ShaderResource);
            using MemoryHandle handle = value.Data.Pin();
            unsafe
            {
                _context.UpdateSubresource(
                    resource.Texture,
                    0,
                    null,
                    (IntPtr)handle.Pointer,
                    (uint)(value.Width * 4),
                    0);
            }

            _textures[value.Name] = resource;
        }

        private D3D11TextureResource CreateTextureResource(
            string name,
            int width,
            int height,
            BindFlags bindFlags)
        {
            Texture2DDescription description = new(
                Format.R8G8B8A8_UNorm,
                (uint)width,
                (uint)height,
                arraySize: 1,
                mipLevels: 1,
                bindFlags,
                ResourceUsage.Default,
                CpuAccessFlags.None,
                sampleCount: 1,
                sampleQuality: 0,
                ResourceOptionFlags.None);
            ID3D11Texture2D texture = _device.CreateTexture2D(in description);
            ID3D11ShaderResourceView shaderResourceView = _device.CreateShaderResourceView(texture, null);
            ID3D11RenderTargetView? renderTargetView = bindFlags.HasFlag(BindFlags.RenderTarget)
                ? _device.CreateRenderTargetView(texture, null)
                : null;
            ID3D11UnorderedAccessView? unorderedAccessView = bindFlags.HasFlag(BindFlags.UnorderedAccess)
                ? _device.CreateUnorderedAccessView(texture, null)
                : null;
            return new D3D11TextureResource(
                name,
                texture,
                shaderResourceView,
                renderTargetView,
                unorderedAccessView,
                width,
                height);
        }
    }

    private sealed class D3D11TextureResource : IDisposable
    {
        public D3D11TextureResource(
            string name,
            ID3D11Texture2D texture,
            ID3D11ShaderResourceView shaderResourceView,
            ID3D11RenderTargetView? renderTargetView,
            ID3D11UnorderedAccessView? unorderedAccessView,
            int width,
            int height)
        {
            Name = name;
            Texture = texture;
            ShaderResourceView = shaderResourceView;
            RenderTargetView = renderTargetView;
            UnorderedAccessView = unorderedAccessView;
            Width = width;
            Height = height;
        }

        public string Name { get; }

        public ID3D11Texture2D Texture { get; }

        public ID3D11ShaderResourceView ShaderResourceView { get; }

        public ID3D11RenderTargetView? RenderTargetView { get; }

        public ID3D11UnorderedAccessView? UnorderedAccessView { get; }

        public int Width { get; }

        public int Height { get; }

        public void Dispose()
        {
            UnorderedAccessView?.Dispose();
            RenderTargetView?.Dispose();
            ShaderResourceView.Dispose();
            Texture.Dispose();
        }
    }
}
