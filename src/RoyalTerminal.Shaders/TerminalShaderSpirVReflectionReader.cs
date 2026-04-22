// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - SPIR-V reflection reader.

using System.Buffers.Binary;
using System.Text;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Reads normalized reflection data from SPIR-V binaries emitted by DXC or Slang.
/// </summary>
public static class TerminalShaderSpirVReflectionReader
{
    private const uint MagicNumber = 0x07230203;

    /// <summary>
    /// Attempts to read entry points, resource bindings, semantics, and compute local size from SPIR-V bytecode.
    /// </summary>
    /// <param name="bytecode">SPIR-V bytecode.</param>
    /// <param name="pass">Package pass associated with the bytecode.</param>
    /// <returns>Reflected data and non-fatal reflection diagnostics.</returns>
    public static TerminalShaderReflectionResult TryRead(
        ReadOnlyMemory<byte> bytecode,
        TerminalShaderPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);

        if (bytecode.Length < 20 || bytecode.Length % sizeof(uint) != 0)
        {
            return Failed(
                "RTSHADERSPIRVREFLECT001",
                $"Pass '{pass.Name}' did not produce a valid SPIR-V word stream.",
                pass.SourcePath);
        }

        uint[] words = ReadWords(bytecode.Span);
        if (words[0] != MagicNumber)
        {
            return Failed(
                "RTSHADERSPIRVREFLECT002",
                $"Pass '{pass.Name}' did not produce a SPIR-V module.",
                pass.SourcePath);
        }

