// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Optional VT processor resize reflow policy contract.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Optional VT processor capability for controlling local resize reflow.
/// </summary>
public interface ITerminalResizeReflowPolicySink
{
    /// <summary>
    /// Gets or sets whether the VT processor should reflow its local primary-screen
    /// buffer during terminal resize.
    /// </summary>
    bool LocalReflowOnResize { get; set; }
}
