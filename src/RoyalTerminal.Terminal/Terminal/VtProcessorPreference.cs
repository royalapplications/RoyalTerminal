// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - VT processor preference.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Selects which VT processor implementation should be used.
/// </summary>
public enum VtProcessorPreference
{
    /// <summary>
    /// Automatically select the best available implementation.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Force the managed VT processor implementation.
    /// </summary>
    Managed = 1,

    /// <summary>
    /// Force a native VT processor implementation.
    /// </summary>
    Native = 2,
}
