// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Pluggable capture session format contracts.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Describes a persisted terminal capture file format.
/// </summary>
public sealed record TerminalCaptureFileFormatDescriptor
{
    /// <summary>
    /// Initializes a capture file format descriptor.
    /// </summary>
    public TerminalCaptureFileFormatDescriptor(
        string id,
        string displayName,
        string defaultExtension,
        IReadOnlyList<string> fileExtensions,
        IReadOnlyList<string> mimeTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultExtension);
        ArgumentNullException.ThrowIfNull(fileExtensions);
        ArgumentNullException.ThrowIfNull(mimeTypes);

        Id = id.Trim();
        DisplayName = displayName.Trim();
        DefaultExtension = NormalizeExtension(defaultExtension);
        FileExtensions = NormalizeExtensions(fileExtensions, DefaultExtension);
        MimeTypes = CopyNonEmptyValues(mimeTypes);
    }

    /// <summary>Stable format identifier.</summary>
    public string Id { get; }

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; }

    /// <summary>Default file extension, including the leading dot.</summary>
    public string DefaultExtension { get; }

    /// <summary>Supported file extensions, including leading dots.</summary>
    public IReadOnlyList<string> FileExtensions { get; }

    /// <summary>Supported MIME types.</summary>
    public IReadOnlyList<string> MimeTypes { get; }

    /// <summary>
    /// Returns whether the descriptor matches the supplied file name.
    /// </summary>
    public bool MatchesFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        IReadOnlyList<string> extensions = FileExtensions;
        for (int i = 0; i < extensions.Count; i++)
        {
            if (fileName.EndsWith(extensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeExtension(string extension)
    {
        string trimmed = extension.Trim();
        return trimmed.StartsWith(".", StringComparison.Ordinal)
            ? trimmed
            : $".{trimmed}";
    }

    private static IReadOnlyList<string> NormalizeExtensions(
        IReadOnlyList<string> extensions,
        string defaultExtension)
    {
        List<string> result = new(extensions.Count + 1);
        AddUniqueExtension(result, defaultExtension);
        for (int i = 0; i < extensions.Count; i++)
        {
            string extension = extensions[i];
            if (string.IsNullOrWhiteSpace(extension))
            {
                continue;
            }

            AddUniqueExtension(result, NormalizeExtension(extension));
        }

        return result.ToArray();
    }

    private static void AddUniqueExtension(List<string> target, string extension)
    {
        for (int i = 0; i < target.Count; i++)
        {
            if (string.Equals(target[i], extension, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        target.Add(extension);
    }

    private static IReadOnlyList<string> CopyNonEmptyValues(IReadOnlyList<string> values)
    {
        List<string> result = new(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            string value = values[i];
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value.Trim());
            }
        }

        return result.ToArray();
    }
}

/// <summary>
/// Reads and writes terminal capture sessions using a concrete file format.
/// </summary>
public interface ITerminalCaptureSessionFormat
{
    /// <summary>Format descriptor used by hosts and file pickers.</summary>
    TerminalCaptureFileFormatDescriptor Descriptor { get; }

    /// <summary>
    /// Saves a capture session to the supplied stream.
    /// </summary>
    ValueTask SaveAsync(
        TerminalCaptureSession session,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a capture session from the supplied stream.
    /// </summary>
    ValueTask<TerminalCaptureSession> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default);
}
