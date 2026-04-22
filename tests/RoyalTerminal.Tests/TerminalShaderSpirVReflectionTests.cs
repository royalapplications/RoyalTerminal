// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - SPIR-V shader reflection tests.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Shaders;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderSpirVReflectionTests
{
    [Fact]
    public void TryRead_ReflectsVulkanFragmentResources()
    {
        byte[] module = BuildFragmentModule();
        TerminalShaderPass pass = new(
            "main",
            TerminalShaderStage.Pixel,
            "main.hlsl",
            "Main");

        TerminalShaderReflectionResult result =
            TerminalShaderSpirVReflectionReader.TryRead(module, pass);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        TerminalShaderEntryPointReflection entryPoint = Assert.Single(result.Reflection.EntryPoints);
        Assert.Equal("Main", entryPoint.Name);
        Assert.Equal(TerminalShaderStage.Pixel, entryPoint.Stage);
        Assert.Contains(entryPoint.Inputs, static item =>
            item.Name == "InputTexCoord" && item.SemanticIndex == 0 && item.ValueType == TerminalShaderValueType.Float2);
        Assert.Contains(entryPoint.Outputs, static item =>
            item.Name == "OutputColor" && item.SemanticIndex == 0 && item.ValueType == TerminalShaderValueType.Float4);

        Assert.Contains(result.Reflection.Resources, static item =>
            item.Name == "TerminalFramebuffer" &&
            item.Kind == TerminalShaderResourceKind.Texture2D &&
            item.ValueType == TerminalShaderValueType.Texture2D &&
            item.RegisterSpace == 0 &&
            item.RegisterIndex == 0);
        Assert.Contains(result.Reflection.Resources, static item =>
            item.Name == "TerminalSampler" &&
            item.Kind == TerminalShaderResourceKind.Sampler &&
            item.RegisterSpace == 0 &&
            item.RegisterIndex == 1);
        Assert.Contains(result.Reflection.Resources, static item =>
            item.Name == "TerminalFrame" &&
            item.Kind == TerminalShaderResourceKind.ConstantBuffer &&
            item.RegisterSpace == 0 &&
            item.RegisterIndex == 2);
    }

    [Fact]
    public void TryRead_ReflectsComputeLocalSizeAndStorageImage()
    {
        byte[] module = BuildComputeModule();
        TerminalShaderPass pass = new(
            "main",
            TerminalShaderStage.Compute,
            "main.hlsl",
            "Main");

        TerminalShaderReflectionResult result =
            TerminalShaderSpirVReflectionReader.TryRead(module, pass);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        TerminalShaderEntryPointReflection entryPoint = Assert.Single(result.Reflection.EntryPoints);
        Assert.Equal(TerminalShaderStage.Compute, entryPoint.Stage);
        Assert.NotNull(entryPoint.ThreadGroupSize);
        Assert.Equal(new TerminalShaderDispatch(8, 4, 1), entryPoint.ThreadGroupSize.Value);
        Assert.Contains(result.Reflection.Resources, static item =>
            item.Name == "OutputFrame" &&
            item.Kind == TerminalShaderResourceKind.UavTexture2D &&
            item.ValueType == TerminalShaderValueType.Texture2D &&
            item.RegisterIndex == 0);
    }

    [Fact]
    public void TryRead_InvalidModule_ReturnsWarningDiagnostic()
    {
        TerminalShaderPass pass = new("main", TerminalShaderStage.Pixel, "main.hlsl", "Main");

        TerminalShaderReflectionResult result =
            TerminalShaderSpirVReflectionReader.TryRead(new byte[] { 1, 2, 3, 4 }, pass);

        Assert.Empty(result.Reflection.EntryPoints);
        Assert.Empty(result.Reflection.Resources);
        Assert.Contains(result.Diagnostics, static item => item.Code == "RTSHADERSPIRVREFLECT001");
    }

    private static byte[] BuildFragmentModule()
    {
        List<uint[]> instructions =
        [
            Instruction(15, [4, 100, .. StringWords("Main"), 70, 71]),
            Instruction(5, [50, .. StringWords("TerminalFramebuffer")]),
            Instruction(5, [51, .. StringWords("TerminalSampler")]),
            Instruction(5, [52, .. StringWords("TerminalFrame")]),
            Instruction(5, [70, .. StringWords("InputTexCoord")]),
            Instruction(5, [71, .. StringWords("OutputColor")]),
            Instruction(71, [50, 34, 0]),
            Instruction(71, [50, 33, 0]),
            Instruction(71, [51, 34, 0]),
            Instruction(71, [51, 33, 1]),
            Instruction(71, [52, 34, 0]),
            Instruction(71, [52, 33, 2]),
            Instruction(71, [70, 30, 0]),
            Instruction(71, [71, 30, 0]),
            Instruction(22, [1, 32]),
            Instruction(23, [2, 1, 2]),
            Instruction(23, [3, 1, 4]),
            Instruction(25, [4, 1, 1, 0, 0, 0, 1, 0]),
            Instruction(26, [5]),
            Instruction(30, [6, 3]),
            Instruction(32, [8, 0, 4]),
            Instruction(32, [9, 0, 5]),
            Instruction(32, [10, 2, 6]),
            Instruction(32, [11, 1, 2]),
            Instruction(32, [12, 3, 3]),
            Instruction(59, [8, 50, 0]),
            Instruction(59, [9, 51, 0]),
            Instruction(59, [10, 52, 2]),
            Instruction(59, [11, 70, 1]),
            Instruction(59, [12, 71, 3]),
        ];

        return Module(instructions, bound: 101);
    }

    private static byte[] BuildComputeModule()
    {
        List<uint[]> instructions =
        [
            Instruction(15, [5, 100, .. StringWords("Main")]),
            Instruction(16, [100, 17, 8, 4, 1]),
            Instruction(5, [50, .. StringWords("OutputFrame")]),
            Instruction(71, [50, 34, 0]),
            Instruction(71, [50, 33, 0]),
            Instruction(22, [1, 32]),
            Instruction(25, [4, 1, 1, 0, 0, 0, 2, 0]),
            Instruction(32, [8, 0, 4]),
            Instruction(59, [8, 50, 0]),
        ];

        return Module(instructions, bound: 101);
    }

    private static byte[] Module(IReadOnlyList<uint[]> instructions, uint bound)
    {
        List<uint> words =
        [
            0x07230203,
            0x00010600,
            0,
            bound,
            0,
        ];

        for (int i = 0; i < instructions.Count; i++)
        {
            words.AddRange(instructions[i]);
        }

        byte[] bytes = new byte[words.Count * sizeof(uint)];
        for (int i = 0; i < words.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint), sizeof(uint)), words[i]);
        }

        return bytes;
    }

    private static uint[] Instruction(ushort opcode, IReadOnlyList<uint> operands)
    {
        uint[] words = new uint[operands.Count + 1];
        words[0] = ((uint)words.Length << 16) | opcode;
        for (int i = 0; i < operands.Count; i++)
        {
            words[i + 1] = operands[i];
        }

        return words;
    }

    private static uint[] StringWords(string value)
    {
        byte[] text = Encoding.UTF8.GetBytes(value);
        byte[] bytes = new byte[((text.Length + 1 + 3) / 4) * 4];
        text.CopyTo(bytes, 0);
        uint[] words = new uint[bytes.Length / sizeof(uint)];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint), sizeof(uint)));
        }

        return words;
    }
}
