// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Reusable terminal shell window activation.

using System;
using Avalonia.Markup.Xaml;
using RoyalTerminal.Avalonia.App.Services;
using RoyalTerminal.Avalonia.App.ViewModels;
using ReactiveUI;
using ReactiveUI.Avalonia;

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
    {
        InitializeComponent();

        ViewModel = new MainWindowViewModel();

        this.WhenActivated(disposables =>
        {
            var controller = new MainWindowController(this, ViewModel!, PaneSplitPolicy);
            disposables.Add(controller.Activate());
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
