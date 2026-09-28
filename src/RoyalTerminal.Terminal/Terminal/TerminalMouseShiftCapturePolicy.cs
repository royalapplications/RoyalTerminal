// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Host policy for reporting Shift-modified mouse input to the terminal application.</summary>
public enum TerminalMouseShiftCapturePolicy
{
    /// <summary>Reserve Shift for selection unless the application requests capture.</summary>
    Disabled,
    /// <summary>Capture Shift unless the application requests selection.</summary>
    Enabled,
    /// <summary>Always reserve Shift for selection, ignoring application requests.</summary>
    Never,
    /// <summary>Always capture Shift, ignoring application requests.</summary>
    Always,
}

/// <summary>Application override set by XTSHIFTESCAPE, independent of host policy and DEC modes.</summary>
public interface ITerminalMouseShiftCaptureState
{
    /// <summary>Gets or sets the override; null restores the host default. Serialize with terminal mutation.</summary>
    bool? MouseShiftCaptureOverride { get; set; }
}

/// <summary>Resolves Ghostty-compatible host and application Shift-mouse policy.</summary>
public static class TerminalMouseCapturePolicy
{
    /// <summary>Returns whether Shift input is captured when mouse reporting is enabled.</summary>
    public static bool IsShiftCaptured(TerminalMouseShiftCapturePolicy policy, bool? applicationOverride) => policy switch
    {
        TerminalMouseShiftCapturePolicy.Never => false,
        TerminalMouseShiftCapturePolicy.Always => true,
        TerminalMouseShiftCapturePolicy.Disabled => applicationOverride ?? false,
        TerminalMouseShiftCapturePolicy.Enabled => applicationOverride ?? true,
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };
}

/// <summary>Effective terminal mouse input state, independent of the DEC mode bank.</summary>
/// <param name="Modes">Tracking and encoding modes.</param>
/// <param name="ShiftCaptureOverride">Application capture override, or null to use host policy.</param>
/// <param name="Shape">Application-requested mouse pointer shape.</param>
public readonly record struct TerminalMouseInputState(TerminalMouseModeState Modes, bool? ShiftCaptureOverride,
    TerminalMouseShape Shape = TerminalMouseShape.Text);
