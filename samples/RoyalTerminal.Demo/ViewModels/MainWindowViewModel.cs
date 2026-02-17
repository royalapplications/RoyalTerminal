// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Main window view model and command surface.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using RoyalTerminal.Terminal;
using ReactiveUI;

namespace RoyalTerminal.Demo.ViewModels;

public sealed class MainWindowViewModel : ReactiveObject
{
    private double _fontSize = 14.0;
    private bool _isDarkTheme = true;
    private bool _ghosttyAvailable;
    private bool _nativeVtAvailable;
    private bool _useNativeControl;
    private bool _useRenderedControl;
    private bool _useNativeVtControl;
    private bool _useTextureInterop;
    private string _statusText = "Ready";
    private string _dimensionsText = "80x24";
    private string _modeButtonText = "Rendered";

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
    {
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
        ToggleRenderedBackendCommand = ReactiveCommand.CreateFromObservable(ToggleRenderedBackend);
        CycleRenderModeCommand = ReactiveCommand.Create(CycleRenderMode);
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
        GhosttyAvailable = ghosttyAvailable;
        NativeVtAvailable = nativeVtAvailable;
        UpdateModeButtonText();
    }

    public void SetRenderMode(bool useRenderedControl, bool useNativeControl, bool useNativeVtControl)
    {
        UseRenderedControl = useRenderedControl;
        UseNativeControl = useNativeControl;
        UseNativeVtControl = useNativeVtControl;
        UpdateModeButtonText();
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
        if (UseRenderedControl)
        {
            return UseTextureInterop
                ? "Rendered (Ghostty VT + TextureInterop)"
                : "Rendered (Ghostty VT + CPU Cell Renderer)";
        }

        if (UseNativeControl)
        {
            return "Native (Ghostty Metal)";
        }

        string transportName = SelectedTransportMode.DisplayName;
        if (UseNativeVtControl)
        {
            return $"Native VT ({transportName})";
        }

        return $"Rendered ({transportName})";
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
        IsDarkTheme = !IsDarkTheme;
        return ApplyThemeInteraction
            .Handle(IsDarkTheme)
            .Do(_ => SetStatus(IsDarkTheme ? "Dark theme" : "Light theme"));
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
        if (GhosttyAvailable)
        {
            // Cycle: Rendered -> Native -> Native VT -> Standalone -> Rendered.
            if (UseRenderedControl)
            {
                SetRenderMode(useRenderedControl: false, useNativeControl: true, useNativeVtControl: false);
            }
            else if (UseNativeControl)
            {
                SetRenderMode(
                    useRenderedControl: false,
                    useNativeControl: false,
                    useNativeVtControl: NativeVtAvailable);
            }
            else if (UseNativeVtControl)
            {
                SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
            }
            else
            {
                SetRenderMode(useRenderedControl: true, useNativeControl: true, useNativeVtControl: false);
            }
        }
        else if (NativeVtAvailable)
        {
            // Cycle: Native VT -> Standalone -> Native VT.
            SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: !UseNativeVtControl);
        }
        else
        {
            SetStatus("Only Rendered mode is available on this platform");
            return;
        }

        SetStatus($"New tabs will use: {GetNewTabModeName()}");
    }

    private void UpdateModeButtonText()
    {
        ModeButtonText = UseRenderedControl ? "Ghostty Rendered"
            : UseNativeControl ? "Ghostty Native"
            : UseNativeVtControl ? "Native VT"
            : "Rendered";
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
