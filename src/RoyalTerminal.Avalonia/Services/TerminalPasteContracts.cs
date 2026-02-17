// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Paste safety and framing contracts.

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Clipboard paste safety policy applied before terminal input is sent.
/// </summary>
public enum TerminalPasteSafetyPolicy
{
    /// <summary>
    /// Paste clipboard text as-is.
    /// </summary>
    None = 0,

    /// <summary>
    /// Require an explicit decision callback when paste text is considered unsafe.
    /// </summary>
    ConfirmUnsafe = 1,

    /// <summary>
    /// Remove unsafe control characters before pasting.
    /// </summary>
    SanitizeControlSequences = 2,

    /// <summary>
    /// Block paste when unsafe content is detected.
    /// </summary>
    BlockUnsafe = 3,
}

/// <summary>
/// Risk flags detected in clipboard paste content.
/// </summary>
[Flags]
public enum TerminalPasteRisk
{
    /// <summary>No risk flags were detected.</summary>
    None = 0,

    /// <summary>Text contains newline-delimited content.</summary>
    Multiline = 1 << 0,

    /// <summary>Text contains control sequence characters (ESC/C0/C1/DEL).</summary>
    ControlSequence = 1 << 1,
}

/// <summary>
/// Decision returned by unsafe paste confirmation handlers.
/// </summary>
public enum TerminalPasteSafetyDecision
{
    /// <summary>Allow paste without modifications.</summary>
    Allow = 0,

    /// <summary>Sanitize control characters before paste.</summary>
    Sanitize = 1,

    /// <summary>Cancel paste operation.</summary>
    Cancel = 2,
}

/// <summary>
/// Context supplied to unsafe paste confirmation handlers.
/// </summary>
/// <param name="Text">Clipboard text proposed for paste.</param>
/// <param name="Risk">Detected risk flags.</param>
public readonly record struct TerminalPasteContext(
    string Text,
    TerminalPasteRisk Risk);

/// <summary>
/// Async callback used to confirm/transform unsafe paste operations.
/// </summary>
public delegate ValueTask<TerminalPasteSafetyDecision> TerminalUnsafePasteHandler(TerminalPasteContext context);

/// <summary>
/// Paste operation options used by terminal selection services.
/// </summary>
/// <param name="BracketedPasteEnabled">Whether to wrap paste in bracketed paste markers.</param>
/// <param name="SafetyPolicy">Safety policy applied before paste.</param>
/// <param name="UnsafePasteHandler">Optional callback for <see cref="TerminalPasteSafetyPolicy.ConfirmUnsafe"/>.</param>
public readonly record struct TerminalPasteRequest(
    bool BracketedPasteEnabled,
    TerminalPasteSafetyPolicy SafetyPolicy,
    TerminalUnsafePasteHandler? UnsafePasteHandler = null);
