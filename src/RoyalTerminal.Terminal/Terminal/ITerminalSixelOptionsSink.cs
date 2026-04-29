// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Sixel configuration sink.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Optional sink for processors that can enable or disable sixel image decoding.
/// </summary>
public interface ITerminalSixelOptionsSink
{
    /// <summary>Gets or sets whether sixel image decoding is enabled.</summary>
    bool SixelGraphicsEnabled { get; set; }
}
