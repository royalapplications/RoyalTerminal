// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - DXC listing reflection tests.

using RoyalTerminal.Shaders;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderDxcReflectionListingTests
{
    [Fact]
    public void TryRead_ReflectsResourceBindingsAndSignatures()
    {
        TerminalShaderPass pass = new(
            "main",
            TerminalShaderStage.Pixel,
            "shader.hlsl",
            "Main",
            TerminalShaderTargetProfile.PixelShader60);

        TerminalShaderReflectionResult result =
            TerminalShaderDxcReflectionListingReader.TryRead(CreatePixelListing(), pass);

        Assert.True(result.IsValid);
        TerminalShaderEntryPointReflection entry = Assert.Single(result.Reflection.EntryPoints);
        Assert.Equal("Main", entry.Name);
        Assert.Equal(TerminalShaderStage.Pixel, entry.Stage);
        Assert.Contains(entry.Inputs, input => input.Name == "TEXCOORD" && input.SemanticIndex == 0);
        Assert.Contains(entry.Outputs, output => output.Name == "SV_Target" && output.SemanticIndex == 0);
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
            "shader.hlsl",
            "Main",
            TerminalShaderTargetProfile.ComputeShader60);

        TerminalShaderReflectionResult result =
            TerminalShaderDxcReflectionListingReader.TryRead(CreateComputeListing(), pass);

        Assert.True(result.IsValid);
        TerminalShaderEntryPointReflection entry = Assert.Single(result.Reflection.EntryPoints);
        Assert.NotNull(entry.ThreadGroupSize);
        TerminalShaderDispatch threadGroupSize = entry.ThreadGroupSize.Value;
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
    public void TryRead_EmptyListing_ReturnsWarningDiagnostic()
    {
        TerminalShaderPass pass = new(
            "main",
            TerminalShaderStage.Pixel,
            "shader.hlsl",
            "Main",
            TerminalShaderTargetProfile.PixelShader60);

        TerminalShaderReflectionResult result =
            TerminalShaderDxcReflectionListingReader.TryRead(string.Empty, pass);

        Assert.Empty(result.Reflection.EntryPoints);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RTSHADERDXCREFLECT001");
    }

    private static string CreatePixelListing()
    {
        return """
            ; Input signature:
            ;
            ; Name                 Index   Mask Register SysValue  Format   Used
            ; -------------------- ----- ------ -------- -------- ------- ------
            ; TEXCOORD                 0   xy          0     NONE   float   xy
            ;
            ; Output signature:
            ;
            ; Name                 Index   Mask Register SysValue  Format   Used
            ; -------------------- ----- ------ -------- -------- ------- ------
            ; SV_Target                0   xyzw        0   TARGET   float   xyzw
            ;
            ; Resource Bindings:
            ;
            ; Name                                 Type  Format         Dim      ID      HLSL Bind  Count
            ; ------------------------------ ---------- ------- ----------- ------- -------------- ------
            ; terminalTexture                  texture     f32          2d      T0      t0,space1     1
            ; linearSampler                    sampler      NA          NA      S0              s0     1
            ; FrameConstants                   cbuffer      NA          NA     CB0              b0     1
            """;
    }

    private static string CreateComputeListing()
    {
        return """
            ; NumThreads: 8, 4, 1
            ;
            ; Resource Bindings:
            ;
            ; Name                                 Type  Format         Dim      ID      HLSL Bind  Count
            ; ------------------------------ ---------- ------- ----------- ------- -------------- ------
            ; outputTexture                       UAV     f32          2d      U0              u0     1
            """;
    }
}
