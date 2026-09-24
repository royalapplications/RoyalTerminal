// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Native-compatible logical page budget for incremental snapshot history admission.
/// This is not a CLR heap limit. Resident viewport pages are never rejected.
/// The separate host scrollback row cap and hard decode limits still apply.
/// </summary>
public sealed record GhosttySnapshotScrollbackQuota
{
    /// <summary>Maximum logical page bytes; null is unlimited. Ghostty's viewport minimum applies.</summary>
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
