// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>The original terminal color representation, before palette/theme resolution.</summary>
public enum TerminalColorKind : byte
{
    /// <summary>The terminal's default color.</summary>
    Default,
    /// <summary>An index in the terminal's 256-color palette.</summary>
    Palette,
    /// <summary>An explicit 24-bit RGB color.</summary>
    Rgb,
}

/// <summary>
/// Compact logical color identity, distinct from the resolved ARGB used for painting.
/// The default value represents the terminal default, not RGB black or palette zero.
/// </summary>
public readonly record struct TerminalColorIdentity
{
    private readonly uint _packed;

    private TerminalColorIdentity(uint packed) => _packed = packed;

    /// <summary>The color's original representation.</summary>
    public TerminalColorKind Kind => (TerminalColorKind)(_packed >> 24);

    /// <summary>The palette index or 24-bit RGB payload; zero for the default color.</summary>
    public uint Value => _packed & 0xFFFFFF;

    /// <summary>Creates a palette color without resolving its displayed RGB value.</summary>
    public static TerminalColorIdentity Palette(byte index) => new(0x01000000u | index);

    /// <summary>Creates an RGB color, ignoring any supplied alpha byte.</summary>
    public static TerminalColorIdentity Rgb(uint rgb) => new(0x02000000u | (rgb & 0xFFFFFF));
}
