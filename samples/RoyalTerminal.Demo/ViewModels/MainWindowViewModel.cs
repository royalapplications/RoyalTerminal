// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Main window view model and command surface.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using ReactiveUI;

namespace RoyalTerminal.Demo.ViewModels;

public sealed class MainWindowViewModel : ReactiveObject
{
    private double _fontSize = 14.0;
    private TerminalFontSource _fontSource = TerminalFontSource.System;
    private string _fontFamilyName = GetDefaultMonospaceFont();
    private string _fontFilePath = string.Empty;
    private bool _isDarkTheme = true;
    private string _themePresetButtonText = "Theme: Default";
    private bool _nativeVtAvailable;
    private bool _useRenderedControl;
    private bool _useNativeVtControl;
    private bool _useManagedVtControl;
    private string _statusText = "Ready";
    private string _dimensionsText = "80x24";
    private string _modeButtonText = "Rendered";
    private TerminalModeCapabilities _terminalCapabilities = TerminalModeCapabilities.Create(nativeVtAvailable: false);
    private TerminalRenderMode _activeRenderMode = TerminalRenderMode.RenderedAuto;
    private readonly ITerminalModeResolver _modeResolver;
    private readonly ITerminalThemeCatalog _themeCatalog;
    private readonly IReadOnlyList<TerminalThemePreset> _themePresets;
    private readonly Dictionary<TerminalRenderMode, ModeThemeState> _modeThemes = [];
    private TerminalSettingsPanelState? _settingsPanelState;

    private IReadOnlyList<ShellProfileOption> _shellProfiles =
    [
        new ShellProfileOption("default", "Default shell", string.Empty),
    ];
    private ShellProfileOption? _selectedShellProfile;

    private readonly IReadOnlyList<SettingsCategoryOption> _settingsCategories;
    private SettingsCategoryOption _selectedSettingsCategory;

    private readonly IReadOnlyList<TransportModeOption> _transportModes;
    private TransportModeOption _selectedTransportMode;

    private string _sessionName = "Default Session";
    private readonly IReadOnlyList<SshAuthModeOption> _sshAuthModes;
    private SshAuthModeOption _selectedSshAuthMode;

    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _pipeCommandText = "echo RoyalTerminal pipe transport";
    private bool _pipeMergeStdErrIntoStdOut = true;
    private string _rawTcpHost = "localhost";
    private string _rawTcpPort = "23";
    private string _telnetHost = "localhost";
    private string _telnetPort = "23";
    private string _telnetTerminalType = "xterm";
    private string _telnetInitialCommand = string.Empty;
    private string _serialPortName = OperatingSystem.IsWindows() ? "COM1" : "/dev/ttyUSB0";
    private string _serialBaudRate = "9600";
    private string _serialDataBits = "8";
    private readonly IReadOnlyList<TerminalSerialParity> _serialParityOptions = Enum.GetValues<TerminalSerialParity>();
    private readonly IReadOnlyList<TerminalSerialStopBits> _serialStopBitsOptions = Enum.GetValues<TerminalSerialStopBits>();
    private readonly IReadOnlyList<TerminalSerialHandshake> _serialHandshakeOptions = Enum.GetValues<TerminalSerialHandshake>();
    private TerminalSerialParity _selectedSerialParity = TerminalSerialParity.None;
    private TerminalSerialStopBits _selectedSerialStopBits = TerminalSerialStopBits.One;
    private TerminalSerialHandshake _selectedSerialHandshake = TerminalSerialHandshake.None;
    private string _serialNewLine = "\n";
    private bool _copyOnSelectEnabled;
    private bool _enableBellNotifications = true;
    private bool _backspaceSendsControlH;
    private bool _enableTextShaping = true;
    private bool _reflowOnResize = true;
    private bool _enableLigatures;
    private readonly IReadOnlyList<TerminalPasteSafetyPolicy> _pasteSafetyPolicies = Enum.GetValues<TerminalPasteSafetyPolicy>();
    private TerminalPasteSafetyPolicy _selectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.None;

    private string _sshHost = "localhost";
    private string _sshPort = "22";
    private string _sshUsername = Environment.UserName;
    private string _sshPassword = string.Empty;
    private string _sshPrivateKeyPath = string.Empty;
    private string _sshExpectedHostKeyFingerprintSha256 = string.Empty;
    private string _sshTerminalType = "xterm-256color";
    private string _sshInitialCommand = string.Empty;
    private bool _sshRequestPty = true;
    private readonly IReadOnlyList<SshProxyType> _sshProxyTypes = Enum.GetValues<SshProxyType>();
    private SshProxyType _selectedSshProxyType = SshProxyType.None;
    private string _sshProxyHost = string.Empty;
    private string _sshProxyPort = "1080";
    private string _sshProxyUsername = string.Empty;
    private string _sshProxyPassword = string.Empty;
    private bool _sshLocalPortForwardEnabled;
    private string _sshLocalPortForwardBindAddress = "127.0.0.1";
    private string _sshLocalPortForwardSourcePort = "15432";
    private string _sshLocalPortForwardDestinationHost = "db.internal";
    private string _sshLocalPortForwardDestinationPort = "5432";
    private bool _sshX11Enabled;
    private string _sshX11Display = ":0";
    private string _sshKeepAliveIntervalSeconds = "30";
    private string _sshConnectTimeoutSeconds = "15";
    private bool _sessionLoggingEnabled;
    private string _sessionLogFilePath = GetDefaultSessionLogPath();
    private readonly IReadOnlyList<TerminalSessionLogFormat> _sessionLogFormats = Enum.GetValues<TerminalSessionLogFormat>();
    private TerminalSessionLogFormat _selectedSessionLogFormat = TerminalSessionLogFormat.PlainText;
    private bool _sessionLogFlushFrequently = true;
    private bool _eventLogEnabled = true;
    private readonly List<string> _eventLogEntries = [];
    private string _eventLogText = string.Empty;

    private bool _isCaptureActive;
    private bool _hasCapture;
    private bool _isReplayEnabled;
    private bool _isReplayPlaying;
    private double _replayDurationSeconds;
    private double _replayTimelineValue;
    private string _replaySourceLabel = string.Empty;
    private string _searchQuery = string.Empty;
    private int _searchMatchTotal;
    private int _searchMatchSelected = -1;
    private bool _searchUsesNativeScrollback;
    private bool _searchApplied;
    private bool _showGhosttyDiagnostics;
    private string _ghosttyDiagnosticsText = "Ghostty VT diagnostics are unavailable for the active tab.";

    public MainWindowViewModel()
        : this(TerminalModeResolver.Default, new TerminalThemeCatalog())
    {
    }

