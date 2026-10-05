// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Services;

/// <summary>Optional lifecycle hook for a surface-owned input adapter and its decorators.</summary>
public interface IResettableTerminalInputAdapter
{
    /// <summary>
    /// Forgets held keys and platform composition/modifier tracking when focus or
    /// session ownership is lost. Does not synthesize terminal input.
    /// </summary>
    void ResetInputState();
}
