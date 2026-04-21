// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - framework-agnostic settings panel state and profile CRUD model.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Settings;

public sealed record TerminalSettingsProfileItem(string Id, string DisplayName);

public sealed record TerminalSettingsTransportModeOption(string Id, string DisplayName);

/// <summary>
/// Font source option displayed by the terminal settings appearance panel.
/// </summary>
/// <param name="Source">Font source value.</param>
/// <param name="DisplayName">Human-readable display name.</param>
public sealed record TerminalSettingsFontSourceOption(TerminalFontSource Source, string DisplayName);

public sealed record TerminalSettingsSshAuthModeOption(string Id, string DisplayName)
{
    public const string PasswordModeId = "password";
    public const string PrivateKeyModeId = "private-key";
    public const string AgentModeId = "agent";
    public const string PasswordAndKeyModeId = "password-key";
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class TerminalSettingsPanelState : AvaloniaObject
{
    private const string DefaultPasswordSecretId = "ssh/password";
    private const string DefaultPrivateKeySecretId = "ssh/private-key";

    private readonly List<TerminalSessionProfile> _profiles = [];
    private bool _suppressDirtyTracking;
    private bool _suppressProfileSwitch;
    private string? _activeProfileId;

    public static readonly StyledProperty<TerminalSettingsProfileItem?> SelectedProfileProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalSettingsProfileItem?>(nameof(SelectedProfile), null);

    public static readonly StyledProperty<string> DefaultProfileIdProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(DefaultProfileId), string.Empty);

