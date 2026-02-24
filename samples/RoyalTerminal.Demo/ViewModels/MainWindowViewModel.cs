// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Main window view model and command surface.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using ReactiveUI;

namespace RoyalTerminal.Demo.ViewModels;

public sealed class MainWindowViewModel : ReactiveObject
{
    private double _fontSize = 14.0;
    private bool _isDarkTheme = true;
    private string _themePresetButtonText = "Theme: Default";
    private bool _ghosttyAvailable;
    private bool _nativeVtAvailable;
    private bool _useNativeControl;
    private bool _useRenderedControl;
    private bool _useNativeVtControl;
    private bool _useManagedVtControl;
    private bool _useTextureInterop;
    private string _statusText = "Ready";
    private string _dimensionsText = "80x24";
    private string _modeButtonText = "Rendered";
    private TerminalModeCapabilities _terminalCapabilities = TerminalModeCapabilities.Create(
        embeddedGhosttyAvailable: false,
        nativeVtAvailable: false);
    private TerminalRenderMode _activeRenderMode = TerminalRenderMode.RenderedAuto;
    private readonly ITerminalModeResolver _modeResolver;
    private readonly ITerminalThemeCatalog _themeCatalog;
    private readonly IReadOnlyList<TerminalThemePreset> _themePresets;
    private readonly Dictionary<TerminalRenderMode, ModeThemeState> _modeThemes = [];

    private IReadOnlyList<ShellProfileOption> _shellProfiles =
    [
        new ShellProfileOption("default", "Default shell", string.Empty),
    ];
    private ShellProfileOption? _selectedShellProfile;

    private readonly IReadOnlyList<TransportModeOption> _transportModes;
    private TransportModeOption _selectedTransportMode;

    private readonly IReadOnlyList<SshAuthModeOption> _sshAuthModes;
    private SshAuthModeOption _selectedSshAuthMode;

    private string _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private string _pipeCommandText = "echo RoyalTerminal pipe transport";
    private bool _pipeMergeStdErrIntoStdOut = true;

    private string _sshHost = "localhost";
    private string _sshPort = "22";
    private string _sshUsername = Environment.UserName;
    private string _sshPassword = string.Empty;
    private string _sshPrivateKeyPath = string.Empty;
    private string _sshExpectedHostKeyFingerprintSha256 = string.Empty;
    private string _sshTerminalType = "xterm-256color";
    private string _sshInitialCommand = string.Empty;
    private bool _sshRequestPty = true;

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

