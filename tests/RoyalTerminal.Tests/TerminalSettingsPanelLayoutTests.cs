// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RoyalTerminal.Avalonia.Settings;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalSettingsPanelLayoutTests
{
    [AvaloniaFact]
    public async Task AppearanceTab_UsesConstrainedScrollableViewport()
    {
        TerminalSettingsPanelState state = new();
        for (int i = 0; i < 4; i++)
        {
            state.Appearance.AddTextHighlightRuleCommand.Execute(null);
        }

        TerminalSettingsPanel panel = new()
        {
            DataContext = state,
        };

        Window window = new()
        {
            Width = 760,
            Height = 740,
            Content = panel,
        };

        try
        {
            window.Show();
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            TabControl tabControl = Assert.Single(panel.GetVisualDescendants().OfType<TabControl>());
            tabControl.SelectedIndex = 3;

            bool layoutReady = await HeadlessTerminalTestCleanup.WaitUntilAsync(
                () => TryGetVisibleSettingsScrollViewer(panel, out ScrollViewer? scrollViewer)
                    && scrollViewer.Extent.Height > 0
                    && scrollViewer.Viewport.Height > 0,
                TimeSpan.FromSeconds(2));

            Assert.True(layoutReady, "The selected settings tab did not produce a measured scroll viewport.");
            Assert.True(TryGetVisibleSettingsScrollViewer(panel, out ScrollViewer? selectedScrollViewer));
            Assert.True(
                selectedScrollViewer.Extent.Height > selectedScrollViewer.Viewport.Height,
                $"Expected Appearance content to scroll. Extent={selectedScrollViewer.Extent.Height}, Viewport={selectedScrollViewer.Viewport.Height}.");
            Assert.True(
                selectedScrollViewer.Bounds.Height <= tabControl.Bounds.Height,
                $"Expected tab ScrollViewer to stay inside the tab body. ScrollViewer={selectedScrollViewer.Bounds.Height}, TabControl={tabControl.Bounds.Height}.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window);
        }
    }

    private static bool TryGetVisibleSettingsScrollViewer(
        TerminalSettingsPanel panel,
        out ScrollViewer scrollViewer)
    {
        foreach (ScrollViewer candidate in panel.GetVisualDescendants().OfType<ScrollViewer>())
        {
            if (candidate.Classes.Contains("settings-tab-scroll")
                && candidate.IsVisible
                && candidate.Bounds.Height > 0)
            {
                scrollViewer = candidate;
                return true;
            }
        }

        scrollViewer = null!;
        return false;
    }
}
