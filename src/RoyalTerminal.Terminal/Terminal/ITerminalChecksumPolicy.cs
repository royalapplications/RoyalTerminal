// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>XTCHECKSUM variants, matching xterm and Ghostty's five-bit registry.</summary>
[Flags]
public enum TerminalChecksumFlags : byte
{
    /// <summary>Negated DEC checksum with attributes and blank trimming.</summary>
    None = 0,
    /// <summary>Return the sum without negating it.</summary>
    Positive = 1,
    /// <summary>Omit VT100 video and protection attributes.</summary>
    NoAttributes = 2,
    /// <summary>Include all spaces and DEC-mode combining codepoints.</summary>
    NoTrim = 4,
    /// <summary>Include unwritten cells as spaces.</summary>
    Undrawn = 8,
    /// <summary>Use full codepoints, skipping wide tails and combining marks.</summary>
    Full = 16,
}

/// <summary>Host policy for DECRQCRA rectangular checksums.</summary>
public interface ITerminalChecksumPolicy
{
    /// <summary>Allows screen checksum replies and XTCHECKSUM changes. Disabled by default;
    /// enabling permits programs to read screen contents through single-cell queries.</summary>
    bool ChecksumReportsEnabled { get; set; }

    /// <summary>Default checksum flags, applied immediately and restored by RIS/DECSTR.
    /// Unknown bits are rejected. Policy survives session resets.</summary>
    TerminalChecksumFlags DefaultChecksumFlags { get; set; }
}
