// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Avalonia composition draw handler.

using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RoyalTerminal.Shaders;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Custom visual draw handler for rendering the terminal using SkiaSharp
/// within Avalonia's composition system.
/// Always performs full redraws because the composition canvas is immediate-mode
/// (cleared each frame). Thread-safe: acquires TerminalScreen.SyncRoot during rendering.
/// </summary>
public class TerminalDrawHandler : CompositionCustomVisualHandler
{
    private SkiaTerminalRenderer? _renderer;
    private TerminalScreen? _screen;
    private TerminalShaderPostProcessor? _shaderPostProcessor;
    private TerminalShaderPackage? _shaderPackage;
    private TerminalShaderBackendPreference _shaderBackendPreference;
    private ITerminalShaderResourceProvider? _shaderResourceProvider;
    private ITerminalShaderDiagnosticsSink? _shaderDiagnosticsSink;
    private ITerminalShaderPackageExecutor? _shaderPackageExecutor;
    private bool _shaderAnimationEnabled;
    private bool _pendingRender;
    private long _shaderStartTimestamp;
    private long _lastShaderTimestamp;
    private int _shaderFrame;
    private string? _lastRuntimeDiagnosticKey;

    /// <summary>
    /// Message types for communicating with the handler from the UI thread.
    /// </summary>
    public record UpdateMessage(SkiaTerminalRenderer Renderer, TerminalScreen Screen);
    public readonly record struct InvalidateMessage();
    public readonly record struct ResizeMessage();
    public readonly record struct ShaderStateMessage(
        IReadOnlyList<TerminalShaderSource>? Sources,
        TerminalShaderPackage? Package,
        TerminalShaderBackendPreference BackendPreference,
        ITerminalShaderResourceProvider? ResourceProvider,
        ITerminalShaderDiagnosticsSink? DiagnosticsSink,
        ITerminalShaderPackageExecutor? PackageExecutor,
        bool AnimationEnabled);

    public override void OnMessage(object message)
    {
        switch (message)
        {
            case UpdateMessage update:
                _renderer = update.Renderer;
                _screen = update.Screen;
                RequestRender();
                break;

            case InvalidateMessage:
                RequestRender();
                break;

            case ResizeMessage:
                RequestRender();
                break;

            case ShaderStateMessage shaderState:
                SetShaderState(
                    shaderState.Sources,
                    shaderState.Package,
                    shaderState.BackendPreference,
                    shaderState.ResourceProvider,
                    shaderState.DiagnosticsSink,
                    shaderState.PackageExecutor,
                    shaderState.AnimationEnabled);
                RequestRender();
                break;
        }
    }

    public override void OnAnimationFrameUpdate()
    {
        if (_pendingRender)
            Invalidate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        try
        {
            var renderer = _renderer;
            var screen = _screen;
            if (renderer is null || screen is null)
            {
                return;
            }

            var leaseFeature = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature;
            if (leaseFeature is null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            if (canvas is null)
            {
                return;
            }

            lock (screen.SyncRoot)
            {
                canvas.Save();
                try
                {
                    TerminalShaderPostProcessor? shaderPostProcessor = _shaderPostProcessor;
                    TerminalShaderPackage? shaderPackage = _shaderPackage;
                    ITerminalShaderPackageExecutor? shaderPackageExecutor = _shaderPackageExecutor;
                    if (shaderPackage is not null && shaderPackageExecutor is not null)
                    {
                        RenderWithShaderPackage(canvas, renderer, screen, shaderPackage, shaderPackageExecutor);
                    }
                    else if (shaderPostProcessor is not null && shaderPostProcessor.HasShaders)
                    {
                        RenderWithShaders(canvas, renderer, screen, shaderPostProcessor);
                    }
                    else
                    {
                        canvas.Clear(new SKColor(screen.DefaultBackground));
                        renderer.RenderFull(canvas, screen);
                    }
                }
                finally
                {
                    canvas.Restore();
                }
            }
        }
        catch
        {
            // Swallow render errors during shutdown/teardown
        }
        finally
        {
            _pendingRender = ShouldContinueShaderAnimation();
            if (_pendingRender)
            {
                RegisterForNextAnimationFrameUpdate();
            }
        }
    }

    private void RequestRender()
    {
        if (_pendingRender)
        {
            return;
        }

        _pendingRender = true;
        RegisterForNextAnimationFrameUpdate();
    }

    private void SetShaderState(
        IReadOnlyList<TerminalShaderSource>? sources,
        TerminalShaderPackage? package,
        TerminalShaderBackendPreference backendPreference,
        ITerminalShaderResourceProvider? resourceProvider,
        ITerminalShaderDiagnosticsSink? diagnosticsSink,
        ITerminalShaderPackageExecutor? packageExecutor,
        bool animationEnabled)
    {
        _shaderPostProcessor?.Dispose();
        _shaderPostProcessor = TerminalShaderPostProcessor.Create(sources);
        _shaderPackage = package;
        _shaderBackendPreference = backendPreference;
        _shaderResourceProvider = resourceProvider;
        _shaderDiagnosticsSink = diagnosticsSink;
        _shaderPackageExecutor = packageExecutor;
        _shaderAnimationEnabled = animationEnabled;
        _shaderStartTimestamp = 0;
        _lastShaderTimestamp = 0;
        _shaderFrame = 0;
        _lastRuntimeDiagnosticKey = null;
    }

    private bool ShouldContinueShaderAnimation()
    {
        TerminalShaderPostProcessor? shaderPostProcessor = _shaderPostProcessor;
        bool packageAnimation = _shaderPackage is not null && _shaderPackageExecutor is not null;
        return _shaderAnimationEnabled &&
               ((shaderPostProcessor is not null &&
                 shaderPostProcessor.HasShaders &&
                 shaderPostProcessor.RequiresContinuousAnimation) ||
                packageAnimation);
    }

    private void RenderWithShaders(
        SKCanvas destinationCanvas,
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        TerminalShaderPostProcessor shaderPostProcessor)
    {
        SKRect clip = destinationCanvas.LocalClipBounds;
        int width = Math.Max(1, (int)Math.Ceiling(clip.Width));
        int height = Math.Max(1, (int)Math.Ceiling(clip.Height));
        SKImageInfo imageInfo = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using SKSurface? terminalSurface = SKSurface.Create(imageInfo);
        if (terminalSurface is null)
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            renderer.RenderFull(destinationCanvas, screen);
            return;
        }

        SKCanvas terminalCanvas = terminalSurface.Canvas;
        terminalCanvas.Clear(new SKColor(screen.DefaultBackground));
        renderer.RenderFull(terminalCanvas, screen);

        using SKImage? terminalFrame = terminalSurface.Snapshot();
        if (terminalFrame is null)
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            renderer.RenderFull(destinationCanvas, screen);
            return;
        }

