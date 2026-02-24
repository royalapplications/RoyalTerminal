// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Optional contract for VT processors that can receive runtime theme updates.
/// </summary>
public interface ITerminalThemeSink
{
    /// <summary>
    /// Applies a new terminal theme.
    /// </summary>
    void ApplyTheme(TerminalTheme theme);
}
