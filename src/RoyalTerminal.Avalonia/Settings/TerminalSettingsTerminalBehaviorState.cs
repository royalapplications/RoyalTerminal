// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Settings;

public sealed class TerminalSettingsTerminalBehaviorState : TerminalSettingsCategoryStateBase
{
    internal TerminalSettingsTerminalBehaviorState(TerminalSettingsPanelState owner)
        : base(
            owner,
            nameof(TerminalSettingsPanelState.CopyOnSelectEnabled),
            nameof(TerminalSettingsPanelState.EnableBellNotifications),
            nameof(TerminalSettingsPanelState.BackspaceSendsControlH),
            nameof(TerminalSettingsPanelState.EnableTextShaping),
            nameof(TerminalSettingsPanelState.EnableLigatures),
            nameof(TerminalSettingsPanelState.SelectedPasteSafetyPolicy),
            nameof(TerminalSettingsPanelState.SshRequestPty),
            nameof(TerminalSettingsPanelState.SshTerminalType),
            nameof(TerminalSettingsPanelState.SshInitialCommand),
            nameof(TerminalSettingsPanelState.TelnetTerminalType),
            nameof(TerminalSettingsPanelState.TelnetInitialCommand))
    {
    }

    public bool CopyOnSelectEnabled
    {
        get => Owner.CopyOnSelectEnabled;
        set => Owner.CopyOnSelectEnabled = value;
    }

    public bool EnableBellNotifications
    {
        get => Owner.EnableBellNotifications;
        set => Owner.EnableBellNotifications = value;
    }

    public bool BackspaceSendsControlH
    {
        get => Owner.BackspaceSendsControlH;
        set => Owner.BackspaceSendsControlH = value;
    }

    public bool EnableTextShaping
    {
        get => Owner.EnableTextShaping;
        set => Owner.EnableTextShaping = value;
    }

    public bool EnableLigatures
    {
        get => Owner.EnableLigatures;
        set => Owner.EnableLigatures = value;
    }

    public TerminalPasteSafetyPolicy SelectedPasteSafetyPolicy
    {
        get => Owner.SelectedPasteSafetyPolicy;
        set => Owner.SelectedPasteSafetyPolicy = value;
    }

    public IReadOnlyList<TerminalPasteSafetyPolicy> PasteSafetyPolicies => Owner.PasteSafetyPolicies;

    public bool SshRequestPty
    {
        get => Owner.SshRequestPty;
        set => Owner.SshRequestPty = value;
    }

    public string SshTerminalType
    {
        get => Owner.SshTerminalType;
        set => Owner.SshTerminalType = value;
    }

    public string SshInitialCommand
    {
        get => Owner.SshInitialCommand;
        set => Owner.SshInitialCommand = value;
    }

    public string TelnetTerminalType
    {
        get => Owner.TelnetTerminalType;
        set => Owner.TelnetTerminalType = value;
    }

    public string TelnetInitialCommand
    {
        get => Owner.TelnetInitialCommand;
        set => Owner.TelnetInitialCommand = value;
    }
}
