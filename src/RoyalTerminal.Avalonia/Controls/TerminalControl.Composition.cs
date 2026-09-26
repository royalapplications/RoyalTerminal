// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    private TerminalTextInputMethodClient? _textInputMethodClient;
    private bool _isComposing;
    private int _compositionNotificationPending;

    private void ProvideTextInputMethodClient(TextInputMethodClientRequestedEventArgs args)
    {
        _textInputMethodClient ??= new TerminalTextInputMethodClient(this, GetCompositionCursorRectangle, SetCompositionText);
        args.Client = _textInputMethodClient;
    }

    private Rect GetCompositionCursorRectangle()
    {
        if (_renderer is not { } renderer || _screen is null) return new Rect(0, 0, 1, 1);
        int column = Math.Clamp(renderer.CursorColumn, 0, _screen.Columns - 1);
        int row = Math.Clamp(renderer.CursorRow, 0, _screen.ViewportRows - 1);
        int width = 1;
        if (renderer.Preedit is { Count: > 0 } preedit)
        {
            var range = preedit.Range(column, _screen.Columns - 1);
            column = range.Caret;
        }
        Point point = new(column * renderer.CellWidth, row * renderer.CellHeight);
        point = _presenter?.TranslatePoint(point, this) ?? new Point(point.X + Padding.Left, point.Y + Padding.Top);
        return new Rect(point, new Size(Math.Max(1, width * renderer.CellWidth), Math.Max(1, renderer.CellHeight)));
    }

    private void SetCompositionText(string? text, int? cursor = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        // IBus uses an empty string for HidePreedit; IMM/macOS use null.
        _isComposing = !string.IsNullOrEmpty(text) && IsFocused;
        if (TerminalInputAdapter is ITerminalCompositionInputAdapter adapter) adapter.SetComposing(_isComposing);
        TerminalPreedit? preedit = _isComposing ? new TerminalPreedit(text!, cursor) : null;
        if (_isComposing && HasRendererSelection()) ClearSelection();
        if (_screen is not null && _renderer is not null)
        {
            using (_screen.Synchronization.AcquireDemand())
            {
                _renderer.Preedit = preedit is { Count: > 0 } ? preedit : null;
                UpdateRendererCursorForViewportLocked();
            }
        }
        _textInputMethodClient?.NotifyCursorChanged();
        _presenter?.Invalidate(fullRedraw: true);
    }

    private void NotifyCompositionCursorChanged()
    {
        if (_textInputMethodClient is null) return;
        // Never invoke platform IME callbacks while the screen lock is held.
        if (Interlocked.Exchange(ref _compositionNotificationPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _compositionNotificationPending, 0);
            _textInputMethodClient?.NotifyCursorChanged();
        });
    }
}
