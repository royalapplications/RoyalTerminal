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
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
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

    private static readonly ThemePalette DarkPalette = new(
        Color.FromRgb(0x1E, 0x1E, 0x1E),
        Color.FromRgb(0x33, 0x33, 0x33),
        Color.FromRgb(0x55, 0x55, 0x55),
        Color.FromRgb(0xCC, 0xCC, 0xCC),
        Color.FromRgb(0x25, 0x25, 0x26),
        Color.FromRgb(0x00, 0x7A, 0xCC),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0x2D, 0x2D, 0x2D),
        Color.FromRgb(0x96, 0x96, 0x96),
        Color.FromRgb(0x1E, 0x1E, 0x1E),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xD4, 0xD4, 0xD4),
        Color.FromRgb(0x1E, 0x1E, 0x1E));

    private static readonly ThemePalette LightPalette = new(
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xE5, 0xE5, 0xE5),
        Color.FromRgb(0xC4, 0xC4, 0xC4),
        Color.FromRgb(0x33, 0x33, 0x33),
        Color.FromRgb(0xF0, 0xF0, 0xF0),
        Color.FromRgb(0x00, 0x7A, 0xCC),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xDC, 0xDC, 0xDC),
        Color.FromRgb(0x33, 0x33, 0x33),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0x11, 0x11, 0x11),
        Color.FromRgb(0x1E, 0x1E, 0x1E),
        Color.FromRgb(0xFF, 0xFF, 0xFF));

    private readonly MainWindowViewModel _viewModel;
    private readonly Grid _terminalHost;
    private readonly StackPanel _tabStrip;
    private readonly List<TerminalTab> _tabs = [];

    private TerminalTab? _activeTab;
    private int _tabCounter;
    private GhosttyApp? _ghosttyApp;
    private GhosttyConfig? _ghosttyConfig;
    private DispatcherTimer? _tickTimer;

    public MainWindowController(Window window, MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _terminalHost = window.FindControl<Grid>("TerminalHost")
            ?? throw new InvalidOperationException("TerminalHost was not found in MainWindow.");
        _tabStrip = window.FindControl<StackPanel>("TabStrip")
            ?? throw new InvalidOperationException("TabStrip was not found in MainWindow.");
    }

    public IDisposable Activate()
    {
        CompositeDisposable lifetime = new();
        RegisterInteractionHandlers(lifetime);

        bool ghosttyAvailable = TryInitializeGhostty();
        bool nativeVtAvailable = GhosttyVtProcessor.IsAvailable();
        _viewModel.SetTerminalCapabilities(ghosttyAvailable, nativeVtAvailable);

        if (!ghosttyAvailable)
        {
            _viewModel.SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
        }

        ApplyThemeResources(_viewModel.IsDarkTheme);
        InitializeShellProfiles();

        CreateNewTab();
        UpdateStatus(_viewModel.UseRenderedControl
            ? $"Ghostty VT + {GetRenderedBackendLabel()} rendering"
            : _viewModel.UseNativeControl
                ? "Ghostty native terminal ready"
                : _viewModel.UseNativeVtControl
                    ? "Native VT (libghostty-terminal) ready"
                    : _viewModel.UseManagedVtControl
                        ? "Managed VT (BasicVtProcessor) ready"
                    : "Terminal ready - Rendered (Custom PTY) mode");

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
            _viewModel.SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
            return false;
        }

        try
        {
            if (!Ghostty.Initialize())
            {
                _viewModel.SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
                return false;
            }

            _ghosttyConfig = new GhosttyConfig();
            _ghosttyConfig.LoadDefaultFiles();
            _ghosttyConfig.Finalize_();

            _ghosttyApp = new GhosttyApp(_ghosttyConfig);

            _tickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };
            _tickTimer.Tick += (_, _) => _ghosttyApp?.Tick();
            _tickTimer.Start();

            _viewModel.SetRenderMode(useRenderedControl: true, useNativeControl: true, useNativeVtControl: false);

            GhosttyLibraryInfo info = Ghostty.GetInfo();
            UpdateStatus($"Ghostty {info.Version} - custom rendered mode");
            return true;
        }
        catch (Exception)
        {
            _viewModel.SetRenderMode(useRenderedControl: false, useNativeControl: false, useNativeVtControl: false);
            return false;
        }
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

    private void CreateNewTab()
    {
        _tabCounter++;
        string tabName = $"Terminal {_tabCounter}";
        Control terminal;

        if (_viewModel.UseRenderedControl && _ghosttyApp is not null)
        {
            GhosttyRenderedTerminalControl renderedControl = new()
            {
                TerminalFontSize = (float)_viewModel.FontSize,
                FontFamilyName = MonoFont,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                RenderingMode = GetRenderedRenderingMode(_viewModel.UseTextureInterop),
            };
            renderedControl.Initialize(_ghosttyApp);
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

            terminal = renderedControl;
        }
        else if (_viewModel.UseNativeControl && _ghosttyApp is not null)
        {
            GhosttyNativeTerminalControl nativeControl = new()
            {
                TerminalFontSize = (float)_viewModel.FontSize,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            nativeControl.Initialize(_ghosttyApp);

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

            terminal = nativeControl;
        }
        else
        {
            ThemePalette palette = GetPalette(_viewModel.IsDarkTheme);

            TerminalControl standaloneControl = CreateStandaloneControl();
            standaloneControl.FontFamilyName = MonoFont;
            standaloneControl.TerminalFontSize = _viewModel.FontSize;
            standaloneControl.Columns = 80;
            standaloneControl.Rows = 24;
            standaloneControl.ScrollbackLimit = 10_000;
            standaloneControl.VtProcessorPreference = _viewModel.UseNativeVtControl
                ? VtProcessorPreference.Native
                : _viewModel.UseManagedVtControl
                    ? VtProcessorPreference.Managed
                : VtProcessorPreference.Auto;
            standaloneControl.DefaultForeground = palette.TerminalForeground;
            standaloneControl.DefaultBackground = palette.TerminalBackground;
            ConfigureRenderer(standaloneControl.Renderer);

            standaloneControl.DataReceived += (_, args) =>
            {
                UpdateStatus($"Received {args.Data.Length} bytes");
            };
            standaloneControl.TerminalResized += (_, args) =>
            {
                UpdateDimensions(args.Columns, args.Rows);
            };

            terminal = standaloneControl;
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await StartStandaloneSessionAsync(standaloneControl);
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Failed to start session: {ex.Message}");
                }
            }, DispatcherPriority.Background);
        }

        TabVisualMode tabMode = ResolveTabMode(terminal);
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
        UpdateStatus($"Opened {tabName}");
    }

    private TabVisualMode ResolveTabMode(Control terminal)
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

        if (_viewModel.UseNativeControl && _ghosttyApp is not null)
        {
            return new TabVisualMode(
                "Native (Ghostty Metal)",
                "\u25C6",
                new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA)));
        }

        bool isPipeTransport = string.Equals(
            _viewModel.SelectedTransportMode.Id,
            TerminalTransportIds.Pipe,
            StringComparison.OrdinalIgnoreCase);
        bool isSshTransport = string.Equals(
            _viewModel.SelectedTransportMode.Id,
            TerminalTransportIds.Ssh,
            StringComparison.OrdinalIgnoreCase);

        string vtLabel = terminal is TerminalControl gtc && gtc.IsUsingNativeVtProcessor
            ? "Ghostty VT"
            : "Basic VT";
        string prefix = _viewModel.UseNativeVtControl
            ? "Native VT"
            : _viewModel.UseManagedVtControl
                ? "Managed VT"
                : "Rendered";
        string transportName = _viewModel.SelectedTransportMode.DisplayName;
        string glyph = isPipeTransport
            ? "\u25A1"
            : isSshTransport
                ? "\u25B3"
                : "\u25A0";
        SolidColorBrush glyphBrush = isPipeTransport
            ? new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0))
            : isSshTransport
                ? new SolidColorBrush(Color.FromRgb(0xD7, 0xBA, 0x7D))
                : new SolidColorBrush(Color.FromRgb(0x6A, 0x99, 0x55));

        return new TabVisualMode(
            $"{prefix} ({transportName} - {vtLabel})",
            glyph,
            glyphBrush);
    }

    private static GhosttyRenderedTerminalRenderingMode GetRenderedRenderingMode(bool useTextureInterop)
        => useTextureInterop
            ? GhosttyRenderedTerminalRenderingMode.TextureInterop
            : GhosttyRenderedTerminalRenderingMode.CpuCellRenderer;

    private string GetRenderedBackendLabel()
        => _viewModel.UseTextureInterop ? "TextureInterop (Preview)" : "CPU Cell Renderer";

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
        RejectAllSshHostKeyValidator hostKeyValidator = new();
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
        if (string.IsNullOrWhiteSpace(expectedHostKeyFingerprint))
        {
            throw new InvalidOperationException(
                "SSH host key SHA-256 fingerprint is required when strict host-key validation is enabled.");
        }

        SshAuthenticationOptions authentication = BuildSshAuthenticationOptions();
        return new SshTransportOptions(
            Endpoint: new SshEndpointOptions(host, port, username),
            RequestPty: _viewModel.SshRequestPty,
            TerminalType: string.IsNullOrWhiteSpace(_viewModel.SshTerminalType)
                ? "xterm-256color"
                : _viewModel.SshTerminalType.Trim(),
            InitialCommand: string.IsNullOrWhiteSpace(_viewModel.SshInitialCommand)
                ? null
                : _viewModel.SshInitialCommand.Trim(),
            Authentication: authentication,
            Dimensions: dimensions)
        {
            ExpectedHostKeyFingerprintSha256 = expectedHostKeyFingerprint,
        };
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

        Dispatcher.UIThread.Post(() => target.Control.Focus(), DispatcherPriority.Input);
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
        ThemePalette palette = GetPalette(isDarkTheme);

        foreach (TerminalTab tab in _tabs)
        {
            if (tab.Control is TerminalControl standalone)
            {
                standalone.DefaultForeground = palette.TerminalForeground;
                standalone.DefaultBackground = palette.TerminalBackground;
                standalone.InvalidateTerminal();
            }
            else if (tab.Control is GhosttyRenderedTerminalControl rendered)
            {
                rendered.SetColorScheme(isDarkTheme ? GhosttyColorScheme.Dark : GhosttyColorScheme.Light);
            }
            else if (tab.Control is GhosttyNativeTerminalControl native)
            {
                native.SetColorScheme(isDarkTheme ? GhosttyColorScheme.Dark : GhosttyColorScheme.Light);
            }
        }

        ApplyThemeResources(isDarkTheme);
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

            TabVisualMode tabMode = ResolveTabMode(rendered);
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

    private static ThemePalette GetPalette(bool isDarkTheme)
        => isDarkTheme ? DarkPalette : LightPalette;

    private static void ApplyThemeResources(bool isDarkTheme)
    {
        ThemePalette palette = GetPalette(isDarkTheme);
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
        _tickTimer?.Stop();

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
