// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Settings;

public sealed class TerminalSettingsAppearanceState : TerminalSettingsCategoryStateBase
{
    internal TerminalSettingsAppearanceState(TerminalSettingsPanelState owner)
        : base(
            owner,
            nameof(TerminalSettingsPanelState.FontFamilyName),
            nameof(TerminalSettingsPanelState.FontSize),
            nameof(TerminalSettingsPanelState.AutoScroll),
            nameof(TerminalSettingsPanelState.BackgroundOpacityEnabled))
    {
    }

    public string FontFamilyName
    {
        get => Owner.FontFamilyName;
        set => Owner.FontFamilyName = value;
    }

    public double FontSize
    {
        get => Owner.FontSize;
        set => Owner.FontSize = value;
    }

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
}
