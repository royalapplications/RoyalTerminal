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

    /// <summary>Gets resource limits and compatibility settings for sixel decoding.</summary>
    public SixelDecoderOptions SixelDecoderOptions { get; init; } = SixelDecoderOptions.Default;
}
