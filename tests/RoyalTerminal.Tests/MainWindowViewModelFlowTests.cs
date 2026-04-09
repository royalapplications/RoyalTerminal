// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — UI flow tests for demo ViewModel command surface.

using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using ReactiveUI;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("MainWindowControllerHeadlessTests")]
public class MainWindowViewModelFlowTests
{
    [AvaloniaFact]
    public void KeyboardShortcut_CtrlT_TriggersNewTabFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);

        try
        {
            using var registration = viewModel.CreateNewTabInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                called.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.T, KeyModifiers.Control),
                Command = viewModel.NewTabCommand,
            });

            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcut_CtrlD2_TriggersSwitchTabFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);
        int capturedIndex = -1;

        try
        {
            using var registration = viewModel.SwitchToTabByIndexInteraction.RegisterHandler(context =>
            {
                capturedIndex = context.Input;
                context.SetOutput(Unit.Default);
                called.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.D2, KeyModifiers.Control),
                Command = viewModel.SwitchToTabByIndexCommand,
                CommandParameter = 1,
            });

            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);

            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, capturedIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_CopyPaste_TriggerClipboardFlowAndStatus()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var copyCalled = new ManualResetEventSlim(false);
        using var pasteCalled = new ManualResetEventSlim(false);
        using var selectAllCalled = new ManualResetEventSlim(false);

        try
        {
            using var copyRegistration = viewModel.CopySelectionInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                copyCalled.Set();
            });
            using var pasteRegistration = viewModel.PasteClipboardInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                pasteCalled.Set();
            });
            using var selectAllRegistration = viewModel.SelectAllInteraction.RegisterHandler(context =>
            {
                context.SetOutput(Unit.Default);
                selectAllCalled.Set();
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.C, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.CopySelectionCommand,
            });
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.V, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.PasteClipboardCommand,
            });
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.A, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.SelectAllCommand,
            });

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(copyCalled.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("Copied to clipboard", viewModel.StatusText);

            window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(pasteCalled.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("Pasted from clipboard", viewModel.StatusText);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.True(selectAllCalled.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal("Selected all text", viewModel.StatusText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_CtrlTabAndCtrlShiftTab_TriggerCycleFlow()
    {
        MainWindowViewModel viewModel = new();
        Window window = CreateWindow(viewModel);
        using var called = new ManualResetEventSlim(false);
        List<bool> directions = [];

        try
        {
            using var registration = viewModel.CycleTabInteraction.RegisterHandler(context =>
            {
                directions.Add(context.Input);
                context.SetOutput(Unit.Default);
                if (directions.Count >= 2)
                {
                    called.Set();
                }
            });

            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control),
                Command = viewModel.CycleTabForwardCommand,
            });
            window.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift),
                Command = viewModel.CycleTabBackwardCommand,
            });

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal([true, false], directions);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ModeSwitching_CycleRenderMode_UpdatesState()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: true, useNativeVtControl: false);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseRenderedControl);
        Assert.True(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.True(viewModel.UseManagedVtControl);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);
    }

    [Fact]
    public void ModeSwitching_NoNative_CyclesManagedAndRendered()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: false);
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseNativeVtControl);
        Assert.True(viewModel.UseManagedVtControl);
        Assert.Equal("Managed VT", viewModel.ModeButtonText);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);
        Assert.Equal("Rendered", viewModel.ModeButtonText);
    }

    [Fact]
    public void ModeSwitching_NativeVtOnly_CyclesNativeManagedAndRendered()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);
        Assert.Equal("Native VT", viewModel.ModeButtonText);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.False(viewModel.UseNativeVtControl);
        Assert.True(viewModel.UseManagedVtControl);
        Assert.Equal("Managed VT", viewModel.ModeButtonText);

        viewModel.CycleRenderModeCommand.Execute().Wait();
        Assert.True(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.False(viewModel.UseManagedVtControl);
        Assert.Equal("Rendered", viewModel.ModeButtonText);
    }

    [Fact]
    public void ModeSwitching_RequestRendered_WithNativeVtOnly_PreservesRenderedMode()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);

        viewModel.SetRenderMode(useRenderedControl: true, useNativeVtControl: false);

        Assert.True(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.Equal("Rendered", viewModel.ModeButtonText);
    }

    [Fact]
    public void ModeSwitching_RequestUnavailableNativeVt_FallsBackToManagedVt()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: false);

        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: true);

        Assert.False(viewModel.UseRenderedControl);
        Assert.False(viewModel.UseNativeVtControl);
        Assert.True(viewModel.UseManagedVtControl);
        Assert.Equal("Managed VT", viewModel.ModeButtonText);
    }

    [Fact]
    public void ThemeSwitching_CycleThemePreset_UpdatesActiveThemeForCurrentMode()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: true, useNativeVtControl: false);

        string initialButtonText = viewModel.ThemePresetButtonText;
        string? appliedThemeName = null;
        int applyCount = 0;
        using var registration = viewModel.ApplyThemeModelInteraction.RegisterHandler(context =>
        {
            appliedThemeName = context.Input.ThemeName;
            applyCount++;
            context.SetOutput(Unit.Default);
        });

        viewModel.CycleThemePresetCommand.Execute().Wait();

        Assert.Equal(1, applyCount);
        Assert.NotNull(appliedThemeName);
        Assert.Equal($"Theme: {appliedThemeName}", viewModel.ThemePresetButtonText);
        Assert.NotEqual(initialButtonText, viewModel.ThemePresetButtonText);
        Assert.Contains("Theme preset:", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeSwitching_GeneratedTheme_IsTrackedPerMode()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: true);

        TerminalTheme nativeBefore = viewModel.ActiveTheme;
        int applyCount = 0;
        using var registration = viewModel.ApplyThemeModelInteraction.RegisterHandler(context =>
        {
            applyCount++;
            context.SetOutput(Unit.Default);
        });

        viewModel.GenerateThemeCommand.Execute().Wait();
        TerminalTheme nativeGenerated = viewModel.ActiveTheme;
        Assert.NotSame(nativeBefore, nativeGenerated);

        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false, useManagedVtControl: true);
        TerminalTheme managedBefore = viewModel.ActiveTheme;

        viewModel.GenerateThemeCommand.Execute().Wait();
        TerminalTheme managedGenerated = viewModel.ActiveTheme;
        Assert.NotSame(managedBefore, managedGenerated);

        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: true);
        Assert.Same(nativeGenerated, viewModel.ActiveTheme);
        Assert.Equal(2, applyCount);
    }

    [Fact]
    public void ThemeSwitching_ToggleTheme_SwitchesBetweenLightAndModeDefaultPreset()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: true);

        using var registration = viewModel.ApplyThemeModelInteraction.RegisterHandler(context =>
        {
            context.SetOutput(Unit.Default);
        });

        viewModel.ToggleThemeCommand.Execute().Wait();
        Assert.False(viewModel.IsDarkTheme);
        Assert.Equal("Theme: Solarized Light", viewModel.ThemePresetButtonText);

        viewModel.ToggleThemeCommand.Execute().Wait();
        Assert.True(viewModel.IsDarkTheme);
        Assert.Equal("Theme: Gruvbox Dark", viewModel.ThemePresetButtonText);
    }

    [Fact]
    public void SessionTransport_DefaultsToPtyAndShowsLocalConfig()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);

        Assert.Equal(TerminalTransportIds.Pty, viewModel.SelectedTransportMode.Id);
        Assert.True(viewModel.ShowSessionTransportPicker);
        Assert.True(viewModel.IsSessionTransportConfigEnabled);
        Assert.False(viewModel.ShowSessionTransportHint);
        Assert.True(viewModel.ShowLocalSessionFields);
        Assert.False(viewModel.ShowSshSessionFields);
    }

    [Fact]
    public void SessionTransport_RenderedMode_KeepsTransportConfigEnabled()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetTerminalCapabilities(nativeVtAvailable: true);
        viewModel.SetRenderMode(useRenderedControl: true, useNativeVtControl: false);

        Assert.True(viewModel.ShowSessionTransportPicker);
        Assert.True(viewModel.IsSessionTransportConfigEnabled);
        Assert.False(viewModel.ShowSessionTransportHint);
    }

    [Fact]
    public void SessionTransport_SwitchToSsh_UpdatesVisibilityFlags()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);

        TransportModeOption sshMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SelectedTransportMode = sshMode;

        Assert.True(viewModel.ShowSessionTransportPicker);
        Assert.False(viewModel.ShowLocalSessionFields);
        Assert.True(viewModel.ShowSshSessionFields);
        Assert.True(viewModel.IsSshTransportSelected);
    }

    [Fact]
    public void SessionTransport_SshAuthMode_Password_ShowsPasswordFieldOnly()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SelectedSshAuthMode = FindSshAuthMode(viewModel, SshAuthModeOption.PasswordModeId);

        Assert.True(viewModel.ShowSshSessionFields);
        Assert.True(viewModel.ShowSshPasswordField);
        Assert.False(viewModel.ShowSshPrivateKeyField);
        Assert.False(viewModel.ShowSshAgentHint);
    }

    [Fact]
    public void SessionTransport_SshAuthMode_Agent_ShowsAgentHintOnly()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetRenderMode(useRenderedControl: false, useNativeVtControl: false);
        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SelectedSshAuthMode = FindSshAuthMode(viewModel, SshAuthModeOption.AgentModeId);

        Assert.True(viewModel.ShowSshSessionFields);
        Assert.False(viewModel.ShowSshPasswordField);
        Assert.False(viewModel.ShowSshPrivateKeyField);
        Assert.True(viewModel.ShowSshAgentHint);
    }

    [Fact]
    public void SessionTransport_IncludesRawTelnetAndSerialModes()
    {
        MainWindowViewModel viewModel = new();

        Assert.Equal(TerminalTransportIds.RawTcp, FindTransportMode(viewModel, TerminalTransportIds.RawTcp).Id);
        Assert.Equal(TerminalTransportIds.Telnet, FindTransportMode(viewModel, TerminalTransportIds.Telnet).Id);
        Assert.Equal(TerminalTransportIds.Serial, FindTransportMode(viewModel, TerminalTransportIds.Serial).Id);
    }

    [Fact]
    public void SettingsCategories_DefaultsToSessionCategory()
    {
        MainWindowViewModel viewModel = new();

        Assert.Equal(SettingsCategoryOption.SessionCategoryId, viewModel.SelectedSettingsCategory.Id);
        Assert.True(viewModel.ShowSessionSettingsCategory);
        Assert.False(viewModel.ShowConnectionSettingsCategory);
        Assert.False(viewModel.ShowTerminalSettingsCategory);
        Assert.False(viewModel.ShowAppearanceSettingsCategory);
        Assert.False(viewModel.ShowSshSettingsCategory);
        Assert.False(viewModel.ShowLoggingSettingsCategory);
    }

    [Fact]
    public void SettingsCategories_SshCategory_ShowsSshFieldsOnlyForSshTransport()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedSettingsCategory = FindSettingsCategory(viewModel, SettingsCategoryOption.SshCategoryId);

        Assert.True(viewModel.ShowSshSettingsCategory);
        Assert.True(viewModel.ShowSshSettingsUnavailableHint);
        Assert.False(viewModel.ShowSshSettingsFields);

        viewModel.SelectedTransportMode = FindTransportMode(viewModel, TerminalTransportIds.Ssh);
        viewModel.SelectedSshProxyType = SshProxyType.Socks5;

        Assert.False(viewModel.ShowSshSettingsUnavailableHint);
        Assert.True(viewModel.ShowSshSettingsFields);
        Assert.True(viewModel.ShowSshProxyFields);
    }

    [Fact]
    public void SettingsCategories_LoggingCategory_ExposesLoggingSurface()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedSettingsCategory = FindSettingsCategory(viewModel, SettingsCategoryOption.LoggingCategoryId);

        Assert.True(viewModel.ShowLoggingSettingsCategory);
        Assert.Contains(TerminalSessionLogFormat.PlainText, viewModel.SessionLogFormats);
        Assert.Contains(TerminalSessionLogFormat.RawBytes, viewModel.SessionLogFormats);
        Assert.False(viewModel.SessionLoggingEnabled);
    }

    [Fact]
    public void SettingsCategories_TerminalCategory_ExposesBehaviorOptions()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SelectedSettingsCategory = FindSettingsCategory(viewModel, SettingsCategoryOption.TerminalCategoryId);

        Assert.True(viewModel.ShowTerminalSettingsCategory);
        Assert.False(viewModel.CopyOnSelectEnabled);
        Assert.True(viewModel.EnableBellNotifications);
        Assert.False(viewModel.BackspaceSendsControlH);
        Assert.True(viewModel.EnableTextShaping);
        Assert.False(viewModel.EnableLigatures);
        Assert.Equal(TerminalPasteSafetyPolicy.None, viewModel.SelectedPasteSafetyPolicy);
        Assert.Contains(TerminalPasteSafetyPolicy.BlockUnsafe, viewModel.PasteSafetyPolicies);
    }

    [Fact]
    public void Logging_EventLogAppendAndClear_UpdatesSurface()
    {
        MainWindowViewModel viewModel = new();
        viewModel.AppendEventLogEntry("Connected.");
        viewModel.AppendEventLogEntry("Host key accepted.");

        Assert.True(viewModel.HasEventLogEntries);
        Assert.Equal("2 event(s)", viewModel.EventLogEntryCountText);
        Assert.Contains("Connected.", viewModel.EventLogText, StringComparison.Ordinal);

        viewModel.ClearEventLogCommand.Execute().Wait();

        Assert.False(viewModel.HasEventLogEntries);
        Assert.Equal("0 event(s)", viewModel.EventLogEntryCountText);
        Assert.Equal(string.Empty, viewModel.EventLogText);
    }

    [Fact]
    public void SetShellProfiles_UpdatesSelection()
    {
        MainWindowViewModel viewModel = new();
        viewModel.SetShellProfiles(
        [
            new ShellProfileOption("zsh", "Zsh", "/bin/zsh"),
            new ShellProfileOption("bash", "Bash", "/bin/bash"),
        ]);

        Assert.NotNull(viewModel.SelectedShellProfile);
        Assert.Equal("zsh", viewModel.SelectedShellProfile!.Id);
        Assert.Equal(2, viewModel.ShellProfiles.Count);
    }

    [Fact]
    public void CaptureReplay_ToggleCaptureCommand_UsesCurrentCaptureState()
    {
        MainWindowViewModel viewModel = new();
        List<bool> toggles = [];
        using IDisposable registration = viewModel.ToggleCaptureInteraction.RegisterHandler(context =>
        {
            toggles.Add(context.Input);
            context.SetOutput(Unit.Default);
        });

        viewModel.ToggleCaptureCommand.Execute().Wait();
        viewModel.SetCaptureState(isCaptureActive: true, hasCapture: false);
        viewModel.ToggleCaptureCommand.Execute().Wait();

        Assert.Equal([true, false], toggles);
    }

    [Fact]
    public void CaptureReplay_ToggleReplayPlaybackCommand_UsesCurrentPlaybackState()
    {
        MainWindowViewModel viewModel = new();
        List<bool> requests = [];
        using IDisposable registration = viewModel.SetReplayPlayingInteraction.RegisterHandler(context =>
        {
            requests.Add(context.Input);
            context.SetOutput(Unit.Default);
        });

        viewModel.SetReplayState(
            isReplayEnabled: true,
            isReplayPlaying: false,
            replayPositionSeconds: 0,
            replayDurationSeconds: 30,
            replaySourceLabel: "capture.rtcap.json");
        viewModel.ToggleReplayPlaybackCommand.Execute().Wait();

        viewModel.SetReplayState(
            isReplayEnabled: true,
            isReplayPlaying: true,
            replayPositionSeconds: 10,
            replayDurationSeconds: 30,
            replaySourceLabel: "capture.rtcap.json");
        viewModel.ToggleReplayPlaybackCommand.Execute().Wait();

        Assert.Equal([true, false], requests);
    }

    [Fact]
    public void CaptureReplay_SetReplayState_UpdatesTimelineSurface()
    {
        MainWindowViewModel viewModel = new();

        viewModel.SetReplayState(
            isReplayEnabled: true,
            isReplayPlaying: false,
            replayPositionSeconds: 65,
            replayDurationSeconds: 125,
            replaySourceLabel: "session.rtcap.json");

        Assert.True(viewModel.IsReplayEnabled);
        Assert.Equal(125, viewModel.ReplayDurationSeconds);
        Assert.Equal(65, viewModel.ReplayTimelineValue);
        Assert.Equal("01:05 / 02:05", viewModel.ReplayTimelineText);
        Assert.Equal("session.rtcap.json", viewModel.ReplaySourceLabel);
        Assert.True(viewModel.CanSeekReplay);
    }

    [Fact]
    public void Showcase_SetSearchStateAndDiagnostics_UpdatesSurface()
    {
        MainWindowViewModel viewModel = new();

        viewModel.SetSearchState("ghostty", total: 3, selected: 1, usesNativeScrollback: true);
        viewModel.SetGhosttyDiagnostics(show: true, text: "SIMD: yes");

        Assert.Equal("ghostty", viewModel.SearchQuery);
        Assert.True(viewModel.CanApplySearch);
        Assert.True(viewModel.CanAdvanceSearch);
        Assert.True(viewModel.CanClearSearch);
        Assert.Equal("2/3 matches · native scrollback", viewModel.SearchResultText);
        Assert.True(viewModel.ShowGhosttyDiagnostics);
        Assert.Equal("Hide Diagnostics", viewModel.GhosttyDiagnosticsButtonText);
        Assert.Equal("SIMD: yes", viewModel.GhosttyDiagnosticsText);

        viewModel.ClearSearchState();
        viewModel.SetGhosttyDiagnostics(show: false, text: string.Empty);

        Assert.False(viewModel.CanAdvanceSearch);
        Assert.False(viewModel.CanClearSearch);
        Assert.Equal("Search idle", viewModel.SearchResultText);
        Assert.False(viewModel.ShowGhosttyDiagnostics);
        Assert.Equal("Native Diagnostics", viewModel.GhosttyDiagnosticsButtonText);
        Assert.Contains("unavailable", viewModel.GhosttyDiagnosticsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Showcase_Commands_RouteThroughInteractions()
    {
        MainWindowViewModel viewModel = new();
        List<string> calls = [];
        List<bool> diagnosticsToggles = [];
        List<TerminalSnapshotExportFormat> snapshotFormats = [];

        using IDisposable applySearch = viewModel.ApplySearchInteraction.RegisterHandler(context =>
        {
            calls.Add($"search:{context.Input}");
            context.SetOutput(Unit.Default);
        });
        using IDisposable nextSearch = viewModel.NextSearchInteraction.RegisterHandler(context =>
        {
            calls.Add("next");
            context.SetOutput(Unit.Default);
        });
        using IDisposable previousSearch = viewModel.PreviousSearchInteraction.RegisterHandler(context =>
        {
            calls.Add("previous");
            context.SetOutput(Unit.Default);
        });
        using IDisposable clearSearch = viewModel.ClearSearchInteraction.RegisterHandler(context =>
        {
            calls.Add("clear");
            context.SetOutput(Unit.Default);
        });
        using IDisposable hyperlinkSample = viewModel.ShowHyperlinkSampleInteraction.RegisterHandler(context =>
        {
            calls.Add("hyperlink");
            context.SetOutput(Unit.Default);
        });
        using IDisposable kittySample = viewModel.ShowKittyGraphicsSampleInteraction.RegisterHandler(context =>
        {
            calls.Add("kitty");
            context.SetOutput(Unit.Default);
        });
        using IDisposable diagnostics = viewModel.ToggleGhosttyDiagnosticsInteraction.RegisterHandler(context =>
        {
            diagnosticsToggles.Add(context.Input);
            viewModel.SetGhosttyDiagnostics(context.Input, context.Input ? "open" : "closed");
            context.SetOutput(Unit.Default);
        });
        using IDisposable copySnapshot = viewModel.CopySnapshotInteraction.RegisterHandler(context =>
        {
            snapshotFormats.Add(context.Input);
            calls.Add($"snapshot:{context.Input}");
            context.SetOutput(Unit.Default);
        });

        viewModel.SearchQuery = "demo";
        viewModel.ApplySearchCommand.Execute().Wait();
        viewModel.NextSearchCommand.Execute().Wait();
        viewModel.PreviousSearchCommand.Execute().Wait();
        viewModel.ClearSearchCommand.Execute().Wait();
        viewModel.ShowHyperlinkSampleCommand.Execute().Wait();
        viewModel.ShowKittyGraphicsSampleCommand.Execute().Wait();
        viewModel.CopyPlainSnapshotCommand.Execute().Wait();
        viewModel.CopyStyledVtSnapshotCommand.Execute().Wait();
        viewModel.CopyHtmlSnapshotCommand.Execute().Wait();
        viewModel.ToggleGhosttyDiagnosticsCommand.Execute().Wait();
        viewModel.ToggleGhosttyDiagnosticsCommand.Execute().Wait();

        Assert.Equal(
            [
                "search:demo",
                "next",
                "previous",
                "clear",
                "hyperlink",
                "kitty",
                $"snapshot:{TerminalSnapshotExportFormat.PlainText}",
                $"snapshot:{TerminalSnapshotExportFormat.StyledVt}",
                $"snapshot:{TerminalSnapshotExportFormat.Html}",
            ],
            calls);
        Assert.Equal([true, false], diagnosticsToggles);
        Assert.Equal(
            [
                TerminalSnapshotExportFormat.PlainText,
                TerminalSnapshotExportFormat.StyledVt,
                TerminalSnapshotExportFormat.Html,
            ],
            snapshotFormats);
    }

    private static TransportModeOption FindTransportMode(MainWindowViewModel viewModel, string transportId)
    {
        for (int i = 0; i < viewModel.TransportModes.Count; i++)
        {
            if (string.Equals(viewModel.TransportModes[i].Id, transportId, StringComparison.Ordinal))
            {
                return viewModel.TransportModes[i];
            }
        }

        throw new InvalidOperationException($"Transport mode '{transportId}' was not found.");
    }

    private static SshAuthModeOption FindSshAuthMode(MainWindowViewModel viewModel, string authModeId)
    {
        for (int i = 0; i < viewModel.SshAuthModes.Count; i++)
        {
            if (string.Equals(viewModel.SshAuthModes[i].Id, authModeId, StringComparison.Ordinal))
            {
                return viewModel.SshAuthModes[i];
            }
        }

        throw new InvalidOperationException($"SSH auth mode '{authModeId}' was not found.");
    }

    private static SettingsCategoryOption FindSettingsCategory(MainWindowViewModel viewModel, string categoryId)
    {
        for (int i = 0; i < viewModel.SettingsCategories.Count; i++)
        {
            if (string.Equals(viewModel.SettingsCategories[i].Id, categoryId, StringComparison.Ordinal))
            {
                return viewModel.SettingsCategories[i];
            }
        }

        throw new InvalidOperationException($"Settings category '{categoryId}' was not found.");
    }

    private static Window CreateWindow(object dataContext)
    {
        Window window = new()
        {
            Width = 800,
            Height = 600,
            DataContext = dataContext,
            Content = new Border(),
        };

        window.Show();
        window.Focus();
        return window;
    }
}
