// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Input;
using RoyalTerminal.Avalonia.Services;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShortcutDispatcherTests
{
    [Fact]
    public void CtrlC_WithSelection_InvokesCopy()
    {
        ShortcutActionCounts counts = Handle(Key.C, KeyModifiers.Control, hasSelection: true);

        Assert.True(counts.Handled);
        Assert.Equal(1, counts.Copy);
    }

    [Fact]
    public void CtrlC_WithoutSelection_IsNotHandled()
    {
        ShortcutActionCounts counts = Handle(Key.C, KeyModifiers.Control, hasSelection: false);

        Assert.False(counts.Handled);
        Assert.Equal(0, counts.Copy);
    }

    [Fact]
    public void CtrlV_InvokesPaste()
    {
        ShortcutActionCounts counts = Handle(Key.V, KeyModifiers.Control, hasSelection: false);

        Assert.True(counts.Handled);
        Assert.Equal(1, counts.Paste);
    }

    [Fact]
    public void CtrlX_WithSelection_InvokesCut()
    {
        ShortcutActionCounts counts = Handle(Key.X, KeyModifiers.Control, hasSelection: true);

        Assert.True(counts.Handled);
        Assert.Equal(1, counts.Cut);
    }

    [Fact]
    public void CtrlShiftShortcuts_AreHandled()
    {
        ShortcutActionCounts copy = Handle(Key.C, KeyModifiers.Control | KeyModifiers.Shift, hasSelection: false);
        ShortcutActionCounts paste = Handle(Key.V, KeyModifiers.Control | KeyModifiers.Shift, hasSelection: false);
        ShortcutActionCounts selectAll = Handle(Key.A, KeyModifiers.Control | KeyModifiers.Shift, hasSelection: false);

        Assert.True(copy.Handled);
        Assert.True(paste.Handled);
        Assert.True(selectAll.Handled);
        Assert.Equal(1, copy.Copy);
        Assert.Equal(1, paste.Paste);
        Assert.Equal(1, selectAll.SelectAll);
    }

    [Fact]
    public void MetaShortcuts_AreHandled()
    {
        ShortcutActionCounts copy = Handle(Key.C, KeyModifiers.Meta, hasSelection: false);
        ShortcutActionCounts paste = Handle(Key.V, KeyModifiers.Meta, hasSelection: false);
        ShortcutActionCounts cut = Handle(Key.X, KeyModifiers.Meta, hasSelection: true);
        ShortcutActionCounts selectAll = Handle(Key.A, KeyModifiers.Meta, hasSelection: false);

        Assert.True(copy.Handled);
        Assert.True(paste.Handled);
        Assert.True(cut.Handled);
        Assert.True(selectAll.Handled);
        Assert.Equal(1, copy.Copy);
        Assert.Equal(1, paste.Paste);
        Assert.Equal(1, cut.Cut);
        Assert.Equal(1, selectAll.SelectAll);
    }

    [Fact]
    public void InsertDeleteShortcuts_AreHandled()
    {
        ShortcutActionCounts copy = Handle(Key.Insert, KeyModifiers.Control, hasSelection: true);
        ShortcutActionCounts paste = Handle(Key.Insert, KeyModifiers.Shift, hasSelection: false);
        ShortcutActionCounts cut = Handle(Key.Delete, KeyModifiers.Shift, hasSelection: true);

        Assert.True(copy.Handled);
        Assert.True(paste.Handled);
        Assert.True(cut.Handled);
        Assert.Equal(1, copy.Copy);
        Assert.Equal(1, paste.Paste);
        Assert.Equal(1, cut.Cut);
    }

    [Fact]
    public void AltModifiedShortcuts_AreIgnored()
    {
        ShortcutActionCounts counts = Handle(Key.C, KeyModifiers.Control | KeyModifiers.Alt, hasSelection: true);

        Assert.False(counts.Handled);
        Assert.Equal(0, counts.Copy);
        Assert.Equal(0, counts.Paste);
        Assert.Equal(0, counts.Cut);
        Assert.Equal(0, counts.SelectAll);
    }

    [Fact]
    public void CustomConfiguration_OverridesDefaultGestures()
    {
        TerminalShortcutConfiguration configuration = new(
            copyGestures: [new TerminalShortcutGesture(Key.F8, KeyModifiers.Control)],
            pasteGestures: [new TerminalShortcutGesture(Key.F9, KeyModifiers.Control)],
            cutGestures: [new TerminalShortcutGesture(Key.F10, KeyModifiers.Control)],
            selectAllGestures: [new TerminalShortcutGesture(Key.F11, KeyModifiers.Control)]);

        ShortcutActionCounts defaultCopy = Handle(Key.C, KeyModifiers.Control, hasSelection: true, configuration);
        ShortcutActionCounts customCopy = Handle(Key.F8, KeyModifiers.Control, hasSelection: true, configuration);
        ShortcutActionCounts customSelectAll = Handle(Key.F11, KeyModifiers.Control, hasSelection: false, configuration);

        Assert.False(defaultCopy.Handled);
        Assert.True(customCopy.Handled);
        Assert.True(customSelectAll.Handled);
        Assert.Equal(1, customCopy.Copy);
        Assert.Equal(1, customSelectAll.SelectAll);
    }

    [Fact]
    public void CustomConfiguration_CanDisableSelectAllShortcut()
    {
        TerminalShortcutConfiguration configuration = new(
            selectAllGestures: Array.Empty<TerminalShortcutGesture>());

        ShortcutActionCounts counts = Handle(Key.A, KeyModifiers.Control | KeyModifiers.Shift, hasSelection: false, configuration);

        Assert.False(counts.Handled);
        Assert.Equal(0, counts.SelectAll);
    }

    private static ShortcutActionCounts Handle(
        Key key,
        KeyModifiers modifiers,
        bool hasSelection,
        TerminalShortcutConfiguration? configuration = null)
    {
        int copy = 0;
        int paste = 0;
        int cut = 0;
        int selectAll = 0;

        bool handled = TerminalShortcutDispatcher.TryHandleCommonShortcut(
            key,
            modifiers,
            hasSelection,
            copyAction: () => copy++,
            pasteAction: () => paste++,
            cutAction: () => cut++,
            selectAllAction: () => selectAll++,
            configuration: configuration);

        return new ShortcutActionCounts(handled, copy, paste, cut, selectAll);
    }

    private readonly record struct ShortcutActionCounts(
        bool Handled,
        int Copy,
        int Paste,
        int Cut,
        int SelectAll);
}
