// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Runtime controller for terminal tab orchestration.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Capture;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Demo.Views;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Shaders;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Pipe;
using RoyalTerminal.Terminal.Transport.Pty;
using RoyalTerminal.Terminal.Transport.Raw;
using RoyalTerminal.Terminal.Transport.Serial;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent;
using RoyalTerminal.Terminal.Transport.Telnet;
using ReactiveUI;

namespace RoyalTerminal.Demo.Services;

internal sealed class MainWindowController
{
    private const string DisableTextShapingEnvVar = "ROYALTERMINAL_DISABLE_TEXT_SHAPING";
    private const string DisableSessionAutostartEnvVar = "ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART";
    private const string EnableRenderDiagnosticsEnvVar = "ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS";
    private const string StartAllRenderModesEnvVar = "ROYALTERMINAL_DEMO_START_ALL_RENDER_MODES";
    private const string TextRenderPipelineEnvVar = "ROYALTERMINAL_TEXT_RENDER_PIPELINE";
    private const string DismissRegularIconResourceKey = "Icon.DismissRegular";
    private const string DismissRegularIconPathData =
        "M4.39705 4.55379L4.46967 4.46967C4.73594 4.2034 5.1526 4.1792 5.44621 4.39705" +
        "L5.53033 4.46967L12 10.939L18.4697 4.46967C18.7626 4.17678 19.2374 4.17678 19.5303 4.46967" +
        "C19.8232 4.76256 19.8232 5.23744 19.5303 5.53033L13.061 12L19.5303 18.4697" +
        "C19.7966 18.7359 19.8208 19.1526 19.6029 19.4462L19.5303 19.5303" +
        "C19.2641 19.7966 18.8474 19.8208 18.5538 19.6029L18.4697 19.5303L12 13.061" +
        "L5.53033 19.5303C5.23744 19.8232 4.76256 19.8232 4.46967 19.5303" +
        "C4.17678 19.2374 4.17678 18.7626 4.46967 18.4697L10.939 12L4.46967 5.53033" +
        "C4.2034 5.26406 4.1792 4.8474 4.39705 4.55379L4.46967 4.46967L4.39705 4.55379Z";
    private static readonly byte[] s_hyperlinkShowcaseBytes = Encoding.UTF8.GetBytes(
        "\r\n\u001b[1mRoyalTerminal OSC8 hyperlink showcase\u001b[0m\r\n" +
        "\u001b]8;;https://ghostty.org\u001b\\Ghostty docs\u001b]8;;\u001b\\  |  " +
        "\u001b]8;;https://github.com/ghostty-org/ghostling\u001b\\Ghostling example\u001b]8;;\u001b\\\r\n" +
        "Ctrl/Cmd+click the highlighted span to launch the link.\r\n");
    private static readonly byte[] s_kittyGraphicsShowcaseBytes = Encoding.UTF8.GetBytes(
        "\r\n\u001b[1mRoyalTerminal Kitty Graphics showcase\u001b[0m\r\n" +
        "Ghostty VT renders the image placement below via the native Kitty Graphics API.\r\n" +
        "\u001b_Ga=T,t=d,f=24,i=1,p=1,s=1,v=2,c=10,r=1;////////\u001b\\\r\n");

    private static readonly string MonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";
    private static readonly bool s_disableTextShaping = ReadEnvironmentToggle(DisableTextShapingEnvVar);
    private static readonly bool s_enableRenderDiagnostics = ReadEnvironmentToggle(EnableRenderDiagnosticsEnvVar);
    private static readonly TerminalTextRenderPipeline s_textRenderPipeline = ReadTextRenderPipeline(TextRenderPipelineEnvVar);
    private static readonly Geometry s_dismissRegularIconFallback = StreamGeometry.Parse(DismissRegularIconPathData);

    private readonly Window _window;
    private readonly MainWindowViewModel _viewModel;
    private readonly Grid _terminalHost;
    private readonly StackPanel _tabStrip;
    private readonly List<TerminalTab> _tabs = [];
    private readonly HashSet<TerminalControl> _startingStandaloneControls = [];
    private readonly Dictionary<TerminalControl, TerminalCaptureRuntime> _captureRuntimes = [];
    private readonly Dictionary<TerminalControl, EventHandler<TerminalDataEventArgs>> _sessionLogOutputHandlers = [];
    private readonly Dictionary<TerminalControl, SessionLogWriter> _sessionLogWriters = [];
    private readonly Dictionary<TerminalControl, EventHandler<TerminalShellIntegrationEventArgs>> _commandHistoryHandlers = [];
    private readonly Dictionary<TerminalControl, TerminalCommandHistoryCaptureService> _commandHistoryCaptures = [];
    private readonly Dictionary<TerminalControl, TerminalLaunchConfiguration> _launchConfigurations = [];
    private readonly Dictionary<TerminalControl, TerminalPaneRuntimeNode> _paneRuntimeNodes = [];
    private readonly HashSet<Task> _commandHistoryWriteTasks = [];
    private readonly object _commandHistoryWriteTaskSync = new();
    private readonly ITerminalModeCapabilityResolver _modeCapabilityResolver;
    private readonly ITerminalModeResolver _modeResolver;
    private readonly ITerminalSessionProfileStore _settingsProfileStore;
    private readonly ITerminalCommandHistoryStore _commandHistoryStore;
    private readonly ITerminalWorkspaceStore _workspaceStore;
    private readonly TerminalCommandSuggestionService _commandSuggestionService = new();
    private readonly SemaphoreSlim _commandHistorySync = new(1, 1);
    private bool _suppressReplayTimelineSeek;
    private bool _settingsProfilesLoaded;
    private TerminalCommandHistoryDocument? _commandHistoryDocument;
    private TerminalSessionProfilesDocument? _sessionLauncherDocument;

    private TerminalTab? _activeTab;
    private TerminalControl? _activePaneControl;
    private int _tabCounter;
    private int _paneCounter;
    private TerminalModeCapabilities _terminalCapabilities = TerminalModeCapabilities.Create(nativeVtAvailable: false);

    public MainWindowController(Window window, MainWindowViewModel viewModel)
        : this(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default)
    {
    }

    internal MainWindowController(
        Window window,
        MainWindowViewModel viewModel,
        ITerminalModeCapabilityResolver modeCapabilityResolver,
        ITerminalModeResolver modeResolver,
        ITerminalSessionProfileStore? settingsProfileStore = null,
        ITerminalCommandHistoryStore? commandHistoryStore = null,
        ITerminalWorkspaceStore? workspaceStore = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel;
        _modeCapabilityResolver = modeCapabilityResolver
            ?? throw new ArgumentNullException(nameof(modeCapabilityResolver));
        _modeResolver = modeResolver ?? throw new ArgumentNullException(nameof(modeResolver));
        _settingsProfileStore = settingsProfileStore ?? TerminalSessionProfileStoreFactory.CreateDefault();
        _commandHistoryStore = commandHistoryStore ?? TerminalCommandHistoryStoreFactory.CreateDefault();
        _workspaceStore = workspaceStore ?? TerminalWorkspaceStoreFactory.CreateDefault();
        _terminalHost = _window.FindControl<Grid>("TerminalHost")
            ?? throw new InvalidOperationException("TerminalHost was not found in MainWindow.");
        _tabStrip = _window.FindControl<StackPanel>("TabStrip")
            ?? throw new InvalidOperationException("TabStrip was not found in MainWindow.");
    }

    public IDisposable Activate()
    {
        CompositeDisposable lifetime = new();
        RegisterInteractionHandlers(lifetime);

        _terminalCapabilities = _modeCapabilityResolver.Resolve(GhosttyVtProcessor.IsAvailable());
        _viewModel.SetTerminalCapabilities(_terminalCapabilities);

        TerminalRenderMode startupMode = TerminalRenderMode.RenderedAuto;
        _viewModel.SetRenderMode(startupMode);

        ApplyThemeResources(CreateChromePalette(_viewModel.ActiveTheme));
        InitializeShellProfiles();
        RefreshSessionLauncherForActivation();

        CreateInitialTabs();
        SyncCaptureReplayState();
        string startupStatus = _viewModel.UseRenderedControl
            ? "Rendered terminal ready"
            : _viewModel.UseNativeVtControl
                ? "Native VT (libghostty-vt) ready"
                : _viewModel.UseManagedVtControl
                    ? "Managed VT (BasicVtProcessor) ready"
                    : "Rendered terminal ready";
        string? nativeModeHint = BuildNativeModeAvailabilityHint();
        UpdateStatus(nativeModeHint is null
            ? startupStatus
            : $"{startupStatus} | {nativeModeHint}");
        SyncActiveTerminalSurface();

        lifetime.Add(Disposable.Create(DisposeResources));
        return lifetime;
    }

