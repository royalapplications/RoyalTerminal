// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Theming;

/// <summary>
/// Output precision for OSC color query responses.
/// </summary>
public enum TerminalOscColorReportFormat
{
    /// <summary>
    /// Reports colors in 16-bit hex component format.
    /// Example: rgb:FFFF/0000/7F7F
    /// </summary>
    Bit16 = 0,

    /// <summary>
    /// Reports colors in 8-bit hex component format.
    /// Example: rgb:FF/00/7F
    /// </summary>
    Bit8 = 1,
}
