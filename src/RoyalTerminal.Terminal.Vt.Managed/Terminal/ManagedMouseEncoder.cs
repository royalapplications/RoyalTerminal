// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.Terminal;

// Persistent encoder state belongs to a processor, never to a frozen render frame.
internal sealed class ManagedMouseEncoder
{
    private TerminalMouseModeState _mode;
    private TerminalPointerEncodingContext? _context;
    private (int Column, int Row)? _lastCell;
    private byte _pressedButtons;

    internal void Reset() { _lastCell = null; _pressedButtons = 0; }
    internal void ResetMotion() => _lastCell = null;

    internal bool TryEncode(in TerminalPointerEvent pointer, in TerminalPointerEncodingContext context,
        in TerminalMouseModeState mode, out byte[] sequence)
    {
        sequence = [];
        if (!TerminalPointerGeometry.TryCreate(pointer, context, mode.Encoding, out TerminalPointerGeometry geometry) ||
            (mode.Encoding == TerminalMouseEncoding.Utf8 &&
             (!Rune.IsValid(geometry.Column + 32) || !Rune.IsValid(geometry.Row + 32)))) return false;
        if (_mode != mode || _context != geometry.Context) _lastCell = null;
        _mode = mode; _context = geometry.Context;

        // Match the adapter's normalization of supported buttons/actions.
        byte mask = pointer.Button switch { TerminalMouseButton.Left => 1, TerminalMouseButton.Middle => 2, TerminalMouseButton.Right => 4, _ => 0 };
        TerminalPointerEvent normalized = pointer;
        if (pointer.Kind == TerminalPointerEventKind.Button)
        {
            if (mask == 0) return false;
            bool release = pointer.Action == TerminalInputAction.Release;
            _pressedButtons = release ? (byte)(_pressedButtons & ~mask) : (byte)(_pressedButtons | mask);
            normalized = pointer with { Action = release ? TerminalInputAction.Release : TerminalInputAction.Press };
        }
        else if (pointer.Kind == TerminalPointerEventKind.Move && mask == 0)
            normalized = pointer with { Button = TerminalMouseButton.None };

        if (!mode.IsMouseReportingEnabled || !TerminalMouseProtocolEncoder.TryGetMouseCode(normalized, mode, out _, out _)) return false;
        bool releaseEvent = normalized.Kind == TerminalPointerEventKind.Button && normalized.Action == TerminalInputAction.Release;
        bool anyButton = _pressedButtons != 0 || (pointer.Kind == TerminalPointerEventKind.Move && mask != 0);
        if (!releaseEvent && geometry.Outside && (!mode.ReportsMotion || !anyButton)) return false;
        (int, int) cell = (geometry.Column, geometry.Row);
        if (pointer.Kind == TerminalPointerEventKind.Move && mode.Encoding != TerminalMouseEncoding.SgrPixels && _lastCell == cell) return false;
        // Ghostty records the cell before checking the encoding's coordinate limit.
        _lastCell = cell;
        return TerminalMouseProtocolEncoder.TryEncode(normalized, mode, geometry.Column, geometry.Row, geometry.PixelX, geometry.PixelY, out sequence);
    }
}
