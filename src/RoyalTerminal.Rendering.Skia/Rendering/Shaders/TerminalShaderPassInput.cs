// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Describes one input resource consumed by a shader package pass.
/// </summary>
public sealed class TerminalShaderPassInput
{
    /// <summary>
    /// Initializes a new pass input.
    /// </summary>
    /// <param name="resourceName">Name of a package resource or earlier pass output.</param>
    /// <param name="bindingName">Optional shader binding name. Defaults to <paramref name="resourceName"/>.</param>
    public TerminalShaderPassInput(string resourceName, string? bindingName = null)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Pass input resource name must be non-empty.", nameof(resourceName));
        }

        ResourceName = resourceName.Trim();
        BindingName = string.IsNullOrWhiteSpace(bindingName) ? ResourceName : bindingName.Trim();
    }

    /// <summary>
    /// Gets the name of a package resource or earlier pass output.
    /// </summary>
    public string ResourceName { get; }

    /// <summary>
    /// Gets the shader binding name.
    /// </summary>
    public string BindingName { get; }
}
