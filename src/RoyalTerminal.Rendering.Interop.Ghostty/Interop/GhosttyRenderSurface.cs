// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Interop.Ghostty - Managed render surface wrapper.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Rendering.Interop.Ghostty.Native;

namespace RoyalTerminal.Rendering.Interop.Ghostty;

/// <summary>
/// Managed renderer surface wrapper over <c>ghostty_render_surface_t</c>.
/// </summary>
public sealed class GhosttyRenderSurface : IRenderSurface
{
    private readonly GhosttyRenderContext _context;
    private readonly GhosttyRenderSurfaceHandle _handle;
    private bool _disposed;

    internal GhosttyRenderSurface(
        GhosttyRenderContext context,
        GhosttyRenderSurfaceHandle handle,
        RenderBackendKind backendKind)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        BackendKind = backendKind;
        Capabilities = CreateCapabilities(backendKind);
    }

    /// <inheritdoc />
    public RenderBackendKind BackendKind { get; }

    /// <inheritdoc />
    public RenderBackendCapabilities Capabilities { get; }

    /// <summary>
    /// Updates native focus state.
    /// </summary>
    public void SetFocus(bool focused)
    {
        ThrowIfDisposed();
        int resultCode = GhosttyRendererNative.SurfaceSetFocus(_handle.DangerousGetHandle(), focused ? (byte)1 : (byte)0);
        ThrowOnFailure(resultCode, "surface_set_focus");
    }

    /// <summary>
    /// Updates native color scheme id.
    /// </summary>
    public void SetColorScheme(uint colorScheme)
    {
        ThrowIfDisposed();
        int resultCode = GhosttyRendererNative.SurfaceSetColorScheme(_handle.DangerousGetHandle(), colorScheme);
        ThrowOnFailure(resultCode, "surface_set_color_scheme");
    }

    /// <summary>
    /// Begins an explicit render frame.
    /// </summary>
    /// <returns>Frame token provided by the native renderer.</returns>
    public ulong BeginFrame()
    {
        ThrowIfDisposed();
        int resultCode = GhosttyRendererNative.SurfaceBeginFrame(_handle.DangerousGetHandle(), out ulong frameToken);
        ThrowOnFailure(resultCode, "surface_begin_frame");
        return frameToken;
    }

    /// <summary>
    /// Ends an explicit render frame.
    /// </summary>
    /// <param name="frameToken">Frame token returned by <see cref="BeginFrame"/>.</param>
    public void EndFrame(ulong frameToken)
    {
        ThrowIfDisposed();
        int resultCode = GhosttyRendererNative.SurfaceEndFrame(_handle.DangerousGetHandle(), frameToken);
        ThrowOnFailure(resultCode, "surface_end_frame");
    }

    /// <summary>
    /// Renders one frame into a CPU RGBA destination buffer.
    /// </summary>
    /// <param name="destination">Destination RGBA bytes.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="stride">Bytes per row.</param>
    /// <returns>Render result.</returns>
    public unsafe RenderFrameResult RenderToRgba(Span<byte> destination, int width, int height, int stride)
    {
        ThrowIfDisposed();

        if (destination.Length == 0)
        {
            throw new ArgumentException("Destination buffer must be non-empty.", nameof(destination));
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride must be greater than zero.");
        }

        int minimumStride = checked(width * 4);
        if (stride < minimumStride)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride must be at least width * 4 bytes.");
        }

        int requiredLength = checked(stride * height);
        if (destination.Length < requiredLength)
        {
            throw new ArgumentException("Destination buffer is too small for the specified dimensions/stride.", nameof(destination));
        }

        uint destinationLength = checked((uint)destination.Length);
        fixed (byte* destinationPtr = destination)
        {
            int resultCode = GhosttyRendererNative.SurfaceRenderToRgba(
                _handle.DangerousGetHandle(),
                (nint)destinationPtr,
                destinationLength,
                width,
                height,
                stride);

            return resultCode == (int)GhosttyRenderInteropResult.Ok
                ? RenderFrameResult.Success()
                : RenderFrameResult.Failure(GhosttyRenderInteropResultMapper.GetMessage(resultCode));
        }
    }

    /// <inheritdoc />
    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        int resultCode = GhosttyRendererNative.SurfaceSetSize(_handle.DangerousGetHandle(), width, height);
        ThrowOnFailure(resultCode, "surface_set_size");
    }

    /// <inheritdoc />
    public void SetScale(double scaleX, double scaleY)
    {
        ThrowIfDisposed();
        if (scaleX <= 0 || !double.IsFinite(scaleX))
        {
            throw new ArgumentOutOfRangeException(nameof(scaleX), "ScaleX must be finite and greater than zero.");
        }

        if (scaleY <= 0 || !double.IsFinite(scaleY))
        {
            throw new ArgumentOutOfRangeException(nameof(scaleY), "ScaleY must be finite and greater than zero.");
        }

        int resultCode = GhosttyRendererNative.SurfaceSetScale(_handle.DangerousGetHandle(), scaleX, scaleY);
        ThrowOnFailure(resultCode, "surface_set_scale");
    }

    /// <inheritdoc />
    public unsafe RenderValidationResult ValidateTarget(in RenderTargetDescriptor descriptor)
    {
        ThrowIfDisposed();
        RenderValidationResult managedValidation = RenderTargetDescriptorValidator.Validate(descriptor);
        if (!managedValidation.IsValid)
        {
            return managedValidation;
        }

        byte[]? debugNameUtf8 = EncodeDebugName(descriptor.DebugName);
        fixed (byte* debugNamePtr = debugNameUtf8)
        {
            GhosttyRenderTargetDescriptorNative nativeDescriptor = ToNativeDescriptor(descriptor, (nint)debugNamePtr);
            int resultCode = GhosttyRendererNative.SurfaceValidateTarget(
                _handle.DangerousGetHandle(),
                in nativeDescriptor,
                out nint errorMessagePtr);

            if (resultCode == (int)GhosttyRenderInteropResult.Ok)
            {
                return RenderValidationResult.Valid();
            }

            string message = DecodeUtf8(errorMessagePtr) ?? GhosttyRenderInteropResultMapper.GetMessage(resultCode);
            return RenderValidationResult.Invalid(message);
        }
    }

    /// <inheritdoc />
    public unsafe RenderFrameResult Render(in RenderTargetDescriptor descriptor)
    {
        ThrowIfDisposed();
        RenderValidationResult managedValidation = RenderTargetDescriptorValidator.Validate(descriptor);
        if (!managedValidation.IsValid)
        {
            return RenderFrameResult.Failure(managedValidation.ErrorMessage ?? "Invalid render target descriptor.");
        }

        byte[]? debugNameUtf8 = EncodeDebugName(descriptor.DebugName);
        fixed (byte* debugNamePtr = debugNameUtf8)
        {
            GhosttyRenderTargetDescriptorNative nativeDescriptor = ToNativeDescriptor(descriptor, (nint)debugNamePtr);
            int resultCode = GhosttyRendererNative.SurfaceRenderToTarget(
                _handle.DangerousGetHandle(),
                in nativeDescriptor,
                out ulong syncToken);

            return resultCode == (int)GhosttyRenderInteropResult.Ok
                ? RenderFrameResult.Success(synchronizationToken: syncToken)
                : RenderFrameResult.Failure(GhosttyRenderInteropResultMapper.GetMessage(resultCode));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _handle.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static RenderBackendCapabilities CreateCapabilities(RenderBackendKind backendKind)
    {
        return backendKind switch
        {
            RenderBackendKind.Metal => new(
                backendKind,
                RenderFeatureFlags.ExternalTextureTargets |
                RenderFeatureFlags.ExplicitFrameLifecycle |
                RenderFeatureFlags.CpuRgbaFallback,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: [RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Bgra8Srgb, RenderPixelFormat.Rgba8Unorm, RenderPixelFormat.Rgba8Srgb]),

            RenderBackendKind.Vulkan => new(
                backendKind,
                RenderFeatureFlags.ExplicitFrameLifecycle |
                RenderFeatureFlags.CpuRgbaFallback,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: [RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Bgra8Srgb, RenderPixelFormat.Rgba8Unorm, RenderPixelFormat.Rgba8Srgb, RenderPixelFormat.Rgba16Float]),

            RenderBackendKind.D3D11 => new(
                backendKind,
                RenderFeatureFlags.ExplicitFrameLifecycle |
                RenderFeatureFlags.CpuRgbaFallback,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: [RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Bgra8Srgb, RenderPixelFormat.Rgba8Unorm]),

            RenderBackendKind.D3D12 => new(
                backendKind,
                RenderFeatureFlags.ExplicitFrameLifecycle |
                RenderFeatureFlags.CpuRgbaFallback,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: [RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Bgra8Srgb, RenderPixelFormat.Rgba8Unorm, RenderPixelFormat.Rgba16Float]),

            RenderBackendKind.Software => new(
                backendKind,
                RenderFeatureFlags.ExplicitFrameLifecycle |
                RenderFeatureFlags.CpuRgbaFallback,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: [RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Rgba8Unorm]),

            RenderBackendKind.OpenGL => new(
                backendKind,
                RenderFeatureFlags.ExplicitFrameLifecycle |
                RenderFeatureFlags.CpuRgbaFallback,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: [RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Rgba8Unorm]),

            _ => new(
                backendKind,
                RenderFeatureFlags.CpuRgbaFallback,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: [RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Rgba8Unorm]),
        };
    }

    private static GhosttyRenderTargetDescriptorNative ToNativeDescriptor(
        in RenderTargetDescriptor descriptor,
        nint debugNameUtf8) =>
        new()
        {
            Backend = descriptor.BackendKind,
            TargetKind = descriptor.TargetKind,
            PixelFormat = descriptor.PixelFormat,
            Width = descriptor.Width,
            Height = descriptor.Height,
            SampleCount = descriptor.SampleCount,
            DeviceHandle = descriptor.DeviceHandle,
            ContextHandle = descriptor.ContextHandle,
            CommandQueueHandle = descriptor.CommandQueueHandle,
            CommandBufferHandle = descriptor.CommandBufferHandle,
            TargetHandle = descriptor.TargetHandle,
            TargetViewHandle = descriptor.TargetViewHandle,
            FrameId = descriptor.FrameId,
            DebugNameUtf8 = debugNameUtf8,
        };

    private static byte[]? EncodeDebugName(string? debugName)
    {
        if (string.IsNullOrWhiteSpace(debugName))
        {
            return null;
        }

        return Encoding.UTF8.GetBytes(debugName + '\0');
    }

    private static string? DecodeUtf8(nint utf8Ptr)
    {
        if (utf8Ptr == nint.Zero)
        {
            return null;
        }

        return Marshal.PtrToStringUTF8(utf8Ptr);
    }

    private static void ThrowOnFailure(int resultCode, string operation)
    {
        GhosttyRenderInteropResult result = GhosttyRenderInteropResultMapper.FromNativeCode(resultCode);
        if (result == GhosttyRenderInteropResult.Ok)
        {
            return;
        }

        string message = GhosttyRenderInteropResultMapper.GetMessage(resultCode);
        throw new GhosttyRenderInteropException(operation, result, message);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.ThrowIfDisposed();
    }
}
