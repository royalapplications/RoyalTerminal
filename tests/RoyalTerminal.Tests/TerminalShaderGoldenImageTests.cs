// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Shader golden image tests.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Shaders;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShaderGoldenImageTests
{
    [Fact]
    public void SkiaRuntimeEffect_InvertShader_MatchesGoldenPixels()
    {
        TerminalShaderSource source = new(
            "invert",
            """
            uniform shader shaderTexture;

            half4 main(float2 fragCoord) {
                float4 color = shaderTexture.eval(fragCoord);
                return half4(1.0 - color.r, 1.0 - color.g, 1.0 - color.b, color.a);
            }
            """);

        using TerminalShaderPostProcessor processor = TerminalShaderPostProcessor.Create([source]);
        using SKBitmap inputBitmap = CreateInputBitmap();
        using SKSurface inputSurface = SKSurface.Create(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        using SKSurface outputSurface = SKSurface.Create(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        inputSurface.Canvas.DrawBitmap(inputBitmap, 0, 0);
        using SKImage inputImage = inputSurface.Snapshot();

        bool applied = processor.TryApply(
            outputSurface.Canvas,
            inputImage,
            new SKRect(0, 0, 2, 2),
            CreateFrameContext());

        Assert.True(applied, processor.CompileLog);
        using SKImage outputImage = outputSurface.Snapshot();
        using SKBitmap actual = SKBitmap.FromImage(outputImage);

        AssertPixel(actual, 0, 0, SKColors.Cyan);
        AssertPixel(actual, 1, 0, SKColors.Magenta);
        AssertPixel(actual, 0, 1, SKColors.Yellow);
        AssertPixel(actual, 1, 1, SKColors.Black);
    }

    private static SKBitmap CreateInputBitmap()
    {
        SKBitmap bitmap = new(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Lime);
        bitmap.SetPixel(0, 1, SKColors.Blue);
        bitmap.SetPixel(1, 1, SKColors.White);
        return bitmap;
    }

    private static void AssertPixel(
        SKBitmap bitmap,
        int x,
        int y,
        SKColor expected,
        byte tolerance = 1)
    {
        SKColor actual = bitmap.GetPixel(x, y);
        Assert.InRange(Math.Abs(actual.Red - expected.Red), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Green - expected.Green), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Blue - expected.Blue), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Alpha - expected.Alpha), 0, tolerance);
    }

    private static TerminalShaderFrameContext CreateFrameContext()
    {
        return new TerminalShaderFrameContext(
            width: 2,
            height: 2,
            time: 0f,
            timeDelta: 0f,
            frame: 0,
            scale: 1f,
            backgroundColor: SKColors.Black,
            foregroundColor: SKColors.White,
            cursorColor: SKColors.White,
            cursorRect: new SKRect(0, 0, 1, 1),
            cursorStyle: CursorStyle.Block,
            cursorVisible: true);
    }
}
