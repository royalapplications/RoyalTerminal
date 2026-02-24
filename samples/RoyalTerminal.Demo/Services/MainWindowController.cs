// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo — Runtime controller for terminal tab orchestration.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Pipe;
using RoyalTerminal.Terminal.Transport.Pty;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent;
using ReactiveUI;

namespace RoyalTerminal.Demo.Services;

internal sealed class MainWindowController
{
    private const string DisableTextShapingEnvVar = "ROYALTERMINAL_DISABLE_TEXT_SHAPING";
    private const string EnableRenderDiagnosticsEnvVar = "ROYALTERMINAL_ENABLE_RENDER_DIAGNOSTICS";

    private static readonly string MonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";
    private static readonly bool s_disableTextShaping = ReadEnvironmentToggle(DisableTextShapingEnvVar);
    private static readonly bool s_enableRenderDiagnostics = ReadEnvironmentToggle(EnableRenderDiagnosticsEnvVar);

    private readonly MainWindowViewModel _viewModel;
    private readonly Grid _terminalHost;
    private readonly StackPanel _tabStrip;
    private readonly List<TerminalTab> _tabs = [];
    private readonly HashSet<TerminalControl> _startingStandaloneControls = [];
    private readonly ITerminalModeCapabilityResolver _modeCapabilityResolver;
    private readonly ITerminalModeResolver _modeResolver;
    private readonly bool _skipEmbeddedGhosttyInitialization;

    private TerminalTab? _activeTab;
    private int _tabCounter;
    private GhosttyApp? _ghosttyApp;
    private GhosttyConfig? _ghosttyConfig;
    private TerminalModeCapabilities _terminalCapabilities = TerminalModeCapabilities.Create(
        embeddedGhosttyAvailable: false,
        nativeVtAvailable: false);

    public MainWindowController(Window window, MainWindowViewModel viewModel)
        : this(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            skipEmbeddedGhosttyInitialization: false)
    {
    }

    internal MainWindowController(
        Window window,
        MainWindowViewModel viewModel,
        ITerminalModeCapabilityResolver modeCapabilityResolver,
        ITerminalModeResolver modeResolver,
        bool skipEmbeddedGhosttyInitialization)
    {
        _viewModel = viewModel;
        _modeCapabilityResolver = modeCapabilityResolver
            ?? throw new ArgumentNullException(nameof(modeCapabilityResolver));
        _modeResolver = modeResolver ?? throw new ArgumentNullException(nameof(modeResolver));
        _skipEmbeddedGhosttyInitialization = skipEmbeddedGhosttyInitialization;
        _terminalHost = window.FindControl<Grid>("TerminalHost")
            ?? throw new InvalidOperationException("TerminalHost was not found in MainWindow.");
        _tabStrip = window.FindControl<StackPanel>("TabStrip")
            ?? throw new InvalidOperationException("TabStrip was not found in MainWindow.");
    }

    public IDisposable Activate()
    {
        CompositeDisposable lifetime = new();
        RegisterInteractionHandlers(lifetime);

        bool embeddedGhosttyAvailable = !_skipEmbeddedGhosttyInitialization
            && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        _terminalCapabilities = _modeCapabilityResolver.Resolve(
            embeddedGhosttyAvailable,
            GhosttyVtProcessor.IsAvailable());
        _viewModel.SetTerminalCapabilities(_terminalCapabilities);

        TerminalRenderMode startupMode = TerminalRenderMode.RenderedAuto;
        _viewModel.SetRenderMode(startupMode);

        ApplyThemeResources(CreateChromePalette(_viewModel.ActiveTheme));
        InitializeShellProfiles();

        CreateInitialTabsForSupportedModes();
        string startupStatus = _viewModel.UseRenderedControl
            ? $"Ghostty VT + {GetRenderedBackendLabel()} rendering"
            : _viewModel.UseNativeControl
                ? "Ghostty native terminal ready"
                : _viewModel.UseNativeVtControl
                    ? "Native VT (libghostty-terminal) ready"
                    : _viewModel.UseManagedVtControl
                        ? "Managed VT (BasicVtProcessor) ready"
                        : "Terminal ready - Rendered (Custom PTY) mode";
        string? nativeModeHint = BuildNativeModeAvailabilityHint();
        UpdateStatus(nativeModeHint is null
            ? startupStatus
            : $"{startupStatus} | {nativeModeHint}");

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

        disposables.Add(_viewModel.ApplyRenderedBackendInteraction.RegisterHandler(context =>
        {
            ApplyRenderedBackend(context.Input);
            context.SetOutput(Unit.Default);
        }));
    }

