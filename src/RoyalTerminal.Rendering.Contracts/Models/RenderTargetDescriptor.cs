// Licensed under the MIT License.
// RoyalTerminal.Rendering.Contracts - Render target descriptor model.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Describes an external render target submission for a single frame.
/// </summary>
public readonly record struct RenderTargetDescriptor
{
    /// <summary>
    /// Gets the GPU backend for this target.
    /// </summary>
    public RenderBackendKind BackendKind { get; init; }

    /// <summary>
    /// Gets the target kind.
    /// </summary>
    public RenderTargetKind TargetKind { get; init; }

    /// <summary>
    /// Gets the target pixel format.
    /// </summary>
    public RenderPixelFormat PixelFormat { get; init; }

    /// <summary>
    /// Gets target width in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Gets target height in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Gets the multisample count.
    /// </summary>
    public uint SampleCount { get; init; }

    /// <summary>
    /// Gets backend device/context handle (backend-specific).
    /// </summary>
    public nint DeviceHandle { get; init; }

    /// <summary>
    /// Gets backend context handle (typically OpenGL context).
    /// </summary>
    public nint ContextHandle { get; init; }

    /// <summary>
    /// Gets backend command queue handle.
    /// </summary>
    public nint CommandQueueHandle { get; init; }

    /// <summary>
    /// Gets backend command buffer/list handle.
    /// </summary>
    public nint CommandBufferHandle { get; init; }

    /// <summary>
    /// Gets the primary target resource handle (texture/image/FBO).
    /// </summary>
    public nint TargetHandle { get; init; }

    /// <summary>
    /// Gets an optional target view handle (RTV/SRV/image view).
    /// </summary>
    public nint TargetViewHandle { get; init; }

    /// <summary>
    /// Gets caller-provided frame identifier.
    /// </summary>
    public ulong FrameId { get; init; }

    /// <summary>
    /// Gets an optional debug name.
    /// </summary>
    public string? DebugName { get; init; }
}
