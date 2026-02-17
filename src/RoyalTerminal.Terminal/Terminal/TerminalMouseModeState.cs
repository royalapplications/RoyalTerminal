// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - VT mouse-reporting mode state.

namespace RoyalTerminal.Terminal;

/// <summary>
/// DEC private mouse-tracking mode currently enabled by the terminal application.
/// </summary>
public enum TerminalMouseTrackingMode
{
    /// <summary>No mouse tracking is enabled.</summary>
    None = 0,

    /// <summary>
    /// X10 press-only tracking (<c>DECSET ?9</c>).
    /// </summary>
    X10Press = 1,

    /// <summary>
    /// Button press/release and wheel tracking (<c>DECSET ?1000</c>).
    /// </summary>
    PressRelease = 2,

    /// <summary>
    /// Press/release/wheel plus motion while a button is pressed (<c>DECSET ?1002</c>).
    /// </summary>
    ButtonMotion = 3,

    /// <summary>
    /// Press/release/wheel plus all pointer motion (<c>DECSET ?1003</c>).
    /// </summary>
    AnyMotion = 4,
}

/// <summary>
/// Active mouse protocol encoding used for outbound pointer events.
/// </summary>
public enum TerminalMouseEncoding
{
    /// <summary>
    /// X10/default byte protocol (legacy <c>CSI M</c> with 8-bit coordinates).
    /// </summary>
    Default = 0,

    /// <summary>
    /// UTF-8 extended coordinates (<c>DECSET ?1005</c>).
    /// </summary>
    Utf8 = 1,

    /// <summary>
    /// SGR extended protocol (<c>DECSET ?1006</c>).
    /// </summary>
    Sgr = 2,

    /// <summary>
    /// URXVT decimal protocol (<c>DECSET ?1015</c>).
    /// </summary>
    Urxvt = 3,
}

/// <summary>
/// Snapshot of VT mouse-reporting behavior used by input routing.
/// </summary>
/// <param name="TrackingMode">Current DEC mouse-tracking mode.</param>
/// <param name="Encoding">Current mouse protocol encoding mode.</param>
public readonly record struct TerminalMouseModeState(
    TerminalMouseTrackingMode TrackingMode,
    TerminalMouseEncoding Encoding)
{
    /// <summary>
    /// Gets whether the active terminal application requested mouse reporting.
    /// </summary>
    public bool IsMouseReportingEnabled => TrackingMode != TerminalMouseTrackingMode.None;

    /// <summary>
    /// Gets whether button release events should be reported.
    /// </summary>
    public bool ReportsButtonRelease => TrackingMode is not (TerminalMouseTrackingMode.None or TerminalMouseTrackingMode.X10Press);

    /// <summary>
    /// Gets whether wheel events should be reported.
    /// </summary>
    public bool ReportsWheel => TrackingMode is not (TerminalMouseTrackingMode.None or TerminalMouseTrackingMode.X10Press);

    /// <summary>
    /// Gets whether pointer move events should be reported.
    /// </summary>
    public bool ReportsMotion => TrackingMode is TerminalMouseTrackingMode.ButtonMotion or TerminalMouseTrackingMode.AnyMotion;

    /// <summary>
    /// Gets whether pointer move events are reported even when no button is pressed.
    /// </summary>
    public bool ReportsMotionWithoutButton => TrackingMode == TerminalMouseTrackingMode.AnyMotion;
}
