// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Native-compatible logical page budget for live scrollback and incremental history.
/// This is not a CLR heap limit. Eviction removes complete historical pages;
/// a page overlapping the active viewport is never split just to fit a quota.
/// Unlike a terminal-level no-scrollback command, zero bytes preserves resident
/// READY overlap instead of erasing partial history from its boundary page.
/// The separate host scrollback row cap and hard decode limits still apply.
/// </summary>
public sealed record GhosttySnapshotScrollbackQuota
{
    /// <summary>Maximum logical page bytes; null is unlimited, zero also disables scrolling. Ghostty's viewport minimum applies.</summary>
    public ulong? MaximumBytes { get; init; }

    /// <summary>Maximum history rows; null is unlimited. At least one standard page is allowed.</summary>
    public ulong? MaximumRows { get; init; }

    /// <summary>Target Ghostty page alignment: 16384 for macOS arm64, 4096 for other desktop targets.</summary>
    public int PageAlignment { get; init; } = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096;

    internal void Validate()
    {
        if (PageAlignment is not (4096 or 16384)) throw new ArgumentOutOfRangeException(nameof(PageAlignment));
    }
}
