// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Reusable terminal shell window activation.

using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RoyalTerminal.Avalonia.App.Services;
using RoyalTerminal.Avalonia.App.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia.Reactive;
using ReactiveUI.Reactive;

namespace RoyalTerminal.Avalonia.App;

/// <summary>
/// Hosts the reusable RoyalTerminal main view, window shortcuts, and native window menu.
/// </summary>
public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    private ITerminalPaneSplitPolicy _paneSplitPolicy = TerminalPaneSplitPolicies.AllowAll;

    /// <summary>
    /// Gets or sets the app-owned split pane policy used by the reusable shell.
    /// </summary>
    public ITerminalPaneSplitPolicy PaneSplitPolicy
    {
        get => _paneSplitPolicy;
        set => _paneSplitPolicy = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
        : this(new MainWindowShellOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="shellOptions">The host-specific shell presentation options.</param>
    public MainWindow(MainWindowShellOptions shellOptions)
    {
        ArgumentNullException.ThrowIfNull(shellOptions);

        InitializeComponent();
        ConfigurePlatformWindowDecorations();

        MainWindowBackdropCoordinator.ConfigureTransparencyHint(this);
        Icon = RoyalTerminalWindowIconHelper.CreateWindowIcon();

        ViewModel = new MainWindowViewModel(shellOptions);

        this.WhenActivated(disposables =>
        {
            var backdropCoordinator = new MainWindowBackdropCoordinator(this, ViewModel!);
            var iconThemeCoordinator = new MainWindowIconThemeCoordinator(this);
            var systemAccentResourceCoordinator = new SystemAccentResourceCoordinator(this);
            var borderAccentCoordinator = new WindowsWindowBorderAccentCoordinator(this);
            var snapLayoutCoordinator = new WindowsCaptionButtonSnapLayoutCoordinator(this);
            var trafficLightPositionCoordinator = new MacOsTrafficLightPositionCoordinator(this);
            var controller = new MainWindowController(
                this,
                ViewModel!,
                PaneSplitPolicy,
                appPreferencesStore: shellOptions.AppPreferencesStore);
            disposables.Add(backdropCoordinator.Activate());
            disposables.Add(iconThemeCoordinator.Activate());
            disposables.Add(systemAccentResourceCoordinator.Activate());
            disposables.Add(borderAccentCoordinator.Activate());
            disposables.Add(snapLayoutCoordinator.Activate());
            disposables.Add(trafficLightPositionCoordinator.Activate());
            disposables.Add(controller.Activate());
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ConfigurePlatformWindowDecorations()
    {
        WindowDecorations = OperatingSystem.IsMacOS()
            ? WindowDecorations.Full
            : WindowDecorations.BorderOnly;
    }

}
