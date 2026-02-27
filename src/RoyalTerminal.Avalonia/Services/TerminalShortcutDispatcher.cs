// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Common keyboard shortcut dispatch for terminal controls.

using Avalonia.Input;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Dispatches common clipboard-style shortcuts used by terminal controls.
/// </summary>
public static class TerminalShortcutDispatcher
{
    /// <summary>
    /// Tries to map a key/modifier combination to a common terminal shortcut action.
    /// </summary>
    /// <returns>True when a shortcut action was invoked and the key event should be handled.</returns>
    public static bool TryHandleCommonShortcut(
        Key key,
        KeyModifiers modifiers,
        bool hasSelection,
        Action copyAction,
        Action pasteAction,
        Action cutAction,
        Action selectAllAction,
        TerminalShortcutConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(copyAction);
        ArgumentNullException.ThrowIfNull(pasteAction);
        ArgumentNullException.ThrowIfNull(cutAction);
        ArgumentNullException.ThrowIfNull(selectAllAction);
        configuration ??= TerminalShortcutConfiguration.Default;

        return TryDispatch(configuration.CopyGestures, key, modifiers, hasSelection, copyAction) ||
               TryDispatch(configuration.PasteGestures, key, modifiers, hasSelection, pasteAction) ||
               TryDispatch(configuration.CutGestures, key, modifiers, hasSelection, cutAction) ||
               TryDispatch(configuration.SelectAllGestures, key, modifiers, hasSelection, selectAllAction);
    }

    private static bool TryDispatch(
        IReadOnlyList<TerminalShortcutGesture> gestures,
        Key key,
        KeyModifiers modifiers,
        bool hasSelection,
        Action action)
    {
        for (int i = 0; i < gestures.Count; i++)
        {
            TerminalShortcutGesture gesture = gestures[i];
            if (gesture.Key != key || gesture.Modifiers != modifiers)
            {
                continue;
            }

            if (gesture.RequiresSelection && !hasSelection)
            {
                continue;
            }

            action();
            return true;
        }

        return false;
    }
}
