// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    internal ManagedTerminalColors SnapshotColors => _colors;

    // Called only during unpublished snapshot assembly, before continuation replay.
    internal void InstallSnapshotColors(GhosttySnapshotTerminalState state, TerminalTheme hostTheme)
    {
        _colors.InstallSnapshot(state, hostTheme);
        ApplyEffectiveTheme(_colors.GetEffectiveTheme());
    }
}
