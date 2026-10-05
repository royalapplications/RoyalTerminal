// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RoyalTerminal.Avalonia.App.Views;

/// <summary>Passive view for a terminal hyperlink decision.</summary>
public partial class HyperlinkPromptWindow : Window
{
    /// <summary>Initializes the compiled dialog view.</summary>
    public HyperlinkPromptWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
