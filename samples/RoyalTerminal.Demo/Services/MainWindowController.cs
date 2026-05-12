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
    private const string EnableRenderDiagnosticsEnvVar = "ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS";
    private const string TextRenderPipelineEnvVar = "ROYALTERMINAL_TEXT_RENDER_PIPELINE";
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

    private readonly Window _window;
    private readonly MainWindowViewModel _viewModel;
    private readonly Grid _terminalHost;
    private readonly StackPanel _tabStrip;
    private readonly List<TerminalTab> _tabs = [];
    private readonly HashSet<TerminalControl> _startingStandaloneControls = [];
    private readonly Dictionary<TerminalControl, TerminalCaptureRuntime> _captureRuntimes = [];
    private readonly Dictionary<TerminalControl, EventHandler<TerminalDataEventArgs>> _sessionLogOutputHandlers = [];
    private readonly Dictionary<TerminalControl, SessionLogWriter> _sessionLogWriters = [];
    private readonly ITerminalModeCapabilityResolver _modeCapabilityResolver;
    private readonly ITerminalModeResolver _modeResolver;
    private readonly ITerminalSessionProfileStore _settingsProfileStore;
    private bool _suppressReplayTimelineSeek;
    private bool _settingsProfilesLoaded;

    private TerminalTab? _activeTab;
    private int _tabCounter;
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
        ITerminalSessionProfileStore? settingsProfileStore = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel;
        _modeCapabilityResolver = modeCapabilityResolver
            ?? throw new ArgumentNullException(nameof(modeCapabilityResolver));
        _modeResolver = modeResolver ?? throw new ArgumentNullException(nameof(modeResolver));
        _settingsProfileStore = settingsProfileStore ?? TerminalSessionProfileStoreFactory.CreateDefault();
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

        CreateInitialTabsForSupportedModes();
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
            await PrepareSettingsPanelAsync();
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

    #region Tab Management

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

    private void CreateNewTab()
    {
        _tabCounter++;
        string tabName = $"Terminal {_tabCounter}";
        TerminalModeSelection modeSelection = ResolveModeSelectionForNewTab();
        TerminalControl terminal = CreateTerminalControlWithRuntimeFallback(
            modeSelection,
            out TerminalModeSelection finalizedModeSelection);

        TabVisualMode tabMode = ResolveTabMode(terminal, finalizedModeSelection.ResolvedMode);
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
            autoStartSession: true,
            resolvedMode: finalizedModeSelection.ResolvedMode);
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
            resolvedMode: TerminalRenderMode.RenderedAuto);
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
        QueueStandaloneSessionStart(standaloneControl);

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

    private async Task StartStandaloneSessionAsync(TerminalControl standaloneControl)
    {
        string workingDirectory = ResolveWorkingDirectory();
        string transportId = _viewModel.SelectedTransportMode.Id;
        string tabName = GetTabDisplayName(standaloneControl);

        if (string.Equals(transportId, TerminalTransportIds.Pty, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsPtyPlatformSupported())
            {
                throw new PlatformNotSupportedException("PTY transport is only supported on macOS, Linux, and Windows.");
            }

            string? shellPath = _viewModel.SelectedShellProfile?.CommandPath;
            standaloneControl.StartPty(
                shell: string.IsNullOrWhiteSpace(shellPath) ? null : shellPath,
                workingDirectory: workingDirectory);

            string shellDisplay = _viewModel.SelectedShellProfile?.DisplayName ?? "Default shell";
            AppendEventLog($"[{tabName}] Starting PTY session ({shellDisplay}).");
            UpdateSessionStartedStatus(standaloneControl, $"Started PTY session ({shellDisplay})");
            AppendEventLog($"[{tabName}] PTY session started.");
            return;
        }

        TerminalSessionDimensions dimensions = BuildSessionDimensions(standaloneControl);

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase))
        {
            TerminalCommandSpec command = BuildPipeCommand(_viewModel.SelectedShellProfile);
            AppendEventLog($"[{tabName}] Starting Pipe session ({command.FileName}).");
            PipeTransportOptions options = new(
                Command: command,
                WorkingDirectory: workingDirectory,
                Environment: null,
                MergeStdErrIntoStdOut: _viewModel.PipeMergeStdErrIntoStdOut,
                Dimensions: dimensions);

            await standaloneControl.StartPipeAsync(options);
            UpdateSessionStartedStatus(standaloneControl, $"Started Pipe session ({command.FileName})");
            AppendEventLog($"[{tabName}] Pipe session started.");
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.OrdinalIgnoreCase))
        {
            RawTcpTransportOptions options = BuildRawTcpOptions(dimensions);
            AppendEventLog($"[{tabName}] Starting Raw TCP session {options.Host}:{options.Port}.");
            await standaloneControl.StartRawTcpAsync(options);
            UpdateSessionStartedStatus(standaloneControl, $"Started Raw TCP session {options.Host}:{options.Port}");
            AppendEventLog($"[{tabName}] Raw TCP session started.");
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.OrdinalIgnoreCase))
        {
            TelnetTransportOptions options = BuildTelnetOptions(dimensions);
            AppendEventLog($"[{tabName}] Starting Telnet session {options.Host}:{options.Port}.");
            await standaloneControl.StartTelnetAsync(options);
            UpdateSessionStartedStatus(standaloneControl, $"Started Telnet session {options.Host}:{options.Port}");
            AppendEventLog($"[{tabName}] Telnet session started.");
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.OrdinalIgnoreCase))
        {
            SerialTransportOptions options = BuildSerialOptions(dimensions);
            AppendEventLog($"[{tabName}] Starting Serial session {options.PortName} ({options.BaudRate}).");
            await standaloneControl.StartSerialAsync(options);
            UpdateSessionStartedStatus(standaloneControl, $"Started Serial session {options.PortName} ({options.BaudRate})");
            AppendEventLog($"[{tabName}] Serial session started.");
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.OrdinalIgnoreCase))
        {
            SshTransportOptions options = BuildSshOptions(dimensions);
            AppendEventLog($"[{tabName}] Starting SSH session {options.Endpoint.Username}@{options.Endpoint.Host}:{options.Endpoint.Port}.");
            await standaloneControl.StartSshAsync(options);
            UpdateSessionStartedStatus(
                standaloneControl,
                $"Started SSH session {_viewModel.SshUsername}@{_viewModel.SshHost}:{_viewModel.SshPort}");
            AppendEventLog($"[{tabName}] SSH session started.");
            return;
        }

        throw new InvalidOperationException($"Unsupported transport id '{transportId}'.");
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

        Button closeButton = new()
        {
            Content = "\u00d7",
            FontSize = 14,
            Padding = new Thickness(2, 0),
            MinWidth = 20,
            MinHeight = 20,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };

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

        DisposeTerminal(tab.Control);

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

        foreach (TerminalTab tab in _tabs)
        {
            tab.Container.IsVisible = false;
            tab.HeaderButton.Classes.Remove("active");
        }

        TerminalTab target = _tabs[index];
        target.Container.IsVisible = true;
        target.HeaderButton.Classes.Add("active");
        _activeTab = target;
        UpdateTextRenderPipelineIndicator(target.Control as TerminalControl);

        if (target.AutoStartSession && target.Control is TerminalControl standaloneControl)
        {
            QueueStandaloneSessionStart(standaloneControl);
        }

        Dispatcher.UIThread.Post(() =>
        {
            target.Control.Focus();
            if (target.Control is TerminalControl standalone)
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
        if (_activeTab?.Control is not TerminalControl control)
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
            UpdateStatus("Capture is available for standalone terminal tabs only.");
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
            UpdateStatus("No standalone terminal is active to save capture.");
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
        TerminalTab? tab = GetActiveTab();
        if (tab?.Control is not TerminalControl standalone)
        {
            return;
        }

        await standalone.CopySelectionAsync();
    }

    private async Task PasteClipboard()
    {
        TerminalTab? tab = GetActiveTab();
        if (tab?.Control is not TerminalControl standalone)
        {
            return;
        }

        await standalone.PasteAsync();
    }

    private void SelectAll()
    {
        TerminalTab? tab = GetActiveTab();
        if (tab?.Control is not TerminalControl standalone)
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
        return GetActiveTab()?.Control as TerminalControl;
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
            if (tab.Control is TerminalControl standalone)
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
            if (tab.Control is TerminalControl standalone)
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
            if (_tabs[i].Control is TerminalControl standalone)
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
        Color softAccent = BlendColor(background, accent, 0.18);
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
            StatusBarBackground: softAccent,
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
        if (!_settingsProfilesLoaded)
        {
            TerminalSessionProfilesDocument document = await _settingsProfileStore.LoadAsync();
            if (document.Profiles.Count == 0)
            {
                SyncSettingsStateFromViewModel(state);
                document = state.BuildDocument();
                state.MarkSaved("Initialized settings profiles from current runtime values.");
            }

            state.LoadDocument(document);
            _settingsProfilesLoaded = true;
        }

        if (alreadyLoaded && !state.IsDirty)
        {
            SyncSettingsStateFromViewModel(state);
        }
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
        _viewModel.SetFontSizeFromSettings(fontSize);
        ApplyFontSize(fontSize);

        _viewModel.TextHighlightingMode = state.SelectedTextHighlightingMode?.Mode ?? TerminalTextHighlightingMode.Static;
        _viewModel.TextHighlightRules = BuildRuntimeTextHighlightRules(state);

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Control is TerminalControl standaloneControl)
            {
                ApplyFontSettings(standaloneControl);
                standaloneControl.AutoScroll = state.AutoScroll;
                standaloneControl.BackgroundOpacityEnabled = state.BackgroundOpacityEnabled;
                standaloneControl.TextHighlightingMode = _viewModel.TextHighlightingMode;
                standaloneControl.TextHighlightRules = _viewModel.TextHighlightRules;
            }
        }

        ApplyTerminalBehaviorSettingsToAllStandaloneTabs();
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
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Control is TerminalControl standaloneControl)
            {
                ApplyTerminalBehaviorSettings(standaloneControl);
            }
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
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabs[i].Control is TerminalControl standaloneControl)
            {
                UpdateSessionLoggingSubscription(standaloneControl);
            }
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

    private void ApplyTerminalBehaviorSettings(TerminalControl control)
    {
        control.PasteSafetyPolicy = _viewModel.SelectedPasteSafetyPolicy;
        control.ReflowOnResize = _viewModel.ReflowOnResize;
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
        TerminalTab? tab = _tabs.Find(next => ReferenceEquals(next.Control, control));
        if (tab is not null)
        {
            return tab.Title;
        }

        return "Terminal";
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
        _startingStandaloneControls.Clear();

        foreach (TerminalTab tab in _tabs)
        {
            DisposeTerminal(tab.Control);
        }
        _tabs.Clear();
        _captureRuntimes.Clear();
        foreach (SessionLogWriter writer in _sessionLogWriters.Values)
        {
            writer.Dispose();
        }
        _sessionLogWriters.Clear();

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
            if (_sessionLogWriters.Remove(standaloneControl, out SessionLogWriter? sessionLogWriter))
            {
                sessionLogWriter.Dispose();
            }

            standaloneControl.StopPty();
            standaloneControl.DetachEndpoint();
        }
    }

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
            TerminalRenderMode resolvedMode)
        {
            HeaderButton = headerButton;
            Control = control;
            Container = container;
            Index = index;
            ModeName = modeName;
            AutoStartSession = autoStartSession;
            ResolvedMode = resolvedMode;
            CloseButton = headerButton.Tag as Button
                ?? throw new InvalidOperationException("Tab header close button is missing.");
            TitleText = ((headerButton.Content as StackPanel)?.Children[1] as TextBlock)
                ?? throw new InvalidOperationException("Tab header title text is missing.");
        }

        public Button HeaderButton { get; }
        public Button CloseButton { get; }
        public Control Control { get; }
        public Control Container { get; }
        public TextBlock TitleText { get; }
        public int Index { get; }
        public string ModeName { get; }
        public bool AutoStartSession { get; }
        public TerminalRenderMode ResolvedMode { get; }
        public string Title => TitleText.Text ?? $"Terminal {Index}";

        public void UpdateTitle(string title)
        {
            TitleText.Text = title;
        }
    }

    private readonly record struct TerminalModeSelection(
        TerminalRenderMode RequestedMode,
        TerminalRenderMode ResolvedMode,
        bool FallbackApplied,
        string? FallbackReason);

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
