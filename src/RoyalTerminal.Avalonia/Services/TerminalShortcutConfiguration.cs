// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Configurable terminal keyboard shortcut bindings.

using Avalonia.Input;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Describes a single terminal shortcut gesture.
/// </summary>
/// <param name="Key">Primary key.</param>
/// <param name="Modifiers">Required modifiers.</param>
/// <param name="RequiresSelection">
/// When true, the gesture only triggers if the control currently has a selection.
/// </param>
public readonly record struct TerminalShortcutGesture(
    Key Key,
    KeyModifiers Modifiers,
    bool RequiresSelection = false);

/// <summary>
/// Configurable keyboard shortcut bindings for terminal actions.
/// </summary>
public sealed class TerminalShortcutConfiguration
{
    private static readonly IReadOnlyList<TerminalShortcutGesture> s_defaultCopyGestures =
    [
        new(Key.C, KeyModifiers.Meta),
        new(Key.C, KeyModifiers.Control | KeyModifiers.Shift),
        new(Key.C, KeyModifiers.Control, RequiresSelection: true),
        new(Key.Insert, KeyModifiers.Control, RequiresSelection: true),
    ];

    private static readonly IReadOnlyList<TerminalShortcutGesture> s_defaultPasteGestures =
    [
        new(Key.V, KeyModifiers.Meta),
        new(Key.V, KeyModifiers.Control | KeyModifiers.Shift),
        new(Key.V, KeyModifiers.Control),
        new(Key.Insert, KeyModifiers.Shift),
    ];

    private static readonly IReadOnlyList<TerminalShortcutGesture> s_defaultCutGestures =
    [
        new(Key.X, KeyModifiers.Meta),
        new(Key.X, KeyModifiers.Control | KeyModifiers.Shift),
        new(Key.X, KeyModifiers.Control, RequiresSelection: true),
        new(Key.Delete, KeyModifiers.Shift, RequiresSelection: true),
    ];

    private static readonly IReadOnlyList<TerminalShortcutGesture> s_defaultSelectAllGestures =
    [
        new(Key.A, KeyModifiers.Meta),
        new(Key.A, KeyModifiers.Control | KeyModifiers.Shift),
    ];

    /// <summary>
    /// Creates a new terminal shortcut configuration.
    /// </summary>
    public TerminalShortcutConfiguration(
        IReadOnlyList<TerminalShortcutGesture>? copyGestures = null,
        IReadOnlyList<TerminalShortcutGesture>? pasteGestures = null,
        IReadOnlyList<TerminalShortcutGesture>? cutGestures = null,
        IReadOnlyList<TerminalShortcutGesture>? selectAllGestures = null)
    {
        CopyGestures = copyGestures ?? s_defaultCopyGestures;
        PasteGestures = pasteGestures ?? s_defaultPasteGestures;
        CutGestures = cutGestures ?? s_defaultCutGestures;
        SelectAllGestures = selectAllGestures ?? s_defaultSelectAllGestures;
    }

    /// <summary>
    /// Gets the default shortcut configuration.
    /// </summary>
    public static TerminalShortcutConfiguration Default { get; } = new();

    /// <summary>
    /// Gets shortcut gestures that trigger copy.
    /// </summary>
    public IReadOnlyList<TerminalShortcutGesture> CopyGestures { get; }

    /// <summary>
    /// Gets shortcut gestures that trigger paste.
    /// </summary>
    public IReadOnlyList<TerminalShortcutGesture> PasteGestures { get; }

    /// <summary>
    /// Gets shortcut gestures that trigger cut.
    /// </summary>
    public IReadOnlyList<TerminalShortcutGesture> CutGestures { get; }

    /// <summary>
    /// Gets shortcut gestures that trigger select-all.
    /// </summary>
    public IReadOnlyList<TerminalShortcutGesture> SelectAllGestures { get; }
}
