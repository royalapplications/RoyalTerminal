// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Capture session format registry.

using System.Text.Json;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Registry for pluggable terminal capture session file formats.
/// </summary>
public sealed class TerminalCaptureSessionFormatRegistry
{
    private readonly ITerminalCaptureSessionFormat[] _formats;

    /// <summary>
    /// Initializes a format registry from the supplied formats.
    /// </summary>
    public TerminalCaptureSessionFormatRegistry(IEnumerable<ITerminalCaptureSessionFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        List<ITerminalCaptureSessionFormat> collectedFormats = [];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (ITerminalCaptureSessionFormat format in formats)
        {
            ArgumentNullException.ThrowIfNull(format);
            string id = format.Descriptor.Id;
            if (!ids.Add(id))
            {
                throw new ArgumentException($"Duplicate capture format id '{id}'.", nameof(formats));
            }

            collectedFormats.Add(format);
        }

        if (collectedFormats.Count == 0)
        {
            throw new ArgumentException("At least one capture format is required.", nameof(formats));
        }

        _formats = collectedFormats.ToArray();
    }

    /// <summary>Registered formats in probing order.</summary>
    public IReadOnlyList<ITerminalCaptureSessionFormat> Formats => _formats;

    /// <summary>
    /// Finds a format by stable identifier.
    /// </summary>
    public ITerminalCaptureSessionFormat? FindById(string formatId)
    {
        if (string.IsNullOrWhiteSpace(formatId))
        {
            return null;
        }

        for (int i = 0; i < _formats.Length; i++)
        {
            ITerminalCaptureSessionFormat format = _formats[i];
            if (string.Equals(format.Descriptor.Id, formatId, StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first registered format matching the supplied file name.
    /// </summary>
    public ITerminalCaptureSessionFormat? FindByFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        for (int i = 0; i < _formats.Length; i++)
        {
            ITerminalCaptureSessionFormat format = _formats[i];
            if (format.Descriptor.MatchesFileName(fileName))
            {
                return format;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a format by identifier or throws if it is not registered.
    /// </summary>
    public ITerminalCaptureSessionFormat GetRequiredFormat(string formatId)
    {
        return FindById(formatId)
            ?? throw new InvalidOperationException($"Capture format '{formatId}' is not registered.");
    }

    /// <summary>
    /// Saves a capture session using the registered format identifier.
    /// </summary>
    public ValueTask SaveAsync(
        TerminalCaptureSession session,
        Stream stream,
        string formatId,
        CancellationToken cancellationToken = default)
    {
        ITerminalCaptureSessionFormat format = GetRequiredFormat(formatId);
        return format.SaveAsync(session, stream, cancellationToken);
    }

    /// <summary>
    /// Loads a capture session, preferring a format inferred from the file name when available.
    /// </summary>
    public async ValueTask<TerminalCaptureSession> LoadAsync(
        Stream stream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        if (!stream.CanSeek)
        {
            await using MemoryStream bufferedStream = new();
            await stream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
            bufferedStream.Position = 0;
            return await LoadAsync(bufferedStream, fileName, cancellationToken).ConfigureAwait(false);
        }

        long startPosition = stream.Position;
        ITerminalCaptureSessionFormat[] candidates = CreateLoadCandidates(fileName);
        InvalidDataException? lastException = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            stream.Position = startPosition;
            try
            {
                return await candidates[i].LoadAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                lastException = ex;
            }
            catch (JsonException ex)
            {
                lastException = new InvalidDataException("Capture file is malformed.", ex);
            }
        }

        stream.Position = startPosition;
        throw new InvalidDataException("Capture file does not match any registered recording format.", lastException);
    }

    private ITerminalCaptureSessionFormat[] CreateLoadCandidates(string? fileName)
    {
        ITerminalCaptureSessionFormat? preferred = string.IsNullOrWhiteSpace(fileName)
            ? null
            : FindByFileName(fileName);
        if (preferred is null)
        {
            return _formats;
        }

        ITerminalCaptureSessionFormat[] candidates = new ITerminalCaptureSessionFormat[_formats.Length];
        candidates[0] = preferred;
        int index = 1;
        for (int i = 0; i < _formats.Length; i++)
        {
            ITerminalCaptureSessionFormat format = _formats[i];
            if (!ReferenceEquals(format, preferred))
            {
                candidates[index] = format;
                index++;
            }
        }

        return candidates;
    }
}
