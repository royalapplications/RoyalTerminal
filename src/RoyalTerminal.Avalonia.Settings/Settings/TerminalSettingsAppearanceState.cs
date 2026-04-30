// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Collections;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Settings;

public sealed class TerminalSettingsAppearanceState : TerminalSettingsCategoryStateBase
{
    internal TerminalSettingsAppearanceState(TerminalSettingsPanelState owner)
        : base(
            owner,
            nameof(TerminalSettingsPanelState.SelectedFontSource),
            nameof(TerminalSettingsPanelState.FontFamilyName),
            nameof(TerminalSettingsPanelState.FontFilePath),
            nameof(TerminalSettingsPanelState.FontSize),
            nameof(TerminalSettingsPanelState.IsSystemFontSourceSelected),
            nameof(TerminalSettingsPanelState.IsFileFontSourceSelected),
            nameof(TerminalSettingsPanelState.AutoScroll),
            nameof(TerminalSettingsPanelState.BackgroundOpacityEnabled),
            nameof(TerminalSettingsPanelState.SelectedTextHighlightingMode))
    {
    }

    public IReadOnlyList<TerminalSettingsFontSourceOption> FontSources => Owner.FontSources;

    public AvaloniaList<string> SystemFontFamilies => Owner.SystemFontFamilies;

    public AvaloniaList<TerminalSettingsHighlightRuleState> TextHighlightRules => Owner.TextHighlightRules;

    public IReadOnlyList<TerminalSettingsTextHighlightingModeOption> TextHighlightingModes => Owner.TextHighlightingModes;

    public TerminalFontSource SelectedFontSource
    {
        get => Owner.SelectedFontSource;
        set => Owner.SelectedFontSource = value;
    }

    public string FontFamilyName
    {
        get => Owner.FontFamilyName;
        set => Owner.FontFamilyName = value;
    }

    public string FontFilePath
    {
        get => Owner.FontFilePath;
        set => Owner.FontFilePath = value;
    }

    public double FontSize
    {
        get => Owner.FontSize;
        set => Owner.FontSize = value;
    }

    public bool IsSystemFontSourceSelected => Owner.IsSystemFontSourceSelected;

    public bool IsFileFontSourceSelected => Owner.IsFileFontSourceSelected;

    public ICommand BrowseFontFileCommand => Owner.BrowseFontFileCommand;

    public ICommand AddTextHighlightRuleCommand => Owner.AddTextHighlightRuleCommand;

    public bool AutoScroll
    {
        get => Owner.AutoScroll;
        set => Owner.AutoScroll = value;
    }

    public bool BackgroundOpacityEnabled
    {
        get => Owner.BackgroundOpacityEnabled;
        set => Owner.BackgroundOpacityEnabled = value;
    }

    public TerminalSettingsTextHighlightingModeOption? SelectedTextHighlightingMode
    {
        get => Owner.SelectedTextHighlightingMode;
        set => Owner.SelectedTextHighlightingMode = value;
    }
}
