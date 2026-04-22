// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader compiler cache.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Stable cache key for a full shader compilation request.
/// </summary>
public readonly record struct TerminalShaderCompilationCacheKey
{
    /// <summary>
    /// Initializes a new compilation cache key.
    /// </summary>
    /// <param name="value">Stable key value.</param>
    public TerminalShaderCompilationCacheKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Compilation cache key must be non-empty.", nameof(value));
        }

        Value = value.Trim();
    }

    /// <summary>
    /// Gets the stable key value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a stable cache key from a compilation request.
    /// </summary>
    /// <param name="request">Compilation request.</param>
    /// <param name="cacheNamespace">Optional compiler-specific namespace.</param>
    /// <returns>The cache key.</returns>
    public static TerminalShaderCompilationCacheKey Create(
        TerminalShaderCompilationRequest request,
        string? cacheNamespace = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        StringBuilder builder = new();
        builder.Append("namespace=").Append(cacheNamespace ?? string.Empty).Append('\n');
        builder.Append("package=").Append(request.Package.Name).Append('\n');
        builder.Append("backend=").Append(request.Options.BackendKind).Append('\n');
        builder.Append("compiler=").Append(request.Options.CompilerKind).Append('\n');
        builder.Append("debug=").Append(request.Options.DebugName ?? string.Empty).Append('\n');

        foreach (KeyValuePair<string, string> define in request.Options.Defines.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append("define:")
                .Append(define.Key)
                .Append('=')
                .Append(define.Value)
                .Append('\n');
        }

        foreach (TerminalShaderFile file in request.ResolvedFiles.OrderBy(static file => file.VirtualPath, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("file:")
                .Append(file.VirtualPath)
                .Append(':')
                .Append(ComputeContentHash(file.Source))
                .Append('\n');
        }

        for (int i = 0; i < request.Package.Resources.Count; i++)
        {
            TerminalShaderResourceBinding resource = request.Package.Resources[i];
            builder.Append("resource:")
                .Append(resource.Name)
                .Append(':')
                .Append(resource.Kind)
                .Append(':')
                .Append(resource.Source)
                .Append(':')
                .Append(resource.ValueType)
                .Append(':')
                .Append(resource.RegisterSpace)
                .Append(':')
                .Append(resource.RegisterIndex)
                .Append(':')
                .Append(resource.Optional)
                .Append('\n');
        }

        for (int i = 0; i < request.Package.Passes.Count; i++)
        {
            TerminalShaderPass pass = request.Package.Passes[i];
            builder.Append("pass:")
                .Append(pass.Name)
                .Append(':')
                .Append(pass.Stage)
                .Append(':')
                .Append(pass.SourcePath ?? string.Empty)
                .Append(':')
                .Append(pass.EntryPoint ?? string.Empty)
                .Append(':')
                .Append(pass.TargetProfile?.Name ?? string.Empty)
                .Append(':')
                .Append(pass.Dispatch?.Kind.ToString() ?? string.Empty)
                .Append(':')
                .Append(pass.Dispatch?.X.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(':')
                .Append(pass.Dispatch?.Y.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(':')
                .Append(pass.Dispatch?.Z.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
                .Append('\n');

            AppendPassInputs(builder, pass);
            AppendPassOutputs(builder, pass);
        }

        string digest = ComputeContentHash(builder.ToString());
        return new TerminalShaderCompilationCacheKey(digest);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    private static void AppendPassInputs(StringBuilder builder, TerminalShaderPass pass)
    {
        for (int i = 0; i < pass.Inputs.Count; i++)
        {
            TerminalShaderPassInput input = pass.Inputs[i];
            builder.Append("input:")
                .Append(pass.Name)
                .Append(':')
                .Append(input.ResourceName)
                .Append(':')
                .Append(input.BindingName)
                .Append('\n');
        }
    }

    private static void AppendPassOutputs(StringBuilder builder, TerminalShaderPass pass)
    {
        for (int i = 0; i < pass.Outputs.Count; i++)
        {
            TerminalShaderPassOutput output = pass.Outputs[i];
            builder.Append("output:")
                .Append(pass.Name)
                .Append(':')
                .Append(output.Name)
                .Append(':')
                .Append(output.Kind)
                .Append(':')
                .Append(output.WidthScale.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(output.HeightScale.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }
    }

    private static string ComputeContentHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}