    private void RegisterInteractionHandlers(CompositeDisposable disposables)
    {
        disposables.Add(_viewModel.CreateNewTabInteraction.RegisterHandler(context =>
        {
            CreateNewTab();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CloseCurrentTabInteraction.RegisterHandler(context =>
        {
            CloseCurrentTab();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ActivateTabInteraction.RegisterHandler(context =>
        {
            ActivateTabById(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CloseTabInteraction.RegisterHandler(context =>
        {
            CloseTabById(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.SwitchToTabByIndexInteraction.RegisterHandler(context =>
        {
            SwitchToTab(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CycleTabInteraction.RegisterHandler(context =>
        {
            CycleTab(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CopySelectionInteraction.RegisterHandler(async context =>
        {
            await CopySelection();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.PasteClipboardInteraction.RegisterHandler(async context =>
        {
            await PasteClipboard();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.SelectAllInteraction.RegisterHandler(context =>
        {
            SelectAll();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ApplyFontSizeInteraction.RegisterHandler(context =>
        {
            ApplyFontSize(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ApplyThemeInteraction.RegisterHandler(context =>
        {
            ApplyTheme(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ApplyThemeModelInteraction.RegisterHandler(context =>
        {
            ApplyTheme(context.Input.Theme);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ToggleCaptureInteraction.RegisterHandler(context =>
        {
            ToggleCapture(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.SaveCaptureInteraction.RegisterHandler(async context =>
        {
            await SaveCaptureAsync();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.LoadReplayInteraction.RegisterHandler(async context =>
        {
            await LoadReplayAsync();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.SetReplayPlayingInteraction.RegisterHandler(context =>
        {
            SetReplayPlaying(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.StopReplayInteraction.RegisterHandler(context =>
        {
            StopReplay();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.PrepareSettingsPanelInteraction.RegisterHandler(async context =>
        {
            await PrepareSettingsPanelAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => context.SetOutput(Unit.Default));
        }));

        disposables.Add(_viewModel.RefreshSessionLauncherInteraction.RegisterHandler(async context =>
        {
            await RefreshSessionLauncherAsync();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.LaunchSessionProfileInteraction.RegisterHandler(async context =>
        {
            await LaunchSessionProfileAsync(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ApplySearchInteraction.RegisterHandler(context =>
        {
            ApplySearch(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.NextSearchInteraction.RegisterHandler(context =>
        {
            SelectNextSearchMatch();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.PreviousSearchInteraction.RegisterHandler(context =>
        {
            SelectPreviousSearchMatch();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ClearSearchInteraction.RegisterHandler(context =>
        {
            ClearSearch();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.RefreshCommandSuggestionsInteraction.RegisterHandler(async context =>
        {
            await RefreshCommandSuggestionsAsync(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.AcceptCommandSuggestionInteraction.RegisterHandler(context =>
        {
            AcceptCommandSuggestion(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.RestartActiveSessionInteraction.RegisterHandler(async context =>
        {
            await RestartActiveSessionAsync();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ClearActiveScrollbackInteraction.RegisterHandler(context =>
        {
            ClearActiveScrollback();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ShowHyperlinkSampleInteraction.RegisterHandler(context =>
        {
            ShowHyperlinkSample();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ShowKittyGraphicsSampleInteraction.RegisterHandler(context =>
        {
            ShowKittyGraphicsSample();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ToggleGhosttyDiagnosticsInteraction.RegisterHandler(context =>
        {
            ToggleGhosttyDiagnostics(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.CopySnapshotInteraction.RegisterHandler(async context =>
        {
            await CopySnapshotAsync(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ApplyShaderSampleInteraction.RegisterHandler(context =>
        {
            ApplyShaderSample(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ShowAboutInteraction.RegisterHandler(async context =>
        {
            await ShowAboutAsync();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.QuitApplicationInteraction.RegisterHandler(context =>
        {
            _window.Close();
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.SplitPaneInteraction.RegisterHandler(context =>
        {
            SplitActivePane(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.FocusPaneInteraction.RegisterHandler(context =>
        {
            FocusPane(context.Input);
            context.SetOutput(Unit.Default);
        }));

        disposables.Add(_viewModel.ResizePaneInteraction.RegisterHandler(context =>
        {
            ResizePane(context.Input);
            context.SetOutput(Unit.Default);
        }));

        EventHandler applySettingsHandler = (_, _) => ApplySettingsPanelState();
        EventHandler saveSettingsHandler = (_, _) => _ = SaveSettingsPanelStateAsync();
        EventHandler browseFontFileHandler = (_, _) => _ = BrowseSettingsFontFileAsync();
        _viewModel.SettingsPanelState.ApplyRequested += applySettingsHandler;
        _viewModel.SettingsPanelState.SaveRequested += saveSettingsHandler;
        _viewModel.SettingsPanelState.BrowseFontFileRequested += browseFontFileHandler;
        disposables.Add(Disposable.Create(() =>
        {
            _viewModel.SettingsPanelState.ApplyRequested -= applySettingsHandler;
            _viewModel.SettingsPanelState.SaveRequested -= saveSettingsHandler;
            _viewModel.SettingsPanelState.BrowseFontFileRequested -= browseFontFileHandler;
        }));

        disposables.Add(_viewModel
            .WhenAnyValue(model => model.ReplayTimelineValue)
            .Skip(1)
            .Subscribe(SeekReplayFromViewModel));

        disposables.Add(_viewModel
            .WhenAnyValue(
                model => model.SelectedPasteSafetyPolicy,
                model => model.EnableTextShaping,
                model => model.ReflowOnResize,
                model => model.PreserveScrollbackOnRestart,
                model => model.SixelGraphicsEnabled,
                model => model.EnableLigatures)
            .Subscribe(_ => ApplyTerminalBehaviorSettingsToAllStandaloneTabs()));

        disposables.Add(_viewModel
            .WhenAnyValue(model => model.SixelGraphicsEnabled)
            .Skip(1)
            .Subscribe(ReportSixelGraphicsSettingChanged));

        disposables.Add(_viewModel
            .WhenAnyValue(model => model.SessionLoggingEnabled)
            .Subscribe(_ => ApplySessionLoggingSubscriptionsToAllStandaloneTabs()));
    }

    private void InitializeShellProfiles()
    {
        try
        {
            IShellProfileCatalog catalog = new DefaultShellProfileCatalog();
            IReadOnlyList<ShellProfile> profiles = catalog.GetProfiles();
            List<ShellProfileOption> options = new(profiles.Count);
            for (int i = 0; i < profiles.Count; i++)
            {
                ShellProfile profile = profiles[i];
                options.Add(new ShellProfileOption(
                    profile.Id,
                    profile.DisplayName,
                    profile.Command.FileName));
            }

            _viewModel.SetShellProfiles(options);
        }
        catch (Exception ex)
        {
            _viewModel.SetShellProfiles(Array.Empty<ShellProfileOption>());
            UpdateStatus($"Shell profile discovery failed: {ex.Message}");
        }
    }

    private async Task ShowAboutAsync()
    {
        AboutRoyalTerminalWindow aboutWindow = new()
        {
            DataContext = AboutRoyalTerminalViewModel.CreateDefault(),
        };

        await aboutWindow.ShowDialog(_window);
    }

    private async Task RefreshSessionLauncherAsync()
    {
        try
        {
            TerminalSessionProfilesDocument document = await LoadLauncherProfileDocumentAsync();
            _sessionLauncherDocument = document;
            RefreshSessionLauncherOptions(document);
        }
        catch (Exception ex)
        {
            RefreshSessionLauncherOptions(new TerminalSessionProfilesDocument());
            UpdateStatus($"Profile launcher refresh failed: {ex.Message}");
            AppendEventLog($"Profile launcher refresh failed: {ex.Message}");
        }
    }

    private void RefreshSessionLauncherForActivation()
    {
        try
        {
            TerminalSessionProfilesDocument document = _settingsProfileStore
                .LoadAsync()
                .AsTask()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _sessionLauncherDocument = document;
            RefreshSessionLauncherOptions(document);
        }
        catch (Exception ex)
        {
            RefreshSessionLauncherOptions(new TerminalSessionProfilesDocument());
            UpdateStatus($"Profile launcher refresh failed: {ex.Message}");
            AppendEventLog($"Profile launcher refresh failed: {ex.Message}");
        }
    }

    private async Task<TerminalSessionProfilesDocument> LoadLauncherProfileDocumentAsync()
    {
        if (_settingsProfilesLoaded)
        {
            return _viewModel.SettingsPanelState.BuildDocument();
        }

        return await _settingsProfileStore.LoadAsync();
    }

    private void RefreshSessionLauncherOptions(TerminalSessionProfilesDocument document)
    {
        List<SessionLaunchOption> options = new(document.Profiles.Count + _viewModel.ShellProfiles.Count);
        for (int i = 0; i < document.Profiles.Count; i++)
        {
            TerminalSessionProfile profile = document.Profiles[i];
            string? workingDirectory = GetProfileWorkingDirectory(profile);
            options.Add(new SessionLaunchOption(
                $"profile:{profile.Id}",
                profile.DisplayName,
                profile.Transport.TransportId,
                BuildProfileLaunchSubtitle(profile),
                workingDirectory));
        }

        for (int i = 0; i < _viewModel.ShellProfiles.Count; i++)
        {
            ShellProfileOption shellProfile = _viewModel.ShellProfiles[i];
            options.Add(new SessionLaunchOption(
                $"shell:{shellProfile.Id}",
                shellProfile.DisplayName,
                TerminalTransportIds.Pty,
                string.IsNullOrWhiteSpace(shellProfile.CommandPath)
                    ? "Default local shell"
                    : shellProfile.CommandPath,
                null));
        }

        _viewModel.SetSessionLaunchOptions(options);
    }

    private static string BuildProfileLaunchSubtitle(TerminalSessionProfile profile)
    {
        string transportId = profile.Transport.TransportId;
        if (string.Equals(transportId, TerminalTransportIds.Pty, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(profile.Transport.Pty.ShellPath)
                ? "PTY / default shell"
                : $"PTY / {profile.Transport.Pty.ShellPath}";
        }

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(profile.Transport.Pipe.FileName)
                ? "Pipe"
                : $"Pipe / {profile.Transport.Pipe.FileName}";
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.Ordinal))
        {
            TerminalSessionSshSettings ssh = profile.Transport.Ssh;
            return $"SSH / {ssh.Username}@{ssh.Host}:{ssh.Port.ToString(CultureInfo.InvariantCulture)}";
        }

        if (string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.Ordinal))
        {
            TerminalSessionRawTcpSettings rawTcp = profile.Transport.RawTcp;
            return $"Raw TCP / {rawTcp.Host}:{rawTcp.Port.ToString(CultureInfo.InvariantCulture)}";
        }

        if (string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.Ordinal))
        {
            TerminalSessionTelnetSettings telnet = profile.Transport.Telnet;
            return $"Telnet / {telnet.Host}:{telnet.Port.ToString(CultureInfo.InvariantCulture)}";
        }

        if (string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.Ordinal))
        {
            return $"Serial / {profile.Transport.Serial.PortName}";
        }

        return transportId;
    }

    private static string? GetProfileWorkingDirectory(TerminalSessionProfile profile)
    {
        if (string.Equals(profile.Transport.TransportId, TerminalTransportIds.Pty, StringComparison.Ordinal))
        {
            return NormalizeOptional(profile.Transport.Pty.WorkingDirectory);
        }

        if (string.Equals(profile.Transport.TransportId, TerminalTransportIds.Pipe, StringComparison.Ordinal))
        {
            return NormalizeOptional(profile.Transport.Pipe.WorkingDirectory);
        }

        return null;
    }

    private async Task LaunchSessionProfileAsync(string launchOptionId)
    {
        if (string.IsNullOrWhiteSpace(launchOptionId))
        {
            return;
        }

        TerminalSessionProfilesDocument document = _sessionLauncherDocument
            ?? await LoadLauncherProfileDocumentAsync();
        _sessionLauncherDocument = document;

        if (launchOptionId.StartsWith("profile:", StringComparison.Ordinal))
        {
            string profileId = launchOptionId["profile:".Length..];
            TerminalSessionProfile? profile = FindProfile(document, profileId);
            if (profile is null)
            {
                UpdateStatus($"Profile '{profileId}' was not found.");
                return;
            }

            ApplySessionProfile(profile);
            CreateNewTab(profile.Id, profile.DisplayName, profile);
            RefreshSessionLauncherOptions(document);
            return;
        }

        if (launchOptionId.StartsWith("shell:", StringComparison.Ordinal))
        {
            string shellProfileId = launchOptionId["shell:".Length..];
            ShellProfileOption? shellProfile = FindShellProfile(shellProfileId);
            if (shellProfile is null)
            {
                UpdateStatus($"Shell profile '{shellProfileId}' was not found.");
                return;
            }

            _viewModel.SelectedTransportMode = ResolveViewModelTransportMode(TerminalTransportIds.Pty);
            _viewModel.SessionName = shellProfile.DisplayName;
            _viewModel.SelectedShellProfile = shellProfile;
            CreateNewTab(shellProfile.Id, shellProfile.DisplayName);
            RefreshSessionLauncherOptions(document);
        }
    }

    private static TerminalSessionProfile? FindProfile(TerminalSessionProfilesDocument document, string profileId)
    {
        for (int i = 0; i < document.Profiles.Count; i++)
        {
            TerminalSessionProfile profile = document.Profiles[i];
            if (string.Equals(profile.Id, profileId, StringComparison.Ordinal))
            {
                return profile;
            }
        }

        return null;
    }

    private ShellProfileOption? FindShellProfile(string shellProfileId)
    {
        for (int i = 0; i < _viewModel.ShellProfiles.Count; i++)
        {
            ShellProfileOption option = _viewModel.ShellProfiles[i];
            if (string.Equals(option.Id, shellProfileId, StringComparison.Ordinal))
            {
                return option;
            }
        }

        return null;
    }

    private void ApplySessionProfile(TerminalSessionProfile profile)
    {
        _viewModel.SessionName = profile.DisplayName;
        _viewModel.SelectedTransportMode = ResolveViewModelTransportMode(profile.Transport.TransportId);
        ApplyProfileAppearance(profile.Appearance);
        ApplyProfileBehavior(profile.Behavior);
        ApplyProfileLogging(profile.Logging);
        ApplyProfileTransport(profile);
        _viewModel.SetStatus($"Launch profile: {profile.DisplayName}");
    }

    private void ApplyProfileAppearance(TerminalSessionAppearanceSettings appearance)
    {
        _viewModel.FontSource = appearance.FontSource;
        _viewModel.FontFamilyName = appearance.FontFamilyName;
        _viewModel.FontFilePath = appearance.FontFilePath ?? string.Empty;
        _viewModel.SetFontSizeFromSettings(appearance.FontSize > 0 ? appearance.FontSize : 14.0);
        _viewModel.FontSubpixelPositioning = appearance.FontRendering.SubpixelPositioning;
        _viewModel.FontEdging = appearance.FontRendering.Edging;
        _viewModel.FontHinting = appearance.FontRendering.Hinting;
        _viewModel.FontBaselineSnap = appearance.FontRendering.BaselineSnap;
        _viewModel.FontEmbeddedBitmaps = appearance.FontRendering.EmbeddedBitmaps;
        _viewModel.FontEmbolden = appearance.FontRendering.Embolden;
        _viewModel.FontForceAutoHinting = appearance.FontRendering.ForceAutoHinting;
        _viewModel.FontLinearMetrics = appearance.FontRendering.LinearMetrics;
        _viewModel.TextHighlightingMode = appearance.TextHighlightingMode;
        _viewModel.TextHighlightRules = BuildRuntimeTextHighlightRules(null, appearance);
    }

    private static IReadOnlyList<TerminalTextHighlightRule> BuildRuntimeTextHighlightRules(
        TerminalSessionProfile? profile,
        TerminalSessionAppearanceSettings? appearance = null)
    {
        TerminalSessionAppearanceSettings source = appearance ?? profile?.Appearance ?? new TerminalSessionAppearanceSettings();
        if (source.TextHighlightRules.Count == 0)
        {
            return [];
        }

        List<TerminalTextHighlightRule> rules = new(source.TextHighlightRules.Count);
        for (int i = 0; i < source.TextHighlightRules.Count; i++)
        {
            TerminalSessionTextHighlightRule rule = source.TextHighlightRules[i];
            if (string.IsNullOrWhiteSpace(rule.Pattern))
            {
                continue;
            }

            rules.Add(new TerminalTextHighlightRule
            {
                Name = string.IsNullOrWhiteSpace(rule.Name) ? "Highlight Rule" : rule.Name.Trim(),
                Pattern = rule.Pattern.Trim(),
                IsEnabled = rule.IsEnabled,
                Foreground = TryParseArgbColor(rule.ForegroundColor, out uint foreground) ? foreground : null,
                Background = TryParseArgbColor(rule.BackgroundColor, out uint background) ? background : null,
                DarkForeground = TryParseArgbColor(rule.DarkForegroundColor, out uint darkForeground) ? darkForeground : null,
                DarkBackground = TryParseArgbColor(rule.DarkBackgroundColor, out uint darkBackground) ? darkBackground : null,
            });
        }

        return rules.Count == 0 ? [] : rules;
    }

    private void ApplyProfileBehavior(TerminalSessionBehaviorSettings behavior)
    {
        _viewModel.CopyOnSelectEnabled = behavior.CopyOnSelectEnabled;
        _viewModel.EnableBellNotifications = behavior.EnableBellNotifications;
        _viewModel.BackspaceSendsControlH = behavior.BackspaceSendsControlH;
        _viewModel.EnableTextShaping = behavior.EnableTextShaping;
        _viewModel.ReflowOnResize = behavior.ReflowOnResize;
        _viewModel.SixelGraphicsEnabled = behavior.SixelGraphicsEnabled;
        _viewModel.EnableLigatures = behavior.EnableLigatures;
        _viewModel.SelectedPasteSafetyPolicy = ParsePasteSafetyPolicy(behavior.PasteSafetyPolicy);
    }

    private void ApplyProfileLogging(TerminalSessionLoggingSettings logging)
    {
        _viewModel.SessionLoggingEnabled = logging.Enabled;
        _viewModel.SessionLogFilePath = logging.FilePath ?? string.Empty;
        _viewModel.SelectedSessionLogFormat = logging.Format;
        _viewModel.SessionLogFlushFrequently = logging.FlushFrequently;
        _viewModel.EventLogEnabled = logging.EventLogEnabled;
    }

    private void ApplyProfileTransport(TerminalSessionProfile profile)
    {
        string transportId = profile.Transport.TransportId;
        if (string.Equals(transportId, TerminalTransportIds.Pty, StringComparison.Ordinal))
        {
            ApplyPtyProfileTransport(profile);
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.Ordinal))
        {
            TerminalSessionPipeSettings pipe = profile.Transport.Pipe;
            _viewModel.WorkingDirectory = NormalizeOptional(pipe.WorkingDirectory) ?? string.Empty;
            _viewModel.PipeCommandText = BuildPipeCommandText(pipe);
            _viewModel.PipeMergeStdErrIntoStdOut = pipe.MergeStdErrIntoStdOut;
            SelectOrAddRuntimeShellProfile(profile.Id, profile.DisplayName, null);
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.Ordinal))
        {
            _viewModel.RawTcpHost = profile.Transport.RawTcp.Host;
            _viewModel.RawTcpPort = profile.Transport.RawTcp.Port.ToString(CultureInfo.InvariantCulture);
            SelectOrAddRuntimeShellProfile(profile.Id, profile.DisplayName, null);
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.Ordinal))
        {
            TerminalSessionTelnetSettings telnet = profile.Transport.Telnet;
            _viewModel.TelnetHost = telnet.Host;
            _viewModel.TelnetPort = telnet.Port.ToString(CultureInfo.InvariantCulture);
            _viewModel.TelnetTerminalType = telnet.TerminalType;
            _viewModel.TelnetInitialCommand = telnet.InitialCommand ?? string.Empty;
            SelectOrAddRuntimeShellProfile(profile.Id, profile.DisplayName, null);
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.Ordinal))
        {
            TerminalSessionSerialSettings serial = profile.Transport.Serial;
            _viewModel.SerialPortName = serial.PortName;
            _viewModel.SerialBaudRate = serial.BaudRate.ToString(CultureInfo.InvariantCulture);
            _viewModel.SerialDataBits = serial.DataBits.ToString(CultureInfo.InvariantCulture);
            _viewModel.SelectedSerialParity = serial.Parity;
            _viewModel.SelectedSerialStopBits = serial.StopBits;
            _viewModel.SelectedSerialHandshake = serial.Handshake;
            _viewModel.SerialNewLine = serial.NewLine;
            SelectOrAddRuntimeShellProfile(profile.Id, profile.DisplayName, null);
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.Ordinal))
        {
            ApplySshProfileTransport(profile);
        }
    }

    private void ApplyPtyProfileTransport(TerminalSessionProfile profile)
    {
        TerminalSessionPtySettings pty = profile.Transport.Pty;
        _viewModel.WorkingDirectory = NormalizeOptional(pty.WorkingDirectory) ?? string.Empty;
        SelectOrAddRuntimeShellProfile(profile.Id, profile.DisplayName, pty.ShellPath);
    }

    private void ApplySshProfileTransport(TerminalSessionProfile profile)
    {
        TerminalSessionSshSettings ssh = profile.Transport.Ssh;
        _viewModel.SshHost = ssh.Host;
        _viewModel.SshPort = ssh.Port.ToString(CultureInfo.InvariantCulture);
        _viewModel.SshUsername = ssh.Username;
        _viewModel.SshRequestPty = ssh.RequestPty;
        _viewModel.SshTerminalType = ssh.TerminalType;
        _viewModel.SshInitialCommand = ssh.InitialCommand ?? string.Empty;
        _viewModel.SshExpectedHostKeyFingerprintSha256 = ssh.ExpectedHostKeyFingerprintSha256 ?? string.Empty;
        _viewModel.SshKeepAliveIntervalSeconds = ssh.Policy.KeepAliveIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        _viewModel.SshConnectTimeoutSeconds = ssh.Policy.ConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _viewModel.SelectedSshAuthMode = ResolveSshAuthMode(ssh.Authentication);
        ApplySshAdvancedProfileTransport(ssh);
        SelectOrAddRuntimeShellProfile(profile.Id, profile.DisplayName, null);
    }

    private void ApplySshAdvancedProfileTransport(TerminalSessionSshSettings ssh)
    {
        SshProxyOptions? proxy = ssh.Proxy;
        _viewModel.SelectedSshProxyType = proxy?.Type ?? SshProxyType.None;
        _viewModel.SshProxyHost = proxy?.Host ?? string.Empty;
        _viewModel.SshProxyPort = proxy is null
            ? _viewModel.SshProxyPort
            : proxy.Port.ToString(CultureInfo.InvariantCulture);
        _viewModel.SshProxyUsername = proxy?.Username ?? string.Empty;
        _viewModel.SshProxyPassword = proxy?.Password ?? string.Empty;

        SshPortForwardOptions? localForward = FindFirstLocalPortForward(ssh.PortForwardings);
        _viewModel.SshLocalPortForwardEnabled = localForward is not null;
        if (localForward is not null)
        {
            _viewModel.SshLocalPortForwardBindAddress = localForward.BindAddress;
            _viewModel.SshLocalPortForwardSourcePort = localForward.SourcePort.ToString(CultureInfo.InvariantCulture);
            _viewModel.SshLocalPortForwardDestinationHost = localForward.DestinationHost ?? string.Empty;
            _viewModel.SshLocalPortForwardDestinationPort = localForward.DestinationPort?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }

        _viewModel.SshX11Enabled = ssh.X11?.Enabled == true;
        _viewModel.SshX11Display = ssh.X11?.Display ?? _viewModel.SshX11Display;
    }

    private static SshPortForwardOptions? FindFirstLocalPortForward(IReadOnlyList<SshPortForwardOptions> portForwardings)
    {
        for (int i = 0; i < portForwardings.Count; i++)
        {
            SshPortForwardOptions option = portForwardings[i];
            if (option.Mode == SshPortForwardMode.Local)
            {
                return option;
            }
        }

        return null;
    }

    private void SelectOrAddRuntimeShellProfile(string profileId, string displayName, string? shellPath)
    {
        string normalizedShellPath = NormalizeOptional(shellPath) ?? string.Empty;
        for (int i = 0; i < _viewModel.ShellProfiles.Count; i++)
        {
            ShellProfileOption option = _viewModel.ShellProfiles[i];
            if (string.Equals(option.Id, profileId, StringComparison.Ordinal))
            {
                _viewModel.SelectedShellProfile = option;
                return;
            }
        }

        List<ShellProfileOption> profiles = new(_viewModel.ShellProfiles.Count + 1);
        profiles.AddRange(_viewModel.ShellProfiles);
        ShellProfileOption launcherProfile = new(profileId, displayName, normalizedShellPath);
        profiles.Add(launcherProfile);
        _viewModel.SetShellProfiles(profiles);
        _viewModel.SelectedShellProfile = launcherProfile;
    }

    private static string BuildPipeCommandText(TerminalSessionPipeSettings pipe)
    {
        if (string.IsNullOrWhiteSpace(pipe.FileName))
        {
            return "echo RoyalTerminal pipe transport";
        }

        if (pipe.Arguments.Count == 0)
        {
            return QuoteShellArgument(pipe.FileName.Trim());
        }

        StringBuilder builder = new(pipe.FileName.Length + pipe.Arguments.Count * 8);
        builder.Append(QuoteShellArgument(pipe.FileName.Trim()));
        for (int i = 0; i < pipe.Arguments.Count; i++)
        {
            builder.Append(' ');
            builder.Append(QuoteShellArgument(pipe.Arguments[i]));
        }

        return builder.ToString();
    }

    private static string QuoteShellArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        bool requiresQuoting = false;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]) || value[i] is '"' or '\'')
            {
                requiresQuoting = true;
                break;
            }
        }

        if (!requiresQuoting)
        {
            return value;
        }

        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private SshAuthModeOption ResolveSshAuthMode(TerminalSessionSshAuthenticationSettings authentication)
    {
        string id =
            authentication.UsePassword && authentication.PrivateKeySecretIds.Count > 0
                ? SshAuthModeOption.PasswordAndKeyModeId
                : authentication.PrivateKeySecretIds.Count > 0
                    ? SshAuthModeOption.PrivateKeyModeId
                    : authentication.UseAgent
                        ? SshAuthModeOption.AgentModeId
                        : SshAuthModeOption.PasswordModeId;
        return ResolveViewModelSshAuthMode(id);
    }

    private static TerminalPasteSafetyPolicy ParsePasteSafetyPolicy(string? policy)
    {
        return Enum.TryParse(policy, ignoreCase: true, out TerminalPasteSafetyPolicy parsed)
            ? parsed
            : TerminalPasteSafetyPolicy.None;
    }

    private TerminalSessionProfile BuildLaunchProfileFromViewModel(string profileId, string displayName)
    {
        string transportId = _viewModel.SelectedTransportMode.Id;
        TerminalSessionTransportProfile transport = new()
        {
            TransportId = transportId,
            Pty = new TerminalSessionPtySettings
            {
                ShellPath = NormalizeOptional(_viewModel.SelectedShellProfile?.CommandPath),
                WorkingDirectory = NormalizeOptional(_viewModel.WorkingDirectory),
            },
            Pipe = BuildLaunchPipeSettings(),
            RawTcp = string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.Ordinal)
                ? BuildLaunchRawTcpSettings()
                : new TerminalSessionRawTcpSettings(),
            Telnet = string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.Ordinal)
                ? BuildLaunchTelnetSettings()
                : new TerminalSessionTelnetSettings(),
            Serial = string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.Ordinal)
                ? BuildLaunchSerialSettings()
                : new TerminalSessionSerialSettings(),
            Ssh = string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.Ordinal)
                ? BuildLaunchSshSettings()
                : new TerminalSessionSshSettings(),
        };

        return new TerminalSessionProfile
        {
            Id = profileId,
            DisplayName = displayName,
            Transport = transport,
        };
    }

    private TerminalSessionPipeSettings BuildLaunchPipeSettings()
    {
        TerminalCommandSpec command = BuildPipeCommand(_viewModel.SelectedShellProfile);
        return new TerminalSessionPipeSettings
        {
            FileName = command.FileName,
            Arguments = new List<string>(command.Arguments),
            WorkingDirectory = NormalizeOptional(_viewModel.WorkingDirectory),
            MergeStdErrIntoStdOut = _viewModel.PipeMergeStdErrIntoStdOut,
        };
    }

    private TerminalSessionRawTcpSettings BuildLaunchRawTcpSettings()
    {
        return new TerminalSessionRawTcpSettings
        {
            Host = NormalizeOptional(_viewModel.RawTcpHost) ?? "localhost",
            Port = ParsePort(_viewModel.RawTcpPort, "Raw TCP port"),
        };
    }

    private TerminalSessionTelnetSettings BuildLaunchTelnetSettings()
    {
        return new TerminalSessionTelnetSettings
        {
            Host = NormalizeOptional(_viewModel.TelnetHost) ?? "localhost",
            Port = ParsePort(_viewModel.TelnetPort, "Telnet port"),
            TerminalType = NormalizeOptional(_viewModel.TelnetTerminalType) ?? "xterm",
            InitialCommand = NormalizeOptional(_viewModel.TelnetInitialCommand),
        };
    }

    private TerminalSessionSerialSettings BuildLaunchSerialSettings()
    {
        return new TerminalSessionSerialSettings
        {
            PortName = NormalizeOptional(_viewModel.SerialPortName) ?? string.Empty,
            BaudRate = ParsePositiveInt(_viewModel.SerialBaudRate, "Serial baud rate"),
            DataBits = ParseIntInRange(_viewModel.SerialDataBits, 5, 8, "Serial data bits"),
            Parity = _viewModel.SelectedSerialParity,
            StopBits = _viewModel.SelectedSerialStopBits,
            Handshake = _viewModel.SelectedSerialHandshake,
            NewLine = string.IsNullOrEmpty(_viewModel.SerialNewLine) ? "\n" : _viewModel.SerialNewLine,
        };
    }

    private TerminalSessionSshSettings BuildLaunchSshSettings()
    {
        return new TerminalSessionSshSettings
        {
            Host = NormalizeOptional(_viewModel.SshHost) ?? "localhost",
            Port = ParsePort(_viewModel.SshPort, "SSH port"),
            Username = NormalizeOptional(_viewModel.SshUsername) ?? Environment.UserName,
            RequestPty = _viewModel.SshRequestPty,
            TerminalType = NormalizeOptional(_viewModel.SshTerminalType) ?? "xterm-256color",
            InitialCommand = NormalizeOptional(_viewModel.SshInitialCommand),
            ExpectedHostKeyFingerprintSha256 = NormalizeOptional(_viewModel.SshExpectedHostKeyFingerprintSha256),
            Authentication = BuildLaunchSshAuthenticationSettings(),
            Proxy = BuildSshProxyOptions(),
            PortForwardings = new List<SshPortForwardOptions>(BuildSshPortForwardings()),
            X11 = BuildSshX11Options(),
            Policy = BuildSshPolicyOptions(),
        };
    }

    private TerminalSessionSshAuthenticationSettings BuildLaunchSshAuthenticationSettings()
    {
        string authModeId = _viewModel.SelectedSshAuthMode.Id;
        bool usePassword = string.Equals(authModeId, SshAuthModeOption.PasswordModeId, StringComparison.Ordinal)
                           || string.Equals(authModeId, SshAuthModeOption.PasswordAndKeyModeId, StringComparison.Ordinal);
        bool usePrivateKey = string.Equals(authModeId, SshAuthModeOption.PrivateKeyModeId, StringComparison.Ordinal)
                             || string.Equals(authModeId, SshAuthModeOption.PasswordAndKeyModeId, StringComparison.Ordinal);
        bool useAgent = string.Equals(authModeId, SshAuthModeOption.AgentModeId, StringComparison.Ordinal);

        return new TerminalSessionSshAuthenticationSettings
        {
            UsePassword = usePassword,
            PasswordSecretId = usePassword ? DemoSshCredentialProvider.PasswordSecretId : null,
            PrivateKeySecretIds = usePrivateKey ? [DemoSshCredentialProvider.PrivateKeySecretId] : [],
            UseAgent = useAgent,
        };
    }

    #region Tab Management

    private void CreateInitialTabs()
    {
        if (ReadEnvironmentToggle(StartAllRenderModesEnvVar))
        {
            CreateInitialTabsForSupportedModes();
            return;
        }

        if (TryRestoreWorkspaceTabs())
        {
            return;
        }

        TerminalRenderMode startupMode = _modeResolver.ResolveSupportedMode(
            TerminalRenderMode.RenderedAuto,
            _terminalCapabilities);
        _viewModel.SetRenderMode(startupMode);
        CreateNewTab();
    }

    private bool TryRestoreWorkspaceTabs()
    {
        TerminalWorkspaceDocument document;
        try
        {
            document = _workspaceStore.LoadAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppendEventLog($"Workspace restore failed: {ex.Message}");
            return false;
        }

        TerminalWorkspaceWindow? window = SelectWorkspaceWindow(document);
        if (window is null || window.Tabs.Count == 0)
        {
            return false;
        }

        ApplyWorkspaceWindowState(window);

        int selectedIndex = 0;
        for (int i = 0; i < window.Tabs.Count; i++)
        {
            TerminalWorkspaceTab workspaceTab = window.Tabs[i];
            RestoreWorkspaceTab(workspaceTab);

            if (string.Equals(workspaceTab.Id, window.SelectedTabId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
            }
        }

        if (_tabs.Count > 0)
        {
            SwitchToTab(Math.Clamp(selectedIndex, 0, _tabs.Count - 1));
        }

        AppendEventLog($"Restored {window.Tabs.Count} workspace tab(s).");
        return true;
    }

    private void ApplyWorkspaceWindowState(TerminalWorkspaceWindow window)
    {
        if (window.WidthPixels > 0)
        {
            _window.Width = window.WidthPixels;
        }

        if (window.HeightPixels > 0)
        {
            _window.Height = window.HeightPixels;
        }

        _window.WindowState = window.IsMaximized
            ? WindowState.Maximized
            : WindowState.Normal;
    }

    private void RestoreWorkspaceTab(TerminalWorkspaceTab workspaceTab)
    {
        _tabCounter++;
        string tabName = string.IsNullOrWhiteSpace(workspaceTab.Title)
            ? $"Terminal {_tabCounter}"
            : workspaceTab.Title!.Trim();

        TerminalRenderMode resolvedMode = ResolveWorkspaceTabMode(workspaceTab);
        TabVisualMode tabMode = CreateDeferredWorkspaceTabMode(workspaceTab, resolvedMode);
        Button headerButton = CreateTabHeader(tabName, tabMode);

        string profileId = NormalizeOptional(workspaceTab.ProfileId) ?? "default";
        string transportId = NormalizeOptional(workspaceTab.TransportId) ?? TerminalTransportIds.Pty;
        string? workingDirectory = NormalizeOptional(workspaceTab.WorkingDirectory);
        Grid deferredContainer = new()
        {
            Background = Brushes.Transparent,
        };

        TerminalTab tab = new(
            headerButton,
            deferredContainer,
            deferredContainer,
            _tabCounter,
            tabMode.Name,
            autoStartSession: true,
            resolvedMode: resolvedMode,
            profileId: profileId,
            transportId: transportId,
            workingDirectory: workingDirectory,
            workspaceId: NormalizeOptional(workspaceTab.Id),
            rootPane: workspaceTab.RootPane,
            rootPaneNode: null,
            leafControls: [],
            deferredWorkspaceTab: workspaceTab);
        tab.CloseButton.Command = _viewModel.CloseTabCommand;
        tab.CloseButton.CommandParameter = tab.Index;
        headerButton.Command = _viewModel.ActivateTabCommand;
        headerButton.CommandParameter = tab.Index;

        _tabs.Add(tab);
        deferredContainer.IsVisible = false;
        _terminalHost.Children.Add(deferredContainer);
        _tabStrip.Children.Add(headerButton);
        AppendEventLog($"[{tabName}] Workspace tab restored lazily.");
    }

    private bool EnsureTabMaterialized(TerminalTab tab)
    {
        if (!tab.IsDeferredWorkspaceTab)
        {
            return true;
        }

        TerminalWorkspaceTab workspaceTab = tab.DeferredWorkspaceTab
            ?? throw new InvalidOperationException("Deferred workspace tab metadata is missing.");

        try
        {
            ApplyWorkspaceTabConfiguration(workspaceTab);

            List<TerminalControl> leafControls = [];
            List<TerminalRenderMode> resolvedModes = [];
            TerminalPaneRuntimeNode rootPaneNode = CreateWorkspacePaneNode(
                workspaceTab.RootPane,
                workspaceTab,
                leafControls,
                resolvedModes);
            Control paneRoot = rootPaneNode.Visual;

            int hostIndex = _terminalHost.Children.IndexOf(tab.Container);
            bool wasVisible = tab.Container.IsVisible;
            if (hostIndex >= 0)
            {
                _terminalHost.Children.RemoveAt(hostIndex);
            }

            ReplaceTabRootVisual(tab, paneRoot, rootPaneNode, hostIndex, wasVisible);
            if (leafControls.Count > 0 && resolvedModes.Count > 0)
            {
                UpdateTabHeaderVisual(tab, ResolveTabMode(leafControls[0], resolvedModes[0]));
            }

            AppendEventLog($"[{tab.Title}] Workspace tab materialized ({leafControls.Count.ToString(CultureInfo.InvariantCulture)} pane(s)).");
            return true;
        }
        catch (Exception ex)
        {
            UpdateStatus($"Workspace tab restore failed: {ex.Message}");
            AppendEventLog($"[{tab.Title}] Workspace tab restore failed: {ex.Message}");
            return false;
        }
    }

    private TerminalPaneRuntimeNode CreateWorkspacePaneNode(
        TerminalWorkspacePane pane,
        TerminalWorkspaceTab tab,
        List<TerminalControl> leafControls,
        List<TerminalRenderMode> resolvedModes)
    {
        if (pane.Split is not { } split)
        {
            return CreateWorkspaceLeafNode(pane, tab, leafControls, resolvedModes);
        }

        double ratio = Math.Clamp(split.Ratio, 0.05, 0.95);

        TerminalPaneRuntimeNode node = new(
            NormalizeOptional(pane.Id) ?? CreatePaneId(),
            NormalizeOptional(pane.Title),
            NormalizeOptional(pane.ProfileId),
            NormalizeOptional(pane.WorkingDirectory),
            NormalizeOptional(pane.TransportId),
            NormalizeOptional(pane.TransportProfileId));

        TerminalPaneRuntimeNode first = CreateWorkspacePaneNode(split.FirstPane, tab, leafControls, resolvedModes);
        TerminalPaneRuntimeNode second = CreateWorkspacePaneNode(split.SecondPane, tab, leafControls, resolvedModes);
        node.SetSplit(
            NormalizeSplitOrientation(split.Orientation),
            ratio,
            first,
            second,
            CreateSplitGrid(NormalizeSplitOrientation(split.Orientation), ratio, first.Visual, second.Visual));
        first.Parent = node;
        second.Parent = node;
        return node;
    }

    private TerminalPaneRuntimeNode CreateWorkspaceLeafNode(
        TerminalWorkspacePane pane,
        TerminalWorkspaceTab tab,
        List<TerminalControl> leafControls,
        List<TerminalRenderMode> resolvedModes)
    {
        TerminalSessionProfile? savedProfile = ApplyWorkspacePaneConfiguration(pane, tab);
        TerminalModeSelection modeSelection = ResolveModeSelectionForNewTab();
        TerminalControl terminal = CreateTerminalControlWithRuntimeFallback(
            modeSelection,
            out TerminalModeSelection finalizedModeSelection);

        string profileId = NormalizeOptional(pane.ProfileId) ?? tab.ProfileId;
        string title = NormalizeOptional(pane.Title) ?? NormalizeOptional(tab.Title) ?? profileId;
        TerminalSessionProfile launchProfile = BuildWorkspacePaneLaunchProfile(
            pane,
            tab,
            profileId,
            title,
            savedProfile);
        _launchConfigurations[terminal] = new TerminalLaunchConfiguration(
            launchProfile);
        ApplyLaunchAppearanceSettings(terminal, launchProfile);
        leafControls.Add(terminal);
        resolvedModes.Add(finalizedModeSelection.ResolvedMode);

        ScrollViewer container = CreatePaneScrollViewer(terminal);
        TerminalPaneRuntimeNode node = new(
            NormalizeOptional(pane.Id) ?? CreatePaneId(),
            NormalizeOptional(pane.Title),
            profileId,
            GetProfileWorkingDirectory(launchProfile),
            launchProfile.Transport.TransportId,
            NormalizeOptional(pane.TransportProfileId));
        node.SetLeaf(terminal, container);
        RegisterPaneControl(terminal, node);

        return node;
    }

    private static ScrollViewer CreatePaneScrollViewer(TerminalControl terminal)
    {
        return new ScrollViewer
        {
            Content = terminal,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    private static Grid CreateSplitGrid(string orientation, double ratio, Control first, Control second)
    {
        double normalizedRatio = Math.Clamp(ratio, 0.05, 0.95);
        Grid grid = new()
        {
            Background = Brushes.Transparent,
        };

        if (string.Equals(orientation, TerminalWorkspacePaneSplitOrientations.Vertical, StringComparison.Ordinal))
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(normalizedRatio, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(4, GridUnitType.Pixel)));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1.0 - normalizedRatio, GridUnitType.Star)));
            Grid.SetRow(first, 0);
            Grid.SetRow(second, 2);
            grid.Children.Add(first);
            GridSplitter splitter = new()
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
            };
            Grid.SetRow(splitter, 1);
            grid.Children.Add(splitter);
            grid.Children.Add(second);
            return grid;
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(normalizedRatio, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(4, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.0 - normalizedRatio, GridUnitType.Star)));
        Grid.SetColumn(first, 0);
        Grid.SetColumn(second, 2);
        grid.Children.Add(first);
        GridSplitter columnSplitter = new()
        {
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
        };
        Grid.SetColumn(columnSplitter, 1);
        grid.Children.Add(columnSplitter);
        grid.Children.Add(second);
        return grid;
    }

    private string CreatePaneId()
    {
        _paneCounter++;
        return $"pane-{_paneCounter.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string NormalizeSplitOrientation(string? orientation)
    {
        return string.Equals(orientation, TerminalWorkspacePaneSplitOrientations.Vertical, StringComparison.OrdinalIgnoreCase)
            ? TerminalWorkspacePaneSplitOrientations.Vertical
            : TerminalWorkspacePaneSplitOrientations.Horizontal;
    }

    private void RegisterPaneControl(TerminalControl control, TerminalPaneRuntimeNode node)
    {
        _paneRuntimeNodes[control] = node;
        RegisterCommandHistoryCapture(control);
        control.GotFocus += (_, _) => _activePaneControl = control;
        control.PointerPressed += (_, _) => _activePaneControl = control;
    }

    private IEnumerable<TerminalControl> EnumerateTerminalControls()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            TerminalTab tab = _tabs[i];
            if (tab.LeafControls.Count > 0)
            {
                for (int paneIndex = 0; paneIndex < tab.LeafControls.Count; paneIndex++)
                {
                    yield return tab.LeafControls[paneIndex];
                }

                continue;
            }

            if (tab.Control is TerminalControl standaloneControl)
            {
                yield return standaloneControl;
            }
        }
    }

    private static TerminalWorkspaceWindow? SelectWorkspaceWindow(TerminalWorkspaceDocument document)
    {
        if (document.Windows.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(document.SelectedWindowId))
        {
            for (int i = 0; i < document.Windows.Count; i++)
            {
                if (string.Equals(
                        document.Windows[i].Id,
                        document.SelectedWindowId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return document.Windows[i];
                }
            }
        }

        return document.Windows[0];
    }

    private void ApplyWorkspaceTabConfiguration(TerminalWorkspaceTab workspaceTab)
    {
        _viewModel.SessionName = string.IsNullOrWhiteSpace(workspaceTab.Title)
            ? workspaceTab.ProfileId
            : workspaceTab.Title;
        SetSelectedTransportMode(workspaceTab.TransportId);
        SetSelectedShellProfile(workspaceTab.ProfileId);
        _viewModel.WorkingDirectory = NormalizeOptional(workspaceTab.WorkingDirectory) ?? string.Empty;

        TerminalRenderMode requestedMode = MapWorkspaceRenderMode(workspaceTab.RenderMode);
        TerminalRenderMode startupMode = _modeResolver.ResolveSupportedMode(
            requestedMode,
            _terminalCapabilities);
        _viewModel.SetRenderMode(startupMode);
    }

    private TerminalRenderMode ResolveWorkspaceTabMode(TerminalWorkspaceTab workspaceTab)
    {
        TerminalRenderMode requestedMode = MapWorkspaceRenderMode(workspaceTab.RenderMode);
        return _modeResolver.ResolveSupportedMode(
            requestedMode,
            _terminalCapabilities);
    }

    private static TabVisualMode CreateDeferredWorkspaceTabMode(
        TerminalWorkspaceTab workspaceTab,
        TerminalRenderMode resolvedMode)
    {
        string transportId = NormalizeOptional(workspaceTab.TransportId) ?? TerminalTransportIds.Pty;
        string modeName = $"{GetModeDisplayName(resolvedMode)} ({transportId} - deferred)";
        return new TabVisualMode(
            modeName,
            CreateStandaloneModeGlyph(resolvedMode),
            CreateStandaloneModeGlyphBrush(resolvedMode));
    }

    private TerminalSessionProfile? ApplyWorkspacePaneConfiguration(TerminalWorkspacePane pane, TerminalWorkspaceTab tab)
    {
        string profileId = NormalizeOptional(pane.ProfileId) ?? tab.ProfileId;
        TerminalSessionProfile? profile = _sessionLauncherDocument is null
            ? null
            : FindProfile(_sessionLauncherDocument, profileId);
        if (profile is not null)
        {
            ApplySessionProfile(profile);
        }
        else
        {
            _viewModel.SessionName = NormalizeOptional(pane.Title)
                ?? NormalizeOptional(tab.Title)
                ?? profileId;
            SetSelectedShellProfile(profileId);
        }

        SetSelectedTransportMode(NormalizeOptional(pane.TransportId) ?? tab.TransportId);
        string? workingDirectory = NormalizeOptional(pane.WorkingDirectory) ?? NormalizeOptional(tab.WorkingDirectory);
        if (workingDirectory is not null)
        {
            _viewModel.WorkingDirectory = workingDirectory;
        }
        else if (profile is null)
        {
            _viewModel.WorkingDirectory = string.Empty;
        }

        return profile;
    }

    private TerminalSessionProfile BuildWorkspacePaneLaunchProfile(
        TerminalWorkspacePane pane,
        TerminalWorkspaceTab tab,
        string profileId,
        string title,
        TerminalSessionProfile? savedProfile)
    {
        TerminalSessionProfile launchProfile = savedProfile is null
            ? BuildLaunchProfileFromViewModel(profileId, title)
            : savedProfile with
            {
                Id = profileId,
                DisplayName = title,
            };
        string? workingDirectory = NormalizeOptional(pane.WorkingDirectory) ?? NormalizeOptional(tab.WorkingDirectory);
        if (workingDirectory is not null)
        {
            return ApplyLaunchWorkingDirectory(launchProfile, workingDirectory);
        }

        return savedProfile is null
            ? ApplyLaunchWorkingDirectory(launchProfile, null)
            : launchProfile;
    }

    private static TerminalSessionProfile ApplyLaunchWorkingDirectory(
        TerminalSessionProfile profile,
        string? workingDirectory)
    {
        string? normalizedWorkingDirectory = NormalizeOptional(workingDirectory);
        return profile with
        {
            Transport = profile.Transport with
            {
                Pty = profile.Transport.Pty with
                {
                    WorkingDirectory = normalizedWorkingDirectory,
                },
                Pipe = profile.Transport.Pipe with
                {
                    WorkingDirectory = normalizedWorkingDirectory,
                },
            },
        };
    }

    private void SetSelectedTransportMode(string? transportId)
    {
        if (string.IsNullOrWhiteSpace(transportId))
        {
            return;
        }

        for (int i = 0; i < _viewModel.TransportModes.Count; i++)
        {
            TransportModeOption option = _viewModel.TransportModes[i];
            if (string.Equals(option.Id, transportId, StringComparison.OrdinalIgnoreCase))
            {
                _viewModel.SelectedTransportMode = option;
                return;
            }
        }
    }

    private void SetSelectedShellProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        for (int i = 0; i < _viewModel.ShellProfiles.Count; i++)
        {
            ShellProfileOption option = _viewModel.ShellProfiles[i];
            if (string.Equals(option.Id, profileId, StringComparison.OrdinalIgnoreCase))
            {
                _viewModel.SelectedShellProfile = option;
                return;
            }
        }
    }

    private static TerminalRenderMode MapWorkspaceRenderMode(string? renderMode)
    {
        if (string.Equals(renderMode, TerminalWorkspaceRenderModes.Ghostty, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalRenderMode.NativeVt;
        }

        if (string.Equals(renderMode, TerminalWorkspaceRenderModes.Text, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalRenderMode.ManagedVt;
        }

        return TerminalRenderMode.RenderedAuto;
    }

    private static string MapWorkspaceRenderMode(TerminalRenderMode renderMode)
    {
        return renderMode switch
        {
            TerminalRenderMode.NativeVt => TerminalWorkspaceRenderModes.Ghostty,
            TerminalRenderMode.ManagedVt => TerminalWorkspaceRenderModes.Text,
            TerminalRenderMode.RenderedAuto => TerminalWorkspaceRenderModes.Skia,
            _ => TerminalWorkspaceRenderModes.Default,
        };
    }

    private void CreateInitialTabsForSupportedModes()
    {
        TerminalRenderMode[] startupModes =
        [
            TerminalRenderMode.NativeVt,
            TerminalRenderMode.ManagedVt,
            TerminalRenderMode.RenderedAuto,
        ];

        for (int i = 0; i < startupModes.Length; i++)
        {
            TerminalRenderMode mode = startupModes[i];
            if (!_modeResolver.IsSupported(mode, _terminalCapabilities))
            {
                continue;
            }

            _viewModel.SetRenderMode(mode);
            CreateNewTab();
        }

        if (_tabs.Count == 0)
        {
            _viewModel.SetRenderMode(TerminalRenderMode.RenderedAuto);
            CreateNewTab();
        }
    }

    private void CreateNewTab(
        string? profileIdOverride = null,
        string? titleOverride = null,
        TerminalSessionProfile? launchProfileOverride = null)
    {
        _tabCounter++;
        string tabName = string.IsNullOrWhiteSpace(titleOverride)
            ? $"Terminal {_tabCounter}"
            : titleOverride.Trim();
        TerminalModeSelection modeSelection = ResolveModeSelectionForNewTab();
        TerminalControl terminal = CreateTerminalControlWithRuntimeFallback(
            modeSelection,
            out TerminalModeSelection finalizedModeSelection);

        TabVisualMode tabMode = ResolveTabMode(terminal, finalizedModeSelection.ResolvedMode);
        Button headerButton = CreateTabHeader(tabName, tabMode);
        string profileId = NormalizeOptional(profileIdOverride) ?? _viewModel.SelectedShellProfile?.Id ?? "default";
        TerminalSessionProfile launchProfile = launchProfileOverride is null
            ? BuildLaunchProfileFromViewModel(profileId, tabName)
            : launchProfileOverride with
            {
                Id = profileId,
                DisplayName = tabName,
            };
        string transportId = launchProfile.Transport.TransportId;
        string? workingDirectory = GetProfileWorkingDirectory(launchProfile);
        TerminalLaunchConfiguration launchConfiguration = new(launchProfile);
        _launchConfigurations[terminal] = launchConfiguration;
        ApplyLaunchAppearanceSettings(terminal, launchProfile);

        ScrollViewer container = CreatePaneScrollViewer(terminal);
        TerminalPaneRuntimeNode rootPaneNode = new(
            CreatePaneId(),
            tabName,
            profileId,
            workingDirectory,
            transportId,
            transportProfileId: null);
        rootPaneNode.SetLeaf(terminal, container);
        RegisterPaneControl(terminal, rootPaneNode);

        TerminalTab tab = new(
            headerButton,
            terminal,
            container,
            _tabCounter,
            tabMode.Name,
            autoStartSession: true,
            resolvedMode: finalizedModeSelection.ResolvedMode,
            profileId: profileId,
            transportId: transportId,
            workingDirectory: workingDirectory,
            workspaceId: null,
            rootPane: null,
            rootPaneNode: rootPaneNode,
            leafControls: [terminal]);
        tab.CloseButton.Command = _viewModel.CloseTabCommand;
        tab.CloseButton.CommandParameter = tab.Index;
        headerButton.Command = _viewModel.ActivateTabCommand;
        headerButton.CommandParameter = tab.Index;

        _tabs.Add(tab);

        container.IsVisible = false;
        _terminalHost.Children.Add(container);
        _tabStrip.Children.Add(headerButton);

        SwitchToTab(_tabs.Count - 1);
        UpdateStatus(BuildTabOpenedStatus(tabName, finalizedModeSelection));
        AppendEventLog($"[{tabName}] Tab opened ({tabMode.Name}).");
    }

    private void CreateReplayTab(TerminalCaptureSession session, string sourceLabel)
    {
        ArgumentNullException.ThrowIfNull(session);

        _tabCounter++;
        string tabName = $"Replay {_tabCounter}";
        TerminalControl terminal = CreateStandaloneReplayControl(session);
        TerminalCaptureRuntime runtime = EnsureCaptureRuntime(terminal);
        runtime.LoadReplay(session, sourceLabel);

        TabVisualMode tabMode = new(
            "Replay Capture",
            "\u25B7",
            new SolidColorBrush(Color.FromRgb(0x9C, 0xD6, 0x56)));
        Button headerButton = CreateTabHeader(tabName, tabMode);

        ScrollViewer container = new()
        {
            Content = terminal,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        TerminalTab tab = new(
            headerButton,
            terminal,
            container,
            _tabCounter,
            tabMode.Name,
            autoStartSession: false,
            resolvedMode: TerminalRenderMode.RenderedAuto,
            profileId: "replay",
            transportId: TerminalTransportIds.Pipe,
            workingDirectory: null,
            workspaceId: null,
            rootPane: null,
            rootPaneNode: null,
            leafControls: []);
        if (!string.IsNullOrWhiteSpace(sourceLabel))
        {
            tab.UpdateTitle($"Replay: {sourceLabel}");
        }

        tab.CloseButton.Command = _viewModel.CloseTabCommand;
        tab.CloseButton.CommandParameter = tab.Index;
        headerButton.Command = _viewModel.ActivateTabCommand;
        headerButton.CommandParameter = tab.Index;

        _tabs.Add(tab);
        container.IsVisible = false;
        _terminalHost.Children.Add(container);
        _tabStrip.Children.Add(headerButton);

        SwitchToTab(_tabs.Count - 1);
        UpdateStatus($"Loaded replay '{sourceLabel}'.");
        AppendEventLog($"[{tabName}] Replay loaded from '{sourceLabel}'.");
    }

    private TerminalModeSelection ResolveModeSelectionForNewTab()
    {
        TerminalRenderMode requestedMode = GetRequestedRenderMode();
        _terminalCapabilities = _terminalCapabilities with { NativeVtAvailable = GhosttyVtProcessor.IsAvailable() };
        _viewModel.SetTerminalCapabilities(_terminalCapabilities);

        TerminalRenderMode resolvedMode = _modeResolver.ResolveSupportedMode(requestedMode, _terminalCapabilities);
        bool fallbackApplied = requestedMode != resolvedMode;
        _viewModel.SetRenderMode(resolvedMode);

        string? fallbackReason = fallbackApplied
            ? DescribeModeFallback(requestedMode)
            : null;
        return new TerminalModeSelection(requestedMode, resolvedMode, fallbackApplied, fallbackReason);
    }

    private TerminalControl CreateTerminalControl(TerminalRenderMode mode)
    {
        return mode switch
        {
            TerminalRenderMode.NativeVt => CreateStandaloneTerminalControl(TerminalRenderMode.NativeVt),
            TerminalRenderMode.ManagedVt => CreateStandaloneTerminalControl(TerminalRenderMode.ManagedVt),
            _ => CreateStandaloneTerminalControl(TerminalRenderMode.RenderedAuto),
        };
    }

    private TerminalControl CreateTerminalControlWithRuntimeFallback(
        TerminalModeSelection selection,
        out TerminalModeSelection finalizedSelection)
    {
        TerminalModeSelection currentSelection = selection;
        TerminalModeCapabilities runtimeCapabilities = _terminalCapabilities;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                TerminalControl terminal = CreateTerminalControl(currentSelection.ResolvedMode);
                finalizedSelection = currentSelection;
                return terminal;
            }
            catch (Exception ex)
            {
                if (!TryMarkModeUnavailable(currentSelection.ResolvedMode, ref runtimeCapabilities))
                {
                    throw;
                }

                _terminalCapabilities = runtimeCapabilities;
                _viewModel.SetTerminalCapabilities(_terminalCapabilities);

                TerminalRenderMode fallbackMode = _modeResolver.ResolveSupportedMode(
                    currentSelection.ResolvedMode,
                    runtimeCapabilities);
                if (fallbackMode == currentSelection.ResolvedMode)
                {
                    throw;
                }

                _viewModel.SetRenderMode(fallbackMode);
                currentSelection = new TerminalModeSelection(
                    RequestedMode: currentSelection.RequestedMode,
                    ResolvedMode: fallbackMode,
                    FallbackApplied: true,
                    FallbackReason: $"{GetModeDisplayName(currentSelection.ResolvedMode)} initialization failed: {ex.Message}");
            }
        }

        throw new InvalidOperationException("Unable to construct terminal control for any supported mode.");
    }

    private TerminalControl CreateStandaloneTerminalControl(TerminalRenderMode mode)
        => CreateStandaloneTerminalControl(mode, ResolveVtProcessorPreference(mode));

    private TerminalControl CreateStandaloneTerminalControl(
        TerminalRenderMode mode,
        VtProcessorPreference vtProcessorPreference)
    {
        TerminalTheme theme = _viewModel.ActiveTheme;
        TerminalControl standaloneControl = CreateStandaloneControl();
        ApplyFontSettings(standaloneControl);
        standaloneControl.TextHighlightingMode = _viewModel.TextHighlightingMode;
        standaloneControl.TextHighlightRules = _viewModel.TextHighlightRules;
        standaloneControl.Columns = 80;
        standaloneControl.Rows = 24;
        standaloneControl.ScrollbackLimit = 10_000;
        standaloneControl.VtProcessorPreference = vtProcessorPreference;
        standaloneControl.ApplyTheme(theme);
        ConfigureRenderer(standaloneControl.Renderer);
        ApplyTerminalBehaviorSettings(standaloneControl);
        ApplyShaderSampleToControl(standaloneControl);

        standaloneControl.TitleChanged += (_, title) =>
        {
            AppendEventLog($"[{GetTabDisplayName(standaloneControl)}] Title changed to '{title}'.");
            if (ReferenceEquals(GetActiveStandaloneControl(), standaloneControl) && _viewModel.ShowGhosttyDiagnostics)
            {
                _viewModel.SetGhosttyDiagnostics(true, BuildGhosttyDiagnosticsText(standaloneControl));
            }
        };
        standaloneControl.Bell += (_, _) =>
        {
            if (!_viewModel.EnableBellNotifications)
            {
                return;
            }

            UpdateStatus($"Bell from {GetTabDisplayName(standaloneControl)}");
            AppendEventLog($"[{GetTabDisplayName(standaloneControl)}] Bell.");
        };
        standaloneControl.ProcessExited += (_, code) =>
        {
            AppendEventLog($"[{GetTabDisplayName(standaloneControl)}] Process exited with code {code}.");
        };
        standaloneControl.TerminalResized += (_, args) =>
        {
            UpdateDimensions(args.Columns, args.Rows);
            AppendEventLog($"[{GetTabDisplayName(standaloneControl)}] Resized to {args.Columns}x{args.Rows}.");
            if (ReferenceEquals(GetActiveStandaloneControl(), standaloneControl))
            {
                SyncActiveTerminalSurface();
            }
        };
        standaloneControl.TerminalSessionService.InputSent += (_, args) =>
        {
            WriteSessionLogInput(standaloneControl, args.Data);
        };
        standaloneControl.PointerReleased += async (_, e) =>
        {
            if (!_viewModel.CopyOnSelectEnabled ||
                e.InitialPressMouseButton != MouseButton.Left ||
                !standaloneControl.HasSelection)
            {
                return;
            }

            await standaloneControl.CopySelectionAsync();
            AppendEventLog($"[{GetTabDisplayName(standaloneControl)}] Copied selection.");
        };
        standaloneControl.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) => HandleStandaloneKeyDown(standaloneControl, e),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        UpdateSessionLoggingSubscription(standaloneControl);

        return standaloneControl;
    }

    private TerminalControl CreateStandaloneReplayControl(TerminalCaptureSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        TerminalTheme theme = _viewModel.ActiveTheme;
        TerminalControl standaloneControl = CreateStandaloneControl();
        ApplyFontSettings(standaloneControl);
        standaloneControl.Columns = Math.Max(1, session.InitialColumns);
        standaloneControl.Rows = Math.Max(1, session.InitialRows);
        standaloneControl.ScrollbackLimit = 10_000;
        standaloneControl.VtProcessorPreference = VtProcessorPreference.Managed;
        standaloneControl.ApplyTheme(theme);
        ConfigureRenderer(standaloneControl.Renderer);
        ApplyTerminalBehaviorSettings(standaloneControl);
        ApplyShaderSampleToControl(standaloneControl);

        standaloneControl.TerminalResized += (_, args) =>
        {
            if (_activeTab?.Control == standaloneControl)
            {
                UpdateDimensions(args.Columns, args.Rows);
            }
        };

        return standaloneControl;
    }

    private void QueueStandaloneSessionStart(TerminalControl standaloneControl)
    {
        if (ReadEnvironmentToggle(DisableSessionAutostartEnvVar))
        {
            return;
        }

        if (standaloneControl.HasActiveSession || standaloneControl.HasPty)
        {
            return;
        }

        if (!_startingStandaloneControls.Add(standaloneControl))
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await WaitForStandaloneControlAttachmentAsync(standaloneControl);
                if (TopLevel.GetTopLevel(standaloneControl) is null || standaloneControl.Parent is null)
                {
                    return;
                }

                if (!standaloneControl.HasActiveSession && !standaloneControl.HasPty)
                {
                    await StartStandaloneSessionAsync(standaloneControl);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to start session: {ex.Message}");
                AppendEventLog($"[{GetTabDisplayName(standaloneControl)}] Failed to start session: {ex.Message}");
            }
            finally
            {
                _startingStandaloneControls.Remove(standaloneControl);
            }
        }, DispatcherPriority.Background);
    }

    private static async Task WaitForStandaloneControlAttachmentAsync(TerminalControl standaloneControl)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (TopLevel.GetTopLevel(standaloneControl) is not null)
            {
                return;
            }

            await Task.Delay(25);
        }
    }

    private static VtProcessorPreference ResolveVtProcessorPreference(TerminalRenderMode mode)
    {
        return mode switch
        {
            TerminalRenderMode.NativeVt => VtProcessorPreference.Native,
            TerminalRenderMode.ManagedVt => VtProcessorPreference.Managed,
            _ => VtProcessorPreference.Auto,
        };
    }

    private TerminalRenderMode GetRequestedRenderMode()
    {
        if (_viewModel.UseNativeVtControl)
        {
            return TerminalRenderMode.NativeVt;
        }

        if (_viewModel.UseManagedVtControl)
        {
            return TerminalRenderMode.ManagedVt;
        }

        return TerminalRenderMode.RenderedAuto;
    }

    private string DescribeModeFallback(TerminalRenderMode requestedMode)
    {
        return requestedMode switch
        {
            TerminalRenderMode.NativeVt => "native VT provider is unavailable",
            TerminalRenderMode.ManagedVt => "managed VT mode is unavailable",
            _ => "selected mode is unavailable",
        };
    }

    private static bool TryMarkModeUnavailable(
        TerminalRenderMode mode,
        ref TerminalModeCapabilities capabilities)
    {
        switch (mode)
        {
            case TerminalRenderMode.NativeVt when capabilities.NativeVtAvailable:
                capabilities = capabilities with { NativeVtAvailable = false };
                return true;
            case TerminalRenderMode.ManagedVt when capabilities.ManagedVtAvailable:
                capabilities = capabilities with { ManagedVtAvailable = false };
                return true;
            default:
                return false;
        }
    }

    private static string BuildTabOpenedStatus(string tabName, TerminalModeSelection modeSelection)
    {
        if (!modeSelection.FallbackApplied)
        {
            return $"Opened {tabName}";
        }

        string fallbackReason = string.IsNullOrWhiteSpace(modeSelection.FallbackReason)
            ? string.Empty
            : $" ({modeSelection.FallbackReason})";
        return $"Opened {tabName} using {GetModeDisplayName(modeSelection.ResolvedMode)}; fallback from {GetModeDisplayName(modeSelection.RequestedMode)}{fallbackReason}";
    }

    private static string GetModeDisplayName(TerminalRenderMode mode)
    {
        return mode switch
        {
            TerminalRenderMode.NativeVt => "Native VT",
            TerminalRenderMode.ManagedVt => "Managed VT",
            _ => "Rendered",
        };
    }

    private TabVisualMode ResolveTabMode(TerminalControl terminal, TerminalRenderMode mode)
    {
        string vtLabel = terminal.IsUsingNativeVtProcessor
            ? "Ghostty VT"
            : "Basic VT";
        string prefix = mode switch
        {
            TerminalRenderMode.NativeVt => "Native VT",
            TerminalRenderMode.ManagedVt => "Managed VT",
            _ => "Rendered",
        };
        string transportName = _viewModel.SelectedTransportMode.DisplayName;
        string glyph = CreateStandaloneModeGlyph(mode);
        SolidColorBrush glyphBrush = CreateStandaloneModeGlyphBrush(mode);

        return new TabVisualMode(
            $"{prefix} ({transportName} - {vtLabel})",
            glyph,
            glyphBrush);
    }

    private static string CreateStandaloneModeGlyph(TerminalRenderMode mode)
    {
        return mode switch
        {
            TerminalRenderMode.NativeVt => "\u25A0", // ■
            TerminalRenderMode.ManagedVt => "\u25B2", // ▲
            _ => "\u25BC", // ▼ Rendered (Auto VT)
        };
    }

    private static SolidColorBrush CreateStandaloneModeGlyphBrush(TerminalRenderMode mode)
    {
        Color color = mode switch
        {
            TerminalRenderMode.NativeVt => Color.FromRgb(0x6A, 0xB0, 0x4C),
            TerminalRenderMode.ManagedVt => Color.FromRgb(0x4E, 0xC9, 0xB0),
            _ => Color.FromRgb(0x6A, 0x99, 0x55),
        };

        return new SolidColorBrush(color);
    }

    private string? BuildNativeModeAvailabilityHint()
    {
        if (_terminalCapabilities.NativeVtAvailable)
        {
            return null;
        }

        return "Native VT libraries were not found; rendered and managed modes remain available.";
    }

    private static void ConfigureRenderer(SkiaTerminalRenderer? renderer)
    {
        if (renderer is null)
        {
            return;
        }

        renderer.EnableTextShaping = !s_disableTextShaping;
        renderer.EnableTextRenderDiagnostics = s_enableRenderDiagnostics;
        renderer.TextRenderPipeline = s_textRenderPipeline;
    }

    private TerminalControl CreateStandaloneControl()
    {
        INativeVtProcessorProvider[] nativeProviders = [new GhosttyVtProcessorProvider()];
        DefaultPtyFactory ptyFactory = new();
        DemoSshCredentialProvider credentialProvider = new(_viewModel);
        PromptingSshHostKeyValidator hostKeyValidator = new(
            new KnownHostsSshHostKeyValidator(),
            PromptForSshHostKeyTrust);
        CompositeTerminalTransportFactory transportFactory = new(
            new ITerminalTransportProvider[]
            {
                new PtyTerminalTransportProvider(ptyFactory),
                new PipeTerminalTransportProvider(),
                new RawTcpTerminalTransportProvider(),
                new TelnetTerminalTransportProvider(),
                new SerialTerminalTransportProvider(),
                new SshNetTerminalTransportProvider(
                    credentialProvider,
                    hostKeyValidator,
                    new ISshNetAuthenticationMethodContributor[]
                    {
                        new SshNetAgentAuthenticationMethodContributor(),
                    }),
            });

        return new TerminalControl(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(nativeProviders),
            ptyFactory,
            credentialProvider,
            hostKeyValidator,
            transportFactory);
    }

    private bool PromptForSshHostKeyTrust(SshHostKeyTrustPromptRequest request)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return false;
        }

        return Dispatcher.UIThread
            .InvokeAsync(() => _viewModel.ShowSshHostKeyPromptAsync(request))
            .GetAwaiter()
            .GetResult();
    }

    private async Task StartStandaloneSessionAsync(
        TerminalControl standaloneControl,
        bool preserveScrollback = false)
    {
        string tabName = GetTabDisplayName(standaloneControl);
        TerminalSessionDimensions dimensions = BuildSessionDimensions(standaloneControl);
        TerminalLaunchConfiguration launchConfiguration = GetLaunchConfiguration(standaloneControl);
        TerminalSessionProfile launchProfile = ApplyLaunchDimensions(
            launchConfiguration.Profile,
            dimensions);
        ITerminalTransportOptions transportOptions = TerminalSessionProfileMapper.ToTransportOptions(launchProfile);

        if (transportOptions is PtyTransportOptions ptyOptions)
        {
            if (!IsPtyPlatformSupported())
            {
                throw new PlatformNotSupportedException("PTY transport is only supported on macOS, Linux, and Windows.");
            }

            await standaloneControl.StartSessionAsync(ptyOptions, preserveScrollback);

            string? shellPath = ptyOptions.Command?.FileName;
            string shellDisplay = string.IsNullOrWhiteSpace(shellPath)
                ? "Default shell"
                : Path.GetFileName(shellPath);
            AppendEventLog($"[{tabName}] Starting PTY session ({shellDisplay}).");
            UpdateSessionStartedStatus(standaloneControl, $"Started PTY session ({shellDisplay})");
            AppendEventLog($"[{tabName}] PTY session started.");
            return;
        }

        if (transportOptions is PipeTransportOptions pipeOptions)
        {
            AppendEventLog($"[{tabName}] Starting Pipe session ({pipeOptions.Command.FileName}).");
            await standaloneControl.StartPipeAsync(pipeOptions, preserveScrollback);
            UpdateSessionStartedStatus(standaloneControl, $"Started Pipe session ({pipeOptions.Command.FileName})");
            AppendEventLog($"[{tabName}] Pipe session started.");
            return;
        }

        if (transportOptions is RawTcpTransportOptions rawTcpOptions)
        {
            AppendEventLog($"[{tabName}] Starting Raw TCP session {rawTcpOptions.Host}:{rawTcpOptions.Port}.");
            await standaloneControl.StartRawTcpAsync(rawTcpOptions, preserveScrollback);
            UpdateSessionStartedStatus(standaloneControl, $"Started Raw TCP session {rawTcpOptions.Host}:{rawTcpOptions.Port}");
            AppendEventLog($"[{tabName}] Raw TCP session started.");
            return;
        }

        if (transportOptions is TelnetTransportOptions telnetOptions)
        {
            AppendEventLog($"[{tabName}] Starting Telnet session {telnetOptions.Host}:{telnetOptions.Port}.");
            await standaloneControl.StartTelnetAsync(telnetOptions, preserveScrollback);
            UpdateSessionStartedStatus(standaloneControl, $"Started Telnet session {telnetOptions.Host}:{telnetOptions.Port}");
            AppendEventLog($"[{tabName}] Telnet session started.");
            return;
        }

        if (transportOptions is SerialTransportOptions serialOptions)
        {
            AppendEventLog($"[{tabName}] Starting Serial session {serialOptions.PortName} ({serialOptions.BaudRate}).");
            await standaloneControl.StartSerialAsync(serialOptions, preserveScrollback);
            UpdateSessionStartedStatus(standaloneControl, $"Started Serial session {serialOptions.PortName} ({serialOptions.BaudRate})");
            AppendEventLog($"[{tabName}] Serial session started.");
            return;
        }

        if (transportOptions is SshTransportOptions sshOptions)
        {
            AppendEventLog($"[{tabName}] Starting SSH session {sshOptions.Endpoint.Username}@{sshOptions.Endpoint.Host}:{sshOptions.Endpoint.Port}.");
            await standaloneControl.StartSshAsync(sshOptions, preserveScrollback);
            UpdateSessionStartedStatus(
                standaloneControl,
                $"Started SSH session {sshOptions.Endpoint.Username}@{sshOptions.Endpoint.Host}:{sshOptions.Endpoint.Port}");
            AppendEventLog($"[{tabName}] SSH session started.");
            return;
        }

        throw new InvalidOperationException($"Unsupported transport options type '{transportOptions.GetType().FullName}'.");
    }

    private TerminalLaunchConfiguration GetLaunchConfiguration(TerminalControl control)
    {
        if (_launchConfigurations.TryGetValue(control, out TerminalLaunchConfiguration configuration))
        {
            return configuration;
        }

        string profileId = _viewModel.SelectedShellProfile?.Id ?? "default";
        return new TerminalLaunchConfiguration(BuildLaunchProfileFromViewModel(profileId, GetTabDisplayName(control)));
    }

    private static TerminalSessionProfile ApplyLaunchDimensions(
        TerminalSessionProfile profile,
        TerminalSessionDimensions dimensions)
    {
        return profile with
        {
            Layout = new TerminalSessionLayoutSettings
            {
                Columns = dimensions.Columns,
                Rows = dimensions.Rows,
                WidthPixels = dimensions.WidthPixels,
                HeightPixels = dimensions.HeightPixels,
            },
        };
    }

    private SshTransportOptions BuildSshOptions(TerminalSessionDimensions dimensions)
    {
        string host = _viewModel.SshHost.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("SSH host is required.");
        }

        string username = _viewModel.SshUsername.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("SSH username is required.");
        }

        int port = ParsePort(_viewModel.SshPort, "SSH port");
        string expectedHostKeyFingerprint = _viewModel.SshExpectedHostKeyFingerprintSha256.Trim();

        SshAuthenticationOptions authentication = BuildSshAuthenticationOptions();
        SshTransportOptions options = new(
            Endpoint: new SshEndpointOptions(host, port, username),
            RequestPty: _viewModel.SshRequestPty,
            TerminalType: string.IsNullOrWhiteSpace(_viewModel.SshTerminalType)
                ? "xterm-256color"
                : _viewModel.SshTerminalType.Trim(),
            InitialCommand: string.IsNullOrWhiteSpace(_viewModel.SshInitialCommand)
                ? null
                : _viewModel.SshInitialCommand.Trim(),
            Authentication: authentication,
            Dimensions: dimensions);
        options = options with
        {
            Proxy = BuildSshProxyOptions(),
            PortForwardings = BuildSshPortForwardings(),
            X11 = BuildSshX11Options(),
            Policy = BuildSshPolicyOptions(),
        };

        if (!string.IsNullOrWhiteSpace(expectedHostKeyFingerprint))
        {
            options = options with { ExpectedHostKeyFingerprintSha256 = expectedHostKeyFingerprint };
        }

        return options;
    }

    private RawTcpTransportOptions BuildRawTcpOptions(TerminalSessionDimensions dimensions)
    {
        string host = _viewModel.RawTcpHost.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Raw TCP host is required.");
        }

        int port = ParsePort(_viewModel.RawTcpPort, "Raw TCP port");
        return new RawTcpTransportOptions(host, port, dimensions);
    }

    private TelnetTransportOptions BuildTelnetOptions(TerminalSessionDimensions dimensions)
    {
        string host = _viewModel.TelnetHost.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Telnet host is required.");
        }

        int port = ParsePort(_viewModel.TelnetPort, "Telnet port");
        return new TelnetTransportOptions(
            Host: host,
            Port: port,
            TerminalType: string.IsNullOrWhiteSpace(_viewModel.TelnetTerminalType)
                ? "xterm"
                : _viewModel.TelnetTerminalType.Trim(),
            Dimensions: dimensions)
        {
            InitialCommand = NullIfWhiteSpace(_viewModel.TelnetInitialCommand),
        };
    }

    private SerialTransportOptions BuildSerialOptions(TerminalSessionDimensions dimensions)
    {
        string portName = _viewModel.SerialPortName.Trim();
        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new InvalidOperationException("Serial port name is required.");
        }

        int baudRate = ParsePositiveInt(_viewModel.SerialBaudRate, "Serial baud rate");
        int dataBits = ParseIntInRange(_viewModel.SerialDataBits, 5, 8, "Serial data bits");
        string newline = string.IsNullOrEmpty(_viewModel.SerialNewLine)
            ? "\n"
            : _viewModel.SerialNewLine;

        return new SerialTransportOptions(
            PortName: portName,
            BaudRate: baudRate,
            DataBits: dataBits,
            Parity: _viewModel.SelectedSerialParity,
            StopBits: _viewModel.SelectedSerialStopBits,
            Handshake: _viewModel.SelectedSerialHandshake,
            Dimensions: dimensions)
        {
            NewLine = newline,
        };
    }

    private SshProxyOptions? BuildSshProxyOptions()
    {
        if (_viewModel.SelectedSshProxyType == SshProxyType.None)
        {
            return null;
        }

        string host = _viewModel.SshProxyHost.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("SSH proxy host is required when proxy is enabled.");
        }

        int port = ParsePort(_viewModel.SshProxyPort, "SSH proxy port");
        return new SshProxyOptions(
            Type: _viewModel.SelectedSshProxyType,
            Host: host,
            Port: port,
            Username: NullIfWhiteSpace(_viewModel.SshProxyUsername),
            Password: NullIfWhiteSpace(_viewModel.SshProxyPassword));
    }

    private IReadOnlyList<SshPortForwardOptions> BuildSshPortForwardings()
    {
        if (!_viewModel.SshLocalPortForwardEnabled)
        {
            return Array.Empty<SshPortForwardOptions>();
        }

        string bindAddress = string.IsNullOrWhiteSpace(_viewModel.SshLocalPortForwardBindAddress)
            ? "127.0.0.1"
            : _viewModel.SshLocalPortForwardBindAddress.Trim();
        string destinationHost = _viewModel.SshLocalPortForwardDestinationHost.Trim();
        if (string.IsNullOrWhiteSpace(destinationHost))
        {
            throw new InvalidOperationException("SSH local forwarding destination host is required.");
        }

        int sourcePort = ParsePort(_viewModel.SshLocalPortForwardSourcePort, "SSH local forwarding source port");
        int destinationPort = ParsePort(_viewModel.SshLocalPortForwardDestinationPort, "SSH local forwarding destination port");

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
        if (!_viewModel.SshX11Enabled)
        {
            return null;
        }

        string display = _viewModel.SshX11Display.Trim();
        if (string.IsNullOrWhiteSpace(display))
        {
            throw new InvalidOperationException("SSH X11 display is required when X11 forwarding is enabled.");
        }

        return new SshX11Options(
            Enabled: true,
            Display: display);
    }

    private SshPolicyOptions BuildSshPolicyOptions()
    {
        int keepAlive = ParsePositiveInt(_viewModel.SshKeepAliveIntervalSeconds, "SSH keepalive interval");
        int connectTimeout = ParsePositiveInt(_viewModel.SshConnectTimeoutSeconds, "SSH connect timeout");
        return new SshPolicyOptions(
            KeepAliveIntervalSeconds: keepAlive,
            ConnectTimeoutSeconds: connectTimeout);
    }

    private SshAuthenticationOptions BuildSshAuthenticationOptions()
    {
        string authModeId = _viewModel.SelectedSshAuthMode.Id;
        bool usePassword = string.Equals(authModeId, SshAuthModeOption.PasswordModeId, StringComparison.Ordinal)
                           || string.Equals(authModeId, SshAuthModeOption.PasswordAndKeyModeId, StringComparison.Ordinal);
        bool usePrivateKey = string.Equals(authModeId, SshAuthModeOption.PrivateKeyModeId, StringComparison.Ordinal)
                             || string.Equals(authModeId, SshAuthModeOption.PasswordAndKeyModeId, StringComparison.Ordinal);
        bool useAgent = string.Equals(authModeId, SshAuthModeOption.AgentModeId, StringComparison.Ordinal);

        if (usePassword && string.IsNullOrWhiteSpace(_viewModel.SshPassword))
        {
            throw new InvalidOperationException("SSH password is required for the selected authentication mode.");
        }

        if (usePrivateKey && string.IsNullOrWhiteSpace(_viewModel.SshPrivateKeyPath))
        {
            throw new InvalidOperationException("SSH private key path is required for the selected authentication mode.");
        }

        IReadOnlyList<string> privateKeySecretIds = usePrivateKey
            ?
            [
                DemoSshCredentialProvider.PrivateKeySecretId,
            ]
            : Array.Empty<string>();

        return new SshAuthenticationOptions(
            UsePassword: usePassword,
            PasswordSecretId: usePassword ? DemoSshCredentialProvider.PasswordSecretId : null,
            PrivateKeySecretIds: privateKeySecretIds,
            UseAgent: useAgent);
    }

    private TerminalCommandSpec BuildPipeCommand(ShellProfileOption? shellProfile)
    {
        string commandText = string.IsNullOrWhiteSpace(_viewModel.PipeCommandText)
            ? "echo RoyalTerminal pipe transport"
            : _viewModel.PipeCommandText.Trim();

        string shellPath = !string.IsNullOrWhiteSpace(shellProfile?.CommandPath)
            ? shellProfile.CommandPath
            : OperatingSystem.IsWindows()
                ? "cmd.exe"
                : "/bin/sh";

        string shellName = Path.GetFileName(shellPath).ToLowerInvariant();
        if (OperatingSystem.IsWindows())
        {
            if (shellName.Contains("pwsh", StringComparison.Ordinal)
                || shellName.Contains("powershell", StringComparison.Ordinal))
            {
                return new TerminalCommandSpec(
                    shellPath,
                    [
                        "-NoLogo",
                        "-NoProfile",
                        "-Command",
                        commandText,
                    ]);
            }

            return new TerminalCommandSpec(shellPath, ["/c", commandText]);
        }

        return new TerminalCommandSpec(shellPath, ["-lc", commandText]);
    }

    private static int ParsePort(string value, string fieldName)
    {
        if (!int.TryParse(value, out int parsed) || parsed < 1 || parsed > 65535)
        {
            throw new InvalidOperationException($"{fieldName} must be a valid number in range 1-65535.");
        }

        return parsed;
    }

    private static int ParsePositiveInt(string value, string fieldName)
    {
        if (!int.TryParse(value, out int parsed) || parsed <= 0)
        {
            throw new InvalidOperationException($"{fieldName} must be a valid number greater than zero.");
        }

        return parsed;
    }

    private static int ParseIntInRange(string value, int min, int max, string fieldName)
    {
        if (!int.TryParse(value, out int parsed) || parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"{fieldName} must be a valid number in range {min}-{max}.");
        }

        return parsed;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private string ResolveWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_viewModel.WorkingDirectory))
        {
            return _viewModel.WorkingDirectory.Trim();
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static TerminalSessionDimensions BuildSessionDimensions(TerminalControl control)
    {
        SkiaTerminalRenderer? renderer = control.Renderer;
        int widthPx = Math.Max(
            1,
            (int)Math.Round(control.Bounds.Width > 0
                ? control.Bounds.Width
                : control.Columns * (renderer?.CellWidth ?? 1)));
        int heightPx = Math.Max(
            1,
            (int)Math.Round(control.Bounds.Height > 0
                ? control.Bounds.Height
                : control.Rows * (renderer?.CellHeight ?? 1)));

        return new TerminalSessionDimensions(
            Columns: control.Columns,
            Rows: control.Rows,
            WidthPixels: widthPx,
            HeightPixels: heightPx);
    }

    private static bool IsPtyPlatformSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
               || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
               || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    private static bool ReadEnvironmentToggle(string variableName)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static TerminalTextRenderPipeline ReadTextRenderPipeline(string variableName)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "pretext", StringComparison.OrdinalIgnoreCase)
            ? TerminalTextRenderPipeline.Pretext
            : TerminalTextRenderPipeline.HarfBuzz;
    }

    private static Button CreateTabHeader(string title, TabVisualMode mode)
    {
        TextBlock modeIndicator = new()
        {
            Text = mode.Glyph,
            Foreground = mode.GlyphBrush,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
        };

        TextBlock titleText = new()
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };

        PathIcon closeIcon = new()
        {
            Data = ResolveDismissRegularIconGeometry(),
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeIcon.Classes.Add("tabCloseIcon");

        Button closeButton = new()
        {
            Content = closeIcon,
            Width = 24,
            Height = 24,
            MinWidth = 24,
            MinHeight = 24,
            MaxWidth = 24,
            MaxHeight = 24,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeButton.Classes.Add("tabCloseButton");
        ToolTip.SetTip(closeButton, "Close tab");

        StackPanel headerContent = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        headerContent.Children.Add(modeIndicator);
        headerContent.Children.Add(titleText);
        headerContent.Children.Add(closeButton);

        Button headerButton = new()
        {
            Content = headerContent,
            Padding = new Thickness(12, 6),
            BorderThickness = new Thickness(0),
        };
        headerButton.Classes.Add("tabHeader");
        ToolTip.SetTip(headerButton, mode.Name);

        headerButton.Tag = closeButton;
        return headerButton;
    }

    private static Geometry ResolveDismissRegularIconGeometry()
    {
        if (Application.Current?.Resources.TryGetResource(DismissRegularIconResourceKey, null, out object? resource) == true &&
            resource is Geometry geometry)
        {
            return geometry;
        }

        return s_dismissRegularIconFallback;
    }

    private void ActivateTabById(int tabId)
    {
        int tabIndex = _tabs.FindIndex(tab => tab.Index == tabId);
        if (tabIndex >= 0)
        {
            SwitchToTab(tabIndex);
        }
    }

    private void CloseTabById(int tabId)
    {
        TerminalTab? tab = _tabs.Find(candidate => candidate.Index == tabId);
        if (tab is not null)
        {
            CloseTab(tab);
        }
    }

    private void CloseTab(TerminalTab tab)
    {
        int tabIndex = _tabs.IndexOf(tab);
        if (tabIndex < 0)
        {
            return;
        }

        _tabs.Remove(tab);

        _terminalHost.Children.Remove(tab.Container);
        _tabStrip.Children.Remove(tab.HeaderButton);

        DisposeTabTerminals(tab);

        if (_tabs.Count == 0)
        {
            _activeTab = null;
            CreateNewTab();
        }
        else
        {
            int newIndex = Math.Min(tabIndex, _tabs.Count - 1);
            SwitchToTab(newIndex);
        }

        UpdateStatus($"Closed {tab.Title}");
        AppendEventLog($"[{tab.Title}] Tab closed.");
        SyncCaptureReplayState();
    }

    private void CloseCurrentTab()
    {
        if (_activeTab is not null)
        {
            CloseTab(_activeTab);
        }
    }

    private TerminalTab? GetActiveTab() => _activeTab;

    private void SwitchToTab(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return;
        }

        TerminalTab target = _tabs[index];
        if (!EnsureTabMaterialized(target))
        {
            return;
        }

        foreach (TerminalTab tab in _tabs)
        {
            tab.Container.IsVisible = false;
            tab.HeaderButton.Classes.Remove("active");
        }

        target.Container.IsVisible = true;
        target.HeaderButton.Classes.Add("active");
        _activeTab = target;
        if (_activePaneControl is null || !ContainsControl(target.LeafControls, _activePaneControl))
        {
            _activePaneControl = target.LeafControls.Count > 0
                ? target.LeafControls[0]
                : target.Control as TerminalControl;
        }
        UpdateTextRenderPipelineIndicator(_activePaneControl);

        if (target.AutoStartSession)
        {
            for (int i = 0; i < target.LeafControls.Count; i++)
            {
                QueueStandaloneSessionStart(target.LeafControls[i]);
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            Control focusTarget = target.LeafControls.Count > 0
                ? _activePaneControl ?? target.LeafControls[0]
                : target.Control;
            focusTarget.Focus();
            if (focusTarget is TerminalControl standalone)
            {
                standalone.InvalidateTerminal();
            }
        }, DispatcherPriority.Input);

        SyncCaptureReplayState();
        SyncActiveTerminalSurface();
    }

    private void CycleTab(bool forward)
    {
        if (_tabs.Count <= 1)
        {
            return;
        }

        int currentIndex = _activeTab is not null ? _tabs.IndexOf(_activeTab) : 0;
        int next = forward
            ? (currentIndex + 1) % _tabs.Count
            : (currentIndex - 1 + _tabs.Count) % _tabs.Count;
        SwitchToTab(next);
    }

    private void SplitActivePane(TerminalPaneSplitRequest request)
    {
        TerminalTab? tab = GetActiveTab();
        TerminalControl? activeControl = GetActiveStandaloneControl();
        if (tab is null || activeControl is null)
        {
            UpdateStatus("No active pane to split.");
            return;
        }

        if (!_paneRuntimeNodes.TryGetValue(activeControl, out TerminalPaneRuntimeNode? activeNode) ||
            activeNode.LeafContainer is null)
        {
            UpdateStatus("The active pane cannot be split.");
            return;
        }

        TerminalModeSelection modeSelection = ResolveModeSelectionForNewTab();
        TerminalControl newControl = CreateTerminalControlWithRuntimeFallback(
            modeSelection,
            out TerminalModeSelection finalizedModeSelection);
        TerminalLaunchConfiguration activeLaunchConfiguration = GetLaunchConfiguration(activeControl);
        string? splitWorkingDirectory = NormalizeOptional(activeNode.WorkingDirectory) ??
                                        NormalizeOptional(tab.WorkingDirectory);
        TerminalLaunchConfiguration newLaunchConfiguration = splitWorkingDirectory is null
            ? activeLaunchConfiguration
            : new TerminalLaunchConfiguration(ApplyLaunchWorkingDirectory(
                activeLaunchConfiguration.Profile,
                splitWorkingDirectory));
        _launchConfigurations[newControl] = newLaunchConfiguration;
        ApplyLaunchAppearanceSettings(newControl, newLaunchConfiguration.Profile);

        ScrollViewer newContainer = CreatePaneScrollViewer(newControl);
        TerminalPaneRuntimeNode newNode = new(
            CreatePaneId(),
            $"{tab.Title} Pane",
            activeNode.ProfileId ?? tab.ProfileId,
            splitWorkingDirectory,
            activeNode.TransportId ?? tab.TransportId,
            activeNode.TransportProfileId);
        newNode.SetLeaf(newControl, newContainer);
        RegisterPaneControl(newControl, newNode);

        string orientation = request == TerminalPaneSplitRequest.Down
            ? TerminalWorkspacePaneSplitOrientations.Vertical
            : TerminalWorkspacePaneSplitOrientations.Horizontal;
        TerminalPaneRuntimeNode splitNode = new(
            CreatePaneId(),
            activeNode.Title,
            activeNode.ProfileId,
            activeNode.WorkingDirectory,
            activeNode.TransportId,
            activeNode.TransportProfileId);
        TerminalPaneRuntimeNode? previousParent = activeNode.Parent;
        int hostIndex = -1;
        bool wasVisible = true;
        int parentChildIndex = -1;
        int parentChildRow = 0;
        int parentChildColumn = 0;

        if (previousParent is null)
        {
            hostIndex = _terminalHost.Children.IndexOf(tab.Container);
            wasVisible = tab.Container.IsVisible;
            if (hostIndex >= 0)
            {
                _terminalHost.Children.RemoveAt(hostIndex);
            }
        }
        else if (previousParent.SplitGrid is not null)
        {
            parentChildIndex = previousParent.SplitGrid.Children.IndexOf(activeNode.Visual);
            if (parentChildIndex >= 0)
            {
                parentChildRow = Grid.GetRow(activeNode.Visual);
                parentChildColumn = Grid.GetColumn(activeNode.Visual);
                previousParent.SplitGrid.Children.RemoveAt(parentChildIndex);
            }
        }

        Grid splitGrid = CreateSplitGrid(orientation, 0.5, activeNode.Visual, newNode.Visual);
        splitNode.SetSplit(orientation, 0.5, activeNode, newNode, splitGrid);

        splitNode.Parent = previousParent;
        activeNode.Parent = splitNode;
        newNode.Parent = splitNode;

        if (previousParent is null)
        {
            ReplaceTabRootVisual(tab, splitGrid, splitNode, hostIndex, wasVisible);
        }
        else
        {
            ReplaceChildPane(
                previousParent,
                activeNode,
                splitNode,
                parentChildIndex,
                parentChildRow,
                parentChildColumn);
            tab.SetLeafControls(CollectLeafControls(tab.RootPaneNode));
        }

        SetActivePane(newControl, focus: true);
        QueueStandaloneSessionStart(newControl);
        UpdateStatus(request == TerminalPaneSplitRequest.Down
            ? $"Split {tab.Title} downward."
            : $"Split {tab.Title} to the right.");
        AppendEventLog($"[{tab.Title}] Split active pane ({orientation}).");

        if (finalizedModeSelection.FallbackApplied && finalizedModeSelection.FallbackReason is not null)
        {
            AppendEventLog($"[{tab.Title}] Pane mode fallback: {finalizedModeSelection.FallbackReason}");
        }
    }

    private void FocusPane(TerminalPaneDirection direction)
    {
        TerminalTab? tab = GetActiveTab();
        TerminalControl? activeControl = GetActiveStandaloneControl();
        if (tab is null || activeControl is null || tab.LeafControls.Count <= 1)
        {
            return;
        }

        TerminalControl? target = FindDirectionalPane(tab, activeControl, direction)
            ?? FindSequentialPane(tab, activeControl, IsForwardDirection(direction));
        if (target is null || ReferenceEquals(target, activeControl))
        {
            return;
        }

        SetActivePane(target, focus: true);
        UpdateStatus($"Focused pane {GetPaneOrdinal(tab, target)} of {tab.LeafControls.Count.ToString(CultureInfo.InvariantCulture)}.");
    }

    private void ResizePane(TerminalPaneDirection direction)
    {
        TerminalTab? tab = GetActiveTab();
        TerminalControl? activeControl = GetActiveStandaloneControl();
        if (tab is null || activeControl is null ||
            !_paneRuntimeNodes.TryGetValue(activeControl, out TerminalPaneRuntimeNode? activeNode))
        {
            return;
        }

        bool wantsHorizontal = direction is TerminalPaneDirection.Left or TerminalPaneDirection.Right;
        TerminalPaneRuntimeNode? splitNode = FindResizeSplit(activeNode, wantsHorizontal);
        if (splitNode is null)
        {
            return;
        }

        double delta = direction is TerminalPaneDirection.Right or TerminalPaneDirection.Down
            ? 0.05
            : -0.05;
        double ratio = Math.Clamp(splitNode.Ratio + delta, 0.05, 0.95);
        ApplySplitRatio(splitNode, ratio);
        UpdateStatus($"Pane ratio {ratio.ToString("0.00", CultureInfo.InvariantCulture)}.");
        AppendEventLog($"[{tab.Title}] Resized pane split to {ratio.ToString("0.00", CultureInfo.InvariantCulture)}.");
    }

    private static bool IsForwardDirection(TerminalPaneDirection direction)
    {
        return direction is TerminalPaneDirection.Right or TerminalPaneDirection.Down;
    }

    private static TerminalPaneRuntimeNode? FindResizeSplit(TerminalPaneRuntimeNode activeNode, bool wantsHorizontal)
    {
        TerminalPaneRuntimeNode? node = activeNode.Parent;
        while (node is not null)
        {
            bool isHorizontal = string.Equals(
                node.Orientation,
                TerminalWorkspacePaneSplitOrientations.Horizontal,
                StringComparison.Ordinal);
            if (isHorizontal == wantsHorizontal)
            {
                return node;
            }

            node = node.Parent;
        }

        return null;
    }

    private static void ApplySplitRatio(TerminalPaneRuntimeNode splitNode, double ratio)
    {
        splitNode.Ratio = Math.Clamp(ratio, 0.05, 0.95);
        if (splitNode.SplitGrid is null)
        {
            return;
        }

        if (string.Equals(splitNode.Orientation, TerminalWorkspacePaneSplitOrientations.Vertical, StringComparison.Ordinal))
        {
            splitNode.SplitGrid.RowDefinitions[0].Height = new GridLength(splitNode.Ratio, GridUnitType.Star);
            splitNode.SplitGrid.RowDefinitions[2].Height = new GridLength(1.0 - splitNode.Ratio, GridUnitType.Star);
            return;
        }

        splitNode.SplitGrid.ColumnDefinitions[0].Width = new GridLength(splitNode.Ratio, GridUnitType.Star);
        splitNode.SplitGrid.ColumnDefinitions[2].Width = new GridLength(1.0 - splitNode.Ratio, GridUnitType.Star);
    }

    private void ReplaceTabRootVisual(
        TerminalTab tab,
        Control newRootVisual,
        TerminalPaneRuntimeNode rootNode,
        int hostIndex,
        bool wasVisible)
    {
        if (hostIndex >= 0)
        {
            _terminalHost.Children.Insert(hostIndex, newRootVisual);
        }
        else if (!_terminalHost.Children.Contains(newRootVisual))
        {
            _terminalHost.Children.Add(newRootVisual);
        }

        newRootVisual.IsVisible = wasVisible;
        tab.SetPaneRoot(newRootVisual, newRootVisual, rootNode, CollectLeafControls(rootNode));
        if (ReferenceEquals(_activeTab, tab))
        {
            _activeTab = tab;
        }
    }

    private static void ReplaceChildPane(
        TerminalPaneRuntimeNode parent,
        TerminalPaneRuntimeNode oldChild,
        TerminalPaneRuntimeNode newChild,
        int childIndex,
        int row,
        int column)
    {
        if (parent.SplitGrid is null)
        {
            return;
        }

        if (childIndex >= 0)
        {
            Grid.SetRow(newChild.Visual, row);
            Grid.SetColumn(newChild.Visual, column);
            parent.SplitGrid.Children.Insert(childIndex, newChild.Visual);
        }

        if (ReferenceEquals(parent.First, oldChild))
        {
            parent.First = newChild;
        }
        else if (ReferenceEquals(parent.Second, oldChild))
        {
            parent.Second = newChild;
        }
    }

    private static IReadOnlyList<TerminalControl> CollectLeafControls(TerminalPaneRuntimeNode? rootNode)
    {
        if (rootNode is null)
        {
            return [];
        }

        List<TerminalControl> controls = [];
        CollectLeafControls(rootNode, controls);
        return controls;
    }

    private static void CollectLeafControls(TerminalPaneRuntimeNode node, List<TerminalControl> controls)
    {
        if (node.Control is not null)
        {
            controls.Add(node.Control);
            return;
        }

        if (node.First is not null)
        {
            CollectLeafControls(node.First, controls);
        }

        if (node.Second is not null)
        {
            CollectLeafControls(node.Second, controls);
        }
    }

    private void SetActivePane(TerminalControl control, bool focus)
    {
        _activePaneControl = control;
        if (focus)
        {
            Dispatcher.UIThread.Post(() =>
            {
                control.Focus();
                control.InvalidateTerminal();
            }, DispatcherPriority.Input);
        }

        SyncActiveTerminalSurface();
        SyncCaptureReplayState();
    }

    private TerminalControl? FindDirectionalPane(
        TerminalTab tab,
        TerminalControl activeControl,
        TerminalPaneDirection direction)
    {
        if (!_paneRuntimeNodes.TryGetValue(activeControl, out TerminalPaneRuntimeNode? activeNode) ||
            activeNode.LeafContainer is null)
        {
            return null;
        }

        Point? activeOrigin = activeNode.LeafContainer.TranslatePoint(new Point(0, 0), tab.Container);
        if (activeOrigin is null)
        {
            return null;
        }

        Point activeCenter = new(
            activeOrigin.Value.X + activeNode.LeafContainer.Bounds.Width / 2.0,
            activeOrigin.Value.Y + activeNode.LeafContainer.Bounds.Height / 2.0);
        TerminalControl? best = null;
        double bestScore = double.MaxValue;

        for (int i = 0; i < tab.LeafControls.Count; i++)
        {
            TerminalControl candidate = tab.LeafControls[i];
            if (ReferenceEquals(candidate, activeControl) ||
                !_paneRuntimeNodes.TryGetValue(candidate, out TerminalPaneRuntimeNode? candidateNode) ||
                candidateNode.LeafContainer is null)
            {
                continue;
            }

            Point? candidateOrigin = candidateNode.LeafContainer.TranslatePoint(new Point(0, 0), tab.Container);
            if (candidateOrigin is null)
            {
                continue;
            }

            Point candidateCenter = new(
                candidateOrigin.Value.X + candidateNode.LeafContainer.Bounds.Width / 2.0,
                candidateOrigin.Value.Y + candidateNode.LeafContainer.Bounds.Height / 2.0);
            double dx = candidateCenter.X - activeCenter.X;
            double dy = candidateCenter.Y - activeCenter.Y;
            if (!IsCandidateInDirection(direction, dx, dy))
            {
                continue;
            }

            double primaryDistance = direction is TerminalPaneDirection.Left or TerminalPaneDirection.Right
                ? Math.Abs(dx)
                : Math.Abs(dy);
            double secondaryDistance = direction is TerminalPaneDirection.Left or TerminalPaneDirection.Right
                ? Math.Abs(dy)
                : Math.Abs(dx);
            double score = primaryDistance * 1000.0 + secondaryDistance;
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool IsCandidateInDirection(TerminalPaneDirection direction, double dx, double dy)
    {
        return direction switch
        {
            TerminalPaneDirection.Left => dx < -0.5,
            TerminalPaneDirection.Right => dx > 0.5,
            TerminalPaneDirection.Up => dy < -0.5,
            TerminalPaneDirection.Down => dy > 0.5,
            _ => false,
        };
    }

    private static TerminalControl? FindSequentialPane(
        TerminalTab tab,
        TerminalControl activeControl,
        bool forward)
    {
        int index = IndexOfControl(tab.LeafControls, activeControl);
        if (index < 0 || tab.LeafControls.Count == 0)
        {
            return null;
        }

        int targetIndex = forward
            ? (index + 1) % tab.LeafControls.Count
            : (index - 1 + tab.LeafControls.Count) % tab.LeafControls.Count;
        return tab.LeafControls[targetIndex];
    }

    private static string GetPaneOrdinal(TerminalTab tab, TerminalControl control)
    {
        int index = IndexOfControl(tab.LeafControls, control);
        return (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static bool ContainsControl(IReadOnlyList<TerminalControl> controls, TerminalControl control)
    {
        return IndexOfControl(controls, control) >= 0;
    }

    private static int IndexOfControl(IReadOnlyList<TerminalControl> controls, TerminalControl control)
    {
        for (int i = 0; i < controls.Count; i++)
        {
            if (ReferenceEquals(controls[i], control))
            {
                return i;
            }
        }

        return -1;
    }

    #endregion

    #region Capture And Replay

    private TerminalCaptureRuntime EnsureCaptureRuntime(TerminalControl control)
    {
        if (_captureRuntimes.TryGetValue(control, out TerminalCaptureRuntime? runtime))
        {
            return runtime;
        }

        runtime = new TerminalCaptureRuntime(control);
        runtime.StateChanged += OnCaptureRuntimeStateChanged;
        _captureRuntimes[control] = runtime;
        return runtime;
    }

    private TerminalCaptureRuntime? GetActiveCaptureRuntime(bool createIfMissing = false)
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            return null;
        }

        return _captureRuntimes.TryGetValue(control, out TerminalCaptureRuntime? runtime)
            ? runtime
            : createIfMissing
                ? EnsureCaptureRuntime(control)
                : null;
    }

    private void ToggleCapture(bool shouldStartCapture)
    {
        TerminalCaptureRuntime? runtime = GetActiveCaptureRuntime(createIfMissing: true);
        if (runtime is null)
        {
            _viewModel.SetCaptureState(false, false);
            UpdateStatus("Capture is available for terminal panes only.");
            return;
        }

        if (shouldStartCapture)
        {
            runtime.StartCapture();
            UpdateStatus("Capture started.");
        }
        else
        {
            TerminalCaptureSession session = runtime.StopCapture();
            UpdateStatus(
                $"Capture stopped ({session.Events.Count} events, {session.DurationMilliseconds / 1000.0:0.00}s).");
        }

        SyncCaptureReplayState();
    }

    private async Task SaveCaptureAsync()
    {
        TerminalCaptureRuntime? runtime = GetActiveCaptureRuntime();
        if (runtime is null)
        {
            UpdateStatus("No terminal pane is active to save capture.");
            return;
        }

        TerminalCaptureSession? session = runtime.GetCaptureSnapshot();
        if (session is null || session.Events.Count == 0)
        {
            UpdateStatus("No captured events are available to save.");
            return;
        }

        if (!_window.StorageProvider.CanSave)
        {
            UpdateStatus("Save file picker is unavailable in this runtime.");
            return;
        }

        try
        {
            ITerminalCaptureSessionFormat selectedFormat = GetSelectedCaptureFormat();
            FilePickerSaveOptions options = new()
            {
                Title = "Save Terminal Capture",
                SuggestedFileName = CreateCaptureFileName(selectedFormat.Descriptor),
                DefaultExtension = selectedFormat.Descriptor.DefaultExtension.TrimStart('.'),
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    CreateCaptureFileType(selectedFormat.Descriptor),
                ],
            };

            IStorageFile? file = await _window.StorageProvider.SaveFilePickerAsync(options);
            if (file is null)
            {
                return;
            }

            await using Stream stream = await file.OpenWriteAsync();
            await selectedFormat.SaveAsync(session, stream);
            await stream.FlushAsync();

            UpdateStatus($"Capture saved to '{file.Name}' ({selectedFormat.Descriptor.DisplayName}).");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Failed to save capture: {ex.Message}");
        }
        finally
        {
            SyncCaptureReplayState();
        }
    }

    private async Task LoadReplayAsync()
    {
        if (!_window.StorageProvider.CanOpen)
        {
            UpdateStatus("Open file picker is unavailable in this runtime.");
            return;
        }

        try
        {
            FilePickerOpenOptions options = new()
            {
                Title = "Load Terminal Capture",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    CreateAllCaptureFileType(),
                    CreateCaptureFileType(TerminalCaptureSessionFormats.RoyalTerminalJson.Descriptor),
                    CreateCaptureFileType(TerminalCaptureSessionFormats.AsciicastV3.Descriptor),
                ],
            };

            IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0)
            {
                return;
            }

            IStorageFile file = files[0];
            await using Stream stream = await file.OpenReadAsync();
            TerminalCaptureSession session = await TerminalCaptureSessionFormats.DefaultRegistry
                .LoadAsync(stream, file.Name);

            CreateReplayTab(session, file.Name);
            SyncCaptureReplayState();
        }
        catch (Exception ex)
        {
            UpdateStatus($"Failed to load replay: {ex.Message}");
        }
    }

    private async Task BrowseSettingsFontFileAsync()
    {
        if (!_window.StorageProvider.CanOpen)
        {
            _viewModel.SettingsPanelState.SetStatus("Open file picker is unavailable in this runtime.");
            return;
        }

        try
        {
            FilePickerOpenOptions options = new()
            {
                Title = "Load Terminal Font",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    CreateFontFileType(),
                ],
            };

            IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0)
            {
                return;
            }

            string? localPath = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
            {
                _viewModel.SettingsPanelState.SetStatus("Selected font must be a local file.");
                return;
            }

            _viewModel.SettingsPanelState.LoadFontFile(localPath);
        }
        catch (Exception ex)
        {
            _viewModel.SettingsPanelState.SetStatus($"Font load failed: {ex.Message}");
        }
    }

    private void SetReplayPlaying(bool shouldPlay)
    {
        TerminalCaptureRuntime? runtime = GetActiveCaptureRuntime();
        if (runtime is null || !runtime.IsReplayEnabled)
        {
            UpdateStatus("Replay is not enabled for the active tab.");
            return;
        }

        if (shouldPlay)
        {
            runtime.PlayReplay();
            UpdateStatus("Replay playing.");
        }
        else
        {
            runtime.PauseReplay();
            UpdateStatus("Replay paused.");
        }

        SyncCaptureReplayState();
    }

    private void StopReplay()
    {
        TerminalCaptureRuntime? runtime = GetActiveCaptureRuntime();
        if (runtime is null || !runtime.IsReplayEnabled)
        {
            return;
        }

        runtime.StopReplay();
        UpdateStatus("Replay stopped.");
        SyncCaptureReplayState();
    }

    private void SeekReplayFromViewModel(double value)
    {
        if (_suppressReplayTimelineSeek)
        {
            return;
        }

        TerminalCaptureRuntime? runtime = GetActiveCaptureRuntime();
        if (runtime is null || !runtime.IsReplayEnabled)
        {
            return;
        }

        runtime.SeekReplay(value);
        SyncCaptureReplayState();
    }

    private void OnCaptureRuntimeStateChanged(object? sender, EventArgs e)
    {
        _ = e;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnCaptureRuntimeStateChanged(sender, EventArgs.Empty));
            return;
        }

        TerminalCaptureRuntime? runtime = GetActiveCaptureRuntime();
        if (runtime is null || !ReferenceEquals(runtime, sender))
        {
            return;
        }

        SyncCaptureReplayState();
    }

    private void SyncCaptureReplayState()
    {
        TerminalCaptureRuntime? runtime = GetActiveCaptureRuntime();
        _suppressReplayTimelineSeek = true;
        try
        {
            if (runtime is null)
            {
                _viewModel.SetCaptureState(false, false);
                _viewModel.SetReplayState(false, false, 0, 0, string.Empty);
                return;
            }

            _viewModel.SetCaptureState(runtime.IsCaptureActive, runtime.HasCapture);
            _viewModel.SetReplayState(
                runtime.IsReplayEnabled,
                runtime.IsReplayPlaying,
                runtime.ReplayPositionSeconds,
                runtime.ReplayDurationSeconds,
                runtime.ReplaySourceLabel);
        }
        finally
        {
            _suppressReplayTimelineSeek = false;
        }
    }

    private ITerminalCaptureSessionFormat GetSelectedCaptureFormat()
    {
        return TerminalCaptureSessionFormats.DefaultRegistry.FindById(_viewModel.SelectedCaptureFormat.FormatId)
            ?? TerminalCaptureSessionFormats.RoyalTerminalJson;
    }

    private static FilePickerFileType CreateAllCaptureFileType()
    {
        IReadOnlyList<ITerminalCaptureSessionFormat> formats = TerminalCaptureSessionFormats.BuiltIn;
        List<string> patterns = [];
        List<string> mimeTypes = [];
        for (int i = 0; i < formats.Count; i++)
        {
            AddFileTypeValues(formats[i].Descriptor, patterns, mimeTypes);
        }

        return new FilePickerFileType("Terminal Capture")
        {
            Patterns = patterns,
            MimeTypes = mimeTypes,
        };
    }

    private static FilePickerFileType CreateCaptureFileType(TerminalCaptureFileFormatDescriptor descriptor)
    {
        List<string> patterns = [];
        List<string> mimeTypes = [];
        AddFileTypeValues(descriptor, patterns, mimeTypes);

        return new FilePickerFileType(descriptor.DisplayName)
        {
            Patterns = patterns,
            MimeTypes = mimeTypes,
        };
    }

    private static void AddFileTypeValues(
        TerminalCaptureFileFormatDescriptor descriptor,
        List<string> patterns,
        List<string> mimeTypes)
    {
        IReadOnlyList<string> extensions = descriptor.FileExtensions;
        for (int i = 0; i < extensions.Count; i++)
        {
            string pattern = $"*{extensions[i]}";
            if (!ContainsOrdinalIgnoreCase(patterns, pattern))
            {
                patterns.Add(pattern);
            }
        }

        IReadOnlyList<string> descriptorMimeTypes = descriptor.MimeTypes;
        for (int i = 0; i < descriptorMimeTypes.Count; i++)
        {
            string mimeType = descriptorMimeTypes[i];
            if (!ContainsOrdinalIgnoreCase(mimeTypes, mimeType))
            {
                mimeTypes.Add(mimeType);
            }
        }
    }

    private static bool ContainsOrdinalIgnoreCase(IReadOnlyList<string> values, string value)
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

    private static FilePickerFileType CreateFontFileType()
    {
        return new FilePickerFileType("Font Files")
        {
            Patterns = ["*.ttf", "*.otf", "*.ttc", "*.otc"],
            MimeTypes = ["font/ttf", "font/otf", "font/collection", "application/font-sfnt"],
        };
    }

    private static string CreateCaptureFileName(TerminalCaptureFileFormatDescriptor descriptor)
    {
        return $"terminal-capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}{descriptor.DefaultExtension}";
    }

    #endregion

    #region Clipboard

    private async Task CopySelection()
    {
        TerminalControl? standalone = GetActiveStandaloneControl();
        if (standalone is null)
        {
            return;
        }

        await standalone.CopySelectionAsync();
    }

    private async Task PasteClipboard()
    {
        TerminalControl? standalone = GetActiveStandaloneControl();
        if (standalone is null)
        {
            return;
        }

        await standalone.PasteAsync();
    }

    private void SelectAll()
    {
        TerminalControl? standalone = GetActiveStandaloneControl();
        if (standalone is null)
        {
            return;
        }

        standalone.SelectAll();
    }

    private async Task CopySnapshotAsync(TerminalSnapshotExportFormat format)
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            UpdateStatus("Snapshot export is available for standalone terminal tabs only.");
            return;
        }

        if (!control.SupportsSnapshotFormat(format))
        {
            UpdateStatus($"{format} snapshot export is unavailable for {GetTabDisplayName(control)}.");
            return;
        }

        TerminalSnapshotExportOptions options = CreateSnapshotExportOptions(format);
        if (!control.TryExportSnapshot(format, options, out string snapshot) || string.IsNullOrEmpty(snapshot))
        {
            UpdateStatus($"No {format} snapshot data is available for {GetTabDisplayName(control)}.");
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(_window)?.Clipboard;
        if (clipboard is null)
        {
            UpdateStatus("Clipboard is unavailable for snapshot export.");
            return;
        }

        await clipboard.SetTextAsync(snapshot);
        UpdateStatus($"Copied {format} snapshot from {GetTabDisplayName(control)}.");
    }

    private static TerminalSnapshotExportOptions CreateSnapshotExportOptions(TerminalSnapshotExportFormat format)
    {
        return format switch
        {
            TerminalSnapshotExportFormat.PlainText => new TerminalSnapshotExportOptions(
                Unwrap: true,
                TrimTrailingWhitespace: true),
            TerminalSnapshotExportFormat.StyledVt => new TerminalSnapshotExportOptions(
                Unwrap: true,
                TrimTrailingWhitespace: true,
                Extras: new TerminalSnapshotExportExtras(
                    IncludeCursor: true,
                    IncludeStyle: true,
                    IncludeHyperlinks: true,
                    IncludeKittyKeyboard: true,
                    IncludeCharsets: true,
                    IncludePalette: true,
                    IncludeModes: true,
                    IncludeScrollingRegion: true,
                    IncludeTabstops: true,
                    IncludeKeyboardModes: true)),
            TerminalSnapshotExportFormat.Html => new TerminalSnapshotExportOptions(
                Unwrap: true,
                TrimTrailingWhitespace: true,
                Extras: new TerminalSnapshotExportExtras(
                    IncludeHyperlinks: true)),
            _ => default,
        };
    }

    #endregion

    #region Showcase And Search

    private void ApplySearch(string? needle)
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            _viewModel.ClearSearchState();
            UpdateStatus("Search is available for standalone terminal tabs only.");
            return;
        }

        if (string.IsNullOrWhiteSpace(needle))
        {
            control.EndSearch();
            _viewModel.ClearSearchState();
            UpdateStatus($"Search cleared in {GetTabDisplayName(control)}.");
            return;
        }

        control.StartSearch(needle);
        SyncSearchSurface(control);
        if (control.SearchTotal > 0)
        {
            string scope = control.IsUsingNativeVtProcessor ? "native scrollback" : "viewport mirror";
            UpdateStatus($"Found {control.SearchTotal} match(es) in {scope}.");
        }
        else
        {
            UpdateStatus($"No matches found in {GetTabDisplayName(control)}.");
        }
    }

    private void ClearSearch()
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            _viewModel.ClearSearchState();
            return;
        }

        control.EndSearch();
        _viewModel.ClearSearchState();
        UpdateStatus($"Search cleared in {GetTabDisplayName(control)}.");
    }

    private async Task RestartActiveSessionAsync()
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            UpdateStatus("Restart is available for standalone terminal tabs only.");
            return;
        }

        string tabName = GetTabDisplayName(control);
        bool preserveScrollback = _viewModel.PreserveScrollbackOnRestart;
        try
        {
            if (control.HasActiveSession || control.HasPty)
            {
                control.StopPty();
            }

            await StartStandaloneSessionAsync(control, preserveScrollback);
            AppendEventLog($"[{tabName}] Restarted session ({(preserveScrollback ? "history preserved" : "clean history")}).");
            UpdateStatus(preserveScrollback
                ? $"Restarted {tabName} with preserved history."
                : $"Restarted {tabName}.");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Failed to restart session: {ex.Message}");
            AppendEventLog($"[{tabName}] Failed to restart session: {ex.Message}");
        }
    }

    private void ClearActiveScrollback()
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            UpdateStatus("Scrollback clear is available for standalone terminal tabs only.");
            return;
        }

        bool canRequestPromptRedraw = control.HasActiveSession || control.HasPty;

        control.ClearHistory();
        control.ScrollToBottom();
        if (canRequestPromptRedraw)
        {
            control.RequestPromptRedraw();
        }

        string tabName = GetTabDisplayName(control);
        AppendEventLog($"[{tabName}] Cleared history.");
        UpdateStatus($"Cleared history for {tabName}.");
    }

    private void SelectNextSearchMatch()
    {
        SelectSearchMatch(directionForward: true);
    }

    private void SelectPreviousSearchMatch()
    {
        SelectSearchMatch(directionForward: false);
    }

    private void SelectSearchMatch(bool directionForward)
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            _viewModel.ClearSearchState();
            return;
        }

        bool moved = directionForward
            ? control.SelectNextSearchMatch()
            : control.SelectPreviousSearchMatch();
        SyncSearchSurface(control);

        if (!moved)
        {
            UpdateStatus("No search matches are active.");
            return;
        }

        int selectedDisplay = Math.Clamp(control.SearchSelected + 1, 1, Math.Max(1, control.SearchTotal));
        UpdateStatus($"Search match {selectedDisplay} of {control.SearchTotal}.");
    }

    private void ShowHyperlinkSample()
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            UpdateStatus("Hyperlink showcase is available for standalone terminal tabs only.");
            return;
        }

        control.WriteOutput(s_hyperlinkShowcaseBytes);
        SyncActiveTerminalSurface();
        string tabName = GetTabDisplayName(control);
        AppendEventLog($"[{tabName}] Injected OSC8 hyperlink showcase.");
        UpdateStatus($"Hyperlink showcase injected into {tabName}.");
    }

    private void ShowKittyGraphicsSample()
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            UpdateStatus("Kitty Graphics showcase is available for standalone terminal tabs only.");
            return;
        }

        if (!control.IsUsingNativeVtProcessor || !GhosttyVtProcessor.IsAvailable())
        {
            UpdateStatus("Kitty Graphics showcase requires an active Ghostty VT tab.");
            return;
        }

        GhosttyVtHelpers.GhosttyBuildFeatures features = GhosttyVtHelpers.GetBuildFeatures();
        if (!features.KittyGraphics)
        {
            UpdateStatus("This libghostty-vt build does not include Kitty Graphics support.");
            return;
        }

        control.WriteOutput(s_kittyGraphicsShowcaseBytes);
        SyncActiveTerminalSurface();
        string tabName = GetTabDisplayName(control);
        AppendEventLog($"[{tabName}] Injected Ghostty Kitty Graphics showcase.");
        UpdateStatus($"Kitty Graphics showcase injected into {tabName}.");
    }

    private void ToggleGhosttyDiagnostics(bool show)
    {
        _viewModel.SetGhosttyDiagnostics(show, BuildGhosttyDiagnosticsText());
        UpdateStatus(show ? "Native diagnostics opened." : "Native diagnostics hidden.");
    }

    private void SyncActiveTerminalSurface()
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            _viewModel.ClearSearchState();
            _viewModel.SetGhosttyDiagnostics(_viewModel.ShowGhosttyDiagnostics, BuildGhosttyDiagnosticsText());
            return;
        }

        UpdateDimensions(control.Columns, control.Rows);
        SyncSearchSurface(control);
        if (_viewModel.ShowGhosttyDiagnostics)
        {
            _viewModel.SetGhosttyDiagnostics(true, BuildGhosttyDiagnosticsText(control));
        }
    }

    private void SyncSearchSurface(TerminalControl control)
    {
        if (string.IsNullOrWhiteSpace(control.SearchNeedle))
        {
            _viewModel.ClearSearchState();
            return;
        }

        _viewModel.SetSearchState(
            control.SearchNeedle,
            control.SearchTotal,
            control.SearchSelected,
            control.IsUsingNativeVtProcessor);
    }

    private TerminalControl? GetActiveStandaloneControl()
    {
        TerminalTab? activeTab = GetActiveTab();
        if (activeTab is not null)
        {
            if (_activePaneControl is not null && ContainsControl(activeTab.LeafControls, _activePaneControl))
            {
                return _activePaneControl;
            }

            if (activeTab.Control is TerminalControl visibleControl)
            {
                return visibleControl;
            }

            return activeTab.LeafControls.Count > 0 ? activeTab.LeafControls[0] : null;
        }

        return null;
    }

    private string BuildGhosttyDiagnosticsText()
    {
        return BuildGhosttyDiagnosticsText(GetActiveStandaloneControl());
    }

    private string BuildGhosttyDiagnosticsText(TerminalControl? control)
    {
        StringBuilder builder = new();
        bool nativeAvailable = GhosttyVtProcessor.IsAvailable();
        builder.AppendLine("Ghostty VT runtime");
        builder.Append("  libghostty-vt available: ").AppendLine(nativeAvailable ? "yes" : "no");
        if (nativeAvailable)
        {
            GhosttyVtHelpers.GhosttyBuildInfoSnapshot buildInfo = GhosttyVtHelpers.GetBuildInfoSnapshot();
            builder.Append("  version: ").AppendLine(string.IsNullOrWhiteSpace(buildInfo.VersionString) ? "(unknown)" : buildInfo.VersionString);
            builder.Append("  semver: ")
                .Append(buildInfo.VersionMajor.ToString(CultureInfo.InvariantCulture))
                .Append('.')
                .Append(buildInfo.VersionMinor.ToString(CultureInfo.InvariantCulture))
                .Append('.')
                .AppendLine(buildInfo.VersionPatch.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(buildInfo.VersionPre))
            {
                builder.Append("  pre-release: ").AppendLine(buildInfo.VersionPre);
            }

            if (!string.IsNullOrWhiteSpace(buildInfo.VersionBuild))
            {
                builder.Append("  build metadata: ").AppendLine(buildInfo.VersionBuild);
            }

            builder.Append("  SIMD: ").AppendLine(buildInfo.Simd ? "yes" : "no");
            builder.Append("  Kitty graphics: ").AppendLine(buildInfo.KittyGraphics ? "yes" : "no");
            builder.Append("  tmux control mode: ").AppendLine(buildInfo.TmuxControlMode ? "yes" : "no");
            builder.Append("  optimize mode: ").AppendLine(buildInfo.OptimizeMode.ToString());
            builder.Append("  focus gained sample: ").AppendLine(EscapeForDiagnostics(
                GhosttyVtHelpers.EncodeFocusString(GhosttyVtNative.GhosttyFocusEvent.Gained)));
        }

        builder.AppendLine();
        builder.AppendLine("Active tab");
        if (control is null || _activeTab is null)
        {
            builder.AppendLine("  No standalone terminal tab is active.");
            return builder.ToString().TrimEnd();
        }

        builder.Append("  Title: ").AppendLine(_activeTab.Title);
        builder.Append("  Mode: ").AppendLine(_activeTab.ModeName);
        builder.Append("  VT backend: ").AppendLine(control.IsUsingNativeVtProcessor ? "Ghostty VT" : "Basic VT");
        builder.Append("  Session transport: ").AppendLine(control.ActiveTransportId ?? _viewModel.SelectedTransportMode.DisplayName);
        builder.Append("  Grid: ").Append(control.Columns.ToString(CultureInfo.InvariantCulture))
            .Append('x')
            .AppendLine(control.Rows.ToString(CultureInfo.InvariantCulture));
        builder.Append("  Search: ").AppendLine(string.IsNullOrWhiteSpace(control.SearchNeedle)
            ? "inactive"
            : $"{Math.Clamp(control.SearchSelected + 1, 1, Math.Max(1, control.SearchTotal))}/{control.SearchTotal} for '{control.SearchNeedle}'");
        builder.Append("  Hovered link: ").AppendLine(control.HoveredLinkUrl ?? "(none)");
        builder.Append("  Sixel graphics enabled: ").AppendLine(control.SixelGraphicsEnabled ? "yes" : "no");
        builder.Append("  Sixel graphics on screen: ").AppendLine(control.Screen?.HasRasterGraphics == true ? "yes" : "no");
        builder.Append("  Kitty graphics on screen: ").AppendLine(control.Screen?.HasKittyGraphics == true ? "yes" : "no");

        if (control.ScrollData is { } scrollData)
        {
            builder.Append("  UI scroll rows: ").Append(scrollData.OffsetRows.ToString(CultureInfo.InvariantCulture))
                .Append(" / ")
                .AppendLine(scrollData.MaxOffsetRows.ToString(CultureInfo.InvariantCulture));
        }

        if (nativeAvailable)
        {
            GhosttyVtNative.GhosttySizeReportSize size = new()
            {
                Rows = (ushort)Math.Clamp(control.Rows, 0, ushort.MaxValue),
                Columns = (ushort)Math.Clamp(control.Columns, 0, ushort.MaxValue),
                CellWidth = (ushort)Math.Clamp((int)Math.Round(control.Renderer?.CellWidth ?? 0), 0, ushort.MaxValue),
                CellHeight = (ushort)Math.Clamp((int)Math.Round(control.Renderer?.CellHeight ?? 0), 0, ushort.MaxValue),
            };
            builder.Append("  size report sample: ").AppendLine(EscapeForDiagnostics(
                GhosttyVtHelpers.EncodeSizeReportString(GhosttyVtNative.GhosttySizeReportStyle.Csi18T, size)));
            builder.Append("  bracketed paste report sample: ").AppendLine(EscapeForDiagnostics(
                GhosttyVtHelpers.EncodeModeReportString(
                    GhosttyVtNative.ModeBracketedPaste,
                    GhosttyVtNative.GhosttyModeReportState.Set)));
        }

        return builder.ToString().TrimEnd();
    }

    private static string EscapeForDiagnostics(string value)
    {
        return value
            .Replace("\u001b", "ESC", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    #endregion

    #region Font And Theme

    private void ApplyFontSettings(TerminalControl standalone)
    {
        string fontFamily = NormalizeFontFamily(_viewModel.FontFamilyName);
        string fontFilePath = NormalizeFontFilePath(_viewModel.FontSource, _viewModel.FontFilePath);

        standalone.FontFamilyName = fontFamily;
        standalone.FontFilePath = fontFilePath;
        standalone.FontSource = _viewModel.FontSource == TerminalFontSource.File && !string.IsNullOrWhiteSpace(fontFilePath)
            ? TerminalFontSource.File
            : TerminalFontSource.System;
        standalone.TerminalFontSize = _viewModel.FontSize;
        standalone.FontSubpixelPositioning = _viewModel.FontSubpixelPositioning;
        standalone.FontEdging = _viewModel.FontEdging;
        standalone.FontHinting = _viewModel.FontHinting;
        standalone.FontBaselineSnap = _viewModel.FontBaselineSnap;
        standalone.FontEmbeddedBitmaps = _viewModel.FontEmbeddedBitmaps;
        standalone.FontEmbolden = _viewModel.FontEmbolden;
        standalone.FontForceAutoHinting = _viewModel.FontForceAutoHinting;
        standalone.FontLinearMetrics = _viewModel.FontLinearMetrics;
    }

    private static void ApplyLaunchAppearanceSettings(
        TerminalControl standalone,
        TerminalSessionProfile profile)
    {
        standalone.AutoScroll = profile.Appearance.AutoScroll;
        standalone.BackgroundOpacityEnabled = profile.Appearance.BackgroundOpacityEnabled;
    }

    private static string NormalizeFontFamily(string? fontFamilyName)
    {
        return string.IsNullOrWhiteSpace(fontFamilyName)
            ? MonoFont
            : fontFamilyName.Trim();
    }

    private static string NormalizeFontFilePath(TerminalFontSource fontSource, string? fontFilePath)
    {
        return fontSource == TerminalFontSource.File && !string.IsNullOrWhiteSpace(fontFilePath)
            ? fontFilePath.Trim()
            : string.Empty;
    }

    private void ApplyFontSize(double fontSize)
    {
        foreach (TerminalTab tab in _tabs)
        {
            for (int i = 0; i < tab.LeafControls.Count; i++)
            {
                tab.LeafControls[i].TerminalFontSize = fontSize;
                tab.LeafControls[i].InvalidateTerminal();
            }

            if (tab.LeafControls.Count == 0 && tab.Control is TerminalControl standalone)
            {
                standalone.TerminalFontSize = fontSize;
                standalone.InvalidateTerminal();
            }
        }
    }

    private void ApplyTheme(bool isDarkTheme)
    {
        ApplyTheme(isDarkTheme ? TerminalTheme.Dark : TerminalTheme.Light);
    }

    private void ApplyTheme(TerminalTheme theme)
    {
        foreach (TerminalTab tab in _tabs)
        {
            for (int i = 0; i < tab.LeafControls.Count; i++)
            {
                tab.LeafControls[i].ApplyTheme(theme);
                tab.LeafControls[i].InvalidateTerminal();
            }

            if (tab.LeafControls.Count == 0 && tab.Control is TerminalControl standalone)
            {
                standalone.ApplyTheme(theme);
                standalone.InvalidateTerminal();
            }
        }

        ApplyThemeResources(CreateChromePalette(theme));
    }

    private void ApplyShaderSample(string shaderId)
    {
        TerminalShaderSampleOption option = TerminalShaderSampleCatalog.FindOption(shaderId);
        for (int i = 0; i < _tabs.Count; i++)
        {
            TerminalTab tab = _tabs[i];
            for (int paneIndex = 0; paneIndex < tab.LeafControls.Count; paneIndex++)
            {
                ApplyShaderSampleToControl(tab.LeafControls[paneIndex]);
                tab.LeafControls[paneIndex].InvalidateTerminal();
            }

            if (tab.LeafControls.Count == 0 && tab.Control is TerminalControl standalone)
            {
                ApplyShaderSampleToControl(standalone);
                standalone.InvalidateTerminal();
            }
        }

        string status = string.Equals(option.Id, TerminalShaderSampleCatalog.OffShaderId, StringComparison.Ordinal)
            ? "Shader effects disabled."
            : $"Shader sample enabled: {option.DisplayName}.";
        UpdateStatus(status);
        AppendEventLog(status);
    }

    private void ApplyShaderSampleToControl(TerminalControl control)
    {
        IReadOnlyList<TerminalShaderSource>? sources =
            TerminalShaderSampleCatalog.GetSources(_viewModel.SelectedShaderSample.Id);
        control.ShaderSources = sources;
        control.ShaderAnimationEnabled = true;
    }

    private static void UpdateTabHeaderVisual(TerminalTab tab, TabVisualMode tabMode)
    {
        tab.SetModeName(tabMode.Name);
        ToolTip.SetTip(tab.HeaderButton, tabMode.Name);
        if (tab.HeaderButton.Content is StackPanel headerContent
            && headerContent.Children.Count > 0
            && headerContent.Children[0] is TextBlock modeIndicator)
        {
            modeIndicator.Text = tabMode.Glyph;
            modeIndicator.Foreground = tabMode.GlyphBrush;
        }
    }

    private static void ApplyThemeResources(ThemePalette palette)
    {
        UpdateBrushResource("WindowBackgroundBrush", palette.WindowBackground);
        UpdateBrushResource("ToolbarBackgroundBrush", palette.ToolbarBackground);
        UpdateBrushResource("ToolbarDividerBrush", palette.ToolbarDivider);
        UpdateBrushResource("ToolbarForegroundBrush", palette.ToolbarForeground);
        UpdateBrushResource("TabStripBackgroundBrush", palette.TabStripBackground);
        UpdateBrushResource("StatusBarBackgroundBrush", palette.StatusBarBackground);
        UpdateBrushResource("StatusBarForegroundBrush", palette.StatusBarForeground);
        UpdateBrushResource("TabHeaderBackgroundBrush", palette.TabHeaderBackground);
        UpdateBrushResource("TabHeaderForegroundBrush", palette.TabHeaderForeground);
        UpdateBrushResource("TabHeaderActiveBackgroundBrush", palette.TabHeaderActiveBackground);
        UpdateBrushResource("TabHeaderActiveForegroundBrush", palette.TabHeaderActiveForeground);
    }

    private static void UpdateBrushResource(string key, Color color)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static ThemePalette CreateChromePalette(TerminalTheme theme)
    {
        Color background = ToAvaloniaColor(theme.DefaultBackground);
        Color foreground = ToAvaloniaColor(theme.DefaultForeground);
        Color accent = ToAvaloniaColor(theme.Palette[4]);
        Color divider = BlendColor(background, foreground, 0.24);
        Color toolbarBackground = BlendColor(background, foreground, 0.06);
        Color tabHeaderBackground = BlendColor(background, foreground, 0.10);
        Color tabHeaderActiveBackground = BlendColor(background, accent, 0.10);

        return new ThemePalette(
            WindowBackground: background,
            ToolbarBackground: toolbarBackground,
            ToolbarDivider: divider,
            ToolbarForeground: foreground,
            TabStripBackground: BlendColor(background, foreground, 0.04),
            StatusBarBackground: toolbarBackground,
            StatusBarForeground: foreground,
            TabHeaderBackground: tabHeaderBackground,
            TabHeaderForeground: foreground,
            TabHeaderActiveBackground: tabHeaderActiveBackground,
            TabHeaderActiveForeground: foreground,
            TerminalForeground: foreground,
            TerminalBackground: background);
    }

    private static Color ToAvaloniaColor(uint argb)
    {
        return Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
    }

    private static Color BlendColor(Color from, Color to, double amount)
    {
        double t = Math.Clamp(amount, 0.0, 1.0);
        byte a = (byte)Math.Clamp((int)Math.Round(from.A + ((to.A - from.A) * t), MidpointRounding.AwayFromZero), 0, 255);
        byte r = (byte)Math.Clamp((int)Math.Round(from.R + ((to.R - from.R) * t), MidpointRounding.AwayFromZero), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(from.G + ((to.G - from.G) * t), MidpointRounding.AwayFromZero), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(from.B + ((to.B - from.B) * t), MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromArgb(a, r, g, b);
    }

    #endregion

    private async Task PrepareSettingsPanelAsync()
    {
        TerminalSettingsPanelState state = _viewModel.SettingsPanelState;
        bool alreadyLoaded = _settingsProfilesLoaded;
        TerminalSessionProfilesDocument? loadedDocument = null;
        if (!_settingsProfilesLoaded)
        {
            loadedDocument = await _settingsProfileStore.LoadAsync().ConfigureAwait(false);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (loadedDocument is not null)
            {
                TerminalSessionProfilesDocument document = loadedDocument;
                if (document.Profiles.Count == 0)
                {
                    SyncSettingsStateFromViewModel(state);
                    document = state.BuildDocument();
                    state.MarkSaved("Initialized settings profiles from current runtime values.");
                }

                state.LoadDocument(document);
                _settingsProfilesLoaded = true;
                _sessionLauncherDocument = state.BuildDocument();
                RefreshSessionLauncherOptions(_sessionLauncherDocument);
            }

            if (alreadyLoaded && !state.IsDirty)
            {
                SyncSettingsStateFromViewModel(state);
                _sessionLauncherDocument = state.BuildDocument();
                RefreshSessionLauncherOptions(_sessionLauncherDocument);
            }
        });
    }

    private void SyncSettingsStateFromViewModel(TerminalSettingsPanelState state)
    {
        state.UpdateFromRuntime(current =>
        {
            current.SessionName = _viewModel.SessionName;
            current.SelectedTransportMode = ResolveSettingsTransportMode(
                current,
                _viewModel.SelectedTransportMode.Id);
            current.WorkingDirectory = _viewModel.WorkingDirectory;
            current.ShellPath = _viewModel.SelectedShellProfile?.CommandPath ?? string.Empty;
            current.PipeCommandText = _viewModel.PipeCommandText;
            current.PipeMergeStdErrIntoStdOut = _viewModel.PipeMergeStdErrIntoStdOut;

            current.RawTcpHost = _viewModel.RawTcpHost;
            current.RawTcpPort = _viewModel.RawTcpPort;
            current.TelnetHost = _viewModel.TelnetHost;
            current.TelnetPort = _viewModel.TelnetPort;
            current.TelnetTerminalType = _viewModel.TelnetTerminalType;
            current.TelnetInitialCommand = _viewModel.TelnetInitialCommand;

            current.SerialPortName = _viewModel.SerialPortName;
            current.SerialBaudRate = _viewModel.SerialBaudRate;
            current.SerialDataBits = _viewModel.SerialDataBits;
            current.SelectedSerialParity = _viewModel.SelectedSerialParity;
            current.SelectedSerialStopBits = _viewModel.SelectedSerialStopBits;
            current.SelectedSerialHandshake = _viewModel.SelectedSerialHandshake;
            current.SerialNewLine = _viewModel.SerialNewLine;

            current.SshHost = _viewModel.SshHost;
            current.SshPort = _viewModel.SshPort;
            current.SshUsername = _viewModel.SshUsername;
            current.SelectedSshAuthMode = ResolveSettingsSshAuthMode(current, _viewModel.SelectedSshAuthMode.Id);
            current.SshPassword = _viewModel.SshPassword;
            current.SshPrivateKeyPath = _viewModel.SshPrivateKeyPath;
            current.SshExpectedHostKeyFingerprintSha256 = _viewModel.SshExpectedHostKeyFingerprintSha256;
            current.SshRequestPty = _viewModel.SshRequestPty;
            current.SshTerminalType = _viewModel.SshTerminalType;
            current.SshInitialCommand = _viewModel.SshInitialCommand;
            current.SelectedSshProxyType = _viewModel.SelectedSshProxyType;
            current.SshProxyHost = _viewModel.SshProxyHost;
            current.SshProxyPort = _viewModel.SshProxyPort;
            current.SshProxyUsername = _viewModel.SshProxyUsername;
            current.SshProxyPassword = _viewModel.SshProxyPassword;
            current.SshLocalPortForwardEnabled = _viewModel.SshLocalPortForwardEnabled;
            current.SshLocalPortForwardBindAddress = _viewModel.SshLocalPortForwardBindAddress;
            current.SshLocalPortForwardSourcePort = _viewModel.SshLocalPortForwardSourcePort;
            current.SshLocalPortForwardDestinationHost = _viewModel.SshLocalPortForwardDestinationHost;
            current.SshLocalPortForwardDestinationPort = _viewModel.SshLocalPortForwardDestinationPort;
            current.SshX11Enabled = _viewModel.SshX11Enabled;
            current.SshX11Display = _viewModel.SshX11Display;
            current.SshKeepAliveIntervalSeconds = _viewModel.SshKeepAliveIntervalSeconds;
            current.SshConnectTimeoutSeconds = _viewModel.SshConnectTimeoutSeconds;

            current.CopyOnSelectEnabled = _viewModel.CopyOnSelectEnabled;
            current.EnableBellNotifications = _viewModel.EnableBellNotifications;
            current.BackspaceSendsControlH = _viewModel.BackspaceSendsControlH;
            current.EnableTextShaping = _viewModel.EnableTextShaping;
            current.ReflowOnResize = _viewModel.ReflowOnResize;
            current.SixelGraphicsEnabled = _viewModel.SixelGraphicsEnabled;
            current.EnableLigatures = _viewModel.EnableLigatures;
            current.SelectedPasteSafetyPolicy = _viewModel.SelectedPasteSafetyPolicy;

            current.SelectedFontSource = _viewModel.FontSource;
            current.FontFamilyName = _viewModel.FontFamilyName;
            current.FontFilePath = _viewModel.FontFilePath;
            current.FontSize = _viewModel.FontSize;
            current.FontSubpixelPositioning = _viewModel.FontSubpixelPositioning;
            current.SelectedFontEdging = _viewModel.FontEdging;
            current.SelectedFontHinting = _viewModel.FontHinting;
            current.FontBaselineSnap = _viewModel.FontBaselineSnap;
            current.FontEmbeddedBitmaps = _viewModel.FontEmbeddedBitmaps;
            current.FontEmbolden = _viewModel.FontEmbolden;
            current.FontForceAutoHinting = _viewModel.FontForceAutoHinting;
            current.FontLinearMetrics = _viewModel.FontLinearMetrics;
            current.AutoScroll = true;
            current.BackgroundOpacityEnabled = false;
            current.SelectedTextHighlightingMode = ResolveSettingsTextHighlightingMode(
                current,
                _viewModel.TextHighlightingMode);

            current.SessionLoggingEnabled = _viewModel.SessionLoggingEnabled;
            current.SessionLogFilePath = _viewModel.SessionLogFilePath;
            current.SelectedSessionLogFormat = _viewModel.SelectedSessionLogFormat;
            current.SessionLogFlushFrequently = _viewModel.SessionLogFlushFrequently;
            current.EventLogEnabled = _viewModel.EventLogEnabled;
        });
    }

    private static TerminalSettingsTransportModeOption ResolveSettingsTransportMode(
        TerminalSettingsPanelState state,
        string transportId)
    {
        for (int i = 0; i < state.TransportModes.Count; i++)
        {
            if (string.Equals(state.TransportModes[i].Id, transportId, StringComparison.Ordinal))
            {
                return state.TransportModes[i];
            }
        }

        return state.TransportModes[0];
    }

    private static TerminalSettingsSshAuthModeOption ResolveSettingsSshAuthMode(
        TerminalSettingsPanelState state,
        string authModeId)
    {
        for (int i = 0; i < state.SshAuthModes.Count; i++)
        {
            if (string.Equals(state.SshAuthModes[i].Id, authModeId, StringComparison.Ordinal))
            {
                return state.SshAuthModes[i];
            }
        }

        return state.SshAuthModes[0];
    }

    private static TerminalSettingsTextHighlightingModeOption ResolveSettingsTextHighlightingMode(
        TerminalSettingsPanelState state,
        TerminalTextHighlightingMode mode)
    {
        TerminalTextHighlightingMode normalized = Enum.IsDefined(mode)
            ? mode
            : TerminalTextHighlightingMode.Static;

        for (int i = 0; i < state.TextHighlightingModes.Count; i++)
        {
            if (state.TextHighlightingModes[i].Mode == normalized)
            {
                return state.TextHighlightingModes[i];
            }
        }

        return state.TextHighlightingModes[0];
    }

    private void ApplySettingsPanelState()
    {
        TerminalSettingsPanelState state = _viewModel.SettingsPanelState;

        _viewModel.SessionName = state.SessionName;
        _viewModel.SelectedTransportMode = ResolveViewModelTransportMode(state.SelectedTransportMode?.Id);
        SelectOrAddRuntimeShellProfile(state.SelectedProfile?.Id ?? "settings", state.SessionName, state.ShellPath);
        _viewModel.WorkingDirectory = state.WorkingDirectory;
        _viewModel.PipeCommandText = state.PipeCommandText;
        _viewModel.PipeMergeStdErrIntoStdOut = state.PipeMergeStdErrIntoStdOut;

        _viewModel.RawTcpHost = state.RawTcpHost;
        _viewModel.RawTcpPort = state.RawTcpPort;
        _viewModel.TelnetHost = state.TelnetHost;
        _viewModel.TelnetPort = state.TelnetPort;
        _viewModel.TelnetTerminalType = state.TelnetTerminalType;
        _viewModel.TelnetInitialCommand = state.TelnetInitialCommand;

        _viewModel.SerialPortName = state.SerialPortName;
        _viewModel.SerialBaudRate = state.SerialBaudRate;
        _viewModel.SerialDataBits = state.SerialDataBits;
        _viewModel.SelectedSerialParity = state.SelectedSerialParity;
        _viewModel.SelectedSerialStopBits = state.SelectedSerialStopBits;
        _viewModel.SelectedSerialHandshake = state.SelectedSerialHandshake;
        _viewModel.SerialNewLine = state.SerialNewLine;

        _viewModel.SshHost = state.SshHost;
        _viewModel.SshPort = state.SshPort;
        _viewModel.SshUsername = state.SshUsername;
        _viewModel.SelectedSshAuthMode = ResolveViewModelSshAuthMode(state.SelectedSshAuthMode?.Id);
        _viewModel.SshPassword = state.SshPassword;
        _viewModel.SshPrivateKeyPath = state.SshPrivateKeyPath;
        _viewModel.SshExpectedHostKeyFingerprintSha256 = state.SshExpectedHostKeyFingerprintSha256;
        _viewModel.SshRequestPty = state.SshRequestPty;
        _viewModel.SshTerminalType = state.SshTerminalType;
        _viewModel.SshInitialCommand = state.SshInitialCommand;
        _viewModel.SelectedSshProxyType = state.SelectedSshProxyType;
        _viewModel.SshProxyHost = state.SshProxyHost;
        _viewModel.SshProxyPort = state.SshProxyPort;
        _viewModel.SshProxyUsername = state.SshProxyUsername;
        _viewModel.SshProxyPassword = state.SshProxyPassword;
        _viewModel.SshLocalPortForwardEnabled = state.SshLocalPortForwardEnabled;
        _viewModel.SshLocalPortForwardBindAddress = state.SshLocalPortForwardBindAddress;
        _viewModel.SshLocalPortForwardSourcePort = state.SshLocalPortForwardSourcePort;
        _viewModel.SshLocalPortForwardDestinationHost = state.SshLocalPortForwardDestinationHost;
        _viewModel.SshLocalPortForwardDestinationPort = state.SshLocalPortForwardDestinationPort;
        _viewModel.SshX11Enabled = state.SshX11Enabled;
        _viewModel.SshX11Display = state.SshX11Display;
        _viewModel.SshKeepAliveIntervalSeconds = state.SshKeepAliveIntervalSeconds;
        _viewModel.SshConnectTimeoutSeconds = state.SshConnectTimeoutSeconds;

        _viewModel.CopyOnSelectEnabled = state.CopyOnSelectEnabled;
        _viewModel.EnableBellNotifications = state.EnableBellNotifications;
        _viewModel.BackspaceSendsControlH = state.BackspaceSendsControlH;
        _viewModel.EnableTextShaping = state.EnableTextShaping;
        _viewModel.ReflowOnResize = state.ReflowOnResize;
        _viewModel.SixelGraphicsEnabled = state.SixelGraphicsEnabled;
        _viewModel.EnableLigatures = state.EnableLigatures;
        _viewModel.SelectedPasteSafetyPolicy = state.SelectedPasteSafetyPolicy;

        _viewModel.SessionLoggingEnabled = state.SessionLoggingEnabled;
        _viewModel.SessionLogFilePath = state.SessionLogFilePath;
        _viewModel.SelectedSessionLogFormat = state.SelectedSessionLogFormat;
        _viewModel.SessionLogFlushFrequently = state.SessionLogFlushFrequently;
        _viewModel.EventLogEnabled = state.EventLogEnabled;

        double fontSize = Math.Clamp(state.FontSize, 8, 72);
        string fontFamilyName = NormalizeFontFamily(state.FontFamilyName);
        string fontFilePath = NormalizeFontFilePath(state.SelectedFontSource, state.FontFilePath);
        TerminalFontSource fontSource = state.SelectedFontSource == TerminalFontSource.File &&
            !string.IsNullOrWhiteSpace(fontFilePath)
                ? TerminalFontSource.File
                : TerminalFontSource.System;
        _viewModel.FontSource = fontSource;
        _viewModel.FontFamilyName = fontFamilyName;
        _viewModel.FontFilePath = fontFilePath;
        _viewModel.FontSubpixelPositioning = state.FontSubpixelPositioning;
        _viewModel.FontEdging = state.SelectedFontEdging;
        _viewModel.FontHinting = state.SelectedFontHinting;
        _viewModel.FontBaselineSnap = state.FontBaselineSnap;
        _viewModel.FontEmbeddedBitmaps = state.FontEmbeddedBitmaps;
        _viewModel.FontEmbolden = state.FontEmbolden;
        _viewModel.FontForceAutoHinting = state.FontForceAutoHinting;
        _viewModel.FontLinearMetrics = state.FontLinearMetrics;
        _viewModel.SetFontSizeFromSettings(fontSize);
        ApplyFontSize(fontSize);

        _viewModel.TextHighlightingMode = state.SelectedTextHighlightingMode?.Mode ?? TerminalTextHighlightingMode.Static;
        _viewModel.TextHighlightRules = BuildRuntimeTextHighlightRules(state);

        foreach (TerminalControl standaloneControl in EnumerateTerminalControls())
        {
            ApplyFontSettings(standaloneControl);
            standaloneControl.AutoScroll = state.AutoScroll;
            standaloneControl.BackgroundOpacityEnabled = state.BackgroundOpacityEnabled;
            standaloneControl.TextHighlightingMode = _viewModel.TextHighlightingMode;
            standaloneControl.TextHighlightRules = _viewModel.TextHighlightRules;
        }

        ApplyTerminalBehaviorSettingsToAllStandaloneTabs();
        _sessionLauncherDocument = state.BuildDocument();
        RefreshSessionLauncherOptions(_sessionLauncherDocument);
        state.SetStatus("Applied settings to demo runtime.");
    }

    private static IReadOnlyList<TerminalTextHighlightRule> BuildRuntimeTextHighlightRules(
        TerminalSettingsPanelState state)
    {
        TerminalSessionProfilesDocument document = state.BuildDocument();
        string? selectedProfileId = state.SelectedProfile?.Id ?? document.DefaultProfileId;
        TerminalSessionProfile? profile = null;
        for (int i = 0; i < document.Profiles.Count; i++)
        {
            if (string.Equals(document.Profiles[i].Id, selectedProfileId, StringComparison.Ordinal))
            {
                profile = document.Profiles[i];
                break;
            }
        }

        profile ??= document.Profiles.Count > 0 ? document.Profiles[0] : null;
        if (profile is null || profile.Appearance.TextHighlightRules.Count == 0)
        {
            return [];
        }

        List<TerminalTextHighlightRule> rules = new(profile.Appearance.TextHighlightRules.Count);
        for (int i = 0; i < profile.Appearance.TextHighlightRules.Count; i++)
        {
            TerminalSessionTextHighlightRule source = profile.Appearance.TextHighlightRules[i];
            if (string.IsNullOrWhiteSpace(source.Pattern))
            {
                continue;
            }

            rules.Add(new TerminalTextHighlightRule
            {
                Name = string.IsNullOrWhiteSpace(source.Name) ? "Highlight Rule" : source.Name.Trim(),
                Pattern = source.Pattern.Trim(),
                IsEnabled = source.IsEnabled,
                Foreground = TryParseArgbColor(source.ForegroundColor, out uint foreground) ? foreground : null,
                Background = TryParseArgbColor(source.BackgroundColor, out uint background) ? background : null,
                DarkForeground = TryParseArgbColor(source.DarkForegroundColor, out uint darkForeground) ? darkForeground : null,
                DarkBackground = TryParseArgbColor(source.DarkBackgroundColor, out uint darkBackground) ? darkBackground : null,
            });
        }

        return rules.Count == 0 ? [] : rules;
    }

    private static bool TryParseArgbColor(string? value, out uint color)
    {
        color = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> text = value.Trim().AsSpan();
        if (text.Length > 0 && text[0] == '#')
        {
            text = text[1..];
        }

        if (text.Length != 6 && text.Length != 8)
        {
            return false;
        }

        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color))
        {
            return false;
        }

        if (text.Length == 6)
        {
            color |= 0xFF000000u;
        }

        return true;
    }

    private async Task SaveSettingsPanelStateAsync()
    {
        try
        {
            TerminalSessionProfilesDocument document = _viewModel.SettingsPanelState.BuildDocument();
            await _settingsProfileStore.SaveAsync(document);

            string storeLabel = _settingsProfileStore is JsonFileTerminalSessionProfileStore fileStore
                ? fileStore.FilePath
                : "configured profile store";
            _viewModel.SettingsPanelState.MarkSaved($"Saved profile document to '{storeLabel}'.");
            _sessionLauncherDocument = document;
            RefreshSessionLauncherOptions(document);
            UpdateStatus("Saved settings profiles.");
        }
        catch (Exception ex)
        {
            _viewModel.SettingsPanelState.SetStatus($"Save failed: {ex.Message}");
            UpdateStatus($"Failed to save settings profiles: {ex.Message}");
        }
    }

    private TransportModeOption ResolveViewModelTransportMode(string? transportId)
    {
        for (int i = 0; i < _viewModel.TransportModes.Count; i++)
        {
            if (string.Equals(_viewModel.TransportModes[i].Id, transportId, StringComparison.Ordinal))
            {
                return _viewModel.TransportModes[i];
            }
        }

        return _viewModel.TransportModes[0];
    }

    private SshAuthModeOption ResolveViewModelSshAuthMode(string? authModeId)
    {
        for (int i = 0; i < _viewModel.SshAuthModes.Count; i++)
        {
            if (string.Equals(_viewModel.SshAuthModes[i].Id, authModeId, StringComparison.Ordinal))
            {
                return _viewModel.SshAuthModes[i];
            }
        }

        return _viewModel.SshAuthModes[0];
    }

    #region Status

    private void ApplyTerminalBehaviorSettingsToAllStandaloneTabs()
    {
        foreach (TerminalControl standaloneControl in EnumerateTerminalControls())
        {
            ApplyTerminalBehaviorSettings(standaloneControl);
        }
    }

    private void ReportSixelGraphicsSettingChanged(bool enabled)
    {
        string status = enabled
            ? "Sixel graphics enabled for terminal tabs."
            : "Sixel graphics disabled for terminal tabs.";
        UpdateStatus(status);
        AppendEventLog(status);

        if (_viewModel.ShowGhosttyDiagnostics)
        {
            _viewModel.SetGhosttyDiagnostics(true, BuildGhosttyDiagnosticsText());
        }
    }

    private void ApplySessionLoggingSubscriptionsToAllStandaloneTabs()
    {
        foreach (TerminalControl standaloneControl in EnumerateTerminalControls())
        {
            UpdateSessionLoggingSubscription(standaloneControl);
        }
    }

    private void UpdateSessionLoggingSubscription(TerminalControl control)
    {
        if (_viewModel.SessionLoggingEnabled)
        {
            if (_sessionLogOutputHandlers.ContainsKey(control))
            {
                return;
            }

            EventHandler<TerminalDataEventArgs> handler = (_, args) =>
            {
                WriteSessionLogOutput(control, args.Data);
            };

            control.DataReceived += handler;
            _sessionLogOutputHandlers[control] = handler;
            return;
        }

        if (_sessionLogOutputHandlers.Remove(control, out EventHandler<TerminalDataEventArgs>? existingHandler))
        {
            control.DataReceived -= existingHandler;
        }
    }

    private void RegisterCommandHistoryCapture(TerminalControl control)
    {
        if (_commandHistoryHandlers.ContainsKey(control))
        {
            return;
        }

        TerminalCommandHistoryCaptureService capture = new(
            CreateCommandHistoryCaptureContext(control));
        EventHandler<TerminalShellIntegrationEventArgs> handler = (_, args) =>
        {
            UpdateRuntimeWorkingDirectory(control, args.Value);
            TerminalCommandHistoryEntry? entry = capture.Process(args.Value);
            if (entry is null)
            {
                return;
            }

            AppendEventLog($"[{GetTabDisplayName(control)}] Command completed: {entry.CommandLine}");
            QueueCommandHistoryEntry(entry);
        };

        control.ShellIntegrationEventReceived += handler;
        _commandHistoryCaptures[control] = capture;
        _commandHistoryHandlers[control] = handler;
    }

    private void UnregisterCommandHistoryCapture(TerminalControl control)
    {
        if (_commandHistoryHandlers.Remove(control, out EventHandler<TerminalShellIntegrationEventArgs>? handler))
        {
            control.ShellIntegrationEventReceived -= handler;
        }

        _commandHistoryCaptures.Remove(control);
    }

    private void UpdateRuntimeWorkingDirectory(
        TerminalControl control,
        TerminalShellIntegrationEvent value)
    {
        if (value.Kind != TerminalShellIntegrationEventKind.WorkingDirectoryChanged)
        {
            return;
        }

        string? workingDirectory = NormalizeOptional(value.WorkingDirectory);
        if (_paneRuntimeNodes.TryGetValue(control, out TerminalPaneRuntimeNode? node))
        {
            node.SetWorkingDirectory(workingDirectory);
        }

        TerminalTab? tab = FindTabForControl(control);
        if (tab is not null && (tab.LeafControls.Count <= 1 || ReferenceEquals(tab.Control, control)))
        {
            tab.SetWorkingDirectory(workingDirectory);
        }
    }

    private TerminalCommandHistoryCaptureContext CreateCommandHistoryCaptureContext(TerminalControl control)
    {
        string? profileId = null;
        string? transportId = null;
        string? shellId = null;
        if (_paneRuntimeNodes.TryGetValue(control, out TerminalPaneRuntimeNode? node))
        {
            profileId = NormalizeOptional(node.ProfileId);
            transportId = NormalizeOptional(node.TransportId);
        }

        if (_launchConfigurations.TryGetValue(control, out TerminalLaunchConfiguration launchConfiguration))
        {
            profileId ??= NormalizeOptional(launchConfiguration.Profile.Id);
            transportId ??= NormalizeOptional(launchConfiguration.Profile.Transport.TransportId);
            shellId = NormalizeOptional(launchConfiguration.Profile.Id);
        }

        profileId ??= _viewModel.SelectedShellProfile?.Id;
        transportId ??= _viewModel.SelectedTransportMode.Id;
        shellId ??= profileId;
        return new TerminalCommandHistoryCaptureContext(
            ProfileId: profileId,
            TransportId: transportId,
            ShellId: shellId);
    }

    private void QueueCommandHistoryEntry(TerminalCommandHistoryEntry entry)
    {
        Task task = AppendCommandHistoryEntryAsync(entry);
        lock (_commandHistoryWriteTaskSync)
        {
            _commandHistoryWriteTasks.Add(task);
        }

        _ = task.ContinueWith(
            RemoveCommandHistoryWriteTask,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RemoveCommandHistoryWriteTask(Task task)
    {
        lock (_commandHistoryWriteTaskSync)
        {
            _commandHistoryWriteTasks.Remove(task);
        }
    }

    private void FlushCommandHistoryWrites()
    {
        while (true)
        {
            Task[] pendingWrites;
            lock (_commandHistoryWriteTaskSync)
            {
                if (_commandHistoryWriteTasks.Count == 0)
                {
                    return;
                }

                pendingWrites = new Task[_commandHistoryWriteTasks.Count];
                _commandHistoryWriteTasks.CopyTo(pendingWrites);
            }

            try
            {
                Task.WhenAll(pendingWrites).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppendEventLog($"Command history flush failed: {ex.Message}");
            }
        }
    }

    private async Task AppendCommandHistoryEntryAsync(TerminalCommandHistoryEntry entry)
    {
        await _commandHistorySync.WaitAsync().ConfigureAwait(false);
        try
        {
            _commandHistoryDocument ??= await _commandHistoryStore.LoadAsync().ConfigureAwait(false);
            List<TerminalCommandHistoryEntry> entries = new(_commandHistoryDocument.Entries.Count + 1);
            entries.AddRange(_commandHistoryDocument.Entries);
            entries.Add(entry);
            TerminalCommandHistoryDocument updated = _commandHistoryDocument with
            {
                Entries = entries,
            };
            TerminalCommandHistoryDocument retained = TerminalCommandHistorySerializer.Normalize(updated);

            await _commandHistoryStore.SaveAsync(retained).ConfigureAwait(false);
            _commandHistoryDocument = retained;
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
                AppendEventLog($"Command history save failed: {ex.Message}"));
        }
        finally
        {
            _commandHistorySync.Release();
        }
    }

    private async Task RefreshCommandSuggestionsAsync(string? query)
    {
        TerminalCommandHistoryDocument document;
        await _commandHistorySync.WaitAsync().ConfigureAwait(false);
        try
        {
            _commandHistoryDocument ??= await _commandHistoryStore.LoadAsync().ConfigureAwait(false);
            document = _commandHistoryDocument;
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _viewModel.SetCommandSuggestions(Array.Empty<TerminalCommandSuggestion>());
                UpdateStatus($"Command history load failed: {ex.Message}");
            });
            return;
        }
        finally
        {
            _commandHistorySync.Release();
        }

        CommandSuggestionScope scope = GetActiveCommandSuggestionScope();
        IReadOnlyList<TerminalCommandSuggestion> suggestions = _commandSuggestionService.GetSuggestions(
            new TerminalCommandSuggestionRequest(document)
            {
                Query = query,
                WorkingDirectory = scope.WorkingDirectory,
                ProfileId = scope.ProfileId,
                TransportId = scope.TransportId,
                Limit = 12,
                Snippets = GetActiveCommandSnippets(),
            });

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _viewModel.SetCommandSuggestions(suggestions);
            UpdateStatus(suggestions.Count == 0
                ? "No command suggestions"
                : $"Loaded {suggestions.Count.ToString(CultureInfo.InvariantCulture)} command suggestion(s)");
        });
    }

    private CommandSuggestionScope GetActiveCommandSuggestionScope()
    {
        TerminalControl? control = GetActiveStandaloneControl();
        if (control is not null)
        {
            string? workingDirectory = null;
            string? profileId = null;
            string? transportId = null;
            if (_paneRuntimeNodes.TryGetValue(control, out TerminalPaneRuntimeNode? node))
            {
                workingDirectory = NormalizeOptional(node.WorkingDirectory);
                profileId = NormalizeOptional(node.ProfileId);
                transportId = NormalizeOptional(node.TransportId);
            }

            if (_launchConfigurations.TryGetValue(control, out TerminalLaunchConfiguration launchConfiguration))
            {
                workingDirectory ??= GetProfileWorkingDirectory(launchConfiguration.Profile);
                profileId ??= NormalizeOptional(launchConfiguration.Profile.Id);
                transportId ??= NormalizeOptional(launchConfiguration.Profile.Transport.TransportId);
            }

            return new CommandSuggestionScope(
                workingDirectory,
                profileId,
                transportId);
        }

        return new CommandSuggestionScope(
            NormalizeOptional(_activeTab?.WorkingDirectory) ?? NormalizeOptional(_viewModel.WorkingDirectory),
            _activeTab?.ProfileId,
            _activeTab?.TransportId);
    }

    private void AcceptCommandSuggestion(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return;
        }

        TerminalControl? control = GetActiveStandaloneControl();
        if (control is null)
        {
            UpdateStatus("No active terminal for command suggestion.");
            return;
        }

        control.SendInput(commandLine);
        control.Focus();
        AppendEventLog($"[{GetTabDisplayName(control)}] Inserted command suggestion.");
    }

    private IReadOnlyList<TerminalCommandSnippet> GetActiveCommandSnippets()
    {
        IReadOnlyList<TerminalCommandSnippet> defaultSnippets = TerminalCommandSnippets.GetDefaultSnippets();
        TerminalSessionProfile? activeProfile = _activeTab is null || _sessionLauncherDocument is null
            ? null
            : FindProfile(_sessionLauncherDocument, _activeTab.ProfileId);
        if (activeProfile?.CommandSnippets is not { Count: > 0 } profileSnippets)
        {
            return defaultSnippets;
        }

        List<TerminalCommandSnippet> snippets = new(profileSnippets.Count + defaultSnippets.Count);
        snippets.AddRange(profileSnippets);
        snippets.AddRange(defaultSnippets);
        return snippets;
    }

    private void ApplyTerminalBehaviorSettings(TerminalControl control)
    {
        control.PasteSafetyPolicy = _viewModel.SelectedPasteSafetyPolicy;
        control.ReflowOnResize = _viewModel.ReflowOnResize;
        control.PreserveScrollbackOnSessionStart = _viewModel.PreserveScrollbackOnRestart;
        control.SixelGraphicsEnabled = _viewModel.SixelGraphicsEnabled;
        SkiaTerminalRenderer? renderer = control.Renderer;
        if (renderer is not null)
        {
            renderer.EnableTextShaping = _viewModel.EnableTextShaping;
            renderer.EnableLigatures = _viewModel.EnableLigatures;
        }
    }

    private void HandleStandaloneKeyDown(TerminalControl control, KeyEventArgs e)
    {
        if (e.Handled ||
            !_viewModel.BackspaceSendsControlH ||
            e.Key != Key.Back)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        control.SendInput("\b");
        e.Handled = true;
    }

    private void AppendEventLog(string message)
    {
        _viewModel.AppendEventLogEntry(message);
    }

    private SessionLogWriter EnsureSessionLogWriter(TerminalControl control)
    {
        if (_sessionLogWriters.TryGetValue(control, out SessionLogWriter? writer))
        {
            return writer;
        }

        writer = new SessionLogWriter();
        _sessionLogWriters[control] = writer;
        return writer;
    }

    private void WriteSessionLogOutput(TerminalControl control, ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty || !_viewModel.SessionLoggingEnabled)
        {
            return;
        }

        EnsureSessionLogWriter(control).WriteOutput(
            _viewModel.GetSessionLoggingSettings(),
            data);
    }

    private void WriteSessionLogInput(TerminalControl control, ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty || !_viewModel.SessionLoggingEnabled)
        {
            return;
        }

        EnsureSessionLogWriter(control).WriteInput(
            _viewModel.GetSessionLoggingSettings(),
            data);
    }

    private string GetTabDisplayName(Control control)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            TerminalTab tab = _tabs[i];
            if (ReferenceEquals(tab.Control, control) ||
                control is TerminalControl terminal && ContainsControl(tab.LeafControls, terminal))
            {
                return tab.Title;
            }
        }

        return "Terminal";
    }

    private TerminalTab? FindTabForControl(TerminalControl control)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            TerminalTab tab = _tabs[i];
            if (ReferenceEquals(tab.Control, control) || ContainsControl(tab.LeafControls, control))
            {
                return tab;
            }
        }

        return null;
    }

    private void UpdateStatus(string text)
    {
        _viewModel.SetStatus(text);
    }

    private void UpdateDimensions(int columns, int rows)
    {
        _viewModel.SetDimensions(columns, rows);
    }

    private void UpdateTextRenderPipelineIndicator(TerminalControl? control)
    {
        _viewModel.SetTextRenderPipelineIndicator(BuildTextRenderPipelineIndicator(control?.Renderer));
    }

    private static string BuildTextRenderPipelineIndicator(SkiaTerminalRenderer? renderer)
    {
        if (s_disableTextShaping)
        {
            return "Text: cell fallback";
        }

        if (s_textRenderPipeline == TerminalTextRenderPipeline.Pretext)
        {
            return renderer?.IsPretextTextRenderPipelineAvailable == true
                ? "Text: Pretext"
                : "Text: HarfBuzz (Pretext unavailable)";
        }

        return "Text: HarfBuzz";
    }

    private void UpdateSessionStartedStatus(TerminalControl control, string message)
    {
        string activeTransport = control.ActiveTransportId ?? "none";
        string sessionState = control.HasActiveSession ? "active" : "inactive";
        UpdateStatus($"{message} [{activeTransport}, {sessionState}]");
    }

    #endregion

    private void DisposeResources()
    {
        SaveWorkspaceSnapshot();
        FlushCommandHistoryWrites();
        _startingStandaloneControls.Clear();

        foreach (TerminalTab tab in _tabs)
        {
            DisposeTabTerminals(tab);
        }
        _tabs.Clear();
        _captureRuntimes.Clear();
        _commandHistoryHandlers.Clear();
        _commandHistoryCaptures.Clear();
        _launchConfigurations.Clear();
        _paneRuntimeNodes.Clear();
        _activePaneControl = null;
        foreach (SessionLogWriter writer in _sessionLogWriters.Values)
        {
            writer.Dispose();
        }
        _sessionLogWriters.Clear();

    }

    private void SaveWorkspaceSnapshot()
    {
        try
        {
            TerminalWorkspaceDocument document = CreateWorkspaceSnapshot();
            _workspaceStore.SaveAsync(document).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppendEventLog($"Workspace save failed: {ex.Message}");
        }
    }

    private TerminalWorkspaceDocument CreateWorkspaceSnapshot()
    {
        List<TerminalWorkspaceTab> tabs = new(_tabs.Count);
        string? selectedTabId = null;
        for (int i = 0; i < _tabs.Count; i++)
        {
            TerminalTab tab = _tabs[i];
            if (!tab.AutoStartSession)
            {
                continue;
            }

            string tabId = NormalizeOptional(tab.WorkspaceId) ??
                           $"tab-{tab.Index.ToString(CultureInfo.InvariantCulture)}";
            if (ReferenceEquals(tab, _activeTab))
            {
                selectedTabId = tabId;
            }

            tabs.Add(new TerminalWorkspaceTab
            {
                Id = tabId,
                ProfileId = tab.ProfileId,
                Title = tab.Title,
                WorkingDirectory = tab.WorkingDirectory,
                TransportId = tab.TransportId,
                RenderMode = MapWorkspaceRenderMode(tab.ResolvedMode),
                RootPane = CreateWorkspacePaneSnapshot(tab.RootPaneNode) ??
                           tab.RootPane ??
                           new TerminalWorkspacePane
                           {
                               Id = $"{tabId}-root",
                               ProfileId = tab.ProfileId,
                               WorkingDirectory = tab.WorkingDirectory,
                               TransportId = tab.TransportId,
                           },
            });
        }

        TerminalWorkspaceWindow window = new()
        {
            Id = "main",
            Title = _window.Title,
            SelectedTabId = selectedTabId,
            WidthPixels = Math.Max(1, (int)Math.Round(_window.Bounds.Width)),
            HeightPixels = Math.Max(1, (int)Math.Round(_window.Bounds.Height)),
            IsMaximized = _window.WindowState == WindowState.Maximized,
            Tabs = tabs,
        };

        return new TerminalWorkspaceDocument
        {
            SelectedWindowId = window.Id,
            Windows = [window],
        };
    }

    private static TerminalWorkspacePane? CreateWorkspacePaneSnapshot(TerminalPaneRuntimeNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.First is not null && node.Second is not null)
        {
            UpdateRuntimeSplitRatio(node);
            return new TerminalWorkspacePane
            {
                Id = node.Id,
                Title = node.Title,
                ProfileId = node.ProfileId,
                WorkingDirectory = node.WorkingDirectory,
                TransportId = node.TransportId,
                TransportProfileId = node.TransportProfileId,
                Split = new TerminalWorkspacePaneSplit
                {
                    Orientation = NormalizeSplitOrientation(node.Orientation),
                    Ratio = Math.Clamp(node.Ratio, 0.05, 0.95),
                    FirstPane = CreateWorkspacePaneSnapshot(node.First) ?? new TerminalWorkspacePane { Id = $"{node.Id}-first" },
                    SecondPane = CreateWorkspacePaneSnapshot(node.Second) ?? new TerminalWorkspacePane { Id = $"{node.Id}-second" },
                },
            };
        }

        return new TerminalWorkspacePane
        {
            Id = node.Id,
            Title = node.Title,
            ProfileId = node.ProfileId,
            WorkingDirectory = node.WorkingDirectory,
            TransportId = node.TransportId,
            TransportProfileId = node.TransportProfileId,
        };
    }

    private static void UpdateRuntimeSplitRatio(TerminalPaneRuntimeNode node)
    {
        if (node.SplitGrid is null)
        {
            return;
        }

        double first;
        double second;
        if (string.Equals(node.Orientation, TerminalWorkspacePaneSplitOrientations.Vertical, StringComparison.Ordinal))
        {
            first = GetGridLengthValue(node.SplitGrid.RowDefinitions[0].Height);
            second = GetGridLengthValue(node.SplitGrid.RowDefinitions[2].Height);
        }
        else
        {
            first = GetGridLengthValue(node.SplitGrid.ColumnDefinitions[0].Width);
            second = GetGridLengthValue(node.SplitGrid.ColumnDefinitions[2].Width);
        }

        double total = first + second;
        if (total > 0)
        {
            node.Ratio = Math.Clamp(first / total, 0.05, 0.95);
        }
    }

    private static double GetGridLengthValue(GridLength length)
    {
        return length.Value > 0 ? length.Value : 0;
    }

    private void DisposeTerminal(Control control)
    {
        if (control is TerminalControl standaloneControl)
        {
            if (_captureRuntimes.Remove(standaloneControl, out TerminalCaptureRuntime? runtime))
            {
                runtime.StateChanged -= OnCaptureRuntimeStateChanged;
                runtime.Dispose();
            }
            UnregisterCommandHistoryCapture(standaloneControl);
            _launchConfigurations.Remove(standaloneControl);
            _paneRuntimeNodes.Remove(standaloneControl);
            if (ReferenceEquals(_activePaneControl, standaloneControl))
            {
                _activePaneControl = null;
            }
            if (_sessionLogWriters.Remove(standaloneControl, out SessionLogWriter? sessionLogWriter))
            {
                sessionLogWriter.Dispose();
            }

            standaloneControl.StopPty();
            standaloneControl.DetachEndpoint();
        }
    }

    private void DisposeTabTerminals(TerminalTab tab)
    {
        if (tab.LeafControls.Count == 0)
        {
            DisposeTerminal(tab.Control);
            return;
        }

        for (int i = 0; i < tab.LeafControls.Count; i++)
        {
            DisposeTerminal(tab.LeafControls[i]);
        }
    }

    private sealed record CommandSuggestionScope(
        string? WorkingDirectory,
        string? ProfileId,
        string? TransportId);

    private sealed class SessionLogWriter : IDisposable
    {
        private readonly object _sync = new();
        private FileStream? _stream;
        private StreamWriter? _textWriter;
        private bool _enabled;
        private string? _filePath;
        private TerminalSessionLogFormat _format;
        private bool _flushFrequently;

        public void WriteOutput(TerminalSessionLoggingSettings settings, ReadOnlyMemory<byte> data)
        {
            if (data.IsEmpty)
            {
                return;
            }

            lock (_sync)
            {
                EnsureConfigured(settings);
                if (!_enabled || _stream is null)
                {
                    return;
                }

                if (_format == TerminalSessionLogFormat.RawBytes)
                {
                    _stream.Write(data.Span);
                    if (_flushFrequently)
                    {
                        _stream.Flush(flushToDisk: true);
                    }

                    return;
                }

                string printable = ToPrintableText(data.Span);
                if (printable.Length == 0)
                {
                    return;
                }

                string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                _textWriter!.WriteLine($"[{timestamp}] {printable}");
                if (_flushFrequently)
                {
                    _textWriter.Flush();
                }
            }
        }

        public void WriteInput(TerminalSessionLoggingSettings settings, ReadOnlyMemory<byte> data)
        {
            if (data.IsEmpty)
            {
                return;
            }

            lock (_sync)
            {
                EnsureConfigured(settings);
                if (!_enabled || _stream is null || _format == TerminalSessionLogFormat.RawBytes)
                {
                    return;
                }

                string printable = ToPrintableText(data.Span);
                if (printable.Length == 0)
                {
                    return;
                }

                string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                _textWriter!.WriteLine($"[{timestamp}] [input] {printable}");
                if (_flushFrequently)
                {
                    _textWriter.Flush();
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                CloseCurrentWriter();
            }
        }

        private void EnsureConfigured(TerminalSessionLoggingSettings settings)
        {
            string? normalizedPath = string.IsNullOrWhiteSpace(settings.FilePath)
                ? null
                : settings.FilePath.Trim();

            bool changed =
                _enabled != settings.Enabled ||
                !string.Equals(_filePath, normalizedPath, StringComparison.Ordinal) ||
                _format != settings.Format ||
                _flushFrequently != settings.FlushFrequently;
            if (!changed)
            {
                return;
            }

            CloseCurrentWriter();

            _enabled = settings.Enabled && !string.IsNullOrWhiteSpace(normalizedPath);
            _filePath = normalizedPath;
            _format = settings.Format;
            _flushFrequently = settings.FlushFrequently;

            if (!_enabled || _filePath is null)
            {
                return;
            }

            string? directoryPath = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            _stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            if (_format == TerminalSessionLogFormat.PlainText)
            {
                _textWriter = new StreamWriter(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
            }
        }

        private void CloseCurrentWriter()
        {
            if (_textWriter is not null)
            {
                try
                {
                    _textWriter.Flush();
                }
                catch
                {
                    // Best effort cleanup.
                }

                _textWriter.Dispose();
                _textWriter = null;
            }

            if (_stream is not null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }

        private static string ToPrintableText(ReadOnlySpan<byte> bytes)
        {
            string text = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder builder = new(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (value == '\r' || value == '\n' || value == '\t' || !char.IsControl(value))
                {
                    builder.Append(value);
                    continue;
                }

                builder.Append("\\x");
                builder.Append(((int)value).ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    private sealed class DemoSshCredentialProvider : ISshCredentialProvider
    {
        public const string PasswordSecretId = "demo-runtime-password";
        public const string PrivateKeySecretId = "demo-runtime-private-key";

        private readonly MainWindowViewModel _viewModel;

        public DemoSshCredentialProvider(MainWindowViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public ValueTask<SshResolvedCredentials> ResolveAsync(
            SshCredentialRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? password = request.Authentication.UsePassword
                ? NullIfWhiteSpace(_viewModel.SshPassword)
                : null;

            IReadOnlyList<string> privateKeys = request.Authentication.PrivateKeySecretIds.Count > 0
                ? SplitPrivateKeys(_viewModel.SshPrivateKeyPath)
                : Array.Empty<string>();

            return ValueTask.FromResult(new SshResolvedCredentials(
                Password: password,
                PrivateKeyPemOrPath: privateKeys,
                UseAgent: request.Authentication.UseAgent));
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        private static IReadOnlyList<string> SplitPrivateKeys(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return Array.Empty<string>();
            }

            string[] parts = rawValue.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries);
            List<string> values = new(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string entry = parts[i].Trim();
                if (!string.IsNullOrWhiteSpace(entry))
                {
                    values.Add(entry);
                }
            }

            return values;
        }
    }

    private sealed class TerminalTab
    {
        public TerminalTab(
            Button headerButton,
            Control control,
            Control container,
            int index,
            string modeName,
            bool autoStartSession,
            TerminalRenderMode resolvedMode,
            string profileId,
            string transportId,
            string? workingDirectory,
            string? workspaceId,
            TerminalWorkspacePane? rootPane,
            TerminalPaneRuntimeNode? rootPaneNode,
            IReadOnlyList<TerminalControl> leafControls,
            TerminalWorkspaceTab? deferredWorkspaceTab = null)
        {
            HeaderButton = headerButton;
            Control = control;
            Container = container;
            Index = index;
            ModeName = modeName;
            AutoStartSession = autoStartSession;
            ResolvedMode = resolvedMode;
            ProfileId = profileId;
            TransportId = transportId;
            WorkingDirectory = workingDirectory;
            WorkspaceId = workspaceId;
            RootPane = rootPane;
            RootPaneNode = rootPaneNode;
            LeafControls = leafControls;
            DeferredWorkspaceTab = deferredWorkspaceTab;
            CloseButton = headerButton.Tag as Button
                ?? throw new InvalidOperationException("Tab header close button is missing.");
            TitleText = ((headerButton.Content as StackPanel)?.Children[1] as TextBlock)
                ?? throw new InvalidOperationException("Tab header title text is missing.");
        }

        public Button HeaderButton { get; }
        public Button CloseButton { get; }
        public Control Control { get; private set; }
        public Control Container { get; private set; }
        public TextBlock TitleText { get; }
        public int Index { get; }
        public string ModeName { get; private set; }
        public bool AutoStartSession { get; }
        public TerminalRenderMode ResolvedMode { get; }
        public string ProfileId { get; }
        public string TransportId { get; }
        public string? WorkingDirectory { get; private set; }
        public string? WorkspaceId { get; }
        public TerminalWorkspacePane? RootPane { get; }
        public TerminalPaneRuntimeNode? RootPaneNode { get; private set; }
        public IReadOnlyList<TerminalControl> LeafControls { get; private set; }
        public TerminalWorkspaceTab? DeferredWorkspaceTab { get; }
        public bool IsDeferredWorkspaceTab => DeferredWorkspaceTab is not null && RootPaneNode is null;
        public string Title => TitleText.Text ?? $"Terminal {Index}";

        public void UpdateTitle(string title)
        {
            TitleText.Text = title;
        }

        public void SetModeName(string modeName)
        {
            ModeName = modeName;
        }

        public void SetWorkingDirectory(string? workingDirectory)
        {
            WorkingDirectory = workingDirectory;
        }

        public void SetPaneRoot(
            Control control,
            Control container,
            TerminalPaneRuntimeNode rootPaneNode,
            IReadOnlyList<TerminalControl> leafControls)
        {
            Control = control;
            Container = container;
            RootPaneNode = rootPaneNode;
            LeafControls = leafControls;
        }

        public void SetLeafControls(IReadOnlyList<TerminalControl> leafControls)
        {
            LeafControls = leafControls;
        }
    }

    private sealed class TerminalPaneRuntimeNode
    {
        public TerminalPaneRuntimeNode(
            string id,
            string? title,
            string? profileId,
            string? workingDirectory,
            string? transportId,
            string? transportProfileId)
        {
            Id = id;
            Title = title;
            ProfileId = profileId;
            WorkingDirectory = workingDirectory;
            TransportId = transportId;
            TransportProfileId = transportProfileId;
        }

        public string Id { get; }
        public string? Title { get; }
        public string? ProfileId { get; }
        public string? WorkingDirectory { get; private set; }
        public string? TransportId { get; }
        public string? TransportProfileId { get; }
        public TerminalPaneRuntimeNode? Parent { get; set; }
        public TerminalPaneRuntimeNode? First { get; set; }
        public TerminalPaneRuntimeNode? Second { get; set; }
        public string? Orientation { get; private set; }
        public double Ratio { get; set; } = 0.5;
        public TerminalControl? Control { get; private set; }
        public ScrollViewer? LeafContainer { get; private set; }
        public Grid? SplitGrid { get; private set; }
        public Control Visual { get; private set; } = new Grid();

        public void SetLeaf(TerminalControl control, ScrollViewer container)
        {
            Control = control;
            LeafContainer = container;
            First = null;
            Second = null;
            Orientation = null;
            SplitGrid = null;
            Visual = container;
        }

        public void SetWorkingDirectory(string? workingDirectory)
        {
            WorkingDirectory = workingDirectory;
        }

        public void SetSplit(
            string orientation,
            double ratio,
            TerminalPaneRuntimeNode first,
            TerminalPaneRuntimeNode second,
            Grid grid)
        {
            Control = null;
            LeafContainer = null;
            Orientation = orientation;
            Ratio = Math.Clamp(ratio, 0.05, 0.95);
            First = first;
            Second = second;
            SplitGrid = grid;
            Visual = grid;
        }
    }

    private readonly record struct TerminalModeSelection(
        TerminalRenderMode RequestedMode,
        TerminalRenderMode ResolvedMode,
        bool FallbackApplied,
        string? FallbackReason);

    private readonly record struct TerminalLaunchConfiguration(TerminalSessionProfile Profile);

    private readonly record struct TabVisualMode(string Name, string Glyph, IBrush GlyphBrush);

    private readonly record struct ThemePalette(
        Color WindowBackground,
        Color ToolbarBackground,
        Color ToolbarDivider,
        Color ToolbarForeground,
        Color TabStripBackground,
        Color StatusBarBackground,
        Color StatusBarForeground,
        Color TabHeaderBackground,
        Color TabHeaderForeground,
        Color TabHeaderActiveBackground,
        Color TabHeaderActiveForeground,
        Color TerminalForeground,
        Color TerminalBackground);
}