    private bool TryInitializeGhostty()
    {
        // The Ghostty embedded API (Ghostty Rendered/Native modes) currently only supports macOS.
        // On Windows and Linux, the app falls through to Rendered (Custom PTY) mode.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        try
        {
            if (!Ghostty.Initialize())
            {
                return false;
            }

            _ghosttyConfig = new GhosttyConfig();
            _ghosttyConfig.LoadDefaultFiles();
            _ghosttyConfig.Finalize_();

            _ghosttyApp = new GhosttyApp(_ghosttyConfig);

            GhosttyLibraryInfo info = Ghostty.GetInfo();
            UpdateStatus($"Ghostty {info.Version} - custom rendered mode");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool EnsureEmbeddedGhosttyInitialized()
    {
        if (_ghosttyApp is not null)
        {
            return true;
        }

        if (_skipEmbeddedGhosttyInitialization)
        {
            return false;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        bool initialized = TryInitializeGhostty();
        if (initialized)
        {
            _terminalCapabilities = _terminalCapabilities with
            {
                EmbeddedGhosttyNativeAvailable = true,
                EmbeddedGhosttyRenderedAvailable = true,
            };
            _viewModel.SetTerminalCapabilities(_terminalCapabilities);
        }

        return initialized;
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
            TerminalRenderMode.GhosttyRendered,
            TerminalRenderMode.GhosttyNative,
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
        Control terminal = CreateTerminalControlWithRuntimeFallback(
            modeSelection,
            out TerminalModeSelection finalizedModeSelection);

        TabVisualMode tabMode = ResolveTabMode(terminal, finalizedModeSelection.ResolvedMode);
        Button headerButton = CreateTabHeader(tabName, tabMode);

        Control container = terminal is TerminalControl
            ? new ScrollViewer
            {
                Content = terminal,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            }
            : terminal;

        TerminalTab tab = new(headerButton, terminal, container, _tabCounter, tabMode.Name);
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
    }

    private TerminalModeSelection ResolveModeSelectionForNewTab()
    {
        TerminalRenderMode requestedMode = GetRequestedRenderMode();
        _terminalCapabilities = _terminalCapabilities with
        {
            NativeVtAvailable = GhosttyVtProcessor.IsAvailable(),
        };
        _viewModel.SetTerminalCapabilities(_terminalCapabilities);

        TerminalRenderMode resolvedMode = _modeResolver.ResolveSupportedMode(requestedMode, _terminalCapabilities);
        bool fallbackApplied = requestedMode != resolvedMode;
        _viewModel.SetRenderMode(resolvedMode);

        string? fallbackReason = fallbackApplied
            ? DescribeModeFallback(requestedMode)
            : null;
        return new TerminalModeSelection(requestedMode, resolvedMode, fallbackApplied, fallbackReason);
    }

    private Control CreateTerminalControl(TerminalRenderMode mode)
    {
        return mode switch
        {
            TerminalRenderMode.GhosttyRendered => CreateGhosttyRenderedControl(),
            TerminalRenderMode.GhosttyNative => CreateGhosttyNativeControl(),
            TerminalRenderMode.NativeVt => CreateStandaloneTerminalControl(TerminalRenderMode.NativeVt),
            TerminalRenderMode.ManagedVt => CreateStandaloneTerminalControl(TerminalRenderMode.ManagedVt),
            _ => CreateStandaloneTerminalControl(TerminalRenderMode.RenderedAuto),
        };
    }

    private Control CreateTerminalControlWithRuntimeFallback(
        TerminalModeSelection selection,
        out TerminalModeSelection finalizedSelection)
    {
        TerminalModeSelection currentSelection = selection;
        TerminalModeCapabilities runtimeCapabilities = _terminalCapabilities;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Control terminal = CreateTerminalControl(currentSelection.ResolvedMode);
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

    private GhosttyRenderedTerminalControl CreateGhosttyRenderedControl()
    {
        if (!EnsureEmbeddedGhosttyInitialized())
        {
            throw new InvalidOperationException("Ghostty rendered mode is unavailable because the embedded Ghostty app is not initialized.");
        }
        GhosttyApp app = _ghosttyApp
            ?? throw new InvalidOperationException("Ghostty rendered mode is unavailable because the embedded Ghostty app is not initialized.");

        GhosttyRenderedTerminalControl renderedControl = new()
        {
            TerminalFontSize = (float)_viewModel.FontSize,
            FontFamilyName = MonoFont,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            RenderingMode = GetRenderedRenderingMode(_viewModel.UseTextureInterop),
        };
        renderedControl.Initialize(app);
        renderedControl.ApplyTheme(_viewModel.ActiveTheme);
        ConfigureRenderer(renderedControl.Renderer);

        renderedControl.TitleChanged += (_, title) =>
        {
            TerminalTab? tab = _tabs.Find(t => t.Control == renderedControl);
            tab?.UpdateTitle(title);
        };
        renderedControl.ProcessExited += (_, code) => UpdateStatus($"Process exited: {code}");
        renderedControl.CloseRequested += (_, _) =>
        {
            TerminalTab? tab = _tabs.Find(t => t.Control == renderedControl);
            if (tab is not null)
            {
                CloseTab(tab);
            }
        };
        renderedControl.TerminalResized += (_, args) =>
        {
            if (_activeTab?.Control == renderedControl)
            {
                UpdateDimensions(args.Columns, args.Rows);
            }
        };

        return renderedControl;
    }

    private GhosttyNativeTerminalControl CreateGhosttyNativeControl()
    {
        if (!EnsureEmbeddedGhosttyInitialized())
        {
            throw new InvalidOperationException("Ghostty native mode is unavailable because the embedded Ghostty app is not initialized.");
        }
        GhosttyApp app = _ghosttyApp
            ?? throw new InvalidOperationException("Ghostty native mode is unavailable because the embedded Ghostty app is not initialized.");

        GhosttyNativeTerminalControl nativeControl = new()
        {
            TerminalFontSize = (float)_viewModel.FontSize,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        nativeControl.Initialize(app);
        nativeControl.ApplyTheme(_viewModel.ActiveTheme);

        nativeControl.TitleChanged += (_, title) =>
        {
            TerminalTab? tab = _tabs.Find(t => t.Control == nativeControl);
            tab?.UpdateTitle(title);
        };
        nativeControl.ProcessExited += (_, code) => UpdateStatus($"Process exited: {code}");
        nativeControl.CloseRequested += (_, _) =>
        {
            TerminalTab? tab = _tabs.Find(t => t.Control == nativeControl);
            if (tab is not null)
            {
                CloseTab(tab);
            }
        };
        nativeControl.TerminalResized += (_, args) =>
        {
            if (_activeTab?.Control == nativeControl)
            {
                UpdateDimensions(args.Columns, args.Rows);
            }
        };

        return nativeControl;
    }

    private TerminalControl CreateStandaloneTerminalControl(TerminalRenderMode mode)
    {
        TerminalTheme theme = _viewModel.ActiveTheme;
        TerminalControl standaloneControl = CreateStandaloneControl();
        standaloneControl.FontFamilyName = MonoFont;
        standaloneControl.TerminalFontSize = _viewModel.FontSize;
        standaloneControl.Columns = 80;
        standaloneControl.Rows = 24;
        standaloneControl.ScrollbackLimit = 10_000;
        standaloneControl.VtProcessorPreference = ResolveVtProcessorPreference(mode);
        standaloneControl.ApplyTheme(theme);
        ConfigureRenderer(standaloneControl.Renderer);

        standaloneControl.DataReceived += (_, args) =>
        {
            UpdateStatus($"Received {args.Data.Length} bytes");
        };
        standaloneControl.TerminalResized += (_, args) =>
        {
            UpdateDimensions(args.Columns, args.Rows);
        };

        QueueStandaloneSessionStart(standaloneControl);

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
                if (standaloneControl.GetVisualRoot() is null || standaloneControl.Parent is null)
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
            if (standaloneControl.GetVisualRoot() is not null)
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
        if (_viewModel.UseRenderedControl)
        {
            return TerminalRenderMode.GhosttyRendered;
        }

        if (_viewModel.UseNativeControl)
        {
            return TerminalRenderMode.GhosttyNative;
        }

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
            TerminalRenderMode.GhosttyRendered => "embedded Ghostty rendered mode is unavailable",
            TerminalRenderMode.GhosttyNative => "embedded Ghostty native mode is unavailable",
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
            case TerminalRenderMode.GhosttyRendered when capabilities.EmbeddedGhosttyRenderedAvailable:
                capabilities = capabilities with { EmbeddedGhosttyRenderedAvailable = false };
                return true;
            case TerminalRenderMode.GhosttyNative when capabilities.EmbeddedGhosttyNativeAvailable:
                capabilities = capabilities with { EmbeddedGhosttyNativeAvailable = false };
                return true;
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
            TerminalRenderMode.GhosttyRendered => "Ghostty Rendered",
            TerminalRenderMode.GhosttyNative => "Ghostty Native",
            TerminalRenderMode.NativeVt => "Native VT",
            TerminalRenderMode.ManagedVt => "Managed VT",
            _ => "Rendered",
        };
    }

    private TabVisualMode ResolveTabMode(Control terminal, TerminalRenderMode mode)
    {
        if (terminal is GhosttyRenderedTerminalControl rendered)
        {
            bool isTextureInterop = rendered.RenderingMode == GhosttyRenderedTerminalRenderingMode.TextureInterop;
            return new TabVisualMode(
                isTextureInterop
                    ? "Rendered (Ghostty VT + TextureInterop)"
                    : "Rendered (Ghostty VT + CPU Cell Renderer)",
                isTextureInterop ? "\u25CF" : "\u25CB",
                new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)));
        }

        if (terminal is GhosttyNativeTerminalControl)
        {
            return new TabVisualMode(
                "Native (Ghostty Metal)",
                "\u25C6",
                new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA)));
        }

        string vtLabel = terminal is TerminalControl gtc && gtc.IsUsingNativeVtProcessor
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

    private static GhosttyRenderedTerminalRenderingMode GetRenderedRenderingMode(bool useTextureInterop)
        => useTextureInterop
            ? GhosttyRenderedTerminalRenderingMode.TextureInterop
            : GhosttyRenderedTerminalRenderingMode.CpuCellRenderer;

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

    private string GetRenderedBackendLabel()
        => _viewModel.UseTextureInterop ? "TextureInterop (Preview)" : "CPU Cell Renderer";

    private string? BuildNativeModeAvailabilityHint()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return null;
        }

