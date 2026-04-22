// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Terminal shader post processor.

using System.Globalization;
using System.Text;
using RoyalTerminal.Shaders;
using RoyalTerminal.Terminal;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Applies one or more framebuffer shaders to a completed terminal frame.
/// </summary>
public sealed class TerminalShaderPostProcessor : IDisposable
{
    private readonly TerminalShaderProgram[] _programs;
    private bool _disposed;

    private TerminalShaderPostProcessor(TerminalShaderProgram[] programs, string? compileLog)
    {
        _programs = programs;
        CompileLog = compileLog;
        RequiresContinuousAnimation = programs.Any(program => program.Source.RequiresContinuousAnimation);
    }

    /// <summary>
    /// Gets whether the processor has at least one compiled shader.
    /// </summary>
    public bool HasShaders => _programs.Length > 0;

    /// <summary>
    /// Gets whether any shader requests continuous animation frames.
    /// </summary>
    public bool RequiresContinuousAnimation { get; }

    /// <summary>
    /// Gets compile diagnostics for failed shader sources.
    /// </summary>
    public string? CompileLog { get; }

    /// <summary>
    /// Builds a post processor from shader sources.
    /// Invalid shader sources are skipped and recorded in <see cref="CompileLog"/>.
    /// </summary>
    public static TerminalShaderPostProcessor Create(IReadOnlyList<TerminalShaderSource>? sources)
    {
        if (sources is null || sources.Count == 0)
        {
            return new TerminalShaderPostProcessor([], compileLog: null);
        }

        List<TerminalShaderProgram> programs = new(sources.Count);
        StringBuilder? diagnostics = null;

        for (int i = 0; i < sources.Count; i++)
        {
            TerminalShaderSource source = sources[i];
            string runtimeSource = TerminalShaderSourceTranslator.Translate(source);
            SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(runtimeSource, out string errorText);
            if (effect is null)
            {
                diagnostics ??= new StringBuilder();
                diagnostics
                    .Append(CultureInfo.InvariantCulture, $"[{source.Name}] ")
                    .AppendLine(string.IsNullOrWhiteSpace(errorText) ? "Shader failed to compile." : errorText.Trim());
                continue;
            }

            programs.Add(new TerminalShaderProgram(source, effect));
        }

        return new TerminalShaderPostProcessor(programs.ToArray(), diagnostics?.ToString().TrimEnd());
    }

