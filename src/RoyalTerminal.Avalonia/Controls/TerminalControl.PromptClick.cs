// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Input;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Defines <see cref="CursorClickToMove"/>.</summary>
    public static readonly DirectProperty<TerminalControl, bool> CursorClickToMoveProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(nameof(CursorClickToMove),
            control => control.CursorClickToMove, (control, value) => control.CursorClickToMove = value);

    private bool _cursorClickToMove = true;
    private (int Column, int Row)? _promptClickCell;

    /// <summary>Allows unmodified primary clicks to send the shell's OSC 133 click events.</summary>
    public bool CursorClickToMove
    {
        get => _cursorClickToMove;
        set => SetAndRaise(CursorClickToMoveProperty, ref _cursorClickToMove, value);
    }

    private void BeginPromptClick(TerminalMouseButton button, KeyModifiers modifiers, int clickCount, bool pointerSent)
    {
        _promptClickCell = CursorClickToMove && !pointerSent && button == TerminalMouseButton.Left &&
            modifiers == KeyModifiers.None && clickCount == 1 && _lastPointerColumn >= 0 && _lastPointerRow >= 0
            ? (_lastPointerColumn, _lastPointerRow) : null;
    }

    private void ContinuePromptClick(bool inContent)
    {
        if (!inContent || _promptClickCell != (_lastPointerColumn, _lastPointerRow)) _promptClickCell = null;
    }

    private void FinishPromptClick(TerminalMouseButton button, KeyModifiers modifiers, bool inContent)
    {
        (int Column, int Row)? cell = _promptClickCell;
        _promptClickCell = null;
        if (!CursorClickToMove || !inContent || button != TerminalMouseButton.Left ||
            modifiers != KeyModifiers.None || cell != (_lastPointerColumn, _lastPointerRow) ||
            cell is null || HasNonEmptyRendererSelection() ||
            TerminalSessionService.SelectionSource?.HasSelection == true || _screen is null ||
            _vtProcessor is not ITerminalPromptClickEncoderSource encoder) return;
        byte[] sequence;
        lock (_screen.SyncRoot)
        {
            if (IsMouseReportingActiveForInput() || !encoder.TryEncodePromptClick(cell.Value.Column, cell.Value.Row, out sequence)) return;
        }
        ClearSelection();
        TerminalSessionService.SendInput(sequence);
    }
}
