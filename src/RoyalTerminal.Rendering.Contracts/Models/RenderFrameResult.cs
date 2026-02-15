// Licensed under the MIT License.
// GhosttySharp.Rendering.Contracts - Frame render result model.

namespace GhosttySharp.Rendering.Contracts;

/// <summary>
/// Represents the outcome of one render invocation.
/// </summary>
public readonly record struct RenderFrameResult
{
    /// <summary>
    /// Initializes a new frame result.
    /// </summary>
    public RenderFrameResult(
        bool succeeded,
        bool requiresRedraw,
        ulong synchronizationToken,
        long gpuTimeNanoseconds,
        string? errorMessage)
    {
        Succeeded = succeeded;
        RequiresRedraw = requiresRedraw;
        SynchronizationToken = synchronizationToken;
        GpuTimeNanoseconds = gpuTimeNanoseconds;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets whether rendering succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets whether a follow-up redraw is requested.
    /// </summary>
    public bool RequiresRedraw { get; }

    /// <summary>
    /// Gets an optional synchronization token from the backend.
    /// </summary>
    public ulong SynchronizationToken { get; }

    /// <summary>
    /// Gets measured GPU time in nanoseconds, if available.
    /// </summary>
    public long GpuTimeNanoseconds { get; }

    /// <summary>
    /// Gets the error message when rendering fails.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful frame result.
    /// </summary>
    public static RenderFrameResult Success(
        bool requiresRedraw = false,
        ulong synchronizationToken = 0,
        long gpuTimeNanoseconds = 0) =>
        new(true, requiresRedraw, synchronizationToken, gpuTimeNanoseconds, null);

    /// <summary>
    /// Creates a failed frame result.
    /// </summary>
    public static RenderFrameResult Failure(string errorMessage) =>
        new(false, false, 0, 0, string.IsNullOrWhiteSpace(errorMessage) ? "Render failed." : errorMessage);
}
