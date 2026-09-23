// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal;

/// <summary>Host policy for restoring Ghostty binary snapshots into the managed engine.</summary>
public sealed record ManagedTerminalSnapshotOptions
{
    /// <summary>Host colors and presentation preferences used when the snapshot leaves a color unset.</summary>
    public TerminalTheme Theme { get; init; } = TerminalTheme.Dark;

    /// <summary>
    /// Maximum primary history rows accepted from incremental pages. Resident READY overlap is
    /// preserved. Alternate history is not retained. Unlike native page-byte quotas this is the
    /// managed host's row contract; a page that does not fit is dropped whole, with all older pages.
    /// Live changes to the screen's scrollback limit affect subsequent pages.
    /// </summary>
    public int ScrollbackLimit { get; init; } = 10_000;

    /// <summary>
    /// Host parser/graphics policy, not snapshot state. Continuation retention is enlarged to
    /// hold the restored fragment when enabled; zero disables subsequent retention, not replay.
    /// </summary>
    public BasicVtProcessorOptions ProcessorOptions { get; init; } = BasicVtProcessorOptions.Default;

    /// <summary>Hard decoding bounds also applied to pages discarded due to live changes or quotas.</summary>
    public GhosttySnapshotDecodeLimits DecodeLimits { get; init; } = new();

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Theme);
        ArgumentNullException.ThrowIfNull(ProcessorOptions);
        ArgumentNullException.ThrowIfNull(DecodeLimits);
        ArgumentOutOfRangeException.ThrowIfNegative(ScrollbackLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(ProcessorOptions.ContinuationMaxBytes);
        DecodeLimits.Validate();
    }
}
