// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Reusable terminal shell window activation.

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
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainWindowViewModel();

        this.WhenActivated(disposables =>
        {
            var controller = new MainWindowController(this, ViewModel!);
            disposables.Add(controller.Activate());
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
