// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Slang JSON reflection tests.

using RoyalTerminal.Shaders;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderSlangReflectionJsonTests
{
    [Fact]
    public void TryRead_ReflectsEntryPointSemanticsAndResources()
    {
        TerminalShaderPass pass = new(
            "main",
            TerminalShaderStage.Pixel,
            "shader.slang",
            "Main",
            TerminalShaderTargetProfile.PixelShader60);

        TerminalShaderReflectionResult result =
            TerminalShaderSlangReflectionJsonReader.TryRead(CreatePixelReflectionJson(), pass);

        Assert.True(result.IsValid);
        TerminalShaderEntryPointReflection entryPoint = Assert.Single(result.Reflection.EntryPoints);
        Assert.Equal("Main", entryPoint.Name);
        Assert.Equal(TerminalShaderStage.Pixel, entryPoint.Stage);
        Assert.Contains(entryPoint.Inputs, input => input.Name == "TEXCOORD" && input.SemanticIndex == 0);
        Assert.Contains(entryPoint.Outputs, output => output.Name == "SV_Target" && output.SemanticIndex == 0);
        Assert.Contains(
            result.Reflection.Resources,
            resource => resource.Name == "terminalTexture" &&
                        resource.Kind == TerminalShaderResourceKind.Texture2D &&
                        resource.RegisterIndex == 0 &&
                        resource.RegisterSpace == 1);
        Assert.Contains(
            result.Reflection.Resources,
            resource => resource.Name == "linearSampler" &&
                        resource.Kind == TerminalShaderResourceKind.Sampler &&
                        resource.RegisterIndex == 0);
        Assert.Contains(
            result.Reflection.Resources,
            resource => resource.Name == "FrameConstants" &&
                        resource.Kind == TerminalShaderResourceKind.ConstantBuffer &&
                        resource.RegisterIndex == 0);
    }

    [Fact]
    public void TryRead_ReflectsComputeThreadGroupAndUav()
    {
        TerminalShaderPass pass = new(
            "compute",
            TerminalShaderStage.Compute,
            "shader.slang",
            "Main",
            TerminalShaderTargetProfile.ComputeShader60);

        TerminalShaderReflectionResult result =
            TerminalShaderSlangReflectionJsonReader.TryRead(CreateComputeReflectionJson(), pass);

        TerminalShaderEntryPointReflection entryPoint = Assert.Single(result.Reflection.EntryPoints);
        Assert.NotNull(entryPoint.ThreadGroupSize);
        TerminalShaderDispatch threadGroupSize = entryPoint.ThreadGroupSize.Value;
        Assert.Equal(8, threadGroupSize.X);
        Assert.Equal(4, threadGroupSize.Y);
        Assert.Equal(1, threadGroupSize.Z);
        Assert.Contains(
            result.Reflection.Resources,
            resource => resource.Name == "outputTexture" &&
                        resource.Kind == TerminalShaderResourceKind.UavTexture2D &&
                        resource.RegisterIndex == 0);
    }

    [Fact]
    public void TryRead_InvalidJson_ReturnsWarningDiagnostic()
    {
        TerminalShaderPass pass = new(
            "main",
            TerminalShaderStage.Pixel,
            "shader.slang",
            "Main",
            TerminalShaderTargetProfile.PixelShader60);

        TerminalShaderReflectionResult result =
            TerminalShaderSlangReflectionJsonReader.TryRead("{", pass);

        Assert.Empty(result.Reflection.EntryPoints);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RTSHADERSLANGREFLECT002");
    }

    private static string CreatePixelReflectionJson()
    {
        return """
            {
              "entryPoints": [
                {
                  "name": "Main",
                  "stage": "fragment",
                  "inputs": [
                    { "semanticName": "TEXCOORD", "semanticIndex": 0, "type": "float2" }
                  ],
                  "outputs": [
                    { "semanticName": "SV_Target", "semanticIndex": 0, "type": "float4" }
                  ]
                }
              ],
              "parameters": [
                {
                  "name": "terminalTexture",
                  "type": { "kind": "Texture2D" },
                  "binding": { "index": 0, "space": 1 }
                },
                {
                  "name": "linearSampler",
                  "type": { "kind": "SamplerState" },
                  "register": "s0"
                },
                {
                  "name": "FrameConstants",
                  "type": { "kind": "ConstantBuffer" },
                  "register": "b0"
                }
              ]
            }
            """;
    }

    private static string CreateComputeReflectionJson()
    {
        return """
            {
              "entryPoints": [
                {
                  "name": "Main",
                  "stage": "compute",
                  "threadGroupSize": [8, 4, 1]
                }
              ],
              "resources": [
                {
                  "name": "outputTexture",
                  "type": "RWTexture2D<float4>",
                  "bindingIndex": 0
                }
              ]
            }
            """;
    }
}
