// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Input.TextInput;

namespace RoyalTerminal.Avalonia.Services;

internal sealed class TerminalTextInputMethodClient(Visual visual, Func<Rect> cursor, Action<string?, int?> preedit)
    : TextInputMethodClient
{
    private Rect _lastCursor;
    public override Visual TextViewVisual => visual;
    public override bool SupportsPreedit => true;
    // Terminal output is not an editable document and must not be exposed as
    // surrounding input (especially when entering a password).
    public override bool SupportsSurroundingText => false;
    public override string SurroundingText => string.Empty;
    public override TextSelection Selection { get => default; set { } }
    public override Rect CursorRectangle => cursor();
    public override void SetPreeditText(string? text) => preedit(text, null);
    public override void SetPreeditText(string? text, int? cursorPos) => preedit(text, cursorPos);

    internal void NotifyCursorChanged()
    {
        Rect next = CursorRectangle;
        if (next == _lastCursor) return;
        _lastCursor = next;
        RaiseCursorRectangleChanged();
    }

    internal void Reset()
    {
        preedit(null, null);
        RequestReset();
    }
}
