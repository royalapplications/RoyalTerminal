// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;

namespace RoyalTerminal.Avalonia.Settings;

public sealed class TerminalSettingsSessionState : TerminalSettingsCategoryStateBase
{
    internal TerminalSettingsSessionState(TerminalSettingsPanelState owner)
        : base(
            owner,
            nameof(TerminalSettingsPanelState.SessionName),
            nameof(TerminalSettingsPanelState.SelectedTransportMode))
    {
    }

    public string SessionName
    {
        get => Owner.SessionName;
        set => Owner.SessionName = value;
    }

    public TerminalSettingsTransportModeOption? SelectedTransportMode
    {
        get => Owner.SelectedTransportMode;
        set => Owner.SelectedTransportMode = value;
    }

    public IReadOnlyList<TerminalSettingsTransportModeOption> TransportModes => Owner.TransportModes;
}
