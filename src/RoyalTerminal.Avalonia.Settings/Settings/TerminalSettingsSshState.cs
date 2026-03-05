// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Generic;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Settings;

public sealed class TerminalSettingsSshState : TerminalSettingsCategoryStateBase
{
    internal TerminalSettingsSshState(TerminalSettingsPanelState owner)
        : base(
            owner,
            nameof(TerminalSettingsPanelState.SelectedSshAuthMode),
            nameof(TerminalSettingsPanelState.SshPassword),
            nameof(TerminalSettingsPanelState.SshPrivateKeyPath),
            nameof(TerminalSettingsPanelState.SshExpectedHostKeyFingerprintSha256),
            nameof(TerminalSettingsPanelState.SelectedSshProxyType),
            nameof(TerminalSettingsPanelState.SshProxyHost),
            nameof(TerminalSettingsPanelState.SshProxyPort),
            nameof(TerminalSettingsPanelState.SshProxyUsername),
            nameof(TerminalSettingsPanelState.SshProxyPassword),
            nameof(TerminalSettingsPanelState.SshLocalPortForwardEnabled),
            nameof(TerminalSettingsPanelState.SshLocalPortForwardBindAddress),
            nameof(TerminalSettingsPanelState.SshLocalPortForwardSourcePort),
            nameof(TerminalSettingsPanelState.SshLocalPortForwardDestinationHost),
            nameof(TerminalSettingsPanelState.SshLocalPortForwardDestinationPort),
            nameof(TerminalSettingsPanelState.SshX11Enabled),
            nameof(TerminalSettingsPanelState.SshX11Display),
            nameof(TerminalSettingsPanelState.SshKeepAliveIntervalSeconds),
            nameof(TerminalSettingsPanelState.SshConnectTimeoutSeconds))
    {
    }

    public IReadOnlyList<TerminalSettingsSshAuthModeOption> SshAuthModes => Owner.SshAuthModes;

    public TerminalSettingsSshAuthModeOption? SelectedSshAuthMode
    {
        get => Owner.SelectedSshAuthMode;
        set => Owner.SelectedSshAuthMode = value;
    }

    public string SshPassword
    {
        get => Owner.SshPassword;
        set => Owner.SshPassword = value;
    }

    public string SshPrivateKeyPath
    {
        get => Owner.SshPrivateKeyPath;
        set => Owner.SshPrivateKeyPath = value;
    }

    public string SshExpectedHostKeyFingerprintSha256
    {
        get => Owner.SshExpectedHostKeyFingerprintSha256;
        set => Owner.SshExpectedHostKeyFingerprintSha256 = value;
    }

    public IReadOnlyList<SshProxyType> SshProxyTypes => Owner.SshProxyTypes;

    public SshProxyType SelectedSshProxyType
    {
        get => Owner.SelectedSshProxyType;
        set => Owner.SelectedSshProxyType = value;
    }

    public string SshProxyHost
    {
        get => Owner.SshProxyHost;
        set => Owner.SshProxyHost = value;
    }

    public string SshProxyPort
    {
        get => Owner.SshProxyPort;
        set => Owner.SshProxyPort = value;
    }

    public string SshProxyUsername
    {
        get => Owner.SshProxyUsername;
        set => Owner.SshProxyUsername = value;
    }

    public string SshProxyPassword
    {
        get => Owner.SshProxyPassword;
        set => Owner.SshProxyPassword = value;
    }

    public bool SshLocalPortForwardEnabled
    {
        get => Owner.SshLocalPortForwardEnabled;
        set => Owner.SshLocalPortForwardEnabled = value;
    }

    public string SshLocalPortForwardBindAddress
    {
        get => Owner.SshLocalPortForwardBindAddress;
        set => Owner.SshLocalPortForwardBindAddress = value;
    }

    public string SshLocalPortForwardSourcePort
    {
        get => Owner.SshLocalPortForwardSourcePort;
        set => Owner.SshLocalPortForwardSourcePort = value;
    }

    public string SshLocalPortForwardDestinationHost
    {
        get => Owner.SshLocalPortForwardDestinationHost;
        set => Owner.SshLocalPortForwardDestinationHost = value;
    }

    public string SshLocalPortForwardDestinationPort
    {
        get => Owner.SshLocalPortForwardDestinationPort;
        set => Owner.SshLocalPortForwardDestinationPort = value;
    }

    public bool SshX11Enabled
    {
        get => Owner.SshX11Enabled;
        set => Owner.SshX11Enabled = value;
    }

    public string SshX11Display
    {
        get => Owner.SshX11Display;
        set => Owner.SshX11Display = value;
    }

    public string SshKeepAliveIntervalSeconds
    {
        get => Owner.SshKeepAliveIntervalSeconds;
        set => Owner.SshKeepAliveIntervalSeconds = value;
    }

    public string SshConnectTimeoutSeconds
    {
        get => Owner.SshConnectTimeoutSeconds;
        set => Owner.SshConnectTimeoutSeconds = value;
    }
}
