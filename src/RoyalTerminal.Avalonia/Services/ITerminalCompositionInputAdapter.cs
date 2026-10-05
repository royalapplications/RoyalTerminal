// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Services;

/// <summary>Optional IME composition state for a terminal input adapter.</summary>
public interface ITerminalCompositionInputAdapter
{
    /// <summary>Updates whether raw key events belong to an active IME composition.</summary>
    void SetComposing(bool composing);
}
