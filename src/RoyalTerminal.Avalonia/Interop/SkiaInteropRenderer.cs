// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty.Skia - Skia bridge for renderer interop.

using System.Buffers;
using System.Runtime.InteropServices;
using RoyalTerminal.Rendering.Contracts;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Interop;

/// <summary>
/// Bridges renderer interop surfaces to Skia rendering targets.
/// Uses backend-aware direct texture/framebuffer interop with CPU RGBA fallback.
/// </summary>
public sealed class SkiaInteropRenderer
{
    private readonly IRenderSurface _renderSurface;
    private readonly ISkiaRgbaFallbackRenderer? _rgbaFallbackRenderer;
    private ulong _nextExplicitSyncFrameId = 1;
    private RenderBackendKind _explicitSyncBackendKind = RenderBackendKind.Unknown;

    /// <summary>
    /// Gets the underlying render surface.
    /// </summary>
    public IRenderSurface RenderSurface => _renderSurface;

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
        return Render(canvas, null, in request);
    }

    /// <summary>
    /// Executes one render pass.
    /// </summary>
    /// <param name="canvas">Destination Skia canvas.</param>
    /// <param name="grContext">GRContext from active Skia lease.</param>
    /// <param name="request">Render request.</param>
    /// <returns>Render result including whether fallback was used.</returns>
    public SkiaInteropRenderResult Render(SKCanvas canvas, GRContext? grContext, in SkiaInteropRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        RenderValidationResult descriptorValidation = RenderTargetDescriptorValidator.Validate(request.TargetDescriptor);
        if (!descriptorValidation.IsValid)
        {
            string message = descriptorValidation.ErrorMessage ?? "Invalid render target descriptor.";
            return new SkiaInteropRenderResult(RenderFrameResult.Failure(message), usedCpuFallback: false);
        }

        bool allowCpuFallback = request.AllowCpuFallback && _rgbaFallbackRenderer is not null;
        bool backendSupportsDirectInterop = SkiaInteropSupport.SupportsDirectInteropTarget(
            _renderSurface.Capabilities.FeatureFlags,
            request.TargetDescriptor.TargetKind);
        bool supportsDirectInterop = backendSupportsDirectInterop && SkiaInteropSupport.CanUseDirectInterop(
            request.TargetDescriptor,
            _renderSurface.BackendKind);
        if (!supportsDirectInterop)
        {
            Console.WriteLine($"[Renderer Info] Direct interop check failed. BackendSupportsDirect={backendSupportsDirectInterop}, Features={_renderSurface.Capabilities.FeatureFlags}, TargetKind={request.TargetDescriptor.TargetKind}, DescriptorBackend={request.TargetDescriptor.BackendKind}, SurfaceBackend={_renderSurface.BackendKind}, DeviceHandle=0x{request.TargetDescriptor.DeviceHandle:X}, TargetHandle=0x{request.TargetDescriptor.TargetHandle:X}");
        }
        if (supportsDirectInterop)
        {
            RenderTargetDescriptor synchronizedDescriptor = PrepareSynchronizedDescriptor(request.TargetDescriptor, out bool explicitSyncEnabled);
            RenderValidationResult surfaceValidation = _renderSurface.ValidateTarget(synchronizedDescriptor);
            if (!surfaceValidation.IsValid)
            {
                if (!allowCpuFallback)
                {
                    string message = surfaceValidation.ErrorMessage ?? "Render target is not valid for direct interop.";
                    return new SkiaInteropRenderResult(RenderFrameResult.Failure(message), usedCpuFallback: false);
                }

                RenderFrameResult validationFallbackResult = RenderWithCpuFallback(canvas, request);
                if (!validationFallbackResult.Succeeded)
                {
                    string combinedMessage = CombineFailureMessages(
                        surfaceValidation.ErrorMessage ?? "Render target is not valid for direct interop.",
                        validationFallbackResult.ErrorMessage);
                    RenderFrameResult combinedFailure = RenderFrameResult.Failure(combinedMessage);
                    return new SkiaInteropRenderResult(combinedFailure, usedCpuFallback: true);
                }

                return new SkiaInteropRenderResult(validationFallbackResult, usedCpuFallback: true);
            }

            RenderFrameResult directResult = _renderSurface.Render(synchronizedDescriptor);
            AdvanceSynchronizationState(explicitSyncEnabled, synchronizedDescriptor.FrameId, directResult);
            if (directResult.Succeeded)
            {
                if (synchronizedDescriptor.DebugName == "avalonia-skiacanvas-metal-shared" && grContext is not null)
                {
                    var mtlInfo = new GRMtlTextureInfo
                    {
                        TextureHandle = synchronizedDescriptor.TargetHandle
                    };
                    using var backendTexture = new GRBackendTexture(
                        synchronizedDescriptor.Width,
                        synchronizedDescriptor.Height,
                        false,
                        mtlInfo);
                    using var image = SKImage.FromTexture(
                        grContext,
                        backendTexture,
                        GRSurfaceOrigin.TopLeft,
                        SKColorType.Bgra8888);
                    if (image is not null)
                    {
                        canvas.DrawImage(image, request.DestinationRect ?? new SKRect(0, 0, synchronizedDescriptor.Width, synchronizedDescriptor.Height));
                    }
                }

                return new SkiaInteropRenderResult(directResult, usedCpuFallback: false);
            }

            if (!allowCpuFallback)
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
            string message = $"Direct Skia interop is unavailable. Descriptor: Backend={request.TargetDescriptor.BackendKind}, Target={request.TargetDescriptor.TargetKind}, Width={request.TargetDescriptor.Width}, Height={request.TargetDescriptor.Height}, Device=0x{request.TargetDescriptor.DeviceHandle:X}, Queue=0x{request.TargetDescriptor.CommandQueueHandle:X}, TargetHandle=0x{request.TargetDescriptor.TargetHandle:X}. Surface: Backend={_renderSurface.BackendKind}, Features={_renderSurface.Capabilities.FeatureFlags}";
            return new SkiaInteropRenderResult(RenderFrameResult.Failure(message), usedCpuFallback: false);
        }

        RenderFrameResult fallbackResult = RenderWithCpuFallback(canvas, request);
        return new SkiaInteropRenderResult(fallbackResult, usedCpuFallback: true);
    }

    private RenderTargetDescriptor PrepareSynchronizedDescriptor(
        in RenderTargetDescriptor descriptor,
        out bool explicitSyncEnabled)
    {
        explicitSyncEnabled = RequiresExplicitSynchronization(descriptor);
        if (!explicitSyncEnabled)
        {
            return descriptor;
        }

        if (_explicitSyncBackendKind != descriptor.BackendKind)
        {
            _explicitSyncBackendKind = descriptor.BackendKind;
            _nextExplicitSyncFrameId = 1;
        }

        ulong frameId = descriptor.FrameId != 0 ? descriptor.FrameId : _nextExplicitSyncFrameId;
        if (frameId == 0)
        {
            frameId = 1;
        }

        return descriptor with { FrameId = frameId };
    }

    private void AdvanceSynchronizationState(bool explicitSyncEnabled, ulong frameId, in RenderFrameResult frameResult)
    {
        if (!explicitSyncEnabled)
        {
            return;
        }

        ulong nextFrameId = IncrementFrameId(frameId);
        if (frameResult.Succeeded && frameResult.SynchronizationToken != 0)
        {
            nextFrameId = IncrementFrameId(frameResult.SynchronizationToken);
        }

        _nextExplicitSyncFrameId = nextFrameId;
    }

    private bool RequiresExplicitSynchronization(in RenderTargetDescriptor descriptor)
    {
        return descriptor.BackendKind == _renderSurface.BackendKind &&
               (_renderSurface.Capabilities.FeatureFlags & RenderFeatureFlags.ExplicitSynchronization) != 0;
    }

    private static ulong IncrementFrameId(ulong frameId)
    {
        return frameId == ulong.MaxValue ? 1 : frameId + 1;
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
                SKColorType.Bgra8888,
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
