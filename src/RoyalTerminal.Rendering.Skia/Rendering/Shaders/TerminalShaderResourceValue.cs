// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader runtime model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Represents one runtime resource supplied to a full shader package frame.
/// </summary>
public sealed class TerminalShaderResourceValue
{
    /// <summary>
    /// Initializes a new runtime shader resource value.
    /// </summary>
    /// <param name="name">Resource name.</param>
    /// <param name="kind">Resource kind.</param>
    /// <param name="nativeHandle">Optional native backend handle.</param>
    /// <param name="data">Optional CPU data.</param>
    /// <param name="width">Optional resource width.</param>
    /// <param name="height">Optional resource height.</param>
    public TerminalShaderResourceValue(
        string name,
        TerminalShaderResourceKind kind,
        nint nativeHandle = 0,
        ReadOnlyMemory<byte> data = default,
        int width = 0,
        int height = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Runtime shader resource name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
        Kind = kind;
        NativeHandle = nativeHandle;
        Data = data.ToArray();
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
    }

    /// <summary>
    /// Gets the resource name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the resource kind.
    /// </summary>
    public TerminalShaderResourceKind Kind { get; }

    /// <summary>
    /// Gets the optional native backend handle.
    /// </summary>
    public nint NativeHandle { get; }

    /// <summary>
    /// Gets optional CPU data.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Gets the optional resource width.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the optional resource height.
    /// </summary>
    public int Height { get; }
}
