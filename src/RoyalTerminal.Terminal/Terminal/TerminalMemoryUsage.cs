// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Storage unit used by a terminal's memory report.</summary>
public enum TerminalMemoryStorageKind
{
    /// <summary>Native Ghostty virtual-memory pages, including allocator pools.</summary>
    NativePages,
    /// <summary>Managed row cell arrays and referenced UTF-16 text payloads.</summary>
    ManagedRows,
}

/// <summary>Storage estimates for one terminal screen, excluding process/renderer overhead.</summary>
/// <param name="Units">Allocated native pages or logical managed rows.</param>
/// <param name="LogicalBytes">Uncompressed capacity, including reserved native address space.</param>
/// <param name="ResidentBytes">Estimated resident payload, including compressed bytes.</param>
/// <param name="CompressedUnits">Pages or rows currently compressed.</param>
/// <param name="CompressedBytes">Compressed payload, already included in ResidentBytes.</param>
/// <param name="ImageBytes">Stored Kitty image payload, separate from row/page bytes.</param>
public readonly record struct TerminalScreenMemoryUsage(ulong Units, ulong LogicalBytes,
    ulong ResidentBytes, ulong CompressedUnits, ulong CompressedBytes, ulong ImageBytes);

/// <summary>
/// Backend storage estimates, not process working-set or CLR heap measurements.
/// Managed reports exclude object headers, snapshot allocation bookkeeping, dictionaries,
/// retained external COW snapshots and renderer/Sixel caches; referenced grapheme payload
/// is counted per cell. Native reports use libghostty-vt's page/image accounting.
/// </summary>
/// <param name="StorageKind">The backend's allocation unit and accounting model.</param>
/// <param name="CompressionSupported">Whether this backend can compress history.</param>
/// <param name="Primary">Primary screen storage.</param>
/// <param name="Alternate">Alternate screen storage, or zeros if unallocated.</param>
public readonly record struct TerminalMemoryUsage(TerminalMemoryStorageKind StorageKind,
    bool CompressionSupported, TerminalScreenMemoryUsage Primary, TerminalScreenMemoryUsage Alternate);

/// <summary>Optional backend memory diagnostics.</summary>
public interface ITerminalMemoryUsageSource
{
    /// <summary>
    /// Scans allocated storage without decompressing it. Serialize with processor
    /// access; intended for diagnostics rather than each render or input write.
    /// </summary>
    TerminalMemoryUsage GetMemoryUsage();
}
