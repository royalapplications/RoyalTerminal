// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Regex-based terminal text highlighting rule.
/// </summary>
public sealed record TerminalTextHighlightRule
{
    /// <summary>
    /// Human-readable rule name.
    /// </summary>
    public string Name { get; init; } = "Highlight Rule";

    /// <summary>
    /// Regular expression pattern matched against each rendered terminal row.
    /// </summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether this rule participates in rendering.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Optional foreground color as packed ARGB.
    /// When unset, matching cells keep their original foreground.
    /// </summary>
    public uint? Foreground { get; init; }

    /// <summary>
    /// Optional background color as packed ARGB.
    /// When unset, matching cells keep their original background.
    /// </summary>
    public uint? Background { get; init; }

    /// <summary>
    /// Optional dark-theme foreground color as packed ARGB.
    /// When unset, <see cref="Foreground"/> is used for dark themes.
    /// </summary>
    public uint? DarkForeground { get; init; }

    /// <summary>
    /// Optional dark-theme background color as packed ARGB.
    /// When unset, <see cref="Background"/> is used for dark themes.
    /// </summary>
    public uint? DarkBackground { get; init; }
}