        TerminalShaderFrameContext frameContext = CreateFrameContext(renderer, screen, width, height);
        SKRect destinationRect = new(0, 0, width, height);
        if (!shaderPostProcessor.TryApply(destinationCanvas, terminalFrame, destinationRect, frameContext))
        {
            destinationCanvas.DrawImage(terminalFrame, destinationRect);
        }
    }

    private void RenderWithShaderPackage(
        SKCanvas destinationCanvas,
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        TerminalShaderPackage package,
        ITerminalShaderPackageExecutor executor)
    {
        SKRect clip = destinationCanvas.LocalClipBounds;
        int width = Math.Max(1, (int)Math.Ceiling(clip.Width));
        int height = Math.Max(1, (int)Math.Ceiling(clip.Height));
        SKImageInfo imageInfo = new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using SKSurface? terminalSurface = SKSurface.Create(imageInfo);
        if (terminalSurface is null)
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            renderer.RenderFull(destinationCanvas, screen);
            return;
        }

        SKCanvas terminalCanvas = terminalSurface.Canvas;
        terminalCanvas.Clear(new SKColor(screen.DefaultBackground));
        renderer.RenderFull(terminalCanvas, screen);

        using SKImage? terminalFrame = terminalSurface.Snapshot();
        if (terminalFrame is null)
        {
            destinationCanvas.Clear(new SKColor(screen.DefaultBackground));
            renderer.RenderFull(destinationCanvas, screen);
            return;
        }

        SKRect destinationRect = new(0, 0, width, height);
        byte[]? terminalPixels = CopyRgbaPixels(terminalFrame, width, height);
        if (terminalPixels is null)
        {
            ReportDiagnosticOnce(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Error,
                "RTSHADERCONTROL002",
                "Terminal framebuffer pixels could not be copied for full shader package execution."));
            destinationCanvas.DrawImage(terminalFrame, destinationRect);
            return;
        }

        TerminalShaderFrameContext frameContext = CreateFrameContext(renderer, screen, width, height);
        TerminalShaderResourceValue terminalFramebuffer = new(
            TerminalShaderBuiltInResourceNames.TerminalFramebuffer,
            TerminalShaderResourceKind.TerminalFramebuffer,
            data: terminalPixels,
            width: width,
            height: height);

        TerminalShaderFrameResult result;
        try
        {
            TerminalShaderFrameRequest request = TerminalShaderRuntimePipeline
                .CreateFrameRequestAsync(
                    package,
                    width,
                    height,
                    frameContext.Time,
                    frameContext.TimeDelta,
                    frameContext.Frame,
                    frameContext.Scale,
                    [terminalFramebuffer],
                    _shaderResourceProvider)
                .GetAwaiter()
                .GetResult();
            result = executor
                .RenderFrameAsync(package, request)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            ReportDiagnosticOnce(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Error,
                "RTSHADERCONTROL003",
                $"Full shader package '{package.Name}' failed during execution: {ex.Message}"));
            destinationCanvas.DrawImage(terminalFrame, destinationRect);
            return;
        }

        ReportDiagnosticsOnce(result.Diagnostics);
        if (result.IsSuccess && TryDrawPixelResult(destinationCanvas, result, destinationRect))
        {
            return;
        }

        if (result.IsSuccess && result.NativeTextureHandle != 0)
        {
            ReportDiagnosticOnce(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Warning,
                "RTSHADERCONTROL004",
                $"Full shader package '{package.Name}' produced a native {result.BackendKind} texture, but this renderer requires CPU pixel data until a zero-copy import adapter is registered."));
        }

        destinationCanvas.DrawImage(terminalFrame, destinationRect);
    }

    private static byte[]? CopyRgbaPixels(SKImage image, int width, int height)
    {
        using SKPixmap pixmap = image.PeekPixels();
        if (pixmap is null || pixmap.ColorType != SKColorType.Rgba8888)
        {
            return null;
        }

        int targetRowBytes = checked(width * 4);
        byte[] pixels = new byte[checked(targetRowBytes * height)];
        IntPtr source = pixmap.GetPixels();
        int sourceRowBytes = pixmap.RowBytes;
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(
                IntPtr.Add(source, y * sourceRowBytes),
                pixels,
                y * targetRowBytes,
                targetRowBytes);
        }

        return pixels;
    }

    private static bool TryDrawPixelResult(
        SKCanvas destinationCanvas,
        TerminalShaderFrameResult result,
        SKRect destinationRect)
    {
        if (result.PixelData.Length == 0 || result.Width <= 0 || result.Height <= 0)
        {
            return false;
        }

        int expectedBytes = checked(result.Width * result.Height * 4);
        byte[] pixels = result.PixelData.ToArray();
        if (pixels.Length < expectedBytes)
        {
            return false;
        }

        SKImageInfo imageInfo = new(result.Width, result.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(imageInfo);
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), expectedBytes);
        using SKImage image = SKImage.FromBitmap(bitmap);
        destinationCanvas.DrawImage(image, destinationRect);
        return true;
    }

    private void ReportDiagnosticsOnce(IReadOnlyList<TerminalShaderDiagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            ReportDiagnosticOnce(diagnostics[i]);
        }
    }

    private void ReportDiagnosticOnce(TerminalShaderDiagnostic diagnostic)
    {
        ITerminalShaderDiagnosticsSink? diagnosticsSink = _shaderDiagnosticsSink;
        if (diagnosticsSink is null)
        {
            return;
        }

        string key = $"{diagnostic.Code}|{diagnostic.Message}";
        if (string.Equals(_lastRuntimeDiagnosticKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _lastRuntimeDiagnosticKey = key;
        diagnosticsSink.Report(diagnostic);
    }

    private TerminalShaderFrameContext CreateFrameContext(
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        int width,
        int height)
    {
        long now = Stopwatch.GetTimestamp();
        if (_shaderStartTimestamp == 0)
        {
            _shaderStartTimestamp = now;
            _lastShaderTimestamp = now;
        }

        float time = (float)Stopwatch.GetElapsedTime(_shaderStartTimestamp, now).TotalSeconds;
        float timeDelta = (float)Stopwatch.GetElapsedTime(_lastShaderTimestamp, now).TotalSeconds;
        _lastShaderTimestamp = now;

        SKRect cursorRect = GetCursorRect(renderer);
        TerminalShaderFrameContext context = new(
            width,
            height,
            time,
            timeDelta,
            _shaderFrame,
            scale: 1f,
            new SKColor(screen.DefaultBackground),
            new SKColor(screen.DefaultForeground),
            renderer.CursorColor,
            cursorRect,
            renderer.CursorStyle,
            renderer.CursorVisible);
        _shaderFrame++;
        return context;
    }

    private static SKRect GetCursorRect(SkiaTerminalRenderer renderer)
    {
        float left = renderer.CursorColumn * renderer.CellWidth;
        float top = renderer.CursorRow * renderer.CellHeight;
        return renderer.CursorStyle switch
        {
            CursorStyle.Bar => new SKRect(left, top, left + Math.Max(1f, renderer.CellWidth * 0.12f), top + renderer.CellHeight),
            CursorStyle.Underline => new SKRect(
                left,
                top + Math.Max(0f, renderer.CellHeight - Math.Max(1f, renderer.CellHeight * 0.14f)),
                left + renderer.CellWidth,
                top + renderer.CellHeight),
            _ => new SKRect(left, top, left + renderer.CellWidth, top + renderer.CellHeight),
        };
    }
}
