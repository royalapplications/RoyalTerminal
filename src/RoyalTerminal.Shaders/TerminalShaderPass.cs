// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package model.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Describes one pass in a full terminal shader package.
/// </summary>
public sealed class TerminalShaderPass
{
    /// <summary>
    /// Initializes a new shader package pass.
    /// </summary>
    /// <param name="name">Stable pass name.</param>
    /// <param name="stage">Pass stage.</param>
    /// <param name="sourcePath">Optional source file path.</param>
    /// <param name="entryPoint">Optional entry point name.</param>
    /// <param name="targetProfile">Optional compiler target profile.</param>
    /// <param name="dispatch">Optional compute dispatch descriptor.</param>
    /// <param name="inputs">Input resources consumed by this pass.</param>
    /// <param name="outputs">Output resources produced by this pass.</param>
    public TerminalShaderPass(
        string name,
        TerminalShaderStage stage,
        string? sourcePath = null,
        string? entryPoint = null,
        TerminalShaderTargetProfile? targetProfile = null,
        TerminalShaderDispatch? dispatch = null,
        IReadOnlyList<TerminalShaderPassInput>? inputs = null,
        IReadOnlyList<TerminalShaderPassOutput>? outputs = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Shader pass name must be non-empty.", nameof(name));
        }

        Name = name.Trim();
        Stage = stage;
        SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : TerminalShaderVirtualPath.Normalize(sourcePath);
        EntryPoint = string.IsNullOrWhiteSpace(entryPoint) ? null : entryPoint.Trim();
        TargetProfile = targetProfile;
        Dispatch = dispatch;
        Inputs = inputs is null ? [] : inputs.ToArray();
        Outputs = outputs is null ? [] : outputs.ToArray();
    }

    /// <summary>
    /// Gets the stable pass name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the pass stage.
    /// </summary>
    public TerminalShaderStage Stage { get; }

    /// <summary>
    /// Gets the optional source file path.
    /// </summary>
    public string? SourcePath { get; }

    /// <summary>
    /// Gets the optional entry point name.
    /// </summary>
    public string? EntryPoint { get; }

    /// <summary>
    /// Gets the optional compiler target profile.
    /// </summary>
    public TerminalShaderTargetProfile? TargetProfile { get; }

    /// <summary>
    /// Gets the optional compute dispatch descriptor.
    /// </summary>
    public TerminalShaderDispatch? Dispatch { get; }

    /// <summary>
    /// Gets input resources consumed by this pass.
    /// </summary>
    public IReadOnlyList<TerminalShaderPassInput> Inputs { get; }

    /// <summary>
    /// Gets output resources produced by this pass.
    /// </summary>
    public IReadOnlyList<TerminalShaderPassOutput> Outputs { get; }
}