        try
        {
            SpirVModule module = ReadModule(words);
            return new TerminalShaderReflectionResult(ReadReflection(module, pass));
        }
        catch (InvalidDataException ex)
        {
            return Failed(
                "RTSHADERSPIRVREFLECT003",
                $"SPIR-V reflection failed for pass '{pass.Name}': {ex.Message}",
                pass.SourcePath);
        }
    }

    private static uint[] ReadWords(ReadOnlySpan<byte> bytes)
    {
        uint[] words = new uint[bytes.Length / sizeof(uint)];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i * sizeof(uint), sizeof(uint)));
        }

        return words;
    }

    private static TerminalShaderReflectionResult Failed(
        string code,
        string message,
        string? filePath)
    {
        return new TerminalShaderReflectionResult(
            diagnostics:
            [
                new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Warning,
                    code,
                    message,
                    filePath),
            ]);
    }

    private static SpirVModule ReadModule(IReadOnlyList<uint> words)
    {
        SpirVModule module = new();
        int offset = 5;
        while (offset < words.Count)
        {
            uint instruction = words[offset];
            int wordCount = checked((int)(instruction >> 16));
            int opcode = checked((int)(instruction & 0xffff));
            if (wordCount <= 0 || offset + wordCount > words.Count)
            {
                throw new InvalidDataException("The module contains an invalid instruction length.");
            }

            ReadInstruction(module, words, offset, wordCount, opcode);
            offset += wordCount;
        }

        return module;
    }

    private static void ReadInstruction(
        SpirVModule module,
        IReadOnlyList<uint> words,
        int offset,
        int wordCount,
        int opcode)
    {
        switch (opcode)
        {
            case 5:
                ReadName(module, words, offset, wordCount);
                break;
            case 15:
                ReadEntryPoint(module, words, offset, wordCount);
                break;
            case 16:
                ReadExecutionMode(module, words, offset, wordCount);
                break;
            case 21:
                ReadIntegerType(module, words, offset, wordCount);
                break;
            case 22:
                ReadFloatType(module, words, offset, wordCount);
                break;
            case 23:
                ReadVectorType(module, words, offset, wordCount);
                break;
            case 24:
                ReadMatrixType(module, words, offset, wordCount);
                break;
            case 25:
                ReadImageType(module, words, offset, wordCount);
                break;
            case 26:
                ReadSimpleType(module, words, offset, wordCount, SpirVTypeKind.Sampler);
                break;
            case 27:
                ReadSampledImageType(module, words, offset, wordCount);
                break;
            case 28:
                ReadArrayType(module, words, offset, wordCount, SpirVTypeKind.Array);
                break;
            case 29:
                ReadArrayType(module, words, offset, wordCount, SpirVTypeKind.RuntimeArray);
                break;
            case 30:
                ReadStructType(module, words, offset, wordCount);
                break;
            case 32:
                ReadPointerType(module, words, offset, wordCount);
                break;
            case 59:
                ReadVariable(module, words, offset, wordCount);
                break;
            case 71:
                ReadDecorate(module, words, offset, wordCount);
                break;
        }
    }

    private static void ReadName(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount < 3)
        {
            return;
        }

        uint target = words[offset + 1];
        module.Names[target] = ReadString(words, offset + 2, offset + wordCount, out _);
    }

    private static void ReadEntryPoint(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount < 4)
        {
            return;
        }

        int end = offset + wordCount;
        uint executionModel = words[offset + 1];
        uint id = words[offset + 2];
        string name = ReadString(words, offset + 3, end, out int consumedWords);
        List<uint> interfaces = [];
        for (int i = offset + 3 + consumedWords; i < end; i++)
        {
            interfaces.Add(words[i]);
        }

        module.EntryPoints.Add(new SpirVEntryPoint(id, executionModel, name, interfaces));
    }

    private static void ReadExecutionMode(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount < 3)
        {
            return;
        }

        uint entryPointId = words[offset + 1];
        uint mode = words[offset + 2];
        if (mode == 17 && wordCount >= 6)
        {
            module.LocalSizes[entryPointId] = new TerminalShaderDispatch(
                checked((int)words[offset + 3]),
                checked((int)words[offset + 4]),
                checked((int)words[offset + 5]));
        }
    }

    private static void ReadIntegerType(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount >= 4)
        {
            module.Types[words[offset + 1]] = new SpirVType(
                SpirVTypeKind.Int,
                Width: checked((int)words[offset + 2]),
                Signed: words[offset + 3] != 0);
        }
    }

    private static void ReadFloatType(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount >= 3)
        {
            module.Types[words[offset + 1]] = new SpirVType(
                SpirVTypeKind.Float,
                Width: checked((int)words[offset + 2]));
        }
    }

    private static void ReadVectorType(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount >= 4)
        {
            module.Types[words[offset + 1]] = new SpirVType(
                SpirVTypeKind.Vector,
                ElementTypeId: words[offset + 2],
                ComponentCount: checked((int)words[offset + 3]));
        }
    }

    private static void ReadMatrixType(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount >= 4)
        {
            module.Types[words[offset + 1]] = new SpirVType(
                SpirVTypeKind.Matrix,
                ElementTypeId: words[offset + 2],
                ComponentCount: checked((int)words[offset + 3]));
        }
    }

    private static void ReadImageType(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount >= 8)
        {
            module.Types[words[offset + 1]] = new SpirVType(
                SpirVTypeKind.Image,
                ElementTypeId: words[offset + 2],
                Dimension: words[offset + 3],
                Sampled: words[offset + 7]);
        }
    }

    private static void ReadSimpleType(
        SpirVModule module,
        IReadOnlyList<uint> words,
        int offset,
        int wordCount,
        SpirVTypeKind kind)
    {
        if (wordCount >= 2)
        {
            module.Types[words[offset + 1]] = new SpirVType(kind);
        }
    }

    private static void ReadSampledImageType(
        SpirVModule module,
        IReadOnlyList<uint> words,
        int offset,
        int wordCount)
    {
        if (wordCount >= 3)
        {
            module.Types[words[offset + 1]] = new SpirVType(
                SpirVTypeKind.SampledImage,
                ElementTypeId: words[offset + 2]);
        }
    }

    private static void ReadArrayType(
        SpirVModule module,
        IReadOnlyList<uint> words,
        int offset,
        int wordCount,
        SpirVTypeKind kind)
    {
        if (wordCount >= 3)
        {
            module.Types[words[offset + 1]] = new SpirVType(kind, ElementTypeId: words[offset + 2]);
        }
    }

    private static void ReadStructType(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount < 2)
        {
            return;
        }

        uint[] members = new uint[Math.Max(0, wordCount - 2)];
        for (int i = 0; i < members.Length; i++)
        {
            members[i] = words[offset + 2 + i];
        }

        module.Types[words[offset + 1]] = new SpirVType(SpirVTypeKind.Struct, MemberTypeIds: members);
    }

    private static void ReadPointerType(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount >= 4)
        {
            module.Types[words[offset + 1]] = new SpirVType(
                SpirVTypeKind.Pointer,
                ElementTypeId: words[offset + 3],
                StorageClass: words[offset + 2]);
        }
    }

    private static void ReadVariable(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount >= 4)
        {
            module.Variables.Add(new SpirVVariable(
                words[offset + 2],
                words[offset + 1],
                words[offset + 3]));
        }
    }

    private static void ReadDecorate(SpirVModule module, IReadOnlyList<uint> words, int offset, int wordCount)
    {
        if (wordCount < 3)
        {
            return;
        }

        uint target = words[offset + 1];
        uint decoration = words[offset + 2];
        if (!module.Decorations.TryGetValue(target, out SpirVDecorationSet? set))
        {
            set = new SpirVDecorationSet();
            module.Decorations[target] = set;
        }

        switch (decoration)
        {
            case 11:
                if (wordCount >= 4)
                {
                    set.BuiltIn = words[offset + 3];
                }

                break;
            case 30:
                if (wordCount >= 4)
                {
                    set.Location = checked((int)words[offset + 3]);
                }

                break;
            case 33:
                if (wordCount >= 4)
                {
                    set.Binding = checked((int)words[offset + 3]);
                }

                break;
            case 34:
                if (wordCount >= 4)
                {
                    set.DescriptorSet = checked((int)words[offset + 3]);
                }

                break;
            case 24:
                set.NonWritable = true;
                break;
        }

    }

    private static TerminalShaderReflection ReadReflection(SpirVModule module, TerminalShaderPass pass)
    {
        List<TerminalShaderEntryPointReflection> entryPoints = [];
        for (int i = 0; i < module.EntryPoints.Count; i++)
        {
            SpirVEntryPoint entryPoint = module.EntryPoints[i];
            if (pass.EntryPoint is not null &&
                !string.Equals(pass.EntryPoint, entryPoint.Name, StringComparison.Ordinal))
            {
                continue;
            }

            TerminalShaderStage stage = MapStage(entryPoint.ExecutionModel, pass.Stage);
            entryPoints.Add(new TerminalShaderEntryPointReflection(
                entryPoint.Name,
                stage,
                stage == TerminalShaderStage.Compute && module.LocalSizes.TryGetValue(entryPoint.Id, out TerminalShaderDispatch size)
                    ? size
                    : null,
                ReadSemantics(module, entryPoint.InterfaceIds, storageClass: 1),
                ReadSemantics(module, entryPoint.InterfaceIds, storageClass: 3)));
        }

        if (entryPoints.Count == 0)
        {
            entryPoints.Add(new TerminalShaderEntryPointReflection(pass.EntryPoint ?? pass.Name, pass.Stage));
        }

        return new TerminalShaderReflection(entryPoints, ReadResources(module));
    }

    private static IReadOnlyList<TerminalShaderResourceReflection> ReadResources(SpirVModule module)
    {
        List<TerminalShaderResourceReflection> resources = [];
        for (int i = 0; i < module.Variables.Count; i++)
        {
            SpirVVariable variable = module.Variables[i];
            if (variable.StorageClass is not (0 or 2 or 12))
            {
                continue;
            }

            SpirVDecorationSet decoration = GetDecoration(module, variable.Id);
            if (decoration.Binding < 0 && variable.StorageClass == 0)
            {
                continue;
            }

            SpirVType? type = ResolvePointerTarget(module, variable.TypeId);
            if (type is null)
            {
                continue;
            }

            (TerminalShaderResourceKind kind, TerminalShaderValueType valueType) =
                MapResourceType(module, type, variable.StorageClass, decoration);
            string name = module.Names.TryGetValue(variable.Id, out string? declaredName) &&
                !string.IsNullOrWhiteSpace(declaredName)
                    ? declaredName
                    : string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"resource_set{Math.Max(0, decoration.DescriptorSet)}_binding{Math.Max(0, decoration.Binding)}");

            resources.Add(new TerminalShaderResourceReflection(
                name,
                kind,
                valueType,
                decoration.Binding,
                Math.Max(0, decoration.DescriptorSet)));
        }

        return resources
            .OrderBy(static resource => resource.RegisterSpace)
            .ThenBy(static resource => resource.RegisterIndex)
            .ThenBy(static resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<TerminalShaderSemanticReflection> ReadSemantics(
        SpirVModule module,
        IReadOnlyList<uint> interfaceIds,
        uint storageClass)
    {
        List<TerminalShaderSemanticReflection> semantics = [];
        for (int i = 0; i < interfaceIds.Count; i++)
        {
            uint id = interfaceIds[i];
            SpirVVariable? variable = FindVariable(module, id);
            if (variable is null || variable.Value.StorageClass != storageClass)
            {
                continue;
            }

            SpirVDecorationSet decoration = GetDecoration(module, id);
            string semanticName = GetSemanticName(module, id, decoration);
            TerminalShaderValueType valueType = TerminalShaderValueType.Unknown;
            SpirVType? type = ResolvePointerTarget(module, variable.Value.TypeId);
            if (type is not null)
            {
                valueType = MapValueType(module, type);
            }

            semantics.Add(new TerminalShaderSemanticReflection(
                semanticName,
                Math.Max(0, decoration.Location),
                valueType));
        }

        return semantics;
    }

    private static string GetSemanticName(SpirVModule module, uint id, SpirVDecorationSet decoration)
    {
        if (module.Names.TryGetValue(id, out string? name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (decoration.BuiltIn is not null)
        {
            return decoration.BuiltIn.Value switch
            {
                0 => "Position",
                15 => "FragCoord",
                16 => "PointCoord",
                17 => "FrontFacing",
                28 => "GlobalInvocationId",
                29 => "LocalInvocationId",
                30 => "WorkgroupId",
                31 => "LocalInvocationIndex",
                _ => string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"BuiltIn{decoration.BuiltIn.Value}"),
            };
        }

        return decoration.Location >= 0
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Location{decoration.Location}")
            : "Interface";
    }

    private static SpirVDecorationSet GetDecoration(SpirVModule module, uint id)
    {
        return module.Decorations.TryGetValue(id, out SpirVDecorationSet? decoration)
            ? decoration
            : SpirVDecorationSet.Empty;
    }

    private static (TerminalShaderResourceKind Kind, TerminalShaderValueType ValueType) MapResourceType(
        SpirVModule module,
        SpirVType type,
        uint storageClass,
        SpirVDecorationSet decoration)
    {
        if (storageClass == 2)
        {
            return (TerminalShaderResourceKind.ConstantBuffer, TerminalShaderValueType.Unknown);
        }

        if (storageClass == 12)
        {
            return decoration.NonWritable
                ? (TerminalShaderResourceKind.StructuredBuffer, TerminalShaderValueType.StructuredBuffer)
                : (TerminalShaderResourceKind.UavBuffer, TerminalShaderValueType.StructuredBuffer);
        }

        SpirVType resolved = ResolveResourceType(module, type);
        return resolved.Kind switch
        {
            SpirVTypeKind.Sampler => (TerminalShaderResourceKind.Sampler, TerminalShaderValueType.Sampler),
            SpirVTypeKind.Image => MapImageResource(resolved),
            SpirVTypeKind.SampledImage => (TerminalShaderResourceKind.Texture2D, TerminalShaderValueType.Texture2D),
            _ => (TerminalShaderResourceKind.Texture2D, TerminalShaderValueType.Unknown),
        };
    }

    private static (TerminalShaderResourceKind Kind, TerminalShaderValueType ValueType) MapImageResource(SpirVType type)
    {
        if (type.Dimension == 5)
        {
            return type.Sampled == 2
                ? (TerminalShaderResourceKind.UavBuffer, TerminalShaderValueType.StructuredBuffer)
                : (TerminalShaderResourceKind.StructuredBuffer, TerminalShaderValueType.StructuredBuffer);
        }

        return type.Sampled == 2
            ? (TerminalShaderResourceKind.UavTexture2D, TerminalShaderValueType.Texture2D)
            : (TerminalShaderResourceKind.Texture2D, TerminalShaderValueType.Texture2D);
    }

    private static TerminalShaderValueType MapValueType(SpirVModule module, SpirVType type)
    {
        SpirVType resolved = ResolveResourceType(module, type);
        return resolved.Kind switch
        {
            SpirVTypeKind.Float => TerminalShaderValueType.Float,
            SpirVTypeKind.Int => resolved.Signed ? TerminalShaderValueType.Int : TerminalShaderValueType.UInt,
            SpirVTypeKind.Vector => MapVectorValueType(module, resolved),
            SpirVTypeKind.Matrix => TerminalShaderValueType.Matrix4x4,
            SpirVTypeKind.Image or SpirVTypeKind.SampledImage => TerminalShaderValueType.Texture2D,
            SpirVTypeKind.Sampler => TerminalShaderValueType.Sampler,
            _ => TerminalShaderValueType.Unknown,
        };
    }

    private static TerminalShaderValueType MapVectorValueType(SpirVModule module, SpirVType type)
    {
        if (!module.Types.TryGetValue(type.ElementTypeId, out SpirVType? elementType))
        {
            return TerminalShaderValueType.Unknown;
        }

        if (elementType.Kind == SpirVTypeKind.Float)
        {
            return type.ComponentCount switch
            {
                2 => TerminalShaderValueType.Float2,
                3 => TerminalShaderValueType.Float3,
                4 => TerminalShaderValueType.Float4,
                _ => TerminalShaderValueType.Float,
            };
        }

        if (elementType.Kind == SpirVTypeKind.Int)
        {
            return elementType.Signed ? TerminalShaderValueType.Int : TerminalShaderValueType.UInt;
        }

        return TerminalShaderValueType.Unknown;
    }

    private static TerminalShaderStage MapStage(uint executionModel, TerminalShaderStage fallback)
    {
        return executionModel switch
        {
            4 => TerminalShaderStage.Pixel,
            5 => TerminalShaderStage.Compute,
            _ => fallback,
        };
    }

    private static SpirVVariable? FindVariable(SpirVModule module, uint id)
    {
        for (int i = 0; i < module.Variables.Count; i++)
        {
            if (module.Variables[i].Id == id)
            {
                return module.Variables[i];
            }
        }

        return null;
    }

    private static SpirVType? ResolvePointerTarget(SpirVModule module, uint typeId)
    {
        if (!module.Types.TryGetValue(typeId, out SpirVType? type))
        {
            return null;
        }

        return type.Kind == SpirVTypeKind.Pointer && module.Types.TryGetValue(type.ElementTypeId, out SpirVType? target)
            ? target
            : type;
    }

    private static SpirVType ResolveResourceType(SpirVModule module, SpirVType type)
    {
        SpirVType resolved = type;
        while (resolved.Kind is SpirVTypeKind.Array or SpirVTypeKind.RuntimeArray or SpirVTypeKind.SampledImage)
        {
            if (!module.Types.TryGetValue(resolved.ElementTypeId, out SpirVType? elementType))
            {
                break;
            }

            if (resolved.Kind == SpirVTypeKind.SampledImage && elementType.Kind == SpirVTypeKind.Image)
            {
                return resolved;
            }

            resolved = elementType;
        }

        return resolved;
    }

    private static string ReadString(
        IReadOnlyList<uint> words,
        int start,
        int end,
        out int consumedWords)
    {
        List<byte> bytes = [];
        consumedWords = 0;
        for (int i = start; i < end; i++)
        {
            consumedWords++;
            uint word = words[i];
            for (int shift = 0; shift < 32; shift += 8)
            {
                byte value = unchecked((byte)(word >> shift));
                if (value == 0)
                {
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }

                bytes.Add(value);
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private sealed class SpirVModule
    {
        public Dictionary<uint, string> Names { get; } = [];

        public Dictionary<uint, SpirVDecorationSet> Decorations { get; } = [];

        public Dictionary<uint, SpirVType> Types { get; } = [];

        public List<SpirVVariable> Variables { get; } = [];

        public List<SpirVEntryPoint> EntryPoints { get; } = [];

        public Dictionary<uint, TerminalShaderDispatch> LocalSizes { get; } = [];
    }

    private readonly record struct SpirVEntryPoint(
        uint Id,
        uint ExecutionModel,
        string Name,
        IReadOnlyList<uint> InterfaceIds);

    private readonly record struct SpirVVariable(
        uint Id,
        uint TypeId,
        uint StorageClass);

    private sealed class SpirVDecorationSet
    {
        public static SpirVDecorationSet Empty { get; } = new();

        public int Binding { get; set; } = -1;

        public int DescriptorSet { get; set; }

        public int Location { get; set; } = -1;

        public uint? BuiltIn { get; set; }

        public bool NonWritable { get; set; }
    }

    private sealed record SpirVType(
        SpirVTypeKind Kind,
        uint ElementTypeId = 0,
        int ComponentCount = 0,
        int Width = 0,
        bool Signed = false,
        uint StorageClass = 0,
        uint Dimension = 0,
        uint Sampled = 0,
        IReadOnlyList<uint>? MemberTypeIds = null);

    private enum SpirVTypeKind
    {
        Unknown,
        Int,
        Float,
        Vector,
        Matrix,
        Image,
        Sampler,
        SampledImage,
        Array,
        RuntimeArray,
        Struct,
        Pointer,
    }
}
