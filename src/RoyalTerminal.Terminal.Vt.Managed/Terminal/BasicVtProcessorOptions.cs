// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Managed VT processor options.

using RoyalTerminal.Sixel;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Options for the managed VT processor.
/// </summary>
public sealed record BasicVtProcessorOptions
{
    /// <summary>Default managed VT options.</summary>
    public static BasicVtProcessorOptions Default { get; } = new();

    /// <summary>Gets whether managed sixel image decoding is enabled.</summary>
    public bool SixelGraphicsEnabled { get; init; }

    /// <summary>
    /// Gets whether ED 2 (<c>CSI 2 J</c>) scrolls the active viewport into
    /// history before clearing it.
    /// </summary>
    public bool ScrollOnEraseInDisplay { get; init; }

    /// <summary>
    /// Gets whether CSI 21 t may report the current window title. This is disabled by
    /// default because a title report can expose host-controlled text to the child.
    /// </summary>
    public bool TitleReportEnabled { get; init; }

    /// <summary>
    /// Gets the terminal name returned for XTGETTCAP <c>TN</c> queries. Null or empty
    /// suppresses only that capability; the static Ghostty capability map remains available.
    /// </summary>
    public string? TerminfoName { get; init; } = "xterm-ghostty";

    /// <summary>Gets resource limits and compatibility settings for sixel decoding.</summary>
    public SixelDecoderOptions SixelDecoderOptions { get; init; } = SixelDecoderOptions.Default;
}
