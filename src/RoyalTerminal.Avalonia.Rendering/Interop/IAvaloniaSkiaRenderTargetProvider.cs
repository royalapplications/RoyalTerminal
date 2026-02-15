// Licensed under the MIT License.
// RoyalTerminal.Avalonia.Rendering - Avalonia render target provider for Skia interop rendering.

using Avalonia;
using Avalonia.Skia;
using RoyalTerminal.Rendering.Interop.Skia;

namespace RoyalTerminal.Avalonia.Rendering.Interop;

/// <summary>
/// Produces interop render requests from the current Avalonia Skia render state.
/// </summary>
public interface IAvaloniaSkiaRenderTargetProvider
{
    /// <summary>
    /// Gets or sets backend selection preference for texture interop attempts.
    /// </summary>
    AvaloniaRenderBackendPreference BackendPreference { get; set; }

    /// <summary>
    /// Gets the last diagnostic emitted by this provider.
    /// </summary>
    string? LastDiagnostic { get; }

    /// <summary>
    /// Raised when a new adapter diagnostic is produced.
    /// </summary>
    event EventHandler<string>? DiagnosticReported;

    /// <summary>
    /// Creates a render request for the current frame.
    /// </summary>
    /// <param name="lease">Active Skia API lease.</param>
    /// <param name="pixelSize">Target pixel size.</param>
    /// <returns>Configured render request.</returns>
    SkiaInteropRenderRequest CreateRenderRequest(ISkiaSharpApiLease lease, PixelSize pixelSize);
}