    /// <summary>
    /// Applies the shader chain to <paramref name="inputFrame"/> and draws the result.
    /// </summary>
    public bool TryApply(
        SKCanvas destinationCanvas,
        SKImage inputFrame,
        SKRect destinationRect,
        in TerminalShaderFrameContext frameContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(destinationCanvas);
        ArgumentNullException.ThrowIfNull(inputFrame);

        if (_programs.Length == 0)
        {
            return false;
        }

        SKImage currentImage = inputFrame;
        bool disposeCurrentImage = false;

        try
        {
            for (int i = 0; i < _programs.Length; i++)
            {
                TerminalShaderProgram program = _programs[i];
                bool isLast = i == _programs.Length - 1;

                if (isLast)
                {
                    return DrawProgram(destinationCanvas, currentImage, destinationRect, program, frameContext);
                }

                SKImageInfo info = new(
                    frameContext.Width,
                    frameContext.Height,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul);
                using SKSurface? intermediate = SKSurface.Create(info);
                if (intermediate is null)
                {
                    return false;
                }

                SKRect intermediateRect = new(0, 0, frameContext.Width, frameContext.Height);
                if (!DrawProgram(intermediate.Canvas, currentImage, intermediateRect, program, frameContext))
                {
                    return false;
                }

                SKImage? snapshot = intermediate.Snapshot();
                if (snapshot is null)
                {
                    return false;
                }

                SKImage nextImage = snapshot;
                if (disposeCurrentImage)
                {
                    currentImage.Dispose();
                }

                currentImage = nextImage;
                disposeCurrentImage = true;
            }

            return false;
        }
        finally
        {
            if (disposeCurrentImage)
            {
                currentImage.Dispose();
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
        for (int i = 0; i < _programs.Length; i++)
        {
            _programs[i].Dispose();
        }
    }

    private static bool DrawProgram(
        SKCanvas canvas,
        SKImage image,
        SKRect destinationRect,
        TerminalShaderProgram program,
        in TerminalShaderFrameContext frameContext)
    {
        using SKShader inputShader = image.ToShader(
            SKShaderTileMode.Clamp,
            SKShaderTileMode.Clamp,
            SKSamplingOptions.Default);
        using SKRuntimeEffectUniforms uniforms = new(program.Effect);
        using SKRuntimeEffectChildren children = new(program.Effect);

        AddInputChildren(children, inputShader);
        AddFrameUniforms(uniforms, frameContext);

        using SKShader? shader = program.Effect.ToShader(uniforms, children);
        if (shader is null)
        {
            return false;
        }

        using SKPaint paint = new()
        {
            Shader = shader,
            IsAntialias = false,
            BlendMode = SKBlendMode.Src,
        };
        canvas.DrawRect(destinationRect, paint);
        return true;
    }

    private static void AddInputChildren(SKRuntimeEffectChildren children, SKShader inputShader)
    {
        AddChildIfPresent(children, "shaderTexture", inputShader);
        AddChildIfPresent(children, "iChannel0", inputShader);
        AddChildIfPresent(children, "inputTexture", inputShader);
    }

    private static void AddChildIfPresent(SKRuntimeEffectChildren children, string name, SKShader inputShader)
    {
        if (children.Contains(name))
        {
            children.Add(name, inputShader);
        }
    }

    private static void AddFrameUniforms(
        SKRuntimeEffectUniforms uniforms,
        in TerminalShaderFrameContext frameContext)
    {
        float width = frameContext.Width;
        float height = frameContext.Height;

        AddUniformIfPresent(uniforms, "iResolution", new[] { width, height, 1f });
        AddUniformIfPresent(uniforms, "iTime", frameContext.Time);
        AddUniformIfPresent(uniforms, "iTimeDelta", frameContext.TimeDelta);
        AddUniformIfPresent(uniforms, "iFrame", frameContext.Frame);
        AddUniformIfPresent(
            uniforms,
            "iChannelResolution",
            new[]
            {
                width, height, 1f,
                0f, 0f, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f,
            });
        AddUniformIfPresent(uniforms, "iBackgroundColor", ToRgbFloats(frameContext.BackgroundColor));
        AddUniformIfPresent(uniforms, "iForegroundColor", ToRgbFloats(frameContext.ForegroundColor));
        AddUniformIfPresent(uniforms, "iCursorColor", ToRgbFloats(frameContext.CursorColor));
        AddUniformIfPresent(uniforms, "iCurrentCursor", ToRectFloats(frameContext.CursorRect));
        AddUniformIfPresent(uniforms, "iCurrentCursorColor", ToRgbaFloats(frameContext.CursorColor));
        AddUniformIfPresent(uniforms, "iCurrentCursorStyle", ToCursorStyleFloats(frameContext.CursorStyle));
        AddUniformIfPresent(uniforms, "iCursorVisible", new[] { frameContext.CursorVisible ? 1f : 0f, 0f, 0f, 0f });

        AddUniformIfPresent(uniforms, "Time", frameContext.Time);
        AddUniformIfPresent(uniforms, "Scale", frameContext.Scale);
        AddUniformIfPresent(uniforms, "Resolution", new[] { width, height });
        AddUniformIfPresent(uniforms, "Background", ToRgbaFloats(frameContext.BackgroundColor));
    }

    private static void AddUniformIfPresent(
        SKRuntimeEffectUniforms uniforms,
        string name,
        SKRuntimeEffectUniform value)
    {
        if (uniforms.Contains(name))
        {
            uniforms[name] = value;
        }
    }

    private static float[] ToRgbFloats(SKColor color)
    {
        const float divisor = 255f;
        return [color.Red / divisor, color.Green / divisor, color.Blue / divisor];
    }

    private static float[] ToRgbaFloats(SKColor color)
    {
        const float divisor = 255f;
        return [color.Red / divisor, color.Green / divisor, color.Blue / divisor, color.Alpha / divisor];
    }

    private static float[] ToRectFloats(SKRect rect)
    {
        return [rect.Left, rect.Top, rect.Width, rect.Height];
    }

    private static float[] ToCursorStyleFloats(CursorStyle style)
    {
        return [(float)GetCursorStyleId(style), 0f, 0f, 0f];
    }

    private static int GetCursorStyleId(CursorStyle style)
    {
        return style switch
        {
            CursorStyle.Block => 0,
            CursorStyle.Underline => 3,
            CursorStyle.Bar => 2,
            _ => 0,
        };
    }

    private sealed class TerminalShaderProgram : IDisposable
    {
        public TerminalShaderProgram(TerminalShaderSource source, SKRuntimeEffect effect)
        {
            Source = source;
            Effect = effect;
        }

        public TerminalShaderSource Source { get; }

        public SKRuntimeEffect Effect { get; }

        public void Dispose()
        {
            Effect.Dispose();
        }
    }
}
