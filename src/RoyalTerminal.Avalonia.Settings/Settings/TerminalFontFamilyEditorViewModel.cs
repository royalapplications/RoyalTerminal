// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Immutable;
using ReactiveUI;
using ReactiveUI.Reactive;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Settings;

/// <summary>Framework-independent editor for ordered per-style family lists, one family per line.</summary>
public sealed class TerminalFontFamilyEditorViewModel : ReactiveObject
{
    private string _regular = string.Empty, _bold = string.Empty, _italic = string.Empty, _boldItalic = string.Empty;
    private string _codepointMaps = string.Empty;
    private string? _codepointMapError;
    private ImmutableArray<string> _validCodepointMaps = [];

    /// <summary>Regular families, highest priority first; empty restores the primary family/file.</summary>
    public string Regular { get => _regular; set => this.RaiseAndSetIfChanged(ref _regular, value ?? string.Empty); }
    /// <summary>Bold families, highest priority first; empty inherits regular-family variants.</summary>
    public string Bold { get => _bold; set => this.RaiseAndSetIfChanged(ref _bold, value ?? string.Empty); }
    /// <summary>Italic families, highest priority first; empty inherits regular-family variants.</summary>
    public string Italic { get => _italic; set => this.RaiseAndSetIfChanged(ref _italic, value ?? string.Empty); }
    /// <summary>Bold-italic families, highest priority first; empty inherits regular-family variants.</summary>
    public string BoldItalic { get => _boldItalic; set => this.RaiseAndSetIfChanged(ref _boldItalic, value ?? string.Empty); }

    /// <summary>One U+XXXX[-U+YYYY][,...]=family mapping per line; later ranges win.</summary>
    public string CodepointMaps
    {
        get => _codepointMaps;
        set
        {
            value ??= string.Empty;
            if (_codepointMaps == value) return;
            string[] lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            bool valid = TerminalFontCodepointMap.TryParse(lines, out _, out string? error);
            if (valid) _validCodepointMaps = Parse(value);
            this.RaiseAndSetIfChanged(ref _codepointMapError, error, nameof(CodepointMapError));
            this.RaisePropertyChanged(nameof(HasCodepointMapError));
            this.RaiseAndSetIfChanged(ref _codepointMaps, value);
        }
    }

    /// <summary>The current mapping syntax error, or null for valid input.</summary>
    public string? CodepointMapError => _codepointMapError;
    /// <summary>Whether mapping edits must be corrected before applying or saving.</summary>
    public bool HasCodepointMapError => _codepointMapError is not null;

    /// <summary>
    /// Builds an immutable configuration, retaining the last valid mapping list
    /// while an invalid edit is on screen. Apply/save must check HasCodepointMapError.
    /// </summary>
    public TerminalFontFamilySettings BuildSettings() => new()
    {
        Regular = Parse(Regular), Bold = Parse(Bold), Italic = Parse(Italic), BoldItalic = Parse(BoldItalic),
        CodepointMaps = _validCodepointMaps,
    };

    /// <summary>Loads a configuration; null restores empty family lists.</summary>
    public void Load(TerminalFontFamilySettings? settings)
    {
        settings = (settings ?? TerminalFontFamilySettings.Default).Normalize();
        Regular = string.Join(Environment.NewLine, settings.Regular);
        Bold = string.Join(Environment.NewLine, settings.Bold);
        Italic = string.Join(Environment.NewLine, settings.Italic);
        BoldItalic = string.Join(Environment.NewLine, settings.BoldItalic);
        CodepointMaps = string.Join(Environment.NewLine, settings.CodepointMaps);
    }

    private static ImmutableArray<string> Parse(string text)
        => ImmutableArray.CreateRange(text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