    internal MainWindowViewModel(ITerminalModeResolver modeResolver, ITerminalThemeCatalog themeCatalog)
    {
        _modeResolver = modeResolver ?? throw new ArgumentNullException(nameof(modeResolver));
        _themeCatalog = themeCatalog ?? throw new ArgumentNullException(nameof(themeCatalog));
        _themePresets = _themeCatalog.Presets;
        InitializeModeThemes();

        _settingsCategories =
        [
            new SettingsCategoryOption(SettingsCategoryOption.SessionCategoryId, "Session"),
            new SettingsCategoryOption(SettingsCategoryOption.ConnectionCategoryId, "Connection"),
            new SettingsCategoryOption(SettingsCategoryOption.TerminalCategoryId, "Terminal"),
            new SettingsCategoryOption(SettingsCategoryOption.AppearanceCategoryId, "Appearance"),
            new SettingsCategoryOption(SettingsCategoryOption.SshCategoryId, "SSH"),
            new SettingsCategoryOption(SettingsCategoryOption.LoggingCategoryId, "Logging"),
        ];
        _selectedSettingsCategory = _settingsCategories[0];

        _transportModes =
        [
            new TransportModeOption(TerminalTransportIds.Pty, "PTY"),
            new TransportModeOption(TerminalTransportIds.Pipe, "Pipe"),
            new TransportModeOption(TerminalTransportIds.RawTcp, "Raw TCP"),
            new TransportModeOption(TerminalTransportIds.Telnet, "Telnet"),
            new TransportModeOption(TerminalTransportIds.Serial, "Serial"),
            new TransportModeOption(TerminalTransportIds.Ssh, "SSH"),
        ];
        _selectedTransportMode = _transportModes[0];

        _sshAuthModes =
        [
            new SshAuthModeOption(SshAuthModeOption.PasswordModeId, "Password"),
            new SshAuthModeOption(SshAuthModeOption.PrivateKeyModeId, "Private Key"),
            new SshAuthModeOption(SshAuthModeOption.AgentModeId, "Agent"),
            new SshAuthModeOption(SshAuthModeOption.PasswordAndKeyModeId, "Password + Key"),
        ];
        _selectedSshAuthMode = _sshAuthModes[0];

        _selectedShellProfile = _shellProfiles[0];

        CreateNewTabInteraction = new Interaction<Unit, Unit>();
        CloseCurrentTabInteraction = new Interaction<Unit, Unit>();
        ActivateTabInteraction = new Interaction<int, Unit>();
        CloseTabInteraction = new Interaction<int, Unit>();
        SwitchToTabByIndexInteraction = new Interaction<int, Unit>();
        CycleTabInteraction = new Interaction<bool, Unit>();
        CopySelectionInteraction = new Interaction<Unit, Unit>();
        PasteClipboardInteraction = new Interaction<Unit, Unit>();
        SelectAllInteraction = new Interaction<Unit, Unit>();
        ApplyFontSizeInteraction = new Interaction<double, Unit>();
        ApplyThemeInteraction = new Interaction<bool, Unit>();
        ApplyThemeModelInteraction = new Interaction<TerminalThemeApplyRequest, Unit>();
        ToggleCaptureInteraction = new Interaction<bool, Unit>();
        SaveCaptureInteraction = new Interaction<Unit, Unit>();
        LoadReplayInteraction = new Interaction<Unit, Unit>();
        SetReplayPlayingInteraction = new Interaction<bool, Unit>();
        StopReplayInteraction = new Interaction<Unit, Unit>();
        PrepareSettingsPanelInteraction = new Interaction<Unit, Unit>();
        ApplySearchInteraction = new Interaction<string?, Unit>();
        NextSearchInteraction = new Interaction<Unit, Unit>();
        PreviousSearchInteraction = new Interaction<Unit, Unit>();
        ClearSearchInteraction = new Interaction<Unit, Unit>();
        ShowHyperlinkSampleInteraction = new Interaction<Unit, Unit>();
        ShowKittyGraphicsSampleInteraction = new Interaction<Unit, Unit>();
        ToggleGhosttyDiagnosticsInteraction = new Interaction<bool, Unit>();
        CopySnapshotInteraction = new Interaction<TerminalSnapshotExportFormat, Unit>();

        NewTabCommand = ReactiveCommand.CreateFromObservable(() => CreateNewTabInteraction.Handle(Unit.Default));
        CloseCurrentTabCommand = ReactiveCommand.CreateFromObservable(() => CloseCurrentTabInteraction.Handle(Unit.Default));
        ActivateTabCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(ActivateTab);
        CloseTabCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(CloseTab);
        SwitchToTabByIndexCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(SwitchToTabByIndex);
        CycleTabForwardCommand = ReactiveCommand.CreateFromObservable(() => CycleTabInteraction.Handle(true));
        CycleTabBackwardCommand = ReactiveCommand.CreateFromObservable(() => CycleTabInteraction.Handle(false));
        CopySelectionCommand = ReactiveCommand.CreateFromObservable(CopySelection);
        PasteClipboardCommand = ReactiveCommand.CreateFromObservable(PasteClipboard);
        SelectAllCommand = ReactiveCommand.CreateFromObservable(SelectAll);
        IncreaseFontSizeCommand = ReactiveCommand.CreateFromObservable(() => ChangeFontSize(1));
        DecreaseFontSizeCommand = ReactiveCommand.CreateFromObservable(() => ChangeFontSize(-1));
        ResetFontSizeCommand = ReactiveCommand.CreateFromObservable(ResetFontSize);
        ToggleThemeCommand = ReactiveCommand.CreateFromObservable(ToggleTheme);
        CycleThemePresetCommand = ReactiveCommand.CreateFromObservable(CycleThemePreset);
        GenerateThemeCommand = ReactiveCommand.CreateFromObservable(GenerateTheme);
        CycleRenderModeCommand = ReactiveCommand.Create(CycleRenderMode);
        ToggleCaptureCommand = ReactiveCommand.CreateFromObservable(ToggleCapture);
        SaveCaptureCommand = ReactiveCommand.CreateFromObservable(SaveCapture);
        LoadReplayCommand = ReactiveCommand.CreateFromObservable(LoadReplay);
        ToggleReplayPlaybackCommand = ReactiveCommand.CreateFromObservable(ToggleReplayPlayback);
        StopReplayCommand = ReactiveCommand.CreateFromObservable(StopReplay);
        PrepareSettingsPanelCommand = ReactiveCommand.CreateFromObservable(PrepareSettingsPanel);
        ClearEventLogCommand = ReactiveCommand.Create(ClearEventLog);
        ApplySearchCommand = ReactiveCommand.CreateFromObservable(ApplySearch);
        NextSearchCommand = ReactiveCommand.CreateFromObservable(NextSearch);
        PreviousSearchCommand = ReactiveCommand.CreateFromObservable(PreviousSearch);
        ClearSearchCommand = ReactiveCommand.CreateFromObservable(ClearSearch);
        ShowHyperlinkSampleCommand = ReactiveCommand.CreateFromObservable(ShowHyperlinkSample);
        ShowKittyGraphicsSampleCommand = ReactiveCommand.CreateFromObservable(ShowKittyGraphicsSample);
        ToggleGhosttyDiagnosticsCommand = ReactiveCommand.CreateFromObservable(ToggleGhosttyDiagnostics);
        CopyPlainSnapshotCommand = ReactiveCommand.CreateFromObservable(() => CopySnapshot(TerminalSnapshotExportFormat.PlainText));
        CopyStyledVtSnapshotCommand = ReactiveCommand.CreateFromObservable(() => CopySnapshot(TerminalSnapshotExportFormat.StyledVt));
        CopyHtmlSnapshotCommand = ReactiveCommand.CreateFromObservable(() => CopySnapshot(TerminalSnapshotExportFormat.Html));

        UpdateThemePresetButtonText();
    }

