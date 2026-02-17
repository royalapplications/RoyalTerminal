// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - VT mouse protocol encoder.

using System.Text;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Encodes normalized pointer events into VT mouse protocol byte sequences.
/// </summary>
public static class TerminalMouseProtocolEncoder
{
    /// <summary>
    /// Encodes a pointer event according to the supplied VT mouse mode.
    /// </summary>
    /// <param name="pointerEvent">Pointer event to encode.</param>
    /// <param name="modeState">Active mouse mode and encoding.</param>
    /// <param name="column">Pointer column (1-based cell coordinate).</param>
    /// <param name="row">Pointer row (1-based cell coordinate).</param>
    /// <param name="sequence">Encoded VT byte sequence when successful.</param>
    /// <returns><see langword="true"/> when the event should be sent to the terminal.</returns>
    public static bool TryEncode(
        in TerminalPointerEvent pointerEvent,
        in TerminalMouseModeState modeState,
        int column,
        int row,
        out byte[] sequence)
    {
        sequence = Array.Empty<byte>();

        if (!modeState.IsMouseReportingEnabled || column <= 0 || row <= 0)
        {
            return false;
        }

        if (!TryGetMouseCode(pointerEvent, modeState, out int mouseCode, out bool sgrRelease))
        {
            return false;
        }

        int modifierBits = GetModifierBits(pointerEvent.Modifiers, modeState);
        int finalCode = mouseCode + modifierBits;

        switch (modeState.Encoding)
        {
            case TerminalMouseEncoding.Sgr:
                sequence = Encoding.ASCII.GetBytes(
                    $"\x1b[<{finalCode};{column};{row}{(sgrRelease ? 'm' : 'M')}");
                return true;

            case TerminalMouseEncoding.Urxvt:
                sequence = Encoding.ASCII.GetBytes(
                    $"\x1b[{finalCode + 32};{column};{row}M");
                return true;

            case TerminalMouseEncoding.Utf8:
                sequence = EncodeUtf8Protocol(finalCode, column, row);
                return true;

            default:
                sequence = EncodeDefaultProtocol(finalCode, column, row);
                return true;
        }
    }

    private static bool TryGetMouseCode(
        in TerminalPointerEvent pointerEvent,
        in TerminalMouseModeState modeState,
        out int mouseCode,
        out bool sgrRelease)
    {
        mouseCode = 0;
        sgrRelease = false;

        switch (pointerEvent.Kind)
        {
            case TerminalPointerEventKind.Button:
                if (pointerEvent.Action == TerminalInputAction.Release)
                {
                    if (!modeState.ReportsButtonRelease)
                    {
                        return false;
                    }

                    // In SGR mode releases preserve the released button code and use 'm'.
                    if (modeState.Encoding == TerminalMouseEncoding.Sgr &&
                        TryGetButtonBaseCode(pointerEvent.Button, out int sgrReleaseCode))
                    {
                        mouseCode = sgrReleaseCode;
                    }
                    else
                    {
                        mouseCode = 3;
                    }

                    sgrRelease = true;
                    return true;
                }

                return TryGetButtonBaseCode(pointerEvent.Button, out mouseCode);

            case TerminalPointerEventKind.Move:
                if (!modeState.ReportsMotion)
                {
                    return false;
                }

                TerminalMouseButton motionButton = pointerEvent.Button;
                if (motionButton == TerminalMouseButton.None)
                {
                    if (!modeState.ReportsMotionWithoutButton)
                    {
                        return false;
                    }

                    mouseCode = 3;
                }
                else if (!TryGetButtonBaseCode(motionButton, out mouseCode))
                {
                    return false;
                }

                mouseCode += 32;
                return true;

            case TerminalPointerEventKind.Scroll:
                if (!modeState.ReportsWheel)
                {
                    return false;
                }

                if (pointerEvent.DeltaY > 0)
                {
                    mouseCode = 64;
                    return true;
                }

                if (pointerEvent.DeltaY < 0)
                {
                    mouseCode = 65;
                    return true;
                }

                if (pointerEvent.DeltaX < 0)
                {
                    mouseCode = 66;
                    return true;
                }

                if (pointerEvent.DeltaX > 0)
                {
                    mouseCode = 67;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryGetButtonBaseCode(TerminalMouseButton button, out int code)
    {
        switch (button)
        {
            case TerminalMouseButton.Left:
                code = 0;
                return true;
            case TerminalMouseButton.Middle:
                code = 1;
                return true;
            case TerminalMouseButton.Right:
                code = 2;
                return true;
            default:
                code = 0;
                return false;
        }
    }

    private static int GetModifierBits(TerminalModifiers modifiers, in TerminalMouseModeState modeState)
    {
        // X10 tracking mode does not include modifier bits.
        if (modeState.TrackingMode == TerminalMouseTrackingMode.X10Press)
        {
            return 0;
        }

        int bits = 0;
        if ((modifiers & TerminalModifiers.Shift) != 0) bits += 4;
        if ((modifiers & TerminalModifiers.Alt) != 0) bits += 8;
        if ((modifiers & TerminalModifiers.Control) != 0) bits += 16;
        return bits;
    }

    private static byte[] EncodeDefaultProtocol(int finalCode, int column, int row)
    {
        // Legacy xterm byte protocol can only represent coordinates up to 223.
        int clampedColumn = Math.Min(column, 223);
        int clampedRow = Math.Min(row, 223);

        return
        [
            0x1B,
            (byte)'[',
            (byte)'M',
            (byte)(finalCode + 32),
            (byte)(clampedColumn + 32),
            (byte)(clampedRow + 32),
        ];
    }

    private static byte[] EncodeUtf8Protocol(int finalCode, int column, int row)
    {
        StringBuilder builder = new(capacity: 16);
        builder.Append('\x1b');
        builder.Append('[');
        builder.Append('M');
        builder.Append(char.ConvertFromUtf32(finalCode + 32));
        builder.Append(char.ConvertFromUtf32(column + 32));
        builder.Append(char.ConvertFromUtf32(row + 32));
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