        _transportModes =
        [
            new TransportModeOption(TerminalTransportIds.Pty, "PTY"),
            new TransportModeOption(TerminalTransportIds.Pipe, "Pipe"),
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
        ApplyFontSizeInteraction = new Interaction<double, Unit>();
        ApplyThemeInteraction = new Interaction<bool, Unit>();
        ApplyThemeModelInteraction = new Interaction<TerminalThemeApplyRequest, Unit>();
        ApplyRenderedBackendInteraction = new Interaction<bool, Unit>();

        NewTabCommand = ReactiveCommand.CreateFromObservable(() => CreateNewTabInteraction.Handle(Unit.Default));
        CloseCurrentTabCommand = ReactiveCommand.CreateFromObservable(() => CloseCurrentTabInteraction.Handle(Unit.Default));
        ActivateTabCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(ActivateTab);
        CloseTabCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(CloseTab);
        SwitchToTabByIndexCommand = ReactiveCommand.CreateFromObservable<object?, Unit>(SwitchToTabByIndex);
        CycleTabForwardCommand = ReactiveCommand.CreateFromObservable(() => CycleTabInteraction.Handle(true));
        CycleTabBackwardCommand = ReactiveCommand.CreateFromObservable(() => CycleTabInteraction.Handle(false));
        CopySelectionCommand = ReactiveCommand.CreateFromObservable(CopySelection);
        PasteClipboardCommand = ReactiveCommand.CreateFromObservable(PasteClipboard);
        IncreaseFontSizeCommand = ReactiveCommand.CreateFromObservable(() => ChangeFontSize(1));
        DecreaseFontSizeCommand = ReactiveCommand.CreateFromObservable(() => ChangeFontSize(-1));
        ResetFontSizeCommand = ReactiveCommand.CreateFromObservable(ResetFontSize);
        ToggleThemeCommand = ReactiveCommand.CreateFromObservable(ToggleTheme);
        CycleThemePresetCommand = ReactiveCommand.CreateFromObservable(CycleThemePreset);
        GenerateThemeCommand = ReactiveCommand.CreateFromObservable(GenerateTheme);
        ToggleRenderedBackendCommand = ReactiveCommand.CreateFromObservable(ToggleRenderedBackend);
        CycleRenderModeCommand = ReactiveCommand.Create(CycleRenderMode);

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
    public Interaction<double, Unit> ApplyFontSizeInteraction { get; }
    public Interaction<bool, Unit> ApplyThemeInteraction { get; }
    public Interaction<TerminalThemeApplyRequest, Unit> ApplyThemeModelInteraction { get; }
    public Interaction<bool, Unit> ApplyRenderedBackendInteraction { get; }

    public ReactiveCommand<Unit, Unit> NewTabCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCurrentTabCommand { get; }
    public ReactiveCommand<object?, Unit> ActivateTabCommand { get; }
    public ReactiveCommand<object?, Unit> CloseTabCommand { get; }
    public ReactiveCommand<object?, Unit> SwitchToTabByIndexCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleTabForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleTabBackwardCommand { get; }
    public ReactiveCommand<Unit, Unit> CopySelectionCommand { get; }
    public ReactiveCommand<Unit, Unit> PasteClipboardCommand { get; }
    public ReactiveCommand<Unit, Unit> IncreaseFontSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> DecreaseFontSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetFontSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleThemePresetCommand { get; }
    public ReactiveCommand<Unit, Unit> GenerateThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleRenderedBackendCommand { get; }
    public ReactiveCommand<Unit, Unit> CycleRenderModeCommand { get; }

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

    public bool GhosttyAvailable
    {
        get => _ghosttyAvailable;
        private set => this.RaiseAndSetIfChanged(ref _ghosttyAvailable, value);
    }

    public bool NativeVtAvailable
    {
        get => _nativeVtAvailable;
        private set => this.RaiseAndSetIfChanged(ref _nativeVtAvailable, value);
    }

    public bool UseNativeControl
    {
        get => _useNativeControl;
        private set
        {
            if (_useNativeControl == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _useNativeControl, value);
            RaiseSessionConfigurationVisibilityChanged();
        }
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

    public bool UseTextureInterop
    {
        get => _useTextureInterop;
        private set
        {
            if (_useTextureInterop == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _useTextureInterop, value);
            this.RaisePropertyChanged(nameof(RenderedBackendButtonText));
        }
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

    public string RenderedBackendButtonText
        => UseTextureInterop ? "Backend: Interop (Preview)" : "Backend: CPU";

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

    public bool ShowSessionTransportPicker => true;

    public bool IsSessionTransportConfigEnabled => !UseRenderedControl && !UseNativeControl;

    public bool ShowSessionTransportHint => !IsSessionTransportConfigEnabled;

    public bool ShowLocalSessionFields => !IsSshTransportSelected;

    public bool ShowSshSessionFields => IsSshTransportSelected;

    public bool IsPipeTransportSelected
        => string.Equals(SelectedTransportMode.Id, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase);

    public bool IsSshTransportSelected
        => string.Equals(SelectedTransportMode.Id, TerminalTransportIds.Ssh, StringComparison.OrdinalIgnoreCase);

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

    public void SetTerminalCapabilities(bool ghosttyAvailable, bool nativeVtAvailable)
    {
        TerminalModeCapabilities capabilities = TerminalModeCapabilities.Create(
            embeddedGhosttyAvailable: ghosttyAvailable,
            nativeVtAvailable: nativeVtAvailable);
        SetTerminalCapabilities(capabilities);
    }

    internal void SetTerminalCapabilities(TerminalModeCapabilities capabilities)
    {
        _terminalCapabilities = capabilities;
        GhosttyAvailable = capabilities.EmbeddedGhosttyNativeAvailable
            || capabilities.EmbeddedGhosttyRenderedAvailable;
        NativeVtAvailable = capabilities.NativeVtAvailable;
        SetRenderMode(_activeRenderMode);
    }

    public void SetRenderMode(
        bool useRenderedControl,
        bool useNativeControl,
        bool useNativeVtControl,
        bool useManagedVtControl = false)
    {
        TerminalRenderMode requestedMode = ResolveRequestedMode(
            useRenderedControl,
            useNativeControl,
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
            case TerminalRenderMode.GhosttyRendered:
                return UseTextureInterop
                    ? "Rendered (Ghostty VT + TextureInterop)"
                    : "Rendered (Ghostty VT + CPU Cell Renderer)";
            case TerminalRenderMode.GhosttyNative:
                return "Native (Ghostty Metal)";
            case TerminalRenderMode.NativeVt:
                return $"Native VT ({SelectedTransportMode.DisplayName})";
            case TerminalRenderMode.ManagedVt:
                return $"Managed VT ({SelectedTransportMode.DisplayName})";
            default:
                return $"Rendered ({SelectedTransportMode.DisplayName})";
        }
    }

    public void SetStatus(string text)
    {
        StatusText = text;
    }

    public void SetDimensions(int columns, int rows)
    {
        DimensionsText = $"{columns}x{rows}";
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

    private IObservable<Unit> ToggleRenderedBackend()
    {
        UseTextureInterop = !UseTextureInterop;
        return ApplyRenderedBackendInteraction
            .Handle(UseTextureInterop)
            .Do(_ => SetStatus(
                $"Rendered backend: {(UseTextureInterop ? "TextureInterop (Preview)" : "CPU Cell Renderer")}"));
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
            TerminalRenderMode.GhosttyRendered => "Ghostty Rendered",
            TerminalRenderMode.GhosttyNative => "Ghostty Native",
            TerminalRenderMode.NativeVt => "Native VT",
            TerminalRenderMode.ManagedVt => "Managed VT",
            _ => "Rendered",
        };
    }

    private void ApplyRenderMode(TerminalRenderMode mode)
    {
        _activeRenderMode = mode;
        UseRenderedControl = mode == TerminalRenderMode.GhosttyRendered;
        UseNativeControl = mode == TerminalRenderMode.GhosttyNative;
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
        bool useNativeControl,
        bool useNativeVtControl,
        bool useManagedVtControl)
    {
        if (useRenderedControl)
        {
            return TerminalRenderMode.GhosttyRendered;
        }

        if (useNativeControl)
        {
            return TerminalRenderMode.GhosttyNative;
        }

        if (useNativeVtControl)
        {
            return TerminalRenderMode.NativeVt;
        }

        if (useManagedVtControl)
        {
            return TerminalRenderMode.ManagedVt;
        }

        return TerminalRenderMode.RenderedAuto;
    }

    private void RaiseSessionConfigurationVisibilityChanged()
    {
        this.RaisePropertyChanged(nameof(ShowSessionTransportPicker));
        this.RaisePropertyChanged(nameof(IsSessionTransportConfigEnabled));
        this.RaisePropertyChanged(nameof(ShowSessionTransportHint));
        this.RaisePropertyChanged(nameof(ShowLocalSessionFields));
        this.RaisePropertyChanged(nameof(ShowSshSessionFields));
        this.RaisePropertyChanged(nameof(IsPipeTransportSelected));
        this.RaisePropertyChanged(nameof(IsSshTransportSelected));
        this.RaisePropertyChanged(nameof(ShowSshPasswordField));
        this.RaisePropertyChanged(nameof(ShowSshPrivateKeyField));
        this.RaisePropertyChanged(nameof(ShowSshAgentHint));
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

public sealed record SshAuthModeOption(string Id, string DisplayName)
{
    public const string PasswordModeId = "password";
    public const string PrivateKeyModeId = "private-key";
    public const string AgentModeId = "agent";
    public const string PasswordAndKeyModeId = "password-key";
}
