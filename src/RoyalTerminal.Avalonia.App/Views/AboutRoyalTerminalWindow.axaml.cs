// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - About dialog view.

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RoyalTerminal.Avalonia.App.Views;

/// <summary>
/// Displays product metadata for the RoyalTerminal application.
/// </summary>
public partial class AboutRoyalTerminalWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutRoyalTerminalWindow"/> class.
    /// </summary>
    public AboutRoyalTerminalWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
