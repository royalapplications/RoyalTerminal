// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Demo.Services;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class MainWindowControllerSettingsPanelTests
{
    [AvaloniaFact]
    public void Controller_PrepareSettingsPanel_LoadsStoredProfile_AndApplyUpdatesViewModel()
    {
        InMemoryProfileStore store = new(CreateStoredDocument());
        MainWindowViewModel viewModel = new();
        Window window = CreateControllerHostWindow(viewModel, out _);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            skipEmbeddedGhosttyInitialization: true,
            settingsProfileStore: store);

        IDisposable? lifetime = null;
        try
        {
            lifetime = controller.Activate();

            viewModel.PrepareSettingsPanelCommand.Execute().Wait();
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(viewModel.SettingsPanelState.SelectedProfile);
            Assert.Equal("Stored Profile", viewModel.SettingsPanelState.SelectedProfile!.DisplayName);
            Assert.Equal(TerminalPasteSafetyPolicy.BlockUnsafe, viewModel.SettingsPanelState.SelectedPasteSafetyPolicy);

            viewModel.SettingsPanelState.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.SanitizeControlSequences;
            viewModel.SettingsPanelState.EnableTextShaping = false;
            viewModel.SettingsPanelState.EnableLigatures = true;
            viewModel.SettingsPanelState.ApplyCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(TerminalPasteSafetyPolicy.SanitizeControlSequences, viewModel.SelectedPasteSafetyPolicy);
            Assert.False(viewModel.EnableTextShaping);
            Assert.True(viewModel.EnableLigatures);
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
        InMemoryProfileStore store = new(CreateStoredDocument());
        MainWindowViewModel viewModel = new();
        Window window = CreateControllerHostWindow(viewModel, out _);
        MainWindowController controller = new(
            window,
            viewModel,
            new TerminalModeCapabilityResolver(),
            TerminalModeResolver.Default,
            skipEmbeddedGhosttyInitialization: true,
            settingsProfileStore: store);

        IDisposable? lifetime = null;
        try
        {
            lifetime = controller.Activate();

            viewModel.PrepareSettingsPanelCommand.Execute().Wait();
            Dispatcher.UIThread.RunJobs();

            viewModel.SettingsPanelState.SessionName = "Renamed Stored Profile";
            viewModel.SettingsPanelState.SelectedPasteSafetyPolicy = TerminalPasteSafetyPolicy.BlockUnsafe;
            viewModel.SettingsPanelState.SaveCommand.Execute(null);

            bool saved = await WaitUntilAsync(() => store.SaveCount > 0, TimeSpan.FromSeconds(2));
            Assert.True(saved, "Settings panel save command did not persist the profile document.");

            TerminalSessionProfile savedProfile = Assert.Single(store.Document.Profiles);
            Assert.Equal("Renamed Stored Profile", savedProfile.DisplayName);
            Assert.Equal("BlockUnsafe", savedProfile.Behavior.PasteSafetyPolicy);
        }
        finally
        {
            lifetime?.Dispose();
            window.Close();
        }
    }

    private static Window CreateControllerHostWindow(MainWindowViewModel viewModel, out Grid terminalHost)
    {
        StackPanel tabStrip = new()
        {
            Name = "TabStrip",
        };

        terminalHost = new Grid
        {
            Name = "TerminalHost",
        };

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.Children.Add(tabStrip);
        root.Children.Add(terminalHost);
        Grid.SetRow(tabStrip, 0);
        Grid.SetRow(terminalHost, 1);

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
                    Behavior = new TerminalSessionBehaviorSettings
                    {
                        CopyOnSelectEnabled = true,
                        EnableBellNotifications = true,
                        BackspaceSendsControlH = false,
                        EnableTextShaping = true,
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
