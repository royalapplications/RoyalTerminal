// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Avalonia.Settings;
using RoyalTerminal.Avalonia.App.Services;
using RoyalTerminal.Avalonia.App.ViewModels;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("MainWindowControllerHeadlessTests")]
public sealed class MainWindowControllerSettingsPanelTests
{
    private const string DisableSessionAutostartEnvVar = "ROYALTERMINAL_DEMO_DISABLE_SESSION_AUTOSTART";

    [AvaloniaFact]
    public async Task Controller_PrepareSettingsPanel_LoadsStoredProfile_AndApplyUpdatesViewModel()
    {
        using IDisposable autostart = SetProcessEnvironmentVariable(DisableSessionAutostartEnvVar, "1");
        InMemoryProfileStore store = new(CreateStoredDocument());
        MainWindowViewModel viewModel = new();
        Window window = CreateControllerHostWindow(viewModel, out Grid terminalHost);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            settingsProfileStore: store,
            workspaceStore: new InMemoryWorkspaceStore());

        IDisposable? lifetime = null;
        try
        {
            lifetime = controller.Activate();

            await viewModel.PrepareSettingsPanelCommand.Execute();
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(viewModel.SettingsPanelState.SelectedProfile);
            Assert.Equal("Stored Profile", viewModel.SettingsPanelState.SelectedProfile!.DisplayName);
            Assert.Equal(TerminalPasteSafetyPolicy.BlockUnsafe, viewModel.SettingsPanelState.SelectedPasteSafetyPolicy);
            Assert.False(viewModel.SettingsPanelState.ReflowOnResize);
            Assert.True(viewModel.SettingsPanelState.SixelGraphicsEnabled);
            Assert.Equal(TerminalFontSource.File, viewModel.SettingsPanelState.SelectedFontSource);
            Assert.Equal(GetStoredFontPath(), viewModel.SettingsPanelState.FontFilePath);
            Assert.Equal(18, viewModel.SettingsPanelState.FontSize);
            Assert.False(viewModel.SettingsPanelState.FontSubpixelPositioning);
            Assert.Equal(TerminalFontEdging.Alias, viewModel.SettingsPanelState.SelectedFontEdging);
            Assert.Equal(TerminalFontHinting.Full, viewModel.SettingsPanelState.SelectedFontHinting);
            Assert.False(viewModel.SettingsPanelState.FontBaselineSnap);
            Assert.True(viewModel.SettingsPanelState.FontEmbeddedBitmaps);
            Assert.True(viewModel.SettingsPanelState.FontEmbolden);
            Assert.True(viewModel.SettingsPanelState.FontForceAutoHinting);
            Assert.True(viewModel.SettingsPanelState.FontLinearMetrics);
            Assert.Equal(
                TerminalTextHighlightingMode.Realtime,
                viewModel.SettingsPanelState.SelectedTextHighlightingMode?.Mode);
            TerminalSettingsHighlightRuleState storedRule = Assert.Single(viewModel.SettingsPanelState.TextHighlightRules);
            Assert.Equal("Stored highlight", storedRule.Name);
            Assert.Equal("ERROR", storedRule.Pattern);

            viewModel.SettingsPanelState.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.SanitizeControlSequences;
            viewModel.SettingsPanelState.EnableTextShaping = false;
            viewModel.SettingsPanelState.ReflowOnResize = true;
            viewModel.SettingsPanelState.SixelGraphicsEnabled = false;
            viewModel.SettingsPanelState.EnableLigatures = true;
            viewModel.SettingsPanelState.SelectedFontSource = TerminalFontSource.System;
            viewModel.SettingsPanelState.FontFamilyName = "Monaco";
            viewModel.SettingsPanelState.FontSize = 17;
            viewModel.SettingsPanelState.FontSubpixelPositioning = true;
            viewModel.SettingsPanelState.SelectedFontEdging = TerminalFontEdging.Antialias;
            viewModel.SettingsPanelState.SelectedFontHinting = TerminalFontHinting.None;
            viewModel.SettingsPanelState.FontBaselineSnap = true;
            viewModel.SettingsPanelState.FontEmbeddedBitmaps = false;
            viewModel.SettingsPanelState.FontEmbolden = false;
            viewModel.SettingsPanelState.FontForceAutoHinting = false;
            viewModel.SettingsPanelState.FontLinearMetrics = false;
            viewModel.SettingsPanelState.ApplyCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(TerminalPasteSafetyPolicy.SanitizeControlSequences, viewModel.SelectedPasteSafetyPolicy);
            Assert.False(viewModel.EnableTextShaping);
            Assert.True(viewModel.ReflowOnResize);
            Assert.False(viewModel.SixelGraphicsEnabled);
            Assert.True(viewModel.EnableLigatures);
            Assert.Equal(TerminalFontSource.System, viewModel.FontSource);
            Assert.Equal("Monaco", viewModel.FontFamilyName);
            Assert.Equal(17, viewModel.FontSize);
            Assert.True(viewModel.FontSubpixelPositioning);
            Assert.Equal(TerminalFontEdging.Antialias, viewModel.FontEdging);
            Assert.Equal(TerminalFontHinting.None, viewModel.FontHinting);
            Assert.True(viewModel.FontBaselineSnap);
            Assert.False(viewModel.FontEmbeddedBitmaps);
            Assert.False(viewModel.FontEmbolden);
            Assert.False(viewModel.FontForceAutoHinting);
            Assert.False(viewModel.FontLinearMetrics);

            TerminalControl control = terminalHost.Children
                .OfType<ScrollViewer>()
                .Select(viewer => viewer.Content)
                .OfType<TerminalControl>()
                .First();
            Assert.Equal(TerminalFontSource.System, control.FontSource);
            Assert.Equal("Monaco", control.FontFamilyName);
            Assert.Equal(17, control.TerminalFontSize);
            Assert.True(control.FontSubpixelPositioning);
            Assert.Equal(TerminalFontEdging.Antialias, control.FontEdging);
            Assert.Equal(TerminalFontHinting.None, control.FontHinting);
            Assert.True(control.FontBaselineSnap);
            Assert.False(control.FontEmbeddedBitmaps);
            Assert.False(control.FontEmbolden);
            Assert.False(control.FontForceAutoHinting);
            Assert.False(control.FontLinearMetrics);
            Assert.True(control.ReflowOnResize);
            Assert.False(control.SixelGraphicsEnabled);
            Assert.Equal(TerminalTextHighlightingMode.Realtime, control.TextHighlightingMode);
            Assert.Single(control.TextHighlightRules!);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Controller_SaveSettingsPanel_PersistsEditedDocument()
    {
        using IDisposable autostart = SetProcessEnvironmentVariable(DisableSessionAutostartEnvVar, "1");
        InMemoryProfileStore store = new(CreateStoredDocument());
        MainWindowViewModel viewModel = new();
        Window window = CreateControllerHostWindow(viewModel, out _);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            settingsProfileStore: store,
            workspaceStore: new InMemoryWorkspaceStore());

        IDisposable? lifetime = null;
        try
        {
            lifetime = controller.Activate();

            await viewModel.PrepareSettingsPanelCommand.Execute();
            Dispatcher.UIThread.RunJobs();

            viewModel.SettingsPanelState.SessionName = "Renamed Stored Profile";
            viewModel.SettingsPanelState.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.BlockUnsafe;
            viewModel.SettingsPanelState.ReflowOnResize = false;
            viewModel.SettingsPanelState.SixelGraphicsEnabled = true;
            viewModel.SettingsPanelState.SelectedFontSource = TerminalFontSource.File;
            viewModel.SettingsPanelState.FontFamilyName = "Saved Font";
            viewModel.SettingsPanelState.FontFilePath = GetSavedFontPath();
            viewModel.SettingsPanelState.FontSubpixelPositioning = true;
            viewModel.SettingsPanelState.SelectedFontEdging = TerminalFontEdging.Antialias;
            viewModel.SettingsPanelState.SelectedFontHinting = TerminalFontHinting.Normal;
            viewModel.SettingsPanelState.FontBaselineSnap = true;
            viewModel.SettingsPanelState.FontEmbeddedBitmaps = false;
            viewModel.SettingsPanelState.FontEmbolden = false;
            viewModel.SettingsPanelState.FontForceAutoHinting = false;
            viewModel.SettingsPanelState.FontLinearMetrics = false;
            viewModel.SettingsPanelState.SaveCommand.Execute(null);

            bool saved = await WaitUntilAsync(() => store.SaveCount > 0, TimeSpan.FromSeconds(2));
            Assert.True(saved, "Settings panel save command did not persist the profile document.");

            TerminalSessionProfile savedProfile = Assert.Single(store.Document.Profiles);
            Assert.Equal("Renamed Stored Profile", savedProfile.DisplayName);
            Assert.Equal("BlockUnsafe", savedProfile.Behavior.PasteSafetyPolicy);
            Assert.False(savedProfile.Behavior.ReflowOnResize);
            Assert.True(savedProfile.Behavior.SixelGraphicsEnabled);
            Assert.Equal(TerminalFontSource.File, savedProfile.Appearance.FontSource);
            Assert.Equal("Saved Font", savedProfile.Appearance.FontFamilyName);
            Assert.Equal(GetSavedFontPath(), savedProfile.Appearance.FontFilePath);
            Assert.True(savedProfile.Appearance.FontRendering.SubpixelPositioning);
            Assert.Equal(TerminalFontEdging.Antialias, savedProfile.Appearance.FontRendering.Edging);
            Assert.Equal(TerminalFontHinting.Normal, savedProfile.Appearance.FontRendering.Hinting);
            Assert.True(savedProfile.Appearance.FontRendering.BaselineSnap);
            Assert.False(savedProfile.Appearance.FontRendering.EmbeddedBitmaps);
            Assert.False(savedProfile.Appearance.FontRendering.Embolden);
            Assert.False(savedProfile.Appearance.FontRendering.ForceAutoHinting);
            Assert.False(savedProfile.Appearance.FontRendering.LinearMetrics);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    private static Window CreateControllerHostWindow(MainWindowViewModel viewModel, out Grid terminalHost)
    {
        ContentControl titleBarTabStripHost = new()
        {
            Name = "TitleBarTabStripHost",
        };

        StackPanel tabStrip = new()
        {
            Name = "TabStrip",
        };
        RepeatButton tabStripScrollLeftButton = new()
        {
            Name = "TabStripScrollLeftButton",
            IsVisible = false,
        };
        ScrollViewer tabStripScrollViewer = new()
        {
            Name = "TabStripScrollViewer",
            Content = tabStrip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        RepeatButton tabStripScrollRightButton = new()
        {
            Name = "TabStripScrollRightButton",
            IsVisible = false,
        };
        Button tabStripNewTabButton = new()
        {
            Name = "TabStripNewTabButton",
            Command = viewModel.NewTabCommand,
        };
        Grid tabStripLayout = new()
        {
            Name = "TabStripLayout",
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        tabStripLayout.Children.Add(tabStripScrollLeftButton);
        tabStripLayout.Children.Add(tabStripScrollViewer);
        tabStripLayout.Children.Add(tabStripScrollRightButton);
        tabStripLayout.Children.Add(tabStripNewTabButton);
        Grid.SetColumn(tabStripScrollViewer, 1);
        Grid.SetColumn(tabStripScrollRightButton, 2);
        Grid.SetColumn(tabStripNewTabButton, 3);

        Border tabStripSurface = new()
        {
            Name = "TabStripSurface",
            Child = tabStripLayout,
        };
        ContentControl bodyTabStripHost = new()
        {
            Name = "BodyTabStripHost",
            Content = tabStripSurface,
        };

        terminalHost = new Grid
        {
            Name = "TerminalHost",
        };

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.Children.Add(titleBarTabStripHost);
        root.Children.Add(bodyTabStripHost);
        root.Children.Add(terminalHost);
        Grid.SetRow(titleBarTabStripHost, 0);
        Grid.SetRow(bodyTabStripHost, 1);
        Grid.SetRow(terminalHost, 2);

        Window window = new()
        {
            Width = 1200,
            Height = 800,
            DataContext = viewModel,
            Content = root,
        };

        NameScope nameScope = new();
        NameScope.SetNameScope(window, nameScope);
        nameScope.Register(tabStrip.Name!, tabStrip);
        nameScope.Register(titleBarTabStripHost.Name!, titleBarTabStripHost);
        nameScope.Register(bodyTabStripHost.Name!, bodyTabStripHost);
        nameScope.Register(tabStripSurface.Name!, tabStripSurface);
        nameScope.Register(tabStripLayout.Name!, tabStripLayout);
        nameScope.Register(tabStripScrollLeftButton.Name!, tabStripScrollLeftButton);
        nameScope.Register(tabStripScrollViewer.Name!, tabStripScrollViewer);
        nameScope.Register(tabStripScrollRightButton.Name!, tabStripScrollRightButton);
        nameScope.Register(tabStripNewTabButton.Name!, tabStripNewTabButton);
        nameScope.Register(terminalHost.Name!, terminalHost);

        window.Show();
        window.Focus();
        return window;
    }

    private static TerminalSessionProfilesDocument CreateStoredDocument()
    {
        return new TerminalSessionProfilesDocument
        {
            DefaultProfileId = "stored-profile",
            Profiles =
            [
                new TerminalSessionProfile
                {
                    Id = "stored-profile",
                    DisplayName = "Stored Profile",
                    Transport = new TerminalSessionTransportProfile
                    {
                        TransportId = TerminalTransportIds.Pipe,
                        Pipe = new TerminalSessionPipeSettings
                        {
                            FileName = "echo stored-profile",
                            WorkingDirectory = null,
                        },
                    },
                    Appearance = new TerminalSessionAppearanceSettings
                    {
                        FontSource = TerminalFontSource.File,
                        FontFamilyName = "Stored Font",
                        FontFilePath = GetStoredFontPath(),
                        FontSize = 18,
                        FontRendering = new TerminalFontRenderingSettings
                        {
                            SubpixelPositioning = false,
                            Edging = TerminalFontEdging.Alias,
                            Hinting = TerminalFontHinting.Full,
                            BaselineSnap = false,
                            EmbeddedBitmaps = true,
                            Embolden = true,
                            ForceAutoHinting = true,
                            LinearMetrics = true,
                        },
                        TextHighlightingMode = TerminalTextHighlightingMode.Realtime,
                        TextHighlightRules =
                        [
                            new TerminalSessionTextHighlightRule
                            {
                                Name = "Stored highlight",
                                Pattern = "ERROR",
                                ForegroundColor = "#FFFF0000",
                            },
                        ],
                    },
                    Behavior = new TerminalSessionBehaviorSettings
                    {
                        CopyOnSelectEnabled = true,
                        EnableBellNotifications = true,
                        BackspaceSendsControlH = false,
                        EnableTextShaping = true,
                        ReflowOnResize = false,
                        SixelGraphicsEnabled = true,
                        EnableLigatures = false,
                        PasteSafetyPolicy = "BlockUnsafe",
                    },
                    Logging = new TerminalSessionLoggingSettings
                    {
                        Enabled = true,
                        EventLogEnabled = true,
                    },
                },
            ],
        };
    }

    private static string GetStoredFontPath()
    {
        return Path.Combine(Path.GetTempPath(), "royalterminal-stored-font.otf");
    }

    private static string GetSavedFontPath()
    {
        return Path.Combine(Path.GetTempPath(), "royalterminal-saved-font.otf");
    }

    private static IDisposable SetProcessEnvironmentVariable(string variable, string? value)
    {
        string? previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, value);
        return Disposable.Create(() => Environment.SetEnvironmentVariable(variable, previous));
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return true;
            }

            await Task.Delay(25);
        }

        Dispatcher.UIThread.RunJobs();
        return predicate();
    }

    private sealed class InMemoryProfileStore(TerminalSessionProfilesDocument document) : ITerminalSessionProfileStore
    {
        public TerminalSessionProfilesDocument Document { get; private set; } = document;

        public int SaveCount { get; private set; }

        public ValueTask<TerminalSessionProfilesDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Document);
        }

        public ValueTask SaveAsync(TerminalSessionProfilesDocument document, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Document = document;
            SaveCount++;
            return ValueTask.CompletedTask;
        }
    }
}
