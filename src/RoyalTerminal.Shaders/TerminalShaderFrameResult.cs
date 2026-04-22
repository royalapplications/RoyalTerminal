// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Contains the result of rendering one full shader package frame.
/// </summary>
public sealed class TerminalShaderFrameResult
{
    /// <summary>
    /// Initializes a new frame result.
    /// </summary>
    /// <param name="backendKind">Backend kind.</param>
    /// <param name="nativeTextureHandle">Optional final native texture handle.</param>
    /// <param name="nativeTexture">Optional final native texture descriptor.</param>
    /// <param name="pixelData">Optional final CPU pixel data.</param>
    /// <param name="width">Final frame width.</param>
    /// <param name="height">Final frame height.</param>
    /// <param name="diagnostics">Frame diagnostics.</param>
    public TerminalShaderFrameResult(
        TerminalShaderBackendKind backendKind,
        nint nativeTextureHandle = 0,
        TerminalShaderNativeTexture? nativeTexture = null,
        ReadOnlyMemory<byte> pixelData = default,
        int width = 0,
        int height = 0,
        IReadOnlyList<TerminalShaderDiagnostic>? diagnostics = null)
    {
        BackendKind = backendKind;
        PixelData = pixelData.ToArray();
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        NativeTexture = nativeTexture ?? CreateLegacyNativeTexture(
            backendKind,
            nativeTextureHandle,
            Width,
            Height);
        NativeTextureHandle = NativeTexture?.TextureHandle ?? nativeTextureHandle;
        Diagnostics = diagnostics is null ? [] : diagnostics.ToArray();
        IsSuccess = !Diagnostics.Any(static diagnostic => diagnostic.Severity == TerminalShaderDiagnosticSeverity.Error) &&
            (NativeTexture is not null || PixelData.Length > 0);
    }

    /// <summary>
    /// Gets whether frame rendering succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the backend kind.
    /// </summary>
    public TerminalShaderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets the optional final native texture handle.
    /// </summary>
    public nint NativeTextureHandle { get; }

    /// <summary>
    /// Gets the optional final native texture descriptor.
    /// </summary>
    public TerminalShaderNativeTexture? NativeTexture { get; }

    /// <summary>
    /// Gets optional final CPU pixel data.
    /// </summary>
    public ReadOnlyMemory<byte> PixelData { get; }

    /// <summary>
    /// Gets final frame width.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets final frame height.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets frame diagnostics.
    /// </summary>
    public IReadOnlyList<TerminalShaderDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Creates a failed frame result.
    /// </summary>
    /// <param name="backendKind">Backend kind.</param>
    /// <param name="diagnostics">Failure diagnostics.</param>
    /// <returns>A failed frame result.</returns>
    public static TerminalShaderFrameResult Failed(
        TerminalShaderBackendKind backendKind,
        IReadOnlyList<TerminalShaderDiagnostic> diagnostics)
    {
        return new TerminalShaderFrameResult(backendKind, diagnostics: diagnostics);
    }

    private static TerminalShaderNativeTexture? CreateLegacyNativeTexture(
        TerminalShaderBackendKind backendKind,
        nint nativeTextureHandle,
        int width,
        int height)
    {
        return nativeTextureHandle == 0 || width <= 0 || height <= 0
            ? null
            : new TerminalShaderNativeTexture(backendKind, nativeTextureHandle, width, height);
    }
}
