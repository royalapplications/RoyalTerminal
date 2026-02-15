// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Skia - Skia bridge for renderer interop.

using System.Buffers;
using System.Runtime.InteropServices;
using RoyalTerminal.Rendering.Contracts;
using SkiaSharp;

namespace RoyalTerminal.Rendering.Interop.Skia;

/// <summary>
/// Bridges renderer interop surfaces to Skia rendering targets.
/// Uses backend-aware direct texture interop with CPU RGBA fallback.
/// </summary>
public sealed class SkiaInteropRenderer
{
    private readonly IRenderSurface _renderSurface;
    private readonly ISkiaRgbaFallbackRenderer? _rgbaFallbackRenderer;

    /// <summary>
    /// Initializes a new Skia interop bridge.
    /// </summary>
    public SkiaInteropRenderer(IRenderSurface renderSurface, ISkiaRgbaFallbackRenderer? rgbaFallbackRenderer = null)
    {
        _renderSurface = renderSurface ?? throw new ArgumentNullException(nameof(renderSurface));
        _rgbaFallbackRenderer = rgbaFallbackRenderer;
    }

    /// <summary>
    /// Executes one render pass.
    /// </summary>
    /// <param name="canvas">Destination Skia canvas.</param>
    /// <param name="request">Render request.</param>
    /// <returns>Render result including whether fallback was used.</returns>
    public SkiaInteropRenderResult Render(SKCanvas canvas, in SkiaInteropRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        RenderValidationResult descriptorValidation = RenderTargetDescriptorValidator.Validate(request.TargetDescriptor);
        if (!descriptorValidation.IsValid)
        {
            string message = descriptorValidation.ErrorMessage ?? "Invalid render target descriptor.";
            return new SkiaInteropRenderResult(RenderFrameResult.Failure(message), usedCpuFallback: false);
        }

        bool allowCpuFallback = request.AllowCpuFallback && _rgbaFallbackRenderer is not null;
        bool backendSupportsDirectInterop =
            _renderSurface.Capabilities.SupportsFeatures(RenderFeatureFlags.ExternalTextureTargets);
        bool supportsDirectInterop = backendSupportsDirectInterop && SkiaInteropSupport.CanUseDirectTextureInterop(
            request.TargetDescriptor,
            _renderSurface.BackendKind);
        if (supportsDirectInterop)
        {
            RenderValidationResult surfaceValidation = _renderSurface.ValidateTarget(request.TargetDescriptor);
            if (!surfaceValidation.IsValid)
            {
                if (!allowCpuFallback)
                {
                    string message = surfaceValidation.ErrorMessage ?? "Render target is not valid for direct texture interop.";
                    return new SkiaInteropRenderResult(RenderFrameResult.Failure(message), usedCpuFallback: false);
                }

                RenderFrameResult validationFallbackResult = RenderWithCpuFallback(canvas, request);
                if (!validationFallbackResult.Succeeded)
                {
                    string combinedMessage = CombineFailureMessages(
                        surfaceValidation.ErrorMessage ?? "Render target is not valid for direct texture interop.",
                        validationFallbackResult.ErrorMessage);
                    RenderFrameResult combinedFailure = RenderFrameResult.Failure(combinedMessage);
                    return new SkiaInteropRenderResult(combinedFailure, usedCpuFallback: true);
                }

                return new SkiaInteropRenderResult(validationFallbackResult, usedCpuFallback: true);
            }

            RenderFrameResult directResult = _renderSurface.Render(request.TargetDescriptor);
            if (directResult.Succeeded || !allowCpuFallback)
            {
                return new SkiaInteropRenderResult(directResult, usedCpuFallback: false);
            }

            RenderFrameResult directFallbackResult = RenderWithCpuFallback(canvas, request);
            if (!directFallbackResult.Succeeded)
            {
                string combinedMessage = CombineFailureMessages(
                    directResult.ErrorMessage ?? "Direct render failed.",
                    directFallbackResult.ErrorMessage);
                RenderFrameResult combinedFailure = RenderFrameResult.Failure(combinedMessage);
                return new SkiaInteropRenderResult(combinedFailure, usedCpuFallback: true);
            }

            return new SkiaInteropRenderResult(directFallbackResult, usedCpuFallback: true);
        }

        if (!allowCpuFallback)
        {
            string message = "Direct Skia texture interop is unavailable for this render target/backend and CPU fallback is disabled.";
            return new SkiaInteropRenderResult(RenderFrameResult.Failure(message), usedCpuFallback: false);
        }

        RenderFrameResult fallbackResult = RenderWithCpuFallback(canvas, request);
        return new SkiaInteropRenderResult(fallbackResult, usedCpuFallback: true);
    }

    private RenderFrameResult RenderWithCpuFallback(SKCanvas canvas, in SkiaInteropRenderRequest request)
    {
        if (_rgbaFallbackRenderer is null)
        {
            return RenderFrameResult.Failure("CPU fallback renderer is not configured.");
        }

        RenderTargetDescriptor descriptor = request.TargetDescriptor;
        if (descriptor.Width <= 0 || descriptor.Height <= 0)
        {
            return RenderFrameResult.Failure("Fallback render requires positive width and height.");
        }

        int stride = checked(descriptor.Width * 4);
        int byteLength = checked(stride * descriptor.Height);
        SKRect destinationRect = request.DestinationRect ??
                                 new SKRect(0, 0, descriptor.Width, descriptor.Height);
        if (destinationRect.Width <= 0 || destinationRect.Height <= 0)
        {
            return RenderFrameResult.Failure("Fallback destination rectangle must have positive width and height.");
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(byteLength);

        try
        {
            Span<byte> destination = buffer.AsSpan(0, byteLength);
            RenderFrameResult fallbackRenderResult =
                _rgbaFallbackRenderer.RenderToRgba(destination, descriptor.Width, descriptor.Height, stride);

            if (!fallbackRenderResult.Succeeded)
            {
                return fallbackRenderResult;
            }

            if (request.ClearCanvasBeforeFallbackDraw)
            {
                canvas.Clear(request.ClearColor);
            }

            SKImageInfo info = new(
                descriptor.Width,
                descriptor.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Unpremul);

            GCHandle pinnedBuffer = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                nint pixels = pinnedBuffer.AddrOfPinnedObject();
                using SKImage? image = SKImage.FromPixels(info, pixels, stride);
                if (image is null)
                {
                    return RenderFrameResult.Failure("Failed to create Skia image from CPU fallback pixels.");
                }

                canvas.DrawImage(image, destinationRect);
            }
            finally
            {
                pinnedBuffer.Free();
            }

            return fallbackRenderResult;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string CombineFailureMessages(string directErrorMessage, string? fallbackErrorMessage)
    {
        if (string.IsNullOrWhiteSpace(fallbackErrorMessage))
        {
            return $"Direct render failed: {directErrorMessage}";
        }

        return $"Direct render failed: {directErrorMessage} CPU fallback render failed: {fallbackErrorMessage}";
    }
}
