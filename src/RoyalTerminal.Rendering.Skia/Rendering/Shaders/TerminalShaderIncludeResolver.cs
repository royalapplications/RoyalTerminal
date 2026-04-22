// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package includes.

using System.Text.RegularExpressions;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Resolves include directives for full terminal shader packages.
/// </summary>
public static partial class TerminalShaderIncludeResolver
{
    /// <summary>
    /// Resolves package source files and reachable include files.
    /// </summary>
    /// <param name="package">Shader package.</param>
    /// <param name="includeProvider">Optional include provider for files not already embedded in the package.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The include resolution result.</returns>
    public static async ValueTask<TerminalShaderIncludeResolutionResult> ResolveAsync(
        TerminalShaderPackage package,
        ITerminalShaderIncludeProvider? includeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        Dictionary<string, TerminalShaderFile> files = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < package.Files.Count; i++)
        {
            files[package.Files[i].VirtualPath] = package.Files[i];
        }

        List<TerminalShaderDiagnostic> diagnostics = [];
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> stack = new();

        for (int i = 0; i < package.Files.Count; i++)
        {
            await VisitAsync(
                package.Files[i],
                package,
                includeProvider,
                files,
                visited,
                stack,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
        }

        return new TerminalShaderIncludeResolutionResult(
            files.Values.OrderBy(static file => file.VirtualPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics);
    }

    private static async ValueTask VisitAsync(
        TerminalShaderFile file,
        TerminalShaderPackage package,
        ITerminalShaderIncludeProvider? includeProvider,
        Dictionary<string, TerminalShaderFile> files,
        HashSet<string> visited,
        Stack<string> stack,
        List<TerminalShaderDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (visited.Contains(file.VirtualPath))
        {
            return;
        }

        if (stack.Contains(file.VirtualPath, StringComparer.OrdinalIgnoreCase))
        {
            AddError(
                diagnostics,
                "RTSHADER120",
                $"Shader include cycle detected at '{file.VirtualPath}'.",
                file.VirtualPath);
            return;
        }

        stack.Push(file.VirtualPath);
        foreach (IncludeDirective include in ParseIncludes(file))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string resolvedPath = include.IsSystemInclude
                ? TerminalShaderVirtualPath.Normalize(include.Path)
                : TerminalShaderVirtualPath.ResolveInclude(file.VirtualPath, include.Path);

            if (TerminalShaderVirtualPath.HasTraversal(resolvedPath))
            {
                AddError(
                    diagnostics,
                    "RTSHADER121",
                    $"Shader include '{include.Path}' resolves outside the package root.",
                    file.VirtualPath,
                    include.Line);
                continue;
            }

            if (files.TryGetValue(resolvedPath, out TerminalShaderFile? includedFile))
            {
                await VisitAsync(
                    includedFile,
                    package,
                    includeProvider,
                    files,
                    visited,
                    stack,
                    diagnostics,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!package.Options.AllowExternalIncludes)
            {
                AddError(
                    diagnostics,
                    "RTSHADER122",
                    $"Shader include '{include.Path}' is not embedded in the package and external includes are disabled.",
                    file.VirtualPath,
                    include.Line);
                continue;
            }

            if (includeProvider is null)
            {
                AddError(
                    diagnostics,
                    "RTSHADER123",
                    $"Shader include '{include.Path}' requires an include provider.",
                    file.VirtualPath,
                    include.Line);
                continue;
            }

            TerminalShaderFile? loadedFile = await includeProvider
                .TryLoadAsync(resolvedPath, file.VirtualPath, cancellationToken)
                .ConfigureAwait(false);
            if (loadedFile is null)
            {
                AddError(
                    diagnostics,
                    "RTSHADER124",
                    $"Shader include '{include.Path}' could not be resolved.",
                    file.VirtualPath,
                    include.Line);
                continue;
            }

            if (TerminalShaderVirtualPath.HasTraversal(loadedFile.VirtualPath))
            {
                AddError(
                    diagnostics,
                    "RTSHADER125",
                    $"Shader include provider returned unsafe path '{loadedFile.VirtualPath}'.",
                    file.VirtualPath,
                    include.Line);
                continue;
            }

            if (!string.Equals(loadedFile.VirtualPath, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                AddError(
                    diagnostics,
                    "RTSHADER126",
                    $"Shader include provider returned '{loadedFile.VirtualPath}' for requested include '{resolvedPath}'.",
                    file.VirtualPath,
                    include.Line);
                continue;
            }

            files[loadedFile.VirtualPath] = loadedFile;
            await VisitAsync(
                loadedFile,
                package,
                includeProvider,
                files,
                visited,
                stack,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
        }

        stack.Pop();
        visited.Add(file.VirtualPath);
    }

    private static IEnumerable<IncludeDirective> ParseIncludes(TerminalShaderFile file)
    {
        string[] lines = file.Source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match match = IncludeRegex().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            Group quoted = match.Groups["quoted"];
            if (quoted.Success)
            {
                yield return new IncludeDirective(quoted.Value, IsSystemInclude: false, i + 1);
                continue;
            }

            Group system = match.Groups["system"];
            if (system.Success)
            {
                yield return new IncludeDirective(system.Value, IsSystemInclude: true, i + 1);
            }
        }
    }

    private static void AddError(
        List<TerminalShaderDiagnostic> diagnostics,
        string code,
        string message,
        string filePath,
        int? line = null)
    {
        diagnostics.Add(new TerminalShaderDiagnostic(
            TerminalShaderDiagnosticSeverity.Error,
            code,
            message,
            filePath,
            line));
    }

    [GeneratedRegex(@"^\s*#\s*include\s+(?:""(?<quoted>[^""]+)""|<(?<system>[^>]+)>)")]
    private static partial Regex IncludeRegex();

    private readonly record struct IncludeDirective(string Path, bool IsSystemInclude, int Line);
}
