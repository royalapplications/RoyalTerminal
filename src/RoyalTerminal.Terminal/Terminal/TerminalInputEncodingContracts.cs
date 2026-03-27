// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal — Optional native input encoding contracts.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Backend-neutral key encoding request for transport fallback paths.
/// </summary>
/// <param name="KeyId">
/// Stable key identity supplied by the UI layer.
/// Current producers use <c>Avalonia.Input.Key.ToString()</c>.
/// </param>
/// <param name="Action">Press or release action.</param>
/// <param name="Text">Optional text payload associated with the key event.</param>
/// <param name="Modifiers">Normalized modifier flags.</param>
/// <param name="IsComposing">Whether IME composition is active.</param>
public readonly record struct TerminalKeyEncodingRequest(
    string KeyId,
    TerminalInputAction Action,
    string? Text,
    TerminalModifiers Modifiers,
    bool IsComposing = false);

/// <summary>
/// Geometry context for encoding pointer events into terminal mouse protocol bytes.
/// </summary>
/// <param name="ScreenWidthPx">Full terminal surface width in pixels.</param>
/// <param name="ScreenHeightPx">Full terminal surface height in pixels.</param>
/// <param name="CellWidthPx">Single cell width in pixels.</param>
/// <param name="CellHeightPx">Single cell height in pixels.</param>
/// <param name="PaddingTopPx">Top padding in pixels.</param>
/// <param name="PaddingBottomPx">Bottom padding in pixels.</param>
/// <param name="PaddingRightPx">Right padding in pixels.</param>
/// <param name="PaddingLeftPx">Left padding in pixels.</param>
public readonly record struct TerminalPointerEncodingContext(
    int ScreenWidthPx,
    int ScreenHeightPx,
    int CellWidthPx,
    int CellHeightPx,
    int PaddingTopPx = 0,
    int PaddingBottomPx = 0,
    int PaddingRightPx = 0,
    int PaddingLeftPx = 0);

/// <summary>
/// Optional source for native key-sequence encoding.
/// </summary>
public interface ITerminalKeySequenceEncoderSource
{
    /// <summary>
    /// Tries to encode the supplied key event into terminal input bytes.
    /// </summary>
    bool TryEncodeKey(in TerminalKeyEncodingRequest request, out byte[] sequence);
}

/// <summary>
/// Optional source for native mouse-sequence encoding.
/// </summary>
public interface ITerminalPointerSequenceEncoderSource
{
    /// <summary>
    /// Tries to encode the supplied pointer event into terminal input bytes.
    /// </summary>
    bool TryEncodePointer(
        in TerminalPointerEvent pointerEvent,
        in TerminalPointerEncodingContext context,
        out byte[] sequence);
}

/// <summary>
/// Optional source for whether the active terminal requested mouse reporting.
/// </summary>
public interface ITerminalMouseReportingStateSource
{
    /// <summary>
    /// Gets whether mouse reporting is currently enabled by terminal state.
    /// </summary>
    bool MouseReportingEnabled { get; }
}
