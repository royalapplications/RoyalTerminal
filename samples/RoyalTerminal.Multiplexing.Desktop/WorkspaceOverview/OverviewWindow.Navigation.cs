// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia.Controls;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

public sealed partial class OverviewWindow
{
    private void AdaptNavigation(object? sender, SizeChangedEventArgs e)
    {
        if (TabViewport is null || WindowsList is null) return;
        bool wide = e.NewSize.Width >= 700;
        TabViewport.IsVisible = wide;
        WindowsList.IsVisible = !wide;
    }
}