    public static readonly StyledProperty<bool> IsDirtyProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(IsDirty), false);

    public static readonly StyledProperty<string> LastOperationStatusProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(LastOperationStatus), string.Empty);

    public static readonly StyledProperty<string> SessionNameProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SessionName), "Default Session");

    public static readonly StyledProperty<TerminalSettingsTransportModeOption?> SelectedTransportModeProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalSettingsTransportModeOption?>(nameof(SelectedTransportMode), null);

    public static readonly StyledProperty<string> WorkingDirectoryProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(WorkingDirectory), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static readonly StyledProperty<string> ShellPathProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(ShellPath), string.Empty);

    public static readonly StyledProperty<string> PipeCommandTextProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(PipeCommandText), "echo RoyalTerminal pipe transport");

    public static readonly StyledProperty<bool> PipeMergeStdErrIntoStdOutProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(PipeMergeStdErrIntoStdOut), true);

    public static readonly StyledProperty<string> RawTcpHostProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(RawTcpHost), "localhost");

    public static readonly StyledProperty<string> RawTcpPortProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(RawTcpPort), "23");

    public static readonly StyledProperty<string> TelnetHostProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(TelnetHost), "localhost");

    public static readonly StyledProperty<string> TelnetPortProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(TelnetPort), "23");

    public static readonly StyledProperty<string> TelnetTerminalTypeProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(TelnetTerminalType), "xterm");

    public static readonly StyledProperty<string> TelnetInitialCommandProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(TelnetInitialCommand), string.Empty);

    public static readonly StyledProperty<string> SerialPortNameProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SerialPortName), OperatingSystem.IsWindows() ? "COM1" : "/dev/ttyUSB0");

    public static readonly StyledProperty<string> SerialBaudRateProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SerialBaudRate), "9600");

    public static readonly StyledProperty<string> SerialDataBitsProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SerialDataBits), "8");

    public static readonly StyledProperty<TerminalSerialParity> SelectedSerialParityProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalSerialParity>(nameof(SelectedSerialParity), TerminalSerialParity.None);

    public static readonly StyledProperty<TerminalSerialStopBits> SelectedSerialStopBitsProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalSerialStopBits>(nameof(SelectedSerialStopBits), TerminalSerialStopBits.One);

    public static readonly StyledProperty<TerminalSerialHandshake> SelectedSerialHandshakeProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalSerialHandshake>(nameof(SelectedSerialHandshake), TerminalSerialHandshake.None);

    public static readonly StyledProperty<string> SerialNewLineProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SerialNewLine), "\n");

    public static readonly StyledProperty<string> SshHostProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshHost), "localhost");

    public static readonly StyledProperty<string> SshPortProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshPort), "22");

    public static readonly StyledProperty<string> SshUsernameProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshUsername), Environment.UserName);

    public static readonly StyledProperty<TerminalSettingsSshAuthModeOption?> SelectedSshAuthModeProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalSettingsSshAuthModeOption?>(nameof(SelectedSshAuthMode), null);

    public static readonly StyledProperty<string> SshPasswordProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshPassword), string.Empty);

    public static readonly StyledProperty<string> SshPrivateKeyPathProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshPrivateKeyPath), string.Empty);

    public static readonly StyledProperty<string> SshExpectedHostKeyFingerprintSha256Property =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshExpectedHostKeyFingerprintSha256), string.Empty);

    public static readonly StyledProperty<bool> SshRequestPtyProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(SshRequestPty), true);

    public static readonly StyledProperty<string> SshTerminalTypeProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshTerminalType), "xterm-256color");

    public static readonly StyledProperty<string> SshInitialCommandProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshInitialCommand), string.Empty);

    public static readonly StyledProperty<SshProxyType> SelectedSshProxyTypeProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, SshProxyType>(nameof(SelectedSshProxyType), SshProxyType.None);

    public static readonly StyledProperty<string> SshProxyHostProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshProxyHost), string.Empty);

    public static readonly StyledProperty<string> SshProxyPortProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshProxyPort), "1080");

    public static readonly StyledProperty<string> SshProxyUsernameProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshProxyUsername), string.Empty);

    public static readonly StyledProperty<string> SshProxyPasswordProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshProxyPassword), string.Empty);

    public static readonly StyledProperty<bool> SshLocalPortForwardEnabledProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(SshLocalPortForwardEnabled), false);

    public static readonly StyledProperty<string> SshLocalPortForwardBindAddressProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshLocalPortForwardBindAddress), "127.0.0.1");

    public static readonly StyledProperty<string> SshLocalPortForwardSourcePortProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshLocalPortForwardSourcePort), "15432");

    public static readonly StyledProperty<string> SshLocalPortForwardDestinationHostProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshLocalPortForwardDestinationHost), "db.internal");

    public static readonly StyledProperty<string> SshLocalPortForwardDestinationPortProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshLocalPortForwardDestinationPort), "5432");

    public static readonly StyledProperty<bool> SshX11EnabledProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(SshX11Enabled), false);

    public static readonly StyledProperty<string> SshX11DisplayProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshX11Display), ":0");

    public static readonly StyledProperty<string> SshKeepAliveIntervalSecondsProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshKeepAliveIntervalSeconds), "30");

    public static readonly StyledProperty<string> SshConnectTimeoutSecondsProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SshConnectTimeoutSeconds), "15");

    public static readonly StyledProperty<bool> CopyOnSelectEnabledProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(CopyOnSelectEnabled), false);

    public static readonly StyledProperty<bool> EnableBellNotificationsProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(EnableBellNotifications), true);

    public static readonly StyledProperty<bool> BackspaceSendsControlHProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(BackspaceSendsControlH), false);

    public static readonly StyledProperty<bool> EnableTextShapingProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(EnableTextShaping), true);

    public static readonly StyledProperty<bool> EnableLigaturesProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(EnableLigatures), false);

    public static readonly StyledProperty<TerminalPasteSafetyPolicy> SelectedPasteSafetyPolicyProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalPasteSafetyPolicy>(nameof(SelectedPasteSafetyPolicy), TerminalPasteSafetyPolicy.None);

    public static readonly StyledProperty<TerminalFontSource> SelectedFontSourceProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalFontSource>(nameof(SelectedFontSource), TerminalFontSource.System);

    public static readonly StyledProperty<string> FontFamilyNameProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(FontFamilyName), GetDefaultMonospaceFont());

    public static readonly StyledProperty<string> FontFilePathProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(FontFilePath), string.Empty);

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, double>(nameof(FontSize), 14.0);

    public static readonly StyledProperty<bool> IsSystemFontSourceSelectedProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(IsSystemFontSourceSelected), true);

    public static readonly StyledProperty<bool> IsFileFontSourceSelectedProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(IsFileFontSourceSelected), false);

    public static readonly StyledProperty<bool> AutoScrollProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(AutoScroll), true);

    public static readonly StyledProperty<bool> BackgroundOpacityEnabledProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(BackgroundOpacityEnabled), false);

    public static readonly StyledProperty<bool> SessionLoggingEnabledProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(SessionLoggingEnabled), false);

    public static readonly StyledProperty<string> SessionLogFilePathProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, string>(nameof(SessionLogFilePath), GetDefaultSessionLogPath());

    public static readonly StyledProperty<TerminalSessionLogFormat> SelectedSessionLogFormatProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, TerminalSessionLogFormat>(nameof(SelectedSessionLogFormat), TerminalSessionLogFormat.PlainText);

    public static readonly StyledProperty<bool> SessionLogFlushFrequentlyProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(SessionLogFlushFrequently), true);

    public static readonly StyledProperty<bool> EventLogEnabledProperty =
        AvaloniaProperty.Register<TerminalSettingsPanelState, bool>(nameof(EventLogEnabled), true);

    public TerminalSettingsPanelState()
    {
        Profiles = [];

        TransportModes =
        [
            new TerminalSettingsTransportModeOption(TerminalTransportIds.Pty, "PTY"),
            new TerminalSettingsTransportModeOption(TerminalTransportIds.Pipe, "Pipe"),
            new TerminalSettingsTransportModeOption(TerminalTransportIds.RawTcp, "Raw TCP"),
            new TerminalSettingsTransportModeOption(TerminalTransportIds.Telnet, "Telnet"),
            new TerminalSettingsTransportModeOption(TerminalTransportIds.Serial, "Serial"),
            new TerminalSettingsTransportModeOption(TerminalTransportIds.Ssh, "SSH"),
        ];

        SshAuthModes =
        [
            new TerminalSettingsSshAuthModeOption(TerminalSettingsSshAuthModeOption.PasswordModeId, "Password"),
            new TerminalSettingsSshAuthModeOption(TerminalSettingsSshAuthModeOption.PrivateKeyModeId, "Private Key"),
            new TerminalSettingsSshAuthModeOption(TerminalSettingsSshAuthModeOption.AgentModeId, "Agent"),
            new TerminalSettingsSshAuthModeOption(TerminalSettingsSshAuthModeOption.PasswordAndKeyModeId, "Password + Key"),
        ];

        SerialParityOptions = Enum.GetValues<TerminalSerialParity>();
        SerialStopBitsOptions = Enum.GetValues<TerminalSerialStopBits>();
        SerialHandshakeOptions = Enum.GetValues<TerminalSerialHandshake>();
        SshProxyTypes = Enum.GetValues<SshProxyType>();
        PasteSafetyPolicies = Enum.GetValues<TerminalPasteSafetyPolicy>();
        SessionLogFormats = Enum.GetValues<TerminalSessionLogFormat>();
        FontSources =
        [
            new TerminalSettingsFontSourceOption(TerminalFontSource.System, "System Font"),
            new TerminalSettingsFontSourceOption(TerminalFontSource.File, "Font File"),
        ];
        SystemFontFamilies = CreateSystemFontFamilies();

        Session = new TerminalSettingsSessionState(this);
        Connection = new TerminalSettingsConnectionState(this);
        TerminalBehavior = new TerminalSettingsTerminalBehaviorState(this);
        Appearance = new TerminalSettingsAppearanceState(this);
        Ssh = new TerminalSettingsSshState(this);
        Logging = new TerminalSettingsLoggingState(this);

        NewProfileCommand = new RelayCommand(CreateProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateProfile, CanModifySelectedProfile);
        DeleteProfileCommand = new RelayCommand(DeleteSelectedProfile, CanDeleteSelectedProfile);
        SetDefaultProfileCommand = new RelayCommand(SetSelectedAsDefault, CanModifySelectedProfile);
        ApplyCommand = new RelayCommand(Apply, CanModifySelectedProfile);
        SaveCommand = new RelayCommand(Save, CanSaveDocument);
        BrowseFontFileCommand = new RelayCommand(RequestBrowseFontFile);

        SelectedTransportMode = TransportModes[0];
        SelectedSshAuthMode = SshAuthModes[0];

        LoadDocument(new TerminalSessionProfilesDocument());
    }

    public event EventHandler? ApplyRequested;

    public event EventHandler? SaveRequested;

    public event EventHandler? BrowseFontFileRequested;

    public AvaloniaList<TerminalSettingsProfileItem> Profiles { get; }

    public IReadOnlyList<TerminalSettingsTransportModeOption> TransportModes { get; }

    public IReadOnlyList<TerminalSettingsSshAuthModeOption> SshAuthModes { get; }

    public IReadOnlyList<TerminalSerialParity> SerialParityOptions { get; }

    public IReadOnlyList<TerminalSerialStopBits> SerialStopBitsOptions { get; }

    public IReadOnlyList<TerminalSerialHandshake> SerialHandshakeOptions { get; }

    public IReadOnlyList<SshProxyType> SshProxyTypes { get; }

    public IReadOnlyList<TerminalPasteSafetyPolicy> PasteSafetyPolicies { get; }

    public IReadOnlyList<TerminalSessionLogFormat> SessionLogFormats { get; }

    public IReadOnlyList<TerminalSettingsFontSourceOption> FontSources { get; }

    public AvaloniaList<string> SystemFontFamilies { get; }

    public TerminalSettingsSessionState Session { get; }

    public TerminalSettingsConnectionState Connection { get; }

    public TerminalSettingsTerminalBehaviorState TerminalBehavior { get; }

    public TerminalSettingsAppearanceState Appearance { get; }

    public TerminalSettingsSshState Ssh { get; }

    public TerminalSettingsLoggingState Logging { get; }

    public ICommand NewProfileCommand { get; }

    public ICommand DuplicateProfileCommand { get; }

    public ICommand DeleteProfileCommand { get; }

    public ICommand SetDefaultProfileCommand { get; }

    public ICommand ApplyCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand BrowseFontFileCommand { get; }

    public TerminalSettingsProfileItem? SelectedProfile
    {
        get => GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    public string DefaultProfileId
    {
        get => GetValue(DefaultProfileIdProperty);
        set => SetValue(DefaultProfileIdProperty, value);
    }

    public bool IsDirty
    {
        get => GetValue(IsDirtyProperty);
        private set => SetValue(IsDirtyProperty, value);
    }

    public string LastOperationStatus
    {
        get => GetValue(LastOperationStatusProperty);
        private set => SetValue(LastOperationStatusProperty, value);
    }

    public string SessionName
    {
        get => GetValue(SessionNameProperty);
        set => SetValue(SessionNameProperty, value);
    }

    public TerminalSettingsTransportModeOption? SelectedTransportMode
    {
        get => GetValue(SelectedTransportModeProperty);
        set => SetValue(SelectedTransportModeProperty, value);
    }

    public string WorkingDirectory
    {
        get => GetValue(WorkingDirectoryProperty);
        set => SetValue(WorkingDirectoryProperty, value);
    }

    public string ShellPath
    {
        get => GetValue(ShellPathProperty);
        set => SetValue(ShellPathProperty, value);
    }

    public string PipeCommandText
    {
        get => GetValue(PipeCommandTextProperty);
        set => SetValue(PipeCommandTextProperty, value);
    }

    public bool PipeMergeStdErrIntoStdOut
    {
        get => GetValue(PipeMergeStdErrIntoStdOutProperty);
        set => SetValue(PipeMergeStdErrIntoStdOutProperty, value);
    }

    public string RawTcpHost
    {
        get => GetValue(RawTcpHostProperty);
        set => SetValue(RawTcpHostProperty, value);
    }

    public string RawTcpPort
    {
        get => GetValue(RawTcpPortProperty);
        set => SetValue(RawTcpPortProperty, value);
    }

    public string TelnetHost
    {
        get => GetValue(TelnetHostProperty);
        set => SetValue(TelnetHostProperty, value);
    }

    public string TelnetPort
    {
        get => GetValue(TelnetPortProperty);
        set => SetValue(TelnetPortProperty, value);
    }

    public string TelnetTerminalType
    {
        get => GetValue(TelnetTerminalTypeProperty);
        set => SetValue(TelnetTerminalTypeProperty, value);
    }

    public string TelnetInitialCommand
    {
        get => GetValue(TelnetInitialCommandProperty);
        set => SetValue(TelnetInitialCommandProperty, value);
    }

    public string SerialPortName
    {
        get => GetValue(SerialPortNameProperty);
        set => SetValue(SerialPortNameProperty, value);
    }

    public string SerialBaudRate
    {
        get => GetValue(SerialBaudRateProperty);
        set => SetValue(SerialBaudRateProperty, value);
    }

    public string SerialDataBits
    {
        get => GetValue(SerialDataBitsProperty);
        set => SetValue(SerialDataBitsProperty, value);
    }

    public TerminalSerialParity SelectedSerialParity
    {
        get => GetValue(SelectedSerialParityProperty);
        set => SetValue(SelectedSerialParityProperty, value);
    }

    public TerminalSerialStopBits SelectedSerialStopBits
    {
        get => GetValue(SelectedSerialStopBitsProperty);
        set => SetValue(SelectedSerialStopBitsProperty, value);
    }

    public TerminalSerialHandshake SelectedSerialHandshake
    {
        get => GetValue(SelectedSerialHandshakeProperty);
        set => SetValue(SelectedSerialHandshakeProperty, value);
    }

    public string SerialNewLine
    {
        get => GetValue(SerialNewLineProperty);
        set => SetValue(SerialNewLineProperty, value);
    }

    public string SshHost
    {
        get => GetValue(SshHostProperty);
        set => SetValue(SshHostProperty, value);
    }

    public string SshPort
    {
        get => GetValue(SshPortProperty);
        set => SetValue(SshPortProperty, value);
    }

    public string SshUsername
    {
        get => GetValue(SshUsernameProperty);
        set => SetValue(SshUsernameProperty, value);
    }

    public TerminalSettingsSshAuthModeOption? SelectedSshAuthMode
    {
        get => GetValue(SelectedSshAuthModeProperty);
        set => SetValue(SelectedSshAuthModeProperty, value);
    }

    public string SshPassword
    {
        get => GetValue(SshPasswordProperty);
        set => SetValue(SshPasswordProperty, value);
    }

    public string SshPrivateKeyPath
    {
        get => GetValue(SshPrivateKeyPathProperty);
        set => SetValue(SshPrivateKeyPathProperty, value);
    }

    public string SshExpectedHostKeyFingerprintSha256
    {
        get => GetValue(SshExpectedHostKeyFingerprintSha256Property);
        set => SetValue(SshExpectedHostKeyFingerprintSha256Property, value);
    }

    public bool SshRequestPty
    {
        get => GetValue(SshRequestPtyProperty);
        set => SetValue(SshRequestPtyProperty, value);
    }

    public string SshTerminalType
    {
        get => GetValue(SshTerminalTypeProperty);
        set => SetValue(SshTerminalTypeProperty, value);
    }

    public string SshInitialCommand
    {
        get => GetValue(SshInitialCommandProperty);
        set => SetValue(SshInitialCommandProperty, value);
    }

    public SshProxyType SelectedSshProxyType
    {
        get => GetValue(SelectedSshProxyTypeProperty);
        set => SetValue(SelectedSshProxyTypeProperty, value);
    }

    public string SshProxyHost
    {
        get => GetValue(SshProxyHostProperty);
        set => SetValue(SshProxyHostProperty, value);
    }

    public string SshProxyPort
    {
        get => GetValue(SshProxyPortProperty);
        set => SetValue(SshProxyPortProperty, value);
    }

    public string SshProxyUsername
    {
        get => GetValue(SshProxyUsernameProperty);
        set => SetValue(SshProxyUsernameProperty, value);
    }

    public string SshProxyPassword
    {
        get => GetValue(SshProxyPasswordProperty);
        set => SetValue(SshProxyPasswordProperty, value);
    }

    public bool SshLocalPortForwardEnabled
    {
        get => GetValue(SshLocalPortForwardEnabledProperty);
        set => SetValue(SshLocalPortForwardEnabledProperty, value);
    }

    public string SshLocalPortForwardBindAddress
    {
        get => GetValue(SshLocalPortForwardBindAddressProperty);
        set => SetValue(SshLocalPortForwardBindAddressProperty, value);
    }

    public string SshLocalPortForwardSourcePort
    {
        get => GetValue(SshLocalPortForwardSourcePortProperty);
        set => SetValue(SshLocalPortForwardSourcePortProperty, value);
    }

    public string SshLocalPortForwardDestinationHost
    {
        get => GetValue(SshLocalPortForwardDestinationHostProperty);
        set => SetValue(SshLocalPortForwardDestinationHostProperty, value);
    }

    public string SshLocalPortForwardDestinationPort
    {
        get => GetValue(SshLocalPortForwardDestinationPortProperty);
        set => SetValue(SshLocalPortForwardDestinationPortProperty, value);
    }

    public bool SshX11Enabled
    {
        get => GetValue(SshX11EnabledProperty);
        set => SetValue(SshX11EnabledProperty, value);
    }

    public string SshX11Display
    {
        get => GetValue(SshX11DisplayProperty);
        set => SetValue(SshX11DisplayProperty, value);
    }

    public string SshKeepAliveIntervalSeconds
    {
        get => GetValue(SshKeepAliveIntervalSecondsProperty);
        set => SetValue(SshKeepAliveIntervalSecondsProperty, value);
    }

    public string SshConnectTimeoutSeconds
    {
        get => GetValue(SshConnectTimeoutSecondsProperty);
        set => SetValue(SshConnectTimeoutSecondsProperty, value);
    }

    public bool CopyOnSelectEnabled
    {
        get => GetValue(CopyOnSelectEnabledProperty);
        set => SetValue(CopyOnSelectEnabledProperty, value);
    }

    public bool EnableBellNotifications
    {
        get => GetValue(EnableBellNotificationsProperty);
        set => SetValue(EnableBellNotificationsProperty, value);
    }

    public bool BackspaceSendsControlH
    {
        get => GetValue(BackspaceSendsControlHProperty);
        set => SetValue(BackspaceSendsControlHProperty, value);
    }

    public bool EnableTextShaping
    {
        get => GetValue(EnableTextShapingProperty);
        set => SetValue(EnableTextShapingProperty, value);
    }

    public bool EnableLigatures
    {
        get => GetValue(EnableLigaturesProperty);
        set => SetValue(EnableLigaturesProperty, value);
    }

    public TerminalPasteSafetyPolicy SelectedPasteSafetyPolicy
    {
        get => GetValue(SelectedPasteSafetyPolicyProperty);
        set => SetValue(SelectedPasteSafetyPolicyProperty, value);
    }

    public TerminalFontSource SelectedFontSource
    {
        get => GetValue(SelectedFontSourceProperty);
        set => SetValue(SelectedFontSourceProperty, value);
    }

    public string FontFamilyName
    {
        get => GetValue(FontFamilyNameProperty);
        set => SetValue(FontFamilyNameProperty, value);
    }

    public string FontFilePath
    {
        get => GetValue(FontFilePathProperty);
        set => SetValue(FontFilePathProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public bool IsSystemFontSourceSelected
    {
        get => GetValue(IsSystemFontSourceSelectedProperty);
        private set => SetValue(IsSystemFontSourceSelectedProperty, value);
    }

    public bool IsFileFontSourceSelected
    {
        get => GetValue(IsFileFontSourceSelectedProperty);
        private set => SetValue(IsFileFontSourceSelectedProperty, value);
    }

    public bool AutoScroll
    {
        get => GetValue(AutoScrollProperty);
        set => SetValue(AutoScrollProperty, value);
    }

    public bool BackgroundOpacityEnabled
    {
        get => GetValue(BackgroundOpacityEnabledProperty);
        set => SetValue(BackgroundOpacityEnabledProperty, value);
    }

    public bool SessionLoggingEnabled
    {
        get => GetValue(SessionLoggingEnabledProperty);
        set => SetValue(SessionLoggingEnabledProperty, value);
    }

    public string SessionLogFilePath
    {
        get => GetValue(SessionLogFilePathProperty);
        set => SetValue(SessionLogFilePathProperty, value);
    }

    public TerminalSessionLogFormat SelectedSessionLogFormat
    {
        get => GetValue(SelectedSessionLogFormatProperty);
        set => SetValue(SelectedSessionLogFormatProperty, value);
    }

    public bool SessionLogFlushFrequently
    {
        get => GetValue(SessionLogFlushFrequentlyProperty);
        set => SetValue(SessionLogFlushFrequentlyProperty, value);
    }

    public bool EventLogEnabled
    {
        get => GetValue(EventLogEnabledProperty);
        set => SetValue(EventLogEnabledProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedProfileProperty && !_suppressProfileSwitch)
        {
            OnSelectedProfileChanged((TerminalSettingsProfileItem?)change.NewValue);
            return;
        }

        if (change.Property == SelectedFontSourceProperty)
        {
            UpdateFontSourceFlags((TerminalFontSource)change.NewValue!);
        }

        if (_suppressDirtyTracking)
        {
            return;
        }

        if (change.Property != IsDirtyProperty &&
            change.Property != LastOperationStatusProperty &&
            change.Property != SelectedProfileProperty &&
            change.Property != IsSystemFontSourceSelectedProperty &&
            change.Property != IsFileFontSourceSelectedProperty)
        {
            IsDirty = true;
        }
    }

    public void LoadDocument(TerminalSessionProfilesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        TerminalSessionProfilesDocument normalized = TerminalSessionProfileSerializer.FromJson(
            TerminalSessionProfileSerializer.ToJson(document));

        _profiles.Clear();
        _profiles.AddRange(normalized.Profiles);

        if (_profiles.Count == 0)
        {
            _profiles.Add(CreateDefaultProfile("default", "Default Session"));
        }

        string selectedId = !string.IsNullOrWhiteSpace(normalized.DefaultProfileId)
            ? normalized.DefaultProfileId!
            : _profiles[0].Id;

        if (FindProfileIndex(selectedId) < 0)
        {
            selectedId = _profiles[0].Id;
        }

        DefaultProfileId = selectedId;
        RefreshProfileItems(selectedId);
        LoadProfileIntoEditor(selectedId);
        _activeProfileId = selectedId;
        IsDirty = false;
        LastOperationStatus = "Loaded settings profiles.";
        UpdateCommandStates();
    }

    public TerminalSessionProfilesDocument BuildDocument()
    {
        CaptureEditorIntoSelectedProfile();
        return new TerminalSessionProfilesDocument
        {
            FormatVersion = TerminalSessionProfilesDocument.CurrentFormatVersion,
            DefaultProfileId = ResolveDefaultProfileId(),
            Profiles = new List<TerminalSessionProfile>(_profiles),
        };
    }

    public void MarkSaved(string? statusMessage = null)
    {
        IsDirty = false;
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            LastOperationStatus = statusMessage;
        }

        UpdateCommandStates();
    }

    public void SetStatus(string statusMessage)
    {
        LastOperationStatus = statusMessage ?? string.Empty;
    }

    /// <summary>
    /// Loads a font file selection into the editor state.
    /// </summary>
    /// <param name="fontFilePath">Local font file path.</param>
    public void LoadFontFile(string fontFilePath)
    {
        string? normalizedPath = NormalizeOptional(fontFilePath);
        if (normalizedPath is null)
        {
            return;
        }

        FontFilePath = normalizedPath;
        FontFamilyName = TerminalFontCatalog.TryGetFontFamilyNameFromFile(normalizedPath)
            ?? Path.GetFileNameWithoutExtension(normalizedPath);
        SelectedFontSource = TerminalFontSource.File;
        LastOperationStatus = $"Loaded font file '{Path.GetFileName(normalizedPath)}'.";
    }

    public void UpdateFromRuntime(Action<TerminalSettingsPanelState> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        bool previousSuppress = _suppressDirtyTracking;
        _suppressDirtyTracking = true;
        try
        {
            updateAction(this);
        }
        finally
        {
            _suppressDirtyTracking = previousSuppress;
        }
    }

    private void OnSelectedProfileChanged(TerminalSettingsProfileItem? next)
    {
        if (_activeProfileId is not null)
        {
            CaptureEditorIntoProfile(_activeProfileId);
        }

        if (next is null)
        {
            _activeProfileId = null;
            UpdateCommandStates();
            return;
        }

        LoadProfileIntoEditor(next.Id);
        _activeProfileId = next.Id;
        UpdateCommandStates();
    }

    private void CreateProfile()
    {
        CaptureEditorIntoSelectedProfile();

        TerminalSessionProfile template = _activeProfileId is not null && FindProfileIndex(_activeProfileId) is int activeIndex && activeIndex >= 0
            ? _profiles[activeIndex]
            : CreateDefaultProfile("default", "Default Session");

        string id = GenerateUniqueProfileId("profile");
        string displayName = GenerateUniqueProfileName("Profile");
        TerminalSessionProfile created = template with
        {
            Id = id,
            DisplayName = displayName,
        };

        _profiles.Add(created);
        if (string.IsNullOrWhiteSpace(DefaultProfileId))
        {
            DefaultProfileId = id;
        }

        RefreshProfileItems(id);
        LoadProfileIntoEditor(id);
        _activeProfileId = id;
        IsDirty = true;
        LastOperationStatus = $"Created profile '{displayName}'.";
        UpdateCommandStates();
    }

    private void DuplicateProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        CaptureEditorIntoSelectedProfile();

        int index = FindProfileIndex(SelectedProfile.Id);
        if (index < 0)
        {
            return;
        }

        TerminalSessionProfile source = _profiles[index];
        string id = GenerateUniqueProfileId(source.Id);
        string displayName = GenerateUniqueProfileName(source.DisplayName + " Copy");
        TerminalSessionProfile duplicate = source with
        {
            Id = id,
            DisplayName = displayName,
        };

        _profiles.Add(duplicate);
        RefreshProfileItems(id);
        LoadProfileIntoEditor(id);
        _activeProfileId = id;
        IsDirty = true;
        LastOperationStatus = $"Duplicated profile as '{displayName}'.";
        UpdateCommandStates();
    }

    private void DeleteSelectedProfile()
    {
        if (SelectedProfile is null || _profiles.Count <= 1)
        {
            return;
        }

        string id = SelectedProfile.Id;
        int index = FindProfileIndex(id);
        if (index < 0)
        {
            return;
        }

        _profiles.RemoveAt(index);
        if (string.Equals(DefaultProfileId, id, StringComparison.Ordinal))
        {
            DefaultProfileId = _profiles[0].Id;
        }

        string nextId = index < _profiles.Count ? _profiles[index].Id : _profiles[_profiles.Count - 1].Id;
        RefreshProfileItems(nextId);
        LoadProfileIntoEditor(nextId);
        _activeProfileId = nextId;
        IsDirty = true;
        LastOperationStatus = "Deleted selected profile.";
        UpdateCommandStates();
    }

    private void SetSelectedAsDefault()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        DefaultProfileId = SelectedProfile.Id;
        IsDirty = true;
        LastOperationStatus = $"Default profile set to '{SelectedProfile.DisplayName}'.";
        UpdateCommandStates();
    }

    private void Apply()
    {
        CaptureEditorIntoSelectedProfile();
        ApplyRequested?.Invoke(this, EventArgs.Empty);
        LastOperationStatus = "Applied settings to runtime.";
        UpdateCommandStates();
    }

    private void Save()
    {
        CaptureEditorIntoSelectedProfile();
        SaveRequested?.Invoke(this, EventArgs.Empty);
        UpdateCommandStates();
    }

    private void RequestBrowseFontFile()
    {
        BrowseFontFileRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanModifySelectedProfile() => SelectedProfile is not null;

    private bool CanDeleteSelectedProfile() => SelectedProfile is not null && _profiles.Count > 1;

    private bool CanSaveDocument() => _profiles.Count > 0 && SelectedProfile is not null;

    private void CaptureEditorIntoSelectedProfile()
    {
        if (_activeProfileId is null && SelectedProfile is not null)
        {
            _activeProfileId = SelectedProfile.Id;
        }

        if (_activeProfileId is not null)
        {
            CaptureEditorIntoProfile(_activeProfileId);
        }
    }

    private void CaptureEditorIntoProfile(string profileId)
    {
        int index = FindProfileIndex(profileId);
        if (index < 0)
        {
            return;
        }

        TerminalSessionProfile source = _profiles[index];
        string normalizedDisplayName = NormalizeDisplayName(SessionName, source.Id);

        TerminalSessionSshAuthenticationSettings authentication = BuildSshAuthenticationSettings();
        SshProxyOptions? proxy = BuildSshProxyOptions();
        List<SshPortForwardOptions> portForwardings = BuildSshPortForwardings();
        SshX11Options? x11 = BuildSshX11Options();
        SshPolicyOptions policy = BuildSshPolicyOptions();

        TerminalSessionProfile updated = source with
        {
            DisplayName = normalizedDisplayName,
            Appearance = source.Appearance with
            {
                FontSource = SelectedFontSource == TerminalFontSource.File && NormalizeOptional(FontFilePath) is not null
                    ? TerminalFontSource.File
                    : TerminalFontSource.System,
                FontFamilyName = NormalizeOptional(FontFamilyName) ?? GetDefaultMonospaceFont(),
                FontFilePath = SelectedFontSource == TerminalFontSource.File
                    ? NormalizeOptional(FontFilePath)
                    : null,
                FontSize = FontSize > 0 ? FontSize : 14.0,
                AutoScroll = AutoScroll,
                BackgroundOpacityEnabled = BackgroundOpacityEnabled,
            },
            Behavior = source.Behavior with
            {
                CopyOnSelectEnabled = CopyOnSelectEnabled,
                EnableBellNotifications = EnableBellNotifications,
                BackspaceSendsControlH = BackspaceSendsControlH,
                EnableTextShaping = EnableTextShaping,
                EnableLigatures = EnableLigatures,
                PasteSafetyPolicy = SelectedPasteSafetyPolicy.ToString(),
            },
            Logging = source.Logging with
            {
                Enabled = SessionLoggingEnabled,
                FilePath = NormalizeOptional(SessionLogFilePath),
                Format = SelectedSessionLogFormat,
                FlushFrequently = SessionLogFlushFrequently,
                EventLogEnabled = EventLogEnabled,
            },
            Transport = source.Transport with
            {
                TransportId = SelectedTransportMode?.Id ?? TerminalTransportIds.Pty,
                Pty = source.Transport.Pty with
                {
                    ShellPath = NormalizeOptional(ShellPath),
                    WorkingDirectory = NormalizeOptional(WorkingDirectory),
                },
                Pipe = source.Transport.Pipe with
                {
                    FileName = NormalizeOptional(PipeCommandText) ?? "echo RoyalTerminal pipe transport",
                    WorkingDirectory = NormalizeOptional(WorkingDirectory),
                    MergeStdErrIntoStdOut = PipeMergeStdErrIntoStdOut,
                },
                RawTcp = source.Transport.RawTcp with
                {
                    Host = NormalizeOptional(RawTcpHost) ?? "localhost",
                    Port = ParsePort(RawTcpPort, 23),
                },
                Telnet = source.Transport.Telnet with
                {
                    Host = NormalizeOptional(TelnetHost) ?? "localhost",
                    Port = ParsePort(TelnetPort, 23),
                    TerminalType = NormalizeOptional(TelnetTerminalType) ?? "xterm",
                    InitialCommand = NormalizeOptional(TelnetInitialCommand),
                },
                Serial = source.Transport.Serial with
                {
                    PortName = NormalizeOptional(SerialPortName) ?? (OperatingSystem.IsWindows() ? "COM1" : "/dev/ttyUSB0"),
                    BaudRate = ParsePositiveInt(SerialBaudRate, 9600),
                    DataBits = ParseIntInRange(SerialDataBits, 5, 8, 8),
                    Parity = SelectedSerialParity,
                    StopBits = SelectedSerialStopBits,
                    Handshake = SelectedSerialHandshake,
                    NewLine = string.IsNullOrEmpty(SerialNewLine) ? "\n" : SerialNewLine,
                },
                Ssh = source.Transport.Ssh with
                {
                    Host = NormalizeOptional(SshHost) ?? "localhost",
                    Port = ParsePort(SshPort, 22),
                    Username = NormalizeOptional(SshUsername) ?? Environment.UserName,
                    RequestPty = SshRequestPty,
                    TerminalType = NormalizeOptional(SshTerminalType) ?? "xterm-256color",
                    InitialCommand = NormalizeOptional(SshInitialCommand),
                    ExpectedHostKeyFingerprintSha256 = NormalizeOptional(SshExpectedHostKeyFingerprintSha256),
                    Authentication = authentication,
                    Proxy = proxy,
                    PortForwardings = portForwardings,
                    X11 = x11,
                    Policy = policy,
                },
            },
        };

        _profiles[index] = updated;
        RefreshProfileItems(profileId);
        _activeProfileId = profileId;
        if (SelectedProfile is not null)
        {
            SessionName = SelectedProfile.DisplayName;
        }
    }

    private void LoadProfileIntoEditor(string profileId)
    {
        int index = FindProfileIndex(profileId);
        if (index < 0)
        {
            return;
        }

        TerminalSessionProfile profile = _profiles[index];
        TerminalSessionTransportProfile transport = profile.Transport;
        TerminalSessionSshSettings ssh = transport.Ssh;
        TerminalSessionSshAuthenticationSettings auth = ssh.Authentication;

        TerminalSettingsSshAuthModeOption authMode = ResolveSshAuthMode(auth);
        TerminalSettingsTransportModeOption transportMode = ResolveTransportMode(transport.TransportId);

        SshProxyOptions? proxy = ssh.Proxy;
        SshPortForwardOptions? localForward = ResolveLocalPortForward(ssh.PortForwardings);

        bool previousSuppress = _suppressDirtyTracking;
        _suppressDirtyTracking = true;
        try
        {
            SessionName = profile.DisplayName;
            SelectedTransportMode = transportMode;

            WorkingDirectory = transportMode.Id switch
            {
                var id when string.Equals(id, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase)
                    => transport.Pipe.WorkingDirectory ?? string.Empty,
                _ => transport.Pty.WorkingDirectory ?? string.Empty,
            };
            ShellPath = transport.Pty.ShellPath ?? string.Empty;
            PipeCommandText = transport.Pipe.FileName;
            PipeMergeStdErrIntoStdOut = transport.Pipe.MergeStdErrIntoStdOut;

            RawTcpHost = transport.RawTcp.Host;
            RawTcpPort = transport.RawTcp.Port.ToString(CultureInfo.InvariantCulture);

            TelnetHost = transport.Telnet.Host;
            TelnetPort = transport.Telnet.Port.ToString(CultureInfo.InvariantCulture);
            TelnetTerminalType = transport.Telnet.TerminalType;
            TelnetInitialCommand = transport.Telnet.InitialCommand ?? string.Empty;

            SerialPortName = transport.Serial.PortName;
            SerialBaudRate = transport.Serial.BaudRate.ToString(CultureInfo.InvariantCulture);
            SerialDataBits = transport.Serial.DataBits.ToString(CultureInfo.InvariantCulture);
            SelectedSerialParity = transport.Serial.Parity;
            SelectedSerialStopBits = transport.Serial.StopBits;
            SelectedSerialHandshake = transport.Serial.Handshake;
            SerialNewLine = transport.Serial.NewLine;

            SshHost = ssh.Host;
            SshPort = ssh.Port.ToString(CultureInfo.InvariantCulture);
            SshUsername = ssh.Username;
            SelectedSshAuthMode = authMode;
            SshPassword = string.Empty;
            SshPrivateKeyPath = string.Empty;
            SshExpectedHostKeyFingerprintSha256 = ssh.ExpectedHostKeyFingerprintSha256 ?? string.Empty;
            SshRequestPty = ssh.RequestPty;
            SshTerminalType = ssh.TerminalType;
            SshInitialCommand = ssh.InitialCommand ?? string.Empty;
            SelectedSshProxyType = proxy?.Type ?? SshProxyType.None;
            SshProxyHost = proxy?.Host ?? string.Empty;
            SshProxyPort = (proxy?.Port ?? 1080).ToString(CultureInfo.InvariantCulture);
            SshProxyUsername = proxy?.Username ?? string.Empty;
            SshProxyPassword = proxy?.Password ?? string.Empty;

            SshLocalPortForwardEnabled = localForward is not null;
            SshLocalPortForwardBindAddress = localForward?.BindAddress ?? "127.0.0.1";
            SshLocalPortForwardSourcePort = (localForward?.SourcePort ?? 15432).ToString(CultureInfo.InvariantCulture);
            SshLocalPortForwardDestinationHost = localForward?.DestinationHost ?? "db.internal";
            SshLocalPortForwardDestinationPort = (localForward?.DestinationPort ?? 5432).ToString(CultureInfo.InvariantCulture);

            SshX11Enabled = ssh.X11?.Enabled ?? false;
            SshX11Display = ssh.X11?.Display ?? ":0";
            SshKeepAliveIntervalSeconds = ssh.Policy.KeepAliveIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            SshConnectTimeoutSeconds = ssh.Policy.ConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture);

            CopyOnSelectEnabled = profile.Behavior.CopyOnSelectEnabled;
            EnableBellNotifications = profile.Behavior.EnableBellNotifications;
            BackspaceSendsControlH = profile.Behavior.BackspaceSendsControlH;
            EnableTextShaping = profile.Behavior.EnableTextShaping;
            EnableLigatures = profile.Behavior.EnableLigatures;
            SelectedPasteSafetyPolicy = ParsePasteSafetyPolicy(profile.Behavior.PasteSafetyPolicy);

            SelectedFontSource = profile.Appearance.FontSource;
            FontFamilyName = profile.Appearance.FontFamilyName;
            FontFilePath = profile.Appearance.FontFilePath ?? string.Empty;
            FontSize = profile.Appearance.FontSize;
            AutoScroll = profile.Appearance.AutoScroll;
            BackgroundOpacityEnabled = profile.Appearance.BackgroundOpacityEnabled;

            SessionLoggingEnabled = profile.Logging.Enabled;
            SessionLogFilePath = profile.Logging.FilePath ?? GetDefaultSessionLogPath();
            SelectedSessionLogFormat = profile.Logging.Format;
            SessionLogFlushFrequently = profile.Logging.FlushFrequently;
            EventLogEnabled = profile.Logging.EventLogEnabled;
        }
        finally
        {
            _suppressDirtyTracking = previousSuppress;
        }
    }

    private TerminalSessionProfile CreateDefaultProfile(string id, string displayName)
    {
        return new TerminalSessionProfile
        {
            Id = id,
            DisplayName = displayName,
        };
    }

    private int FindProfileIndex(string profileId)
    {
        for (int i = 0; i < _profiles.Count; i++)
        {
            if (string.Equals(_profiles[i].Id, profileId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void RefreshProfileItems(string? selectedId = null)
    {
        string? id = selectedId ?? SelectedProfile?.Id ?? _activeProfileId;

        _suppressProfileSwitch = true;
        bool previousSuppress = _suppressDirtyTracking;
        _suppressDirtyTracking = true;
        try
        {
            Profiles.Clear();
            for (int i = 0; i < _profiles.Count; i++)
            {
                TerminalSessionProfile profile = _profiles[i];
                Profiles.Add(new TerminalSettingsProfileItem(profile.Id, profile.DisplayName));
            }

            TerminalSettingsProfileItem? selected = null;
            if (!string.IsNullOrWhiteSpace(id))
            {
                for (int i = 0; i < Profiles.Count; i++)
                {
                    if (string.Equals(Profiles[i].Id, id, StringComparison.Ordinal))
                    {
                        selected = Profiles[i];
                        break;
                    }
                }
            }

            SelectedProfile = selected ?? (Profiles.Count > 0 ? Profiles[0] : null);
        }
        finally
        {
            _suppressDirtyTracking = previousSuppress;
            _suppressProfileSwitch = false;
        }
    }

    private string ResolveDefaultProfileId()
    {
        if (!string.IsNullOrWhiteSpace(DefaultProfileId) && FindProfileIndex(DefaultProfileId) >= 0)
        {
            return DefaultProfileId;
        }

        return _profiles.Count > 0 ? _profiles[0].Id : "default";
    }

    private string GenerateUniqueProfileId(string baseId)
    {
        string normalizedBase = NormalizeIdentifier(baseId);
        if (FindProfileIndex(normalizedBase) < 0)
        {
            return normalizedBase;
        }

        int suffix = 2;
        while (true)
        {
            string candidate = $"{normalizedBase}-{suffix}";
            if (FindProfileIndex(candidate) < 0)
            {
                return candidate;
            }

            suffix++;
        }
    }

    private string GenerateUniqueProfileName(string baseName)
    {
        string normalizedBase = NormalizeDisplayName(baseName, "Profile");
        if (!ContainsProfileDisplayName(normalizedBase))
        {
            return normalizedBase;
        }

        int suffix = 2;
        while (true)
        {
            string candidate = $"{normalizedBase} {suffix}";
            if (!ContainsProfileDisplayName(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private bool ContainsProfileDisplayName(string displayName)
    {
        for (int i = 0; i < _profiles.Count; i++)
        {
            if (string.Equals(_profiles[i].DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeIdentifier(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "profile" : value.Trim().ToLowerInvariant();
        Span<char> buffer = stackalloc char[trimmed.Length];
        int index = 0;
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                buffer[index++] = c;
            }
            else if (c == '-' || c == '_' || c == ' ' || c == '.')
            {
                buffer[index++] = '-';
            }
        }

        return index == 0 ? "profile" : new string(buffer[..index]);
    }

    private static string NormalizeDisplayName(string value, string fallback)
    {
        string? normalized = NormalizeOptional(value);
        return normalized ?? fallback;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void UpdateFontSourceFlags(TerminalFontSource fontSource)
    {
        IsSystemFontSourceSelected = fontSource == TerminalFontSource.System;
        IsFileFontSourceSelected = fontSource == TerminalFontSource.File;
    }

    private static AvaloniaList<string> CreateSystemFontFamilies()
    {
        AvaloniaList<string> families = [];
        IReadOnlyList<string> discoveredFamilies = TerminalFontCatalog.GetSystemFontFamilies();
        for (int i = 0; i < discoveredFamilies.Count; i++)
        {
            families.Add(discoveredFamilies[i]);
        }

        string defaultFont = GetDefaultMonospaceFont();
        if (!ContainsString(families, defaultFont))
        {
            families.Insert(0, defaultFont);
        }

        return families;
    }

    private static bool ContainsString(IReadOnlyList<string> values, string value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int ParsePort(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= 1 && parsed <= 65535
            ? parsed
            : fallback;
    }

    private static int ParsePositiveInt(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static int ParseIntInRange(string value, int min, int max, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed >= min && parsed <= max
            ? parsed
            : fallback;
    }

    private static TerminalPasteSafetyPolicy ParsePasteSafetyPolicy(string? value)
    {
        return Enum.TryParse(value, ignoreCase: true, out TerminalPasteSafetyPolicy parsed)
            ? parsed
            : TerminalPasteSafetyPolicy.None;
    }

    private static string GetDefaultMonospaceFont()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
            "Consolas";
    }

    private static string GetDefaultSessionLogPath()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoyalTerminal");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".royalterminal");
        }

        return Path.Combine(directory, "session.log");
    }

    private TerminalSettingsTransportModeOption ResolveTransportMode(string transportId)
    {
        for (int i = 0; i < TransportModes.Count; i++)
        {
            if (string.Equals(TransportModes[i].Id, transportId, StringComparison.OrdinalIgnoreCase))
            {
                return TransportModes[i];
            }
        }

        return TransportModes[0];
    }

    private TerminalSettingsSshAuthModeOption ResolveSshAuthMode(TerminalSessionSshAuthenticationSettings auth)
    {
        string id = auth.UseAgent
            ? TerminalSettingsSshAuthModeOption.AgentModeId
            : auth.UsePassword && auth.PrivateKeySecretIds.Count > 0
                ? TerminalSettingsSshAuthModeOption.PasswordAndKeyModeId
                : auth.PrivateKeySecretIds.Count > 0
                    ? TerminalSettingsSshAuthModeOption.PrivateKeyModeId
                    : TerminalSettingsSshAuthModeOption.PasswordModeId;

        for (int i = 0; i < SshAuthModes.Count; i++)
        {
            if (string.Equals(SshAuthModes[i].Id, id, StringComparison.Ordinal))
            {
                return SshAuthModes[i];
            }
        }

        return SshAuthModes[0];
    }

    private static SshPortForwardOptions? ResolveLocalPortForward(List<SshPortForwardOptions> forwardings)
    {
        for (int i = 0; i < forwardings.Count; i++)
        {
            if (forwardings[i].Mode == SshPortForwardMode.Local)
            {
                return forwardings[i];
            }
        }

        return null;
    }

    private TerminalSessionSshAuthenticationSettings BuildSshAuthenticationSettings()
    {
        string modeId = SelectedSshAuthMode?.Id ?? TerminalSettingsSshAuthModeOption.PasswordModeId;
        bool usePassword = string.Equals(modeId, TerminalSettingsSshAuthModeOption.PasswordModeId, StringComparison.Ordinal)
                           || string.Equals(modeId, TerminalSettingsSshAuthModeOption.PasswordAndKeyModeId, StringComparison.Ordinal);
        bool usePrivateKey = string.Equals(modeId, TerminalSettingsSshAuthModeOption.PrivateKeyModeId, StringComparison.Ordinal)
                             || string.Equals(modeId, TerminalSettingsSshAuthModeOption.PasswordAndKeyModeId, StringComparison.Ordinal);
        bool useAgent = string.Equals(modeId, TerminalSettingsSshAuthModeOption.AgentModeId, StringComparison.Ordinal);

        List<string> privateKeys = usePrivateKey
            ? [DefaultPrivateKeySecretId]
            : [];

        return new TerminalSessionSshAuthenticationSettings
        {
            UsePassword = usePassword,
            PasswordSecretId = usePassword ? DefaultPasswordSecretId : null,
            PrivateKeySecretIds = privateKeys,
            UseAgent = useAgent,
        };
    }

    private SshProxyOptions? BuildSshProxyOptions()
    {
        if (SelectedSshProxyType == SshProxyType.None)
        {
            return null;
        }

        string host = NormalizeOptional(SshProxyHost) ?? "localhost";
        int port = ParsePort(SshProxyPort, 1080);
        return new SshProxyOptions(
            SelectedSshProxyType,
            host,
            port,
            NormalizeOptional(SshProxyUsername),
            NormalizeOptional(SshProxyPassword));
    }

    private List<SshPortForwardOptions> BuildSshPortForwardings()
    {
        if (!SshLocalPortForwardEnabled)
        {
            return [];
        }

        string bindAddress = NormalizeOptional(SshLocalPortForwardBindAddress) ?? "127.0.0.1";
        string destinationHost = NormalizeOptional(SshLocalPortForwardDestinationHost) ?? "db.internal";
        int sourcePort = ParsePort(SshLocalPortForwardSourcePort, 15432);
        int destinationPort = ParsePort(SshLocalPortForwardDestinationPort, 5432);

        return
        [
            new SshPortForwardOptions(
                Mode: SshPortForwardMode.Local,
                BindAddress: bindAddress,
                SourcePort: (uint)sourcePort,
                DestinationHost: destinationHost,
                DestinationPort: (uint)destinationPort),
        ];
    }

    private SshX11Options? BuildSshX11Options()
    {
        if (!SshX11Enabled)
        {
            return null;
        }

        return new SshX11Options(
            Enabled: true,
            Display: NormalizeOptional(SshX11Display) ?? ":0");
    }

    private SshPolicyOptions BuildSshPolicyOptions()
    {
        int keepAlive = ParsePositiveInt(SshKeepAliveIntervalSeconds, 30);
        int connectTimeout = ParsePositiveInt(SshConnectTimeoutSeconds, 15);
        return new SshPolicyOptions(keepAlive, connectTimeout);
    }

    private void UpdateCommandStates()
    {
        if (DuplicateProfileCommand is RelayCommand duplicate)
        {
            duplicate.RaiseCanExecuteChanged();
        }

        if (DeleteProfileCommand is RelayCommand delete)
        {
            delete.RaiseCanExecuteChanged();
        }

        if (SetDefaultProfileCommand is RelayCommand setDefault)
        {
            setDefault.RaiseCanExecuteChanged();
        }

        if (ApplyCommand is RelayCommand apply)
        {
            apply.RaiseCanExecuteChanged();
        }

        if (SaveCommand is RelayCommand save)
        {
            save.RaiseCanExecuteChanged();
        }
    }
}
