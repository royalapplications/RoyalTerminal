// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Host policy for bounded capture of unsupported terminal sequences.</summary>
public interface ITerminalUnknownSequencePolicy
{
    /// <summary>
    /// Gets or sets the maximum retained unknown-APC payload bytes, excluding
    /// introducer and terminator. Zero disables capture; negative values are
    /// rejected. The default is 4096 bytes. A capture keeps the limit selected
    /// when unknown capture begins, even if policy changes
    /// before termination. Known protocols use their independent limits.
    /// </summary>
    /// <remarks>
    /// Serialize policy updates with processor operations. The policy survives
    /// terminal resets and is not serialized into terminal snapshots. Empty APCs
    /// and unfinished prefixes of recognized protocols are not unknown commands.
    /// Captured payloads are reported through <see cref="ITerminalEffectSource.UnknownSequenceCallback"/>.
    /// </remarks>
    int UnknownSequenceMaxBytes { get; set; }
}