    public Interaction<Unit, Unit> CreateNewTabInteraction { get; }
    public Interaction<Unit, Unit> CloseCurrentTabInteraction { get; }
    public Interaction<int, Unit> ActivateTabInteraction { get; }
    public Interaction<int, Unit> CloseTabInteraction { get; }
    public Interaction<int, Unit> SwitchToTabByIndexInteraction { get; }
    public Interaction<bool, Unit> CycleTabInteraction { get; }
    public Interaction<Unit, Unit> CopySelectionInteraction { get; }
    public Interaction<Unit, Unit> PasteClipboardInteraction { get; }
    public Interaction<Unit, Unit> SelectAllInteraction { get; }
    public Interaction<double, Unit> ApplyFontSizeInteraction { get; }
    public Interaction<bool, Unit> ApplyThemeInteraction { get; }
    public Interaction<TerminalThemeApplyRequest, Unit> ApplyThemeModelInteraction { get; }
    public Interaction<bool, Unit> ToggleCaptureInteraction { get; }
    public Interaction<Unit, Unit> SaveCaptureInteraction { get; }
    public Interaction<Unit, Unit> LoadReplayInteraction { get; }
    public Interaction<bool, Unit> SetReplayPlayingInteraction { get; }
    public Interaction<Unit, Unit> StopReplayInteraction { get; }
    public Interaction<Unit, Unit> PrepareSettingsPanelInteraction { get; }
    public Interaction<string?, Unit> ApplySearchInteraction { get; }
    public Interaction<Unit, Unit> NextSearchInteraction { get; }
    public Interaction<Unit, Unit> PreviousSearchInteraction { get; }
    public Interaction<Unit, Unit> ClearSearchInteraction { get; }
    public Interaction<Unit, Unit> ShowHyperlinkSampleInteraction { get; }
    public Interaction<Unit, Unit> ShowKittyGraphicsSampleInteraction { get; }
    public Interaction<bool, Unit> ToggleGhosttyDiagnosticsInteraction { get; }
    public Interaction<TerminalSnapshotExportFormat, Unit> CopySnapshotInteraction { get; }

    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCurrentTabCommand { get; }
    public ReactiveCommand<object?, Unit> ActivateTabCommand { get; }
    public ReactiveCommand<object?, Unit> CloseTabCommand { get; }
    public ReactiveCommand<object?, Unit> SwitchToTabByIndexCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleTabForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleTabBackwardCommand { get; }
    public ReactiveCommand<Unit, Unit> CopySelectionCommand { get; }
    public ReactiveCommand<Unit, Unit> PasteClipboardCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> IncreaseFontSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> DecreaseFontSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetFontSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleThemePresetCommand { get; }
    public ReactiveCommand<Unit, Unit> GenerateThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleRenderModeCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleCaptureCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCaptureCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadReplayCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleReplayPlaybackCommand { get; }
    public ReactiveCommand<Unit, Unit> StopReplayCommand { get; }
    public ReactiveCommand<Unit, Unit> PrepareSettingsPanelCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearEventLogCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplySearchCommand { get; }
    public ReactiveCommand<Unit, Unit> NextSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowHyperlinkSampleCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowKittyGraphicsSampleCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleGhosttyDiagnosticsCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyPlainSnapshotCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyStyledVtSnapshotCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyHtmlSnapshotCommand { get; }

    public TerminalSettingsPanelState SettingsPanelState => _settingsPanelState ??= new TerminalSettingsPanelState();

