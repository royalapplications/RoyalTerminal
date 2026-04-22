// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders.D3D11 - Direct3D 11 shader reflection backend.

using RoyalTerminal.Shaders;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11.Shader;

namespace RoyalTerminal.Shaders.D3D11;

internal static class TerminalShaderD3D11ReflectionReader
{
    public static TerminalShaderReflectionResult TryRead(
        ReadOnlyMemory<byte> bytecode,
        TerminalShaderPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);

        if (!OperatingSystem.IsWindows())
        {
            return new TerminalShaderReflectionResult(
                diagnostics:
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Warning,
                        "RTSHADERD3DREFLECT000",
                        "D3DCompiler DXBC reflection is available only on Windows."),
                ]);
        }

        try
        {
            using ID3D11ShaderReflection reflection = Compiler.Reflect<ID3D11ShaderReflection>(bytecode.Span);
            return new TerminalShaderReflectionResult(ReadReflection(reflection, pass));
        }
        catch (Exception ex) when (ex is SharpGenException or DllNotFoundException or EntryPointNotFoundException)
        {
            return new TerminalShaderReflectionResult(
                diagnostics:
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Warning,
                        "RTSHADERD3DREFLECT001",
                        $"D3DCompiler could not reflect pass '{pass.Name}' from DXBC bytecode: {ex.Message}",
                        pass.SourcePath),
                ]);
        }
    }

    private static TerminalShaderReflection ReadReflection(
        ID3D11ShaderReflection reflection,
        TerminalShaderPass pass)
    {
        Dictionary<string, int> constantBufferSizes = ReadConstantBufferSizes(reflection);
        TerminalShaderEntryPointReflection entryPoint = new(
            pass.EntryPoint ?? pass.Name,
            pass.Stage,
            pass.Stage == TerminalShaderStage.Compute ? ReadThreadGroupSize(reflection) : null,
            ReadSemantics(reflection.InputParameters),
            ReadSemantics(reflection.OutputParameters));

        return new TerminalShaderReflection(
            [entryPoint],
            ReadResources(reflection.BoundResources, constantBufferSizes));
    }

    private static Dictionary<string, int> ReadConstantBufferSizes(ID3D11ShaderReflection reflection)
    {
        Dictionary<string, int> sizes = new(StringComparer.OrdinalIgnoreCase);
        ID3D11ShaderReflectionConstantBuffer[] buffers = reflection.ConstantBuffers;
        for (int i = 0; i < buffers.Length; i++)
        {
            ConstantBufferDescription description = buffers[i].Description;
            if (!string.IsNullOrWhiteSpace(description.Name) && description.Size > 0)
            {
                sizes[description.Name] = checked((int)description.Size);
            }
        }

        return sizes;
    }

    private static TerminalShaderDispatch ReadThreadGroupSize(ID3D11ShaderReflection reflection)
    {
        Vortice.Mathematics.Int3 threadGroupSize = reflection.ThreadGroupSize;
        return new TerminalShaderDispatch(
            Math.Max(1, threadGroupSize.X),
            Math.Max(1, threadGroupSize.Y),
            Math.Max(1, threadGroupSize.Z));
    }

    private static IReadOnlyList<TerminalShaderResourceReflection> ReadResources(
        IReadOnlyList<InputBindingDescription> bindings,
        IReadOnlyDictionary<string, int> constantBufferSizes)
    {
        List<TerminalShaderResourceReflection> resources = new(bindings.Count);
        for (int i = 0; i < bindings.Count; i++)
        {
            InputBindingDescription binding = bindings[i];
            if (string.IsNullOrWhiteSpace(binding.Name))
            {
                continue;
            }

            (TerminalShaderResourceKind kind, TerminalShaderValueType valueType) =
                MapResourceType(binding.Type, binding.Dimension);
            int? sizeInBytes = kind == TerminalShaderResourceKind.ConstantBuffer &&
                constantBufferSizes.TryGetValue(binding.Name, out int size)
                    ? size
                    : null;

            resources.Add(new TerminalShaderResourceReflection(
                binding.Name,
                kind,
                valueType,
                checked((int)binding.BindPoint),
                registerSpace: 0,
                sizeInBytes));
        }

        return resources
            .OrderBy(static resource => resource.RegisterSpace)
            .ThenBy(static resource => resource.RegisterIndex)
            .ThenBy(static resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<TerminalShaderSemanticReflection> ReadSemantics(
        IReadOnlyList<ShaderParameterDescription> parameters)
    {
        List<TerminalShaderSemanticReflection> semantics = new(parameters.Count);
        for (int i = 0; i < parameters.Count; i++)
        {
            ShaderParameterDescription parameter = parameters[i];
            if (string.IsNullOrWhiteSpace(parameter.SemanticName))
            {
                continue;
            }

            semantics.Add(new TerminalShaderSemanticReflection(
                parameter.SemanticName,
                checked((int)parameter.SemanticIndex),
                MapSemanticValueType(parameter.ComponentType, parameter.UsageMask)));
        }

        return semantics;
    }

    private static (TerminalShaderResourceKind Kind, TerminalShaderValueType ValueType) MapResourceType(
        ShaderInputType type,
        ShaderResourceViewDimension dimension)
    {
        return type switch
        {
            ShaderInputType.ConstantBuffer => (TerminalShaderResourceKind.ConstantBuffer, TerminalShaderValueType.Unknown),
            ShaderInputType.Sampler => (TerminalShaderResourceKind.Sampler, TerminalShaderValueType.Sampler),
            ShaderInputType.Texture => MapTextureDimension(dimension),
            ShaderInputType.TextureBuffer => (TerminalShaderResourceKind.StructuredBuffer, TerminalShaderValueType.StructuredBuffer),
            ShaderInputType.Structured => (TerminalShaderResourceKind.StructuredBuffer, TerminalShaderValueType.StructuredBuffer),
            ShaderInputType.ByteAddress => (TerminalShaderResourceKind.ByteAddressBuffer, TerminalShaderValueType.ByteAddressBuffer),
            ShaderInputType.UnorderedAccessViewRWTyped => MapUavDimension(dimension, TerminalShaderValueType.Texture2D),
            ShaderInputType.UnorderedAccessViewRWStructured => (TerminalShaderResourceKind.UavBuffer, TerminalShaderValueType.StructuredBuffer),
            ShaderInputType.UnorderedAccessViewAppendStructured => (TerminalShaderResourceKind.UavBuffer, TerminalShaderValueType.StructuredBuffer),
            ShaderInputType.UnorderedAccessViewConsumeStructured => (TerminalShaderResourceKind.UavBuffer, TerminalShaderValueType.StructuredBuffer),
            ShaderInputType.UnorderedAccessViewRWStructuredWithCounter => (TerminalShaderResourceKind.UavBuffer, TerminalShaderValueType.StructuredBuffer),
            ShaderInputType.UnorderedAccessViewRWByteAddress => (TerminalShaderResourceKind.UavBuffer, TerminalShaderValueType.ByteAddressBuffer),
            _ => (TerminalShaderResourceKind.Texture2D, TerminalShaderValueType.Unknown),
        };
    }

    private static (TerminalShaderResourceKind Kind, TerminalShaderValueType ValueType) MapTextureDimension(
        ShaderResourceViewDimension dimension)
    {
        return dimension switch
        {
            ShaderResourceViewDimension.Buffer or ShaderResourceViewDimension.BufferExtended =>
                (TerminalShaderResourceKind.StructuredBuffer, TerminalShaderValueType.StructuredBuffer),
            ShaderResourceViewDimension.Texture2D or ShaderResourceViewDimension.Texture2DArray or
                ShaderResourceViewDimension.Texture2DMultisampled or ShaderResourceViewDimension.Texture2DMultisampledArray =>
                (TerminalShaderResourceKind.Texture2D, TerminalShaderValueType.Texture2D),
            _ => (TerminalShaderResourceKind.Texture2D, TerminalShaderValueType.Unknown),
        };
    }

    private static (TerminalShaderResourceKind Kind, TerminalShaderValueType ValueType) MapUavDimension(
        ShaderResourceViewDimension dimension,
        TerminalShaderValueType valueType)
    {
        return dimension switch
        {
            ShaderResourceViewDimension.Texture2D or ShaderResourceViewDimension.Texture2DArray =>
                (TerminalShaderResourceKind.UavTexture2D, TerminalShaderValueType.Texture2D),
            _ => (TerminalShaderResourceKind.UavBuffer, valueType),
        };
    }

    private static TerminalShaderValueType MapSemanticValueType(
        RegisterComponentType componentType,
        RegisterComponentMaskFlags mask)
    {
        int componentCount = CountComponents(mask);
        return componentType switch
        {
            RegisterComponentType.Float32 or RegisterComponentType.Float16 => componentCount switch
            {
                1 => TerminalShaderValueType.Float,
                2 => TerminalShaderValueType.Float2,
                3 => TerminalShaderValueType.Float3,
                4 => TerminalShaderValueType.Float4,
                _ => TerminalShaderValueType.Unknown,
            },
            RegisterComponentType.SInt32 or RegisterComponentType.SInt16 => TerminalShaderValueType.Int,
            RegisterComponentType.UInt32 or RegisterComponentType.UInt16 => TerminalShaderValueType.UInt,
            _ => TerminalShaderValueType.Unknown,
        };
    }

    private static int CountComponents(RegisterComponentMaskFlags mask)
    {
        int count = 0;
        if ((mask & RegisterComponentMaskFlags.ComponentX) != 0)
        {
            count++;
        }

        if ((mask & RegisterComponentMaskFlags.ComponentY) != 0)
        {
            count++;
        }

        if ((mask & RegisterComponentMaskFlags.ComponentZ) != 0)
        {
            count++;
        }

        if ((mask & RegisterComponentMaskFlags.ComponentW) != 0)
        {
            count++;
        }

        return count;
    }
}
