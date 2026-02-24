// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Kitty keyboard protocol state source.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Optional source for kitty keyboard protocol mode flags.
/// </summary>
public interface IKittyKeyboardStateSource
{
    /// <summary>
    /// Gets the active kitty keyboard mode flags for the current screen.
    /// </summary>
    int KittyKeyboardFlags { get; }
}
