// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Built-in capture session formats.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Built-in terminal capture session file formats.
/// </summary>
public static class TerminalCaptureSessionFormats
{
    /// <summary>Stable identifier for the native RoyalTerminal JSON format.</summary>
    public const string RoyalTerminalJsonId = "royalterminal-json";

    /// <summary>Stable identifier for the asciicast v3 format.</summary>
    public const string AsciicastV3Id = "asciicast-v3";

    /// <summary>Native RoyalTerminal JSON format.</summary>
    public static ITerminalCaptureSessionFormat RoyalTerminalJson { get; } =
        new RoyalTerminalCaptureSessionFormat();

    /// <summary>Asciinema-compatible asciicast v3 format.</summary>
    public static ITerminalCaptureSessionFormat AsciicastV3 { get; } =
        new AsciicastV3CaptureSessionFormat();

    /// <summary>Built-in capture formats in default probing order.</summary>
    public static IReadOnlyList<ITerminalCaptureSessionFormat> BuiltIn { get; } =
    [
        RoyalTerminalJson,
        AsciicastV3,
    ];

    /// <summary>Default registry containing all built-in capture formats.</summary>
    public static TerminalCaptureSessionFormatRegistry DefaultRegistry { get; } =
        new(BuiltIn);
}