        if (_terminalCapabilities.EmbeddedGhosttyRenderedAvailable || _terminalCapabilities.NativeVtAvailable)
        {
            return null;
        }

        return "Ghostty native libraries were not found; run ./scripts/build-native.sh --debug.";
    }

    private static void ConfigureRenderer(SkiaTerminalRenderer? renderer)
    {
        if (renderer is null)
        {
            return;
        }

        renderer.EnableTextShaping = !s_disableTextShaping;
        renderer.EnableTextRenderDiagnostics = s_enableRenderDiagnostics;
    }

    private TerminalControl CreateStandaloneControl()
    {
        INativeVtProcessorProvider[] nativeProviders = [new GhosttyVtProcessorProvider()];
        DefaultPtyFactory ptyFactory = new();
        DemoSshCredentialProvider credentialProvider = new(_viewModel);
        KnownHostsSshHostKeyValidator hostKeyValidator = new();
        CompositeTerminalTransportFactory transportFactory = new(
            new ITerminalTransportProvider[]
            {
                new PtyTerminalTransportProvider(ptyFactory),
                new PipeTerminalTransportProvider(),
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

    private async Task StartStandaloneSessionAsync(TerminalControl standaloneControl)
    {
        string workingDirectory = ResolveWorkingDirectory();
        string transportId = _viewModel.SelectedTransportMode.Id;

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
            UpdateSessionStartedStatus(standaloneControl, $"Started PTY session ({shellDisplay})");
            return;
        }

        TerminalSessionDimensions dimensions = BuildSessionDimensions(standaloneControl);

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase))
        {
            TerminalCommandSpec command = BuildPipeCommand(_viewModel.SelectedShellProfile);
            PipeTransportOptions options = new(
                Command: command,
                WorkingDirectory: workingDirectory,
                Environment: null,
                MergeStdErrIntoStdOut: _viewModel.PipeMergeStdErrIntoStdOut,
                Dimensions: dimensions);

            await standaloneControl.StartPipeAsync(options);
            UpdateSessionStartedStatus(standaloneControl, $"Started Pipe session ({command.FileName})");
            return;
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.OrdinalIgnoreCase))
        {
            SshTransportOptions options = BuildSshOptions(dimensions);
            await standaloneControl.StartSshAsync(options);
            UpdateSessionStartedStatus(
                standaloneControl,
                $"Started SSH session {_viewModel.SshUsername}@{_viewModel.SshHost}:{_viewModel.SshPort}");
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

        if (!int.TryParse(_viewModel.SshPort, out int port) || port < 1 || port > 65535)
        {
            throw new InvalidOperationException("SSH port must be a valid number in range 1-65535.");
        }

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

        if (!string.IsNullOrWhiteSpace(expectedHostKeyFingerprint))
        {
            options = options with { ExpectedHostKeyFingerprintSha256 = expectedHostKeyFingerprint };
        }

        return options;
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

        if (target.Control is TerminalControl standaloneControl)
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

    #region Clipboard

    private async Task CopySelection()
    {
        TerminalTab? tab = GetActiveTab();
        if (tab is null)
        {
            return;
        }

        if (tab.Control is GhosttyNativeTerminalControl native)
        {
            await native.CopySelectionAsync();
        }
        else if (tab.Control is GhosttyRenderedTerminalControl rendered)
        {
            await rendered.CopySelectionAsync();
        }
        else if (tab.Control is TerminalControl standalone)
        {
            await standalone.CopySelectionAsync();
        }
    }

    private async Task PasteClipboard()
    {
        TerminalTab? tab = GetActiveTab();
        if (tab is null)
        {
            return;
        }

        if (tab.Control is GhosttyNativeTerminalControl native)
        {
            await native.PasteAsync();
        }
        else if (tab.Control is GhosttyRenderedTerminalControl rendered)
        {
            await rendered.PasteAsync();
        }
        else if (tab.Control is TerminalControl standalone)
        {
            await standalone.PasteAsync();
        }
    }

    #endregion

    #region Font And Theme

    private void ApplyFontSize(double fontSize)
    {
        foreach (TerminalTab tab in _tabs)
        {
            if (tab.Control is TerminalControl standalone)
            {
                standalone.TerminalFontSize = fontSize;
                standalone.InvalidateTerminal();
            }
            else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            {
                rendered.TerminalFontSize = (float)fontSize;
            }
            else if (tab.Control is GhosttyNativeTerminalControl native)
            {
                native.TerminalFontSize = (float)fontSize;
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
            else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            {
                rendered.ApplyTheme(theme);
            }
            else if (tab.Control is GhosttyNativeTerminalControl native)
            {
                native.ApplyTheme(theme);
            }
        }

        ApplyThemeResources(CreateChromePalette(theme));
    }

    private void ApplyRenderedBackend(bool useTextureInterop)
    {
        GhosttyRenderedTerminalRenderingMode renderingMode = GetRenderedRenderingMode(useTextureInterop);
        foreach (TerminalTab tab in _tabs)
        {
            if (tab.Control is not GhosttyRenderedTerminalControl rendered)
            {
                continue;
            }

            rendered.RenderingMode = renderingMode;

            TabVisualMode tabMode = ResolveTabMode(rendered, TerminalRenderMode.GhosttyRendered);
            ToolTip.SetTip(tab.HeaderButton, tabMode.Name);
            if (tab.HeaderButton.Content is StackPanel headerContent
                && headerContent.Children.Count > 0
                && headerContent.Children[0] is TextBlock modeIndicator)
            {
                modeIndicator.Text = tabMode.Glyph;
                modeIndicator.Foreground = tabMode.GlyphBrush;
            }
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

    #region Status

    private void UpdateStatus(string text)
    {
        _viewModel.SetStatus(text);
    }

    private void UpdateDimensions(int columns, int rows)
    {
        _viewModel.SetDimensions(columns, rows);
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

        _ghosttyApp?.Dispose();
        _ghosttyConfig?.Dispose();
    }

    private static void DisposeTerminal(Control control)
    {
        if (control is TerminalControl standaloneControl)
        {
            standaloneControl.StopPty();
            standaloneControl.DetachEndpoint();
            return;
        }

        if (control is GhosttyRenderedTerminalControl renderedControl)
        {
            renderedControl.Dispose();
            return;
        }

        if (control is GhosttyNativeTerminalControl nativeControl)
        {
            nativeControl.Dispose();
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
        public TerminalTab(Button headerButton, Control control, Control container, int index, string modeName)
        {
            HeaderButton = headerButton;
            Control = control;
            Container = container;
            Index = index;
            ModeName = modeName;
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