    public double FontSize
    {
        get => _fontSize;
        private set
        {
            if (Math.Abs(_fontSize - value) < double.Epsilon)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _fontSize, value);
            this.RaisePropertyChanged(nameof(FontSizeDisplay));
        }
    }

    public string FontSizeDisplay => FontSize.ToString("0", CultureInfo.InvariantCulture);

    public TerminalFontSource FontSource
    {
        get => _fontSource;
        set => this.RaiseAndSetIfChanged(ref _fontSource, value);
    }

    public string FontFamilyName
    {
        get => _fontFamilyName;
        set => this.RaiseAndSetIfChanged(ref _fontFamilyName, value);
    }

    public string FontFilePath
    {
        get => _fontFilePath;
        set => this.RaiseAndSetIfChanged(ref _fontFilePath, value);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set
        {
            if (_isDarkTheme == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
            this.RaisePropertyChanged(nameof(ThemeToggleContent));
        }
    }

    public string ThemeToggleContent => IsDarkTheme ? "\u2600" : "\U0001F319";

    public string ThemePresetButtonText
    {
        get => _themePresetButtonText;
        private set => this.RaiseAndSetIfChanged(ref _themePresetButtonText, value);
    }

    internal TerminalTheme ActiveTheme => GetModeThemeState(_activeRenderMode).Theme;

    public bool NativeVtAvailable
    {
        get => _nativeVtAvailable;
        private set => this.RaiseAndSetIfChanged(ref _nativeVtAvailable, value);
    }

    public bool UseRenderedControl
    {
        get => _useRenderedControl;
        private set
        {
            if (_useRenderedControl == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _useRenderedControl, value);
            RaiseSessionConfigurationVisibilityChanged();
        }
    }

    public bool UseNativeVtControl
    {
        get => _useNativeVtControl;
        private set => this.RaiseAndSetIfChanged(ref _useNativeVtControl, value);
    }

    public bool UseManagedVtControl
    {
        get => _useManagedVtControl;
        private set => this.RaiseAndSetIfChanged(ref _useManagedVtControl, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string DimensionsText
    {
        get => _dimensionsText;
        private set => this.RaiseAndSetIfChanged(ref _dimensionsText, value);
    }

    public string ModeButtonText
    {
        get => _modeButtonText;
        private set => this.RaiseAndSetIfChanged(ref _modeButtonText, value);
    }

    public bool IsCaptureActive
    {
        get => _isCaptureActive;
        private set
        {
            if (_isCaptureActive == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isCaptureActive, value);
            this.RaisePropertyChanged(nameof(CaptureToggleButtonText));
            this.RaisePropertyChanged(nameof(CanSaveCapture));
        }
    }

    public bool HasCapture
    {
        get => _hasCapture;
        private set
        {
            if (_hasCapture == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _hasCapture, value);
            this.RaisePropertyChanged(nameof(CanSaveCapture));
        }
    }

    public string CaptureToggleButtonText => IsCaptureActive ? "Stop Capture" : "Start Capture";

    public bool CanSaveCapture => HasCapture || IsCaptureActive;

    public bool IsReplayEnabled
    {
        get => _isReplayEnabled;
        private set
        {
            if (_isReplayEnabled == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isReplayEnabled, value);
            this.RaisePropertyChanged(nameof(CanReplayControl));
            this.RaisePropertyChanged(nameof(CanSeekReplay));
            this.RaisePropertyChanged(nameof(ReplayTimelineText));
        }
    }

    public bool IsReplayPlaying
    {
        get => _isReplayPlaying;
        private set
        {
            if (_isReplayPlaying == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isReplayPlaying, value);
            this.RaisePropertyChanged(nameof(ReplayPlayPauseButtonText));
        }
    }

    public double ReplayDurationSeconds
    {
        get => _replayDurationSeconds;
        private set
        {
            double normalized = Math.Max(0, value);
            if (Math.Abs(_replayDurationSeconds - normalized) < double.Epsilon)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _replayDurationSeconds, normalized);
            this.RaisePropertyChanged(nameof(CanSeekReplay));
            this.RaisePropertyChanged(nameof(ReplayTimelineText));
        }
    }

    public double ReplayTimelineValue
    {
        get => _replayTimelineValue;
        set
        {
            double clamped = ReplayDurationSeconds > 0
                ? Math.Clamp(value, 0, ReplayDurationSeconds)
                : 0;
            if (Math.Abs(_replayTimelineValue - clamped) < double.Epsilon)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _replayTimelineValue, clamped);
            this.RaisePropertyChanged(nameof(ReplayTimelineText));
        }
    }

    public string ReplaySourceLabel
    {
        get => _replaySourceLabel;
        private set => this.RaiseAndSetIfChanged(ref _replaySourceLabel, value);
    }

    public string ReplayPlayPauseButtonText => IsReplayPlaying ? "Pause" : "Play";

    public bool CanReplayControl => IsReplayEnabled;

    public bool CanSeekReplay => IsReplayEnabled && ReplayDurationSeconds > 0;

    public string ReplayTimelineText
        => $"{FormatDuration(ReplayTimelineValue)} / {FormatDuration(ReplayDurationSeconds)}";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (string.Equals(_searchQuery, value, StringComparison.Ordinal))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            this.RaisePropertyChanged(nameof(CanApplySearch));
            this.RaisePropertyChanged(nameof(CanClearSearch));
            this.RaisePropertyChanged(nameof(SearchResultText));
        }
    }

    public bool CanApplySearch => !string.IsNullOrWhiteSpace(SearchQuery);

    public bool CanAdvanceSearch => _searchApplied && _searchMatchTotal > 0;

    public bool CanClearSearch => !string.IsNullOrWhiteSpace(SearchQuery) || _searchApplied;

    public string SearchResultText
    {
        get
        {
            if (!_searchApplied || string.IsNullOrWhiteSpace(SearchQuery))
            {
                return "Search idle";
            }

            string scope = _searchUsesNativeScrollback
                ? "native scrollback"
                : "viewport mirror";
            if (_searchMatchTotal <= 0)
            {
                return $"No matches in {scope}";
            }

            int selectedDisplay = Math.Clamp(_searchMatchSelected + 1, 1, _searchMatchTotal);
            return $"{selectedDisplay}/{_searchMatchTotal} matches · {scope}";
        }
    }

    public bool ShowGhosttyDiagnostics
    {
        get => _showGhosttyDiagnostics;
        private set
        {
            if (_showGhosttyDiagnostics == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _showGhosttyDiagnostics, value);
            this.RaisePropertyChanged(nameof(GhosttyDiagnosticsButtonText));
        }
    }

    public string GhosttyDiagnosticsText
    {
        get => _ghosttyDiagnosticsText;
        private set => this.RaiseAndSetIfChanged(ref _ghosttyDiagnosticsText, value);
    }

    public string GhosttyDiagnosticsButtonText => ShowGhosttyDiagnostics ? "Hide Diagnostics" : "Native Diagnostics";

    public IReadOnlyList<SettingsCategoryOption> SettingsCategories => _settingsCategories;

    public SettingsCategoryOption SelectedSettingsCategory
    {
        get => _selectedSettingsCategory;
        set
        {
            if (_selectedSettingsCategory == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedSettingsCategory, value);
            RaiseSessionConfigurationVisibilityChanged();
        }
    }

    public bool ShowSessionSettingsCategory
        => string.Equals(
            SelectedSettingsCategory.Id,
            SettingsCategoryOption.SessionCategoryId,
            StringComparison.Ordinal);

    public bool ShowConnectionSettingsCategory
        => string.Equals(
            SelectedSettingsCategory.Id,
            SettingsCategoryOption.ConnectionCategoryId,
            StringComparison.Ordinal);

    public bool ShowTerminalSettingsCategory
        => string.Equals(
            SelectedSettingsCategory.Id,
            SettingsCategoryOption.TerminalCategoryId,
            StringComparison.Ordinal);

    public bool ShowAppearanceSettingsCategory
        => string.Equals(
            SelectedSettingsCategory.Id,
            SettingsCategoryOption.AppearanceCategoryId,
            StringComparison.Ordinal);

    public bool ShowSshSettingsCategory
        => string.Equals(
            SelectedSettingsCategory.Id,
            SettingsCategoryOption.SshCategoryId,
            StringComparison.Ordinal);

    public bool ShowLoggingSettingsCategory
        => string.Equals(
            SelectedSettingsCategory.Id,
            SettingsCategoryOption.LoggingCategoryId,
            StringComparison.Ordinal);

    public IReadOnlyList<TransportModeOption> TransportModes => _transportModes;

    public TransportModeOption SelectedTransportMode
    {
        get => _selectedTransportMode;
        set
        {
            if (_selectedTransportMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTransportMode, value);
            RaiseSessionConfigurationVisibilityChanged();
        }
    }

    public IReadOnlyList<ShellProfileOption> ShellProfiles => _shellProfiles;

    public ShellProfileOption? SelectedShellProfile
    {
        get => _selectedShellProfile;
        set => this.RaiseAndSetIfChanged(ref _selectedShellProfile, value);
    }

    public string SessionName
    {
        get => _sessionName;
        set => this.RaiseAndSetIfChanged(ref _sessionName, value);
    }

    public bool ShowSessionTransportPicker => true;

    public bool IsSessionTransportConfigEnabled => true;

    public bool ShowSessionTransportHint => false;

    public bool ShowLocalSessionFields => IsPtyTransportSelected || IsPipeTransportSelected;

    public bool ShowSshSessionFields => IsSshTransportSelected;

    public bool IsPtyTransportSelected
        => string.Equals(SelectedTransportMode.Id, TerminalTransportIds.Pty, StringComparison.OrdinalIgnoreCase);

    public bool IsPipeTransportSelected
        => string.Equals(SelectedTransportMode.Id, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase);

    public bool IsRawTcpTransportSelected
        => string.Equals(SelectedTransportMode.Id, TerminalTransportIds.RawTcp, StringComparison.OrdinalIgnoreCase);

    public bool IsTelnetTransportSelected
        => string.Equals(SelectedTransportMode.Id, TerminalTransportIds.Telnet, StringComparison.OrdinalIgnoreCase);

    public bool IsSerialTransportSelected
        => string.Equals(SelectedTransportMode.Id, TerminalTransportIds.Serial, StringComparison.OrdinalIgnoreCase);

    public bool IsSshTransportSelected
        => string.Equals(SelectedTransportMode.Id, TerminalTransportIds.Ssh, StringComparison.OrdinalIgnoreCase);

    public bool ShowTerminalSshFields => ShowTerminalSettingsCategory && IsSshTransportSelected;

    public bool ShowTerminalTelnetFields => ShowTerminalSettingsCategory && IsTelnetTransportSelected;

    public bool ShowTerminalSettingsHint
        => ShowTerminalSettingsCategory && !ShowTerminalSshFields && !ShowTerminalTelnetFields;

    public bool ShowSshSettingsUnavailableHint => ShowSshSettingsCategory && !IsSshTransportSelected;

    public bool ShowSshSettingsFields => ShowSshSettingsCategory && IsSshTransportSelected;

    public bool ShowSshProxyFields => ShowSshSettingsFields && SelectedSshProxyType != SshProxyType.None;

    public bool ShowSshPortForwardFields => ShowSshSettingsFields && SshLocalPortForwardEnabled;

    public bool ShowSshX11DisplayField => ShowSshSettingsFields && SshX11Enabled;

    public bool CopyOnSelectEnabled
    {
        get => _copyOnSelectEnabled;
        set => this.RaiseAndSetIfChanged(ref _copyOnSelectEnabled, value);
    }

    public bool EnableBellNotifications
    {
        get => _enableBellNotifications;
        set => this.RaiseAndSetIfChanged(ref _enableBellNotifications, value);
    }

    public bool BackspaceSendsControlH
    {
        get => _backspaceSendsControlH;
        set => this.RaiseAndSetIfChanged(ref _backspaceSendsControlH, value);
    }

    public bool EnableTextShaping
    {
        get => _enableTextShaping;
        set => this.RaiseAndSetIfChanged(ref _enableTextShaping, value);
    }

    public bool ReflowOnResize
    {
        get => _reflowOnResize;
        set => this.RaiseAndSetIfChanged(ref _reflowOnResize, value);
    }

    public bool EnableLigatures
    {
        get => _enableLigatures;
        set => this.RaiseAndSetIfChanged(ref _enableLigatures, value);
    }

    public IReadOnlyList<TerminalPasteSafetyPolicy> PasteSafetyPolicies => _pasteSafetyPolicies;

    public TerminalPasteSafetyPolicy SelectedPasteSafetyPolicy
    {
        get => _selectedPasteSafetyPolicy;
        set => this.RaiseAndSetIfChanged(ref _selectedPasteSafetyPolicy, value);
    }

    public bool SessionLoggingEnabled
    {
        get => _sessionLoggingEnabled;
        set => this.RaiseAndSetIfChanged(ref _sessionLoggingEnabled, value);
    }

    public string SessionLogFilePath
    {
        get => _sessionLogFilePath;
        set => this.RaiseAndSetIfChanged(ref _sessionLogFilePath, value);
    }

    public IReadOnlyList<TerminalSessionLogFormat> SessionLogFormats => _sessionLogFormats;

    public TerminalSessionLogFormat SelectedSessionLogFormat
    {
        get => _selectedSessionLogFormat;
        set => this.RaiseAndSetIfChanged(ref _selectedSessionLogFormat, value);
    }

    public bool SessionLogFlushFrequently
    {
        get => _sessionLogFlushFrequently;
        set => this.RaiseAndSetIfChanged(ref _sessionLogFlushFrequently, value);
    }

    public bool EventLogEnabled
    {
        get => _eventLogEnabled;
        set => this.RaiseAndSetIfChanged(ref _eventLogEnabled, value);
    }

    public string EventLogText
    {
        get => _eventLogText;
        private set => this.RaiseAndSetIfChanged(ref _eventLogText, value);
    }

    public bool HasEventLogEntries => _eventLogEntries.Count > 0;

    public string EventLogEntryCountText => $"{_eventLogEntries.Count} event(s)";

    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => this.RaiseAndSetIfChanged(ref _workingDirectory, value);
    }

    public string PipeCommandText
    {
        get => _pipeCommandText;
        set => this.RaiseAndSetIfChanged(ref _pipeCommandText, value);
    }

    public bool PipeMergeStdErrIntoStdOut
    {
        get => _pipeMergeStdErrIntoStdOut;
        set => this.RaiseAndSetIfChanged(ref _pipeMergeStdErrIntoStdOut, value);
    }

    public string RawTcpHost
    {
        get => _rawTcpHost;
        set => this.RaiseAndSetIfChanged(ref _rawTcpHost, value);
    }

    public string RawTcpPort
    {
        get => _rawTcpPort;
        set => this.RaiseAndSetIfChanged(ref _rawTcpPort, value);
    }

    public string TelnetHost
    {
        get => _telnetHost;
        set => this.RaiseAndSetIfChanged(ref _telnetHost, value);
    }

    public string TelnetPort
    {
        get => _telnetPort;
        set => this.RaiseAndSetIfChanged(ref _telnetPort, value);
    }

    public string TelnetTerminalType
    {
        get => _telnetTerminalType;
        set => this.RaiseAndSetIfChanged(ref _telnetTerminalType, value);
    }

    public string TelnetInitialCommand
    {
        get => _telnetInitialCommand;
        set => this.RaiseAndSetIfChanged(ref _telnetInitialCommand, value);
    }

    public string SerialPortName
    {
        get => _serialPortName;
        set => this.RaiseAndSetIfChanged(ref _serialPortName, value);
    }

    public string SerialBaudRate
    {
        get => _serialBaudRate;
        set => this.RaiseAndSetIfChanged(ref _serialBaudRate, value);
    }

    public string SerialDataBits
    {
        get => _serialDataBits;
        set => this.RaiseAndSetIfChanged(ref _serialDataBits, value);
    }

    public IReadOnlyList<TerminalSerialParity> SerialParityOptions => _serialParityOptions;

    public IReadOnlyList<TerminalSerialStopBits> SerialStopBitsOptions => _serialStopBitsOptions;

    public IReadOnlyList<TerminalSerialHandshake> SerialHandshakeOptions => _serialHandshakeOptions;

    public TerminalSerialParity SelectedSerialParity
    {
        get => _selectedSerialParity;
        set => this.RaiseAndSetIfChanged(ref _selectedSerialParity, value);
    }

    public TerminalSerialStopBits SelectedSerialStopBits
    {
        get => _selectedSerialStopBits;
        set => this.RaiseAndSetIfChanged(ref _selectedSerialStopBits, value);
    }

    public TerminalSerialHandshake SelectedSerialHandshake
    {
        get => _selectedSerialHandshake;
        set => this.RaiseAndSetIfChanged(ref _selectedSerialHandshake, value);
    }

    public string SerialNewLine
    {
        get => _serialNewLine;
        set => this.RaiseAndSetIfChanged(ref _serialNewLine, value);
    }

    public IReadOnlyList<SshAuthModeOption> SshAuthModes => _sshAuthModes;

    public SshAuthModeOption SelectedSshAuthMode
    {
        get => _selectedSshAuthMode;
        set
        {
            if (_selectedSshAuthMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedSshAuthMode, value);
            RaiseSessionConfigurationVisibilityChanged();
        }
    }

    public bool ShowSshPasswordField
        => ShowSshSessionFields && (
            string.Equals(SelectedSshAuthMode.Id, SshAuthModeOption.PasswordModeId, StringComparison.Ordinal) ||
            string.Equals(SelectedSshAuthMode.Id, SshAuthModeOption.PasswordAndKeyModeId, StringComparison.Ordinal));

    public bool ShowSshPrivateKeyField
        => ShowSshSessionFields && (
            string.Equals(SelectedSshAuthMode.Id, SshAuthModeOption.PrivateKeyModeId, StringComparison.Ordinal) ||
            string.Equals(SelectedSshAuthMode.Id, SshAuthModeOption.PasswordAndKeyModeId, StringComparison.Ordinal));

    public bool ShowSshAgentHint
        => ShowSshSessionFields &&
           string.Equals(SelectedSshAuthMode.Id, SshAuthModeOption.AgentModeId, StringComparison.Ordinal);

    public string SshHost
    {
        get => _sshHost;
        set => this.RaiseAndSetIfChanged(ref _sshHost, value);
    }

    public string SshPort
    {
        get => _sshPort;
        set => this.RaiseAndSetIfChanged(ref _sshPort, value);
    }

    public string SshUsername
    {
        get => _sshUsername;
        set => this.RaiseAndSetIfChanged(ref _sshUsername, value);
    }

    public string SshPassword
    {
        get => _sshPassword;
        set => this.RaiseAndSetIfChanged(ref _sshPassword, value);
    }

    public string SshPrivateKeyPath
    {
        get => _sshPrivateKeyPath;
        set => this.RaiseAndSetIfChanged(ref _sshPrivateKeyPath, value);
    }

    public string SshExpectedHostKeyFingerprintSha256
    {
        get => _sshExpectedHostKeyFingerprintSha256;
        set => this.RaiseAndSetIfChanged(ref _sshExpectedHostKeyFingerprintSha256, value);
    }

    public string SshTerminalType
    {
        get => _sshTerminalType;
        set => this.RaiseAndSetIfChanged(ref _sshTerminalType, value);
    }

    public string SshInitialCommand
    {
        get => _sshInitialCommand;
        set => this.RaiseAndSetIfChanged(ref _sshInitialCommand, value);
    }

    public bool SshRequestPty
    {
        get => _sshRequestPty;
        set => this.RaiseAndSetIfChanged(ref _sshRequestPty, value);
    }

    public IReadOnlyList<SshProxyType> SshProxyTypes => _sshProxyTypes;

    public SshProxyType SelectedSshProxyType
    {
        get => _selectedSshProxyType;
        set
        {
            if (_selectedSshProxyType == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedSshProxyType, value);
            RaiseSessionConfigurationVisibilityChanged();
        }
    }

    public string SshProxyHost
    {
        get => _sshProxyHost;
        set => this.RaiseAndSetIfChanged(ref _sshProxyHost, value);
    }

    public string SshProxyPort
    {
        get => _sshProxyPort;
        set => this.RaiseAndSetIfChanged(ref _sshProxyPort, value);
    }

    public string SshProxyUsername
    {
        get => _sshProxyUsername;
        set => this.RaiseAndSetIfChanged(ref _sshProxyUsername, value);
    }

    public string SshProxyPassword
    {
        get => _sshProxyPassword;
        set => this.RaiseAndSetIfChanged(ref _sshProxyPassword, value);
    }

    public bool SshLocalPortForwardEnabled
    {
        get => _sshLocalPortForwardEnabled;
        set
        {
            if (_sshLocalPortForwardEnabled == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _sshLocalPortForwardEnabled, value);
            RaiseSessionConfigurationVisibilityChanged();
        }
    }

    public string SshLocalPortForwardBindAddress
    {
        get => _sshLocalPortForwardBindAddress;
        set => this.RaiseAndSetIfChanged(ref _sshLocalPortForwardBindAddress, value);
    }

    public string SshLocalPortForwardSourcePort
    {
        get => _sshLocalPortForwardSourcePort;
        set => this.RaiseAndSetIfChanged(ref _sshLocalPortForwardSourcePort, value);
    }

    public string SshLocalPortForwardDestinationHost
    {
        get => _sshLocalPortForwardDestinationHost;
        set => this.RaiseAndSetIfChanged(ref _sshLocalPortForwardDestinationHost, value);
    }

    public string SshLocalPortForwardDestinationPort
    {
        get => _sshLocalPortForwardDestinationPort;
        set => this.RaiseAndSetIfChanged(ref _sshLocalPortForwardDestinationPort, value);
    }

    public bool SshX11Enabled
    {
        get => _sshX11Enabled;
        set
        {
            if (_sshX11Enabled == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _sshX11Enabled, value);
            RaiseSessionConfigurationVisibilityChanged();
        }
    }

    public string SshX11Display
    {
        get => _sshX11Display;
        set => this.RaiseAndSetIfChanged(ref _sshX11Display, value);
    }

    public string SshKeepAliveIntervalSeconds
    {
        get => _sshKeepAliveIntervalSeconds;
        set => this.RaiseAndSetIfChanged(ref _sshKeepAliveIntervalSeconds, value);
    }

    public string SshConnectTimeoutSeconds
    {
        get => _sshConnectTimeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _sshConnectTimeoutSeconds, value);
    }

    public void SetTerminalCapabilities(bool nativeVtAvailable)
    {
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(nativeVtAvailable);
        SetTerminalCapabilities(capabilities);
    }

    internal void SetTerminalCapabilities(TerminalModeCapabilities capabilities)
    {
        _terminalCapabilities = capabilities;
        NativeVtAvailable = capabilities.NativeVtAvailable;
        SetRenderMode(_activeRenderMode);
    }

    public void SetRenderMode(
        bool useRenderedControl,
        bool useNativeVtControl,
        bool useManagedVtControl = false)
    {
        TerminalRenderMode requestedMode = ResolveRequestedMode(
            useRenderedControl,
            useNativeVtControl,
            useManagedVtControl);
        SetRenderMode(requestedMode);
    }

    internal void SetRenderMode(TerminalRenderMode mode)
    {
        TerminalRenderMode resolvedMode = _modeResolver.ResolveSupportedMode(mode, _terminalCapabilities);
        ApplyRenderMode(resolvedMode);
    }

    public void SetShellProfiles(IReadOnlyList<ShellProfileOption> profiles)
    {
        IReadOnlyList<ShellProfileOption> normalizedProfiles = profiles.Count > 0
            ? profiles
            :
            [
                new ShellProfileOption("default", "Default shell", string.Empty),
            ];

        _shellProfiles = normalizedProfiles;
        this.RaisePropertyChanged(nameof(ShellProfiles));

        if (_selectedShellProfile is null || !ContainsShellProfile(normalizedProfiles, _selectedShellProfile.Id))
        {
            SelectedShellProfile = normalizedProfiles[0];
        }
    }

    public string GetNewTabModeName()
    {
        switch (_activeRenderMode)
        {
            case TerminalRenderMode.NativeVt:
                return $"Native VT ({SelectedTransportMode.DisplayName})";
            case TerminalRenderMode.ManagedVt:
                return $"Managed VT ({SelectedTransportMode.DisplayName})";
            default:
                return $"Rendered ({SelectedTransportMode.DisplayName})";
        }
    }

    public void SetCaptureState(bool isCaptureActive, bool hasCapture)
    {
        IsCaptureActive = isCaptureActive;
        HasCapture = hasCapture;
    }

    public void SetReplayState(
        bool isReplayEnabled,
        bool isReplayPlaying,
        double replayPositionSeconds,
        double replayDurationSeconds,
        string? replaySourceLabel = null)
    {
        IsReplayEnabled = isReplayEnabled;
        IsReplayPlaying = isReplayPlaying;
        ReplayDurationSeconds = Math.Max(0, replayDurationSeconds);
        ReplayTimelineValue = Math.Clamp(replayPositionSeconds, 0, ReplayDurationSeconds);
        if (replaySourceLabel is not null)
        {
            ReplaySourceLabel = replaySourceLabel;
        }
        else if (!isReplayEnabled)
        {
            ReplaySourceLabel = string.Empty;
        }
    }

    public void SetSearchState(string? needle, int total, int selected, bool usesNativeScrollback)
    {
        SearchQuery = needle ?? string.Empty;
        _searchApplied = !string.IsNullOrWhiteSpace(SearchQuery);
        _searchMatchTotal = Math.Max(0, total);
        _searchMatchSelected = _searchMatchTotal > 0
            ? Math.Clamp(selected, 0, _searchMatchTotal - 1)
            : -1;
        _searchUsesNativeScrollback = usesNativeScrollback;
        RaiseSearchSurfaceChanged();
    }

    public void ClearSearchState()
    {
        SearchQuery = string.Empty;
        _searchApplied = false;
        _searchMatchTotal = 0;
        _searchMatchSelected = -1;
        _searchUsesNativeScrollback = false;
        RaiseSearchSurfaceChanged();
    }

    public void SetGhosttyDiagnostics(bool show, string text)
    {
        ShowGhosttyDiagnostics = show;
        GhosttyDiagnosticsText = string.IsNullOrWhiteSpace(text)
            ? "Ghostty VT diagnostics are unavailable for the active tab."
            : text;
    }

    public void SetStatus(string text)
    {
        StatusText = text;
    }

    public void SetDimensions(int columns, int rows)
    {
        DimensionsText = $"{columns}x{rows}";
    }

    public void SetFontSizeFromSettings(double fontSize)
    {
        FontSize = Math.Clamp(fontSize, 8, 72);
    }

    private static string GetDefaultMonospaceFont()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
            "Consolas";
    }

    public TerminalSessionLoggingSettings GetSessionLoggingSettings()
    {
        return new TerminalSessionLoggingSettings
        {
            Enabled = SessionLoggingEnabled,
            FilePath = string.IsNullOrWhiteSpace(SessionLogFilePath)
                ? null
                : SessionLogFilePath.Trim(),
            Format = SelectedSessionLogFormat,
            FlushFrequently = SessionLogFlushFrequently,
            EventLogEnabled = EventLogEnabled,
        };
    }

    public void AppendEventLogEntry(string message)
    {
        if (!EventLogEnabled || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string timestamp = DateTimeOffset.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        _eventLogEntries.Add($"[{timestamp}] {message.Trim()}");
        if (_eventLogEntries.Count > 400)
        {
            _eventLogEntries.RemoveRange(0, _eventLogEntries.Count - 400);
        }

        RefreshEventLogSurface();
    }

    private IObservable<Unit> ActivateTab(object? parameter)
    {
        if (!TryParseInt(parameter, out int tabId))
        {
            return Observable.Return(Unit.Default);
        }

        return ActivateTabInteraction.Handle(tabId);
    }

    private IObservable<Unit> CloseTab(object? parameter)
    {
        if (!TryParseInt(parameter, out int tabId))
        {
            return Observable.Return(Unit.Default);
        }

        return CloseTabInteraction.Handle(tabId);
    }

    private IObservable<Unit> SwitchToTabByIndex(object? parameter)
    {
        if (!TryParseInt(parameter, out int index))
        {
            return Observable.Return(Unit.Default);
        }

        return SwitchToTabByIndexInteraction.Handle(index);
    }

    private IObservable<Unit> CopySelection()
    {
        return CopySelectionInteraction
            .Handle(Unit.Default)
            .Do(_ => SetStatus("Copied to clipboard"));
    }

    private IObservable<Unit> PasteClipboard()
    {
        return PasteClipboardInteraction
            .Handle(Unit.Default)
            .Do(_ => SetStatus("Pasted from clipboard"));
    }

    private IObservable<Unit> SelectAll()
    {
        return SelectAllInteraction
            .Handle(Unit.Default)
            .Do(_ => SetStatus("Selected all text"));
    }

    private IObservable<Unit> ChangeFontSize(int delta)
    {
        FontSize = Math.Clamp(FontSize + delta, 8, 32);
        return ApplyFontSizeInteraction
            .Handle(FontSize)
            .Do(_ => SetStatus($"Font size: {FontSize.ToString("0", CultureInfo.InvariantCulture)}"));
    }

    private IObservable<Unit> ResetFontSize()
    {
        FontSize = 14;
        return ApplyFontSizeInteraction
            .Handle(FontSize)
            .Do(_ => SetStatus($"Font size: {FontSize.ToString("0", CultureInfo.InvariantCulture)}"));
    }

    private IObservable<Unit> ToggleTheme()
    {
        string presetId = IsThemeDark(ActiveTheme)
            ? TerminalThemeCatalog.LightPresetId
            : _themeCatalog.GetDefaultPreset(_activeRenderMode).Id;

        ApplyPresetToMode(_activeRenderMode, presetId);
        return ApplyCurrentModeTheme($"Theme preset: {GetModeThemeState(_activeRenderMode).DisplayName}");
    }

    private IObservable<Unit> CycleThemePreset()
    {
        ModeThemeState state = GetModeThemeState(_activeRenderMode);
        int currentIndex = FindPresetIndex(state.PresetId);
        int nextIndex = (currentIndex + 1) % _themePresets.Count;
        TerminalThemePreset preset = _themePresets[nextIndex];

        ApplyPresetToMode(_activeRenderMode, preset.Id);
        return ApplyCurrentModeTheme($"Theme preset: {preset.DisplayName}");
    }

    private IObservable<Unit> GenerateTheme()
    {
        ModeThemeState state = GetModeThemeState(_activeRenderMode);
        state.Generation++;
        state.Theme = _themeCatalog.CreateGeneratedTheme(_activeRenderMode, state.Generation);
        state.DisplayName = $"Generated {_activeRenderMode} #{state.Generation}";
        state.PresetId = null;
        UpdateThemePresetButtonText();

        return ApplyCurrentModeTheme($"Generated theme: {state.DisplayName}");
    }

    private IObservable<Unit> ToggleCapture()
    {
        bool shouldStartCapture = !IsCaptureActive;
        return ToggleCaptureInteraction.Handle(shouldStartCapture);
    }

    private IObservable<Unit> SaveCapture()
    {
        return SaveCaptureInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> LoadReplay()
    {
        return LoadReplayInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> ToggleReplayPlayback()
    {
        bool shouldPlay = !IsReplayPlaying;
        return SetReplayPlayingInteraction.Handle(shouldPlay);
    }

    private IObservable<Unit> StopReplay()
    {
        return StopReplayInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> PrepareSettingsPanel()
    {
        return PrepareSettingsPanelInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> ApplySearch()
    {
        return ApplySearchInteraction.Handle(SearchQuery);
    }

    private IObservable<Unit> NextSearch()
    {
        return NextSearchInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> PreviousSearch()
    {
        return PreviousSearchInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> ClearSearch()
    {
        return ClearSearchInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> ShowHyperlinkSample()
    {
        return ShowHyperlinkSampleInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> ShowKittyGraphicsSample()
    {
        return ShowKittyGraphicsSampleInteraction.Handle(Unit.Default);
    }

    private IObservable<Unit> ToggleGhosttyDiagnostics()
    {
        return ToggleGhosttyDiagnosticsInteraction.Handle(!ShowGhosttyDiagnostics);
    }

    private IObservable<Unit> CopySnapshot(TerminalSnapshotExportFormat format)
    {
        return CopySnapshotInteraction.Handle(format);
    }

    private void ClearEventLog()
    {
        _eventLogEntries.Clear();
        RefreshEventLogSurface();
    }

    private void CycleRenderMode()
    {
        TerminalRenderMode nextMode = _modeResolver.ResolveNextMode(_activeRenderMode, _terminalCapabilities);
        ApplyRenderMode(nextMode);

        SetStatus($"New tabs will use: {GetNewTabModeName()}");
    }

    private void UpdateModeButtonText()
    {
        ModeButtonText = _activeRenderMode switch
        {
            TerminalRenderMode.NativeVt => "Native VT",
            TerminalRenderMode.ManagedVt => "Managed VT",
            _ => "Rendered",
        };
    }

    private void ApplyRenderMode(TerminalRenderMode mode)
    {
        _activeRenderMode = mode;
        UseRenderedControl = mode == TerminalRenderMode.RenderedAuto;
        UseNativeVtControl = mode == TerminalRenderMode.NativeVt;
        UseManagedVtControl = mode == TerminalRenderMode.ManagedVt;
        UpdateModeButtonText();
        UpdateThemePresetButtonText();
        IsDarkTheme = IsThemeDark(ActiveTheme);
    }

    private void InitializeModeThemes()
    {
        foreach (TerminalRenderMode mode in Enum.GetValues<TerminalRenderMode>())
        {
            TerminalThemePreset preset = _themeCatalog.GetDefaultPreset(mode);
            TerminalTheme theme = _themeCatalog.CreatePresetTheme(preset.Id, mode);
            _modeThemes[mode] = new ModeThemeState(
                preset.Id,
                preset.DisplayName,
                theme,
                0);
        }
    }

    private void ApplyPresetToMode(TerminalRenderMode mode, string presetId)
    {
        ModeThemeState state = GetModeThemeState(mode);
        TerminalThemePreset preset = _themeCatalog.GetPreset(presetId);
        state.PresetId = preset.Id;
        state.DisplayName = preset.DisplayName;
        state.Theme = _themeCatalog.CreatePresetTheme(preset.Id, mode);
        state.Generation = 0;
        UpdateThemePresetButtonText();
    }

    private IObservable<Unit> ApplyCurrentModeTheme(string statusText)
    {
        TerminalTheme theme = ActiveTheme;
        string themeName = GetModeThemeState(_activeRenderMode).DisplayName;
        IsDarkTheme = IsThemeDark(theme);

        return ApplyThemeModelInteraction
            .Handle(new TerminalThemeApplyRequest(theme, themeName))
            .Do(_ => SetStatus(statusText));
    }

    private ModeThemeState GetModeThemeState(TerminalRenderMode mode)
    {
        if (_modeThemes.TryGetValue(mode, out ModeThemeState? state))
        {
            return state;
        }

        TerminalThemePreset preset = _themeCatalog.GetDefaultPreset(mode);
        state = new ModeThemeState(
            preset.Id,
            preset.DisplayName,
            _themeCatalog.CreatePresetTheme(preset.Id, mode),
            0);
        _modeThemes[mode] = state;
        return state;
    }

    private int FindPresetIndex(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return -1;
        }

        for (int i = 0; i < _themePresets.Count; i++)
        {
            if (string.Equals(_themePresets[i].Id, presetId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void UpdateThemePresetButtonText()
    {
        ThemePresetButtonText = $"Theme: {GetModeThemeState(_activeRenderMode).DisplayName}";
    }

    private static bool IsThemeDark(TerminalTheme theme)
    {
        double luminance = RelativeLuminance(theme.DefaultBackground);
        return luminance < 0.5;
    }

    private static double RelativeLuminance(uint argb)
    {
        double r = ((argb >> 16) & 0xFF) / 255.0;
        double g = ((argb >> 8) & 0xFF) / 255.0;
        double b = (argb & 0xFF) / 255.0;

        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        double lr = ToLinear(r);
        double lg = ToLinear(g);
        double lb = ToLinear(b);
        return (0.2126 * lr) + (0.7152 * lg) + (0.0722 * lb);
    }

    private static TerminalRenderMode ResolveRequestedMode(
        bool useRenderedControl,
        bool useNativeVtControl,
        bool useManagedVtControl)
    {
        if (useNativeVtControl)
        {
            return TerminalRenderMode.NativeVt;
        }

        if (useManagedVtControl)
        {
            return TerminalRenderMode.ManagedVt;
        }

        if (useRenderedControl)
        {
            return TerminalRenderMode.RenderedAuto;
        }

        return TerminalRenderMode.RenderedAuto;
    }

    private void RaiseSessionConfigurationVisibilityChanged()
    {
        this.RaisePropertyChanged(nameof(ShowSessionSettingsCategory));
        this.RaisePropertyChanged(nameof(ShowConnectionSettingsCategory));
        this.RaisePropertyChanged(nameof(ShowTerminalSettingsCategory));
        this.RaisePropertyChanged(nameof(ShowAppearanceSettingsCategory));
        this.RaisePropertyChanged(nameof(ShowSshSettingsCategory));
        this.RaisePropertyChanged(nameof(ShowLoggingSettingsCategory));
        this.RaisePropertyChanged(nameof(ShowSessionTransportPicker));
        this.RaisePropertyChanged(nameof(IsSessionTransportConfigEnabled));
        this.RaisePropertyChanged(nameof(ShowSessionTransportHint));
        this.RaisePropertyChanged(nameof(ShowLocalSessionFields));
        this.RaisePropertyChanged(nameof(ShowSshSessionFields));
        this.RaisePropertyChanged(nameof(IsPtyTransportSelected));
        this.RaisePropertyChanged(nameof(IsPipeTransportSelected));
        this.RaisePropertyChanged(nameof(IsRawTcpTransportSelected));
        this.RaisePropertyChanged(nameof(IsTelnetTransportSelected));
        this.RaisePropertyChanged(nameof(IsSerialTransportSelected));
        this.RaisePropertyChanged(nameof(IsSshTransportSelected));
        this.RaisePropertyChanged(nameof(ShowTerminalSshFields));
        this.RaisePropertyChanged(nameof(ShowTerminalTelnetFields));
        this.RaisePropertyChanged(nameof(ShowTerminalSettingsHint));
        this.RaisePropertyChanged(nameof(ShowSshSettingsUnavailableHint));
        this.RaisePropertyChanged(nameof(ShowSshSettingsFields));
        this.RaisePropertyChanged(nameof(ShowSshProxyFields));
        this.RaisePropertyChanged(nameof(ShowSshPortForwardFields));
        this.RaisePropertyChanged(nameof(ShowSshX11DisplayField));
        this.RaisePropertyChanged(nameof(ShowSshPasswordField));
        this.RaisePropertyChanged(nameof(ShowSshPrivateKeyField));
        this.RaisePropertyChanged(nameof(ShowSshAgentHint));
    }

    private static string FormatDuration(double seconds)
    {
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static bool ContainsShellProfile(IReadOnlyList<ShellProfileOption> profiles, string id)
    {
        for (int i = 0; i < profiles.Count; i++)
        {
            if (string.Equals(profiles[i].Id, id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseInt(object? value, out int result)
    {
        switch (value)
        {
            case int intValue:
                result = intValue;
                return true;
            case string stringValue when int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
                result = parsed;
                return true;
            default:
                result = -1;
                return false;
        }
    }

    private void RefreshEventLogSurface()
    {
        EventLogText = string.Join(Environment.NewLine, _eventLogEntries);
        this.RaisePropertyChanged(nameof(HasEventLogEntries));
        this.RaisePropertyChanged(nameof(EventLogEntryCountText));
    }

    private void RaiseSearchSurfaceChanged()
    {
        this.RaisePropertyChanged(nameof(CanAdvanceSearch));
        this.RaisePropertyChanged(nameof(CanClearSearch));
        this.RaisePropertyChanged(nameof(SearchResultText));
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

    private sealed class ModeThemeState
    {
        public ModeThemeState(string? presetId, string displayName, TerminalTheme theme, int generation)
        {
            PresetId = presetId;
            DisplayName = displayName;
            Theme = theme;
            Generation = generation;
        }

        public string? PresetId { get; set; }

        public string DisplayName { get; set; }

        public TerminalTheme Theme { get; set; }

        public int Generation { get; set; }
    }
}

public sealed record TransportModeOption(string Id, string DisplayName);

public sealed record ShellProfileOption(string Id, string DisplayName, string CommandPath);

public sealed record SettingsCategoryOption(string Id, string DisplayName)
{
    public const string SessionCategoryId = "session";
    public const string ConnectionCategoryId = "connection";
    public const string TerminalCategoryId = "terminal";
    public const string AppearanceCategoryId = "appearance";
    public const string SshCategoryId = "ssh";
    public const string LoggingCategoryId = "logging";
}

public sealed record SshAuthModeOption(string Id, string DisplayName)
{
    public const string PasswordModeId = "password";
    public const string PrivateKeyModeId = "private-key";
    public const string AgentModeId = "agent";
    public const string PasswordAndKeyModeId = "password-key";
}
