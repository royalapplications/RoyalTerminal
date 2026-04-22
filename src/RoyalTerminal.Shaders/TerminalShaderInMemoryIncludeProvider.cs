// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package includes.

namespace RoyalTerminal.Shaders;

/// <summary>
/// In-memory include provider for tests, embedded shader libraries, and generated packages.
/// </summary>
public sealed class TerminalShaderInMemoryIncludeProvider : ITerminalShaderIncludeProvider
{
    private readonly IReadOnlyDictionary<string, TerminalShaderFile> _files;

    /// <summary>
    /// Initializes a new in-memory include provider.
    /// </summary>
    /// <param name="files">Files keyed by virtual path.</param>
    public TerminalShaderInMemoryIncludeProvider(IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        Dictionary<string, TerminalShaderFile> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> file in files)
        {
            TerminalShaderFile shaderFile = new(file.Key, file.Value);
            normalized[shaderFile.VirtualPath] = shaderFile;
        }

        _files = normalized;
    }

    /// <inheritdoc />
    public ValueTask<TerminalShaderFile?> TryLoadAsync(
        string includePath,
        string? includingFile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedPath = TerminalShaderVirtualPath.Normalize(includePath);
        _files.TryGetValue(normalizedPath, out TerminalShaderFile? file);
        return ValueTask.FromResult(file);
    }
}
