// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Optional live transport/PTY password-entry detection. This heuristic is not a
/// security boundary and does not itself enable operating-system secure input.
/// </summary>
public interface ITerminalPasswordInputSource
{
    /// <summary>Whether this source supports detecting its terminal's input mode.</summary>
    bool SupportsPasswordInputDetection { get; }

    /// <summary>
    /// Reads canonical-without-echo state. Returns false when unavailable, setting
    /// <paramref name="passwordInput"/> to false. Must be safe during concurrent
    /// IO and disposal; callers must not hold a processor lock while querying.
    /// </summary>
    bool TryGetPasswordInput(out bool passwordInput);
}
