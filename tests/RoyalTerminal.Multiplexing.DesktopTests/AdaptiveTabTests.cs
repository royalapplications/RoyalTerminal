using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;
public sealed class AdaptiveTabTests
{
    [AvaloniaFact]
    public void NarrowPickerAndWideTabsRetainTheSameWindowIdentity()
    {
        var window = new OverviewWindow { Width = 900, Height = 550 };
        try
        {
            window.Show();
            var picker = window.FindControl<ComboBox>("WindowsList")!;
            var tabs = window.FindControl<TabStrip>("WindowTabs")!;
            var viewport = window.FindControl<ScrollViewer>("TabViewport")!;
            var first = new object(); var second = new object();
            picker.ItemsSource = new[] { first, second }; picker.SelectedItem = first;
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewport.IsVisible); Assert.False(picker.IsVisible);
            tabs.SelectedItem = second; Dispatcher.UIThread.RunJobs(); Assert.Same(second, picker.SelectedItem);
            window.Width = 540; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.False(viewport.IsVisible); Assert.True(picker.IsVisible); Assert.Same(second, picker.SelectedItem);
            picker.SelectedItem = first; Dispatcher.UIThread.RunJobs(); Assert.Same(first, tabs.SelectedItem);
        }
        finally { window.Close(); }
    }
}
