// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Settings;

public sealed class TerminalSettingsLoggingState : TerminalSettingsCategoryStateBase
{
    internal TerminalSettingsLoggingState(TerminalSettingsPanelState owner)
        : base(
            owner,
            nameof(TerminalSettingsPanelState.SessionLoggingEnabled),
            nameof(TerminalSettingsPanelState.SessionLogFilePath),
            nameof(TerminalSettingsPanelState.SelectedSessionLogFormat),
            nameof(TerminalSettingsPanelState.SessionLogFlushFrequently),
            nameof(TerminalSettingsPanelState.EventLogEnabled))
    {
    }

    public bool SessionLoggingEnabled
    {
        get => Owner.SessionLoggingEnabled;
        set => Owner.SessionLoggingEnabled = value;
    }

    public string SessionLogFilePath
    {
        get => Owner.SessionLogFilePath;
        set => Owner.SessionLogFilePath = value;
    }

    public TerminalSessionLogFormat SelectedSessionLogFormat
    {
        get => Owner.SelectedSessionLogFormat;
        set => Owner.SelectedSessionLogFormat = value;
    }

    public bool SessionLogFlushFrequently
    {
        get => Owner.SessionLogFlushFrequently;
        set => Owner.SessionLogFlushFrequently = value;
    }

    public bool EventLogEnabled
    {
        get => Owner.EventLogEnabled;
        set => Owner.EventLogEnabled = value;
    }

    public IReadOnlyList<TerminalSessionLogFormat> SessionLogFormats => Owner.SessionLogFormats;
}
