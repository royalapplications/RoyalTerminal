// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia.Interactivity;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

public sealed partial class OverviewWindow
{
    private CancellationTokenSource? _viewConsent;
    private void ToggleLocalZoom(object? sender, RoutedEventArgs e)
    {
        if (!Surface.ToggleZoom() && !EditBar.IsVisible) StatusText.Text="Focus a terminal to zoom it.";
    }
    private async void ToggleRemoteView(object? sender, RoutedEventArgs e)
    {
        if (_viewConsent is { } current) { current.Cancel(); return; }
        if (_snapshot is null || _endpoint is null || _life.IsCancellationRequested) return;
        using var consent = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        _viewConsent = consent; ViewConsent.Content = "Stop remote focus";
        try
        {
            await MultiplexingViewReceiver.RunOverviewAsync(new(_http,_endpoint,_token), _sessionId, "Desktop overview", PresentAsync,
                id => StatusText.Text = $"Remote focus enabled for this overview · {id:N}", consent.Token);
        }
        catch (OperationCanceledException) when (consent.IsCancellationRequested) { }
        catch (Exception error) { if (!consent.IsCancellationRequested) StatusText.Text = "Remote focus stopped: " + error.Message; }
        finally { _viewConsent = null; ViewConsent.Content = "Allow remote focus"; }
    }

    internal async ValueTask<bool> PresentAsync(MultiplexingViewAction action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_life.IsCancellationRequested || _snapshot is not { } s || EditBar.IsVisible) return false;
        Guid? targetBlock = action.BlockId, targetWindow = action.WindowId;
        switch (action.Name)
        {
            case "pane.zoom": return Surface.ToggleZoom();
            case "client.tab.next" or "client.tab.previous":
                int n = s.Windows.Count, index = -1;
                for (int i = 0; i < n; i++) if (s.Windows[i].Id == _windowId) { index = i; break; }
                if (index < 0) return false;
                targetWindow = s.Windows[(index+n+(action.Name=="client.tab.next"?1:-1))%n].Id;
                break;
            case "pane.focus" when action.Direction is { } direction:
                Guid? anchor=null;
                foreach (var (id,pane) in _panes) if (ReferenceEquals(Surface.LastFocusedPane,pane.Root)) { anchor=id; break; }
                targetBlock=PaneNavigation.Resolve(LayoutProjection.Build(s,_windowId).Slots,anchor,direction);
                if (targetBlock is null) return false;
                goto case "pane.focus";
            case "pane.focus":
                var block=s.Blocks.FirstOrDefault(b=>b.Id==targetBlock);
                if (block is null || block.Placement==MultiplexerPaneKind.Unplaced) return false;
                targetWindow=block.WindowId;
                break;
            case "window.focus": break;
            default: return false;
        }
        if (WindowsList.ItemsSource is not IEnumerable<Choice> choices || choices.FirstOrDefault(c=>c.Id==targetWindow) is not { } choice) return false;
        if (_windowId!=choice.Id)
        {
            _binding=true;
            try { WindowsList.SelectedItem=choice; _windowId=choice.Id; }
            finally { _binding=false; }
        }
        await RenderAsync(); ct.ThrowIfCancellationRequested();
        if (EditBar.IsVisible || _life.IsCancellationRequested || _windowId!=choice.Id || !ReferenceEquals(_snapshot,s)) return false;
        if (targetBlock is null) return true;
        if (!_panes.TryGetValue(targetBlock.Value,out var destination)) return false;
        Surface.Reveal(destination.Root);
        return destination.Terminal.IsFocused || destination.Terminal.Focus();
    }
}
