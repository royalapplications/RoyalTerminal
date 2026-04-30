// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using RoyalTerminal.Avalonia.Settings;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalSettingsPanelLayoutTests
{
    [AvaloniaFact]
    public async Task AppearanceTab_ShowsSingleHighlightRuleAboveFooter()
    {
        TerminalSettingsPanelState state = new();
        state.Appearance.AddTextHighlightRuleCommand.Execute(null);
        TerminalSettingsHighlightRuleState rule = Assert.Single(state.Appearance.TextHighlightRules);
        rule.Pattern = @"\b(WARN|WARNING)\b";

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
                    && scrollViewer.Viewport.Height > 0
                    && TryGetVisualWithClass(panel, "settings-highlight-rule-card", out Border ruleCard)
                    && ruleCard.Bounds.Height > 0,
                TimeSpan.FromSeconds(2));

            Assert.True(layoutReady, "The selected Appearance tab did not produce a measured highlight rule card.");
            Assert.True(TryGetVisibleSettingsScrollViewer(panel, out ScrollViewer? selectedScrollViewer));
            Assert.True(TryGetVisualWithClass(panel, "settings-highlight-rule-card", out Border selectedRuleCard));
            Assert.True(TryGetVisualWithClass(panel, "settings-footer", out Border footer));
            ColorPicker[] colorPickers = panel.GetVisualDescendants()
                .OfType<ColorPicker>()
                .Where(picker => picker.Classes.Contains("settings-highlight-color-picker"))
                .ToArray();
            Assert.Equal(4, colorPickers.Length);
            for (int i = 0; i < colorPickers.Length; i++)
            {
                Assert.True(colorPickers[i].Bounds.Width > 0);
                Assert.True(colorPickers[i].Bounds.Height > 0);
            }

            TextBox[] colorInputs = panel.GetVisualDescendants()
                .OfType<TextBox>()
                .Where(input => input.Classes.Contains("settings-highlight-color-input"))
                .ToArray();
            Assert.Equal(4, colorInputs.Length);
            for (int i = 0; i < colorInputs.Length; i++)
            {
                Assert.Equal(VerticalAlignment.Center, colorInputs[i].VerticalContentAlignment);
                Assert.InRange(colorInputs[i].Bounds.Height, 33.5, 34.5);
            }

            CheckBox[] colorToggles = panel.GetVisualDescendants()
                .OfType<CheckBox>()
                .Where(toggle => toggle.Classes.Contains("settings-highlight-color-toggle"))
                .ToArray();
            Assert.Equal(4, colorToggles.Length);
            for (int i = 0; i < colorToggles.Length; i++)
            {
                Assert.Equal(VerticalAlignment.Center, colorToggles[i].VerticalContentAlignment);
                Assert.True(colorToggles[i].Bounds.Height >= 34);
            }

            double ruleCardBottom = GetBottomRelativeToPanel(selectedRuleCard, panel);
            double footerTop = GetTopRelativeToPanel(footer, panel);
            double scrollViewerBottom = GetBottomRelativeToPanel(selectedScrollViewer, panel);

            Assert.True(
                ruleCardBottom <= footerTop + 0.5,
                $"Expected one highlight rule to fit above the footer. RuleBottom={ruleCardBottom}, FooterTop={footerTop}.");
            Assert.True(
                ruleCardBottom <= scrollViewerBottom + 0.5,
                $"Expected one highlight rule to fit inside the visible scroll viewport. RuleBottom={ruleCardBottom}, ScrollBottom={scrollViewerBottom}.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window);
        }
    }

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
            Assert.True(TryGetVisualWithClass(panel, "settings-content-host", out Border contentHost));
            Assert.True(TryGetVisualWithClass(panel, "settings-footer", out Border footer));

            double contentBottom = GetBottomRelativeToPanel(contentHost, panel);
            double footerTop = GetTopRelativeToPanel(footer, panel);
            double scrollViewerBottom = GetBottomRelativeToPanel(selectedScrollViewer, panel);

            Assert.True(
                contentBottom <= footerTop + 0.5,
                $"Expected settings content to end before the footer. ContentBottom={contentBottom}, FooterTop={footerTop}.");
            Assert.True(
                scrollViewerBottom <= footerTop + 0.5,
                $"Expected selected tab viewport to end before the footer. ScrollBottom={scrollViewerBottom}, FooterTop={footerTop}.");
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

    private static bool TryGetVisualWithClass<T>(
        TerminalSettingsPanel panel,
        string className,
        out T control)
        where T : Control
    {
        foreach (T candidate in panel.GetVisualDescendants().OfType<T>())
        {
            if (candidate.Classes.Contains(className) && candidate.Bounds.Height > 0)
            {
                control = candidate;
                return true;
            }
        }

        control = null!;
        return false;
    }

    private static double GetTopRelativeToPanel(Control control, TerminalSettingsPanel panel)
    {
        Point point = control.TranslatePoint(new Point(0, 0), panel)
            ?? throw new InvalidOperationException("Control is not connected to the settings panel visual tree.");
        return point.Y;
    }

    private static double GetBottomRelativeToPanel(Control control, TerminalSettingsPanel panel)
    {
        return GetTopRelativeToPanel(control, panel) + control.Bounds.Height;
    }
}
