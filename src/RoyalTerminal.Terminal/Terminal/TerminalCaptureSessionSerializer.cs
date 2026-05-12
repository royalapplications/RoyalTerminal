// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Capture session persistence helpers.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Serialization helpers for <see cref="TerminalCaptureSession"/>.
/// </summary>
public static class TerminalCaptureSessionSerializer
{
    /// <summary>
    /// Saves a capture session to a stream in JSON format.
    /// </summary>
    public static ValueTask SaveAsync(
        TerminalCaptureSession session,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return TerminalCaptureSessionFormats.RoyalTerminalJson
            .SaveAsync(session, stream, cancellationToken);
    }

    /// <summary>
    /// Saves a capture session to a stream using the supplied format.
    /// </summary>
    public static ValueTask SaveAsync(
        TerminalCaptureSession session,
        Stream stream,
        ITerminalCaptureSessionFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format.SaveAsync(session, stream, cancellationToken);
    }

    /// <summary>
    /// Loads a capture session from a JSON stream.
    /// </summary>
    public static async ValueTask<TerminalCaptureSession> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return await TerminalCaptureSessionFormats.RoyalTerminalJson
            .LoadAsync(stream, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a capture session from a stream using the supplied format.
    /// </summary>
    public static async ValueTask<TerminalCaptureSession> LoadAsync(
        Stream stream,
        ITerminalCaptureSessionFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);
        return await format.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves a capture session to a file in JSON format.
    /// </summary>
    public static async ValueTask SaveToFileAsync(
        TerminalCaptureSession session,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        string directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using FileStream stream = File.Create(filePath);
        await SaveAsync(session, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves a capture session to a file using the supplied format.
    /// </summary>
    public static async ValueTask SaveToFileAsync(
        TerminalCaptureSession session,
        string filePath,
        ITerminalCaptureSessionFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(format);
        cancellationToken.ThrowIfCancellationRequested();

        string directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using FileStream stream = File.Create(filePath);
        await SaveAsync(session, stream, format, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a capture session from a JSON file.
    /// </summary>
    public static async ValueTask<TerminalCaptureSession> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        await using FileStream stream = File.OpenRead(filePath);
        return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a capture session from a file using the supplied format.
    /// </summary>
    public static async ValueTask<TerminalCaptureSession> LoadFromFileAsync(
        string filePath,
        ITerminalCaptureSessionFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(format);
        cancellationToken.ThrowIfCancellationRequested();

        await using FileStream stream = File.OpenRead(filePath);
        return await LoadAsync(stream, format, cancellationToken).ConfigureAwait(false);
    }
}
