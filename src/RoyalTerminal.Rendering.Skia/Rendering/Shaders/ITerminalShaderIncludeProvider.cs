// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package includes.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Loads virtual shader source files referenced by include directives.
/// </summary>
public interface ITerminalShaderIncludeProvider
{
    /// <summary>
    /// Attempts to load an include file.
    /// </summary>
    /// <param name="includePath">Normalized virtual include path.</param>
    /// <param name="includingFile">Virtual path of the including file, if available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded shader file, or <c>null</c> when the file is unavailable.</returns>
    ValueTask<TerminalShaderFile?> TryLoadAsync(
        string includePath,
        string? includingFile,
        CancellationToken cancellationToken = default);
}
