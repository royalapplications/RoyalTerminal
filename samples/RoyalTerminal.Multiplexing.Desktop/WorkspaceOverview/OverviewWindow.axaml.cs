// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Multiplexing.Desktop.Services;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

/// <summary>Read-only live view of the server's tiled and floating terminals.</summary>
public sealed partial class OverviewWindow : Window
{
    private sealed record Choice(Guid Id, string Label) { public override string ToString() => Label; }
    private sealed class Pane(HttpClient http, IMultiplexingSocketConnector? connector)
    {
        internal readonly RemoteTerminalControl Terminal = connector is null ? new() : new(connector);
        internal readonly TextBlock Caption = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(5, 2) };
        internal readonly Border Root = new() { ClipToBounds = true, Margin = new Thickness(2), Background = Brushes.Black,
            BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1) };
        private string _label = string.Empty, _status = "Attaching…";
        private WorkspaceTerminalController? _controller;
        private readonly CancellationTokenSource _life = new();
        private Task _startup = Task.CompletedTask;
        internal void Start(MultiplexingTransportOptions options)
        {
            Terminal.TerminalFontSize = 12;
            var layout = new DockPanel(); DockPanel.SetDock(Caption, Dock.Top); layout.Children.Add(Caption);
            layout.Children.Add(new ScrollViewer { Content = Terminal, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }); Root.Child = layout;
            _controller = new(Terminal, http); _controller.StatusChanged += Updated;
            _startup = AttachAsync(_controller, options, _life.Token);
        }
        internal void Refresh(string label) { _label = label; Caption.Text = $"{_label} · {_status}"; }
        private void Updated(string status) { _status = status; Refresh(_label); }
        private async Task AttachAsync(WorkspaceTerminalController controller, MultiplexingTransportOptions options, CancellationToken token)
        {
            try { await controller.AttachAsync(options with { Observer = true }, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception e) { Updated("Unable to observe: " + e.Message); }
        }
        internal async Task ReleaseAsync()
        {
            _life.Cancel();
            try { await _startup; if (_controller is not null) { _controller.StatusChanged -= Updated; await _controller.DisposeAsync(); } }
            finally { _life.Dispose(); }
        }
    }

    private readonly Dictionary<Guid, Pane> _panes = [];
    private readonly HttpClient _http;
    private readonly IMultiplexingSocketConnector? _connector;
    private readonly CancellationTokenSource _life = new();
    private readonly SemaphoreSlim _render = new(1, 1);
    private Task? _watch;
    private Uri? _endpoint;
    private string _token = string.Empty;
    private Guid _sessionId, _windowId;
    private MultiplexerSessionDescriptor? _snapshot;
    private bool _binding;
    internal Task Shutdown { get; private set; } = Task.CompletedTask;

    public OverviewWindow() : this(new HttpClientHandler { AllowAutoRedirect = false }, null) { }

    internal OverviewWindow(HttpMessageHandler http, IMultiplexingSocketConnector? connector)
    {
        _http = new(http) { Timeout = TimeSpan.FromSeconds(35) };
        _connector = connector;
        InitializeComponent();
        WindowsList.SelectionChanged += async (_, _) =>
        {
            if (_binding || WindowsList.SelectedItem is not Choice choice) return;
            _windowId = choice.Id;
            try { await RenderAsync(); } catch (Exception e) { StatusText.Text = e.Message; }
        };
        Opened += (_, _) => { if (_endpoint is not null) _watch = WatchAsync(); };
        Closed += (_, _) => Shutdown = StopAsync();
    }

    private async Task StopAsync()
    {
        _life.Cancel();
        try { if (_watch is not null) await _watch; await ClearAsync(); }
        finally { _http.Dispose(); _token = string.Empty; _life.Dispose(); }
    }

    internal void Connect(Uri endpoint, string token, Guid sessionId) => (_endpoint, _token, _sessionId) = (endpoint, token, sessionId);

    private async Task WatchAsync()
    {
        var ct = _life.Token;
        try
        {
            var api = new MultiplexingApiClient(_http, _endpoint!, _token);
            var json = await api.SendAsync(HttpMethod.Get, $"api/sessions/{_sessionId:D}", cancellationToken: ct);
            while (!ct.IsCancellationRequested)
            {
                var session = json.Deserialize(OverviewJson.Default.MultiplexerSessionDescriptor) ?? throw new InvalidDataException("Missing session snapshot.");
                if (session.Id != _sessionId || session.Windows.Count == 0) throw new InvalidDataException("Wrong or empty session.");
                if (session.Revision != _snapshot?.Revision)
                {
                    _snapshot = session; Title = $"{session.Label} · Workspace overview";
                    var choices = session.Windows.Select(w => new Choice(w.Id, w.Label)).ToArray();
                    var selected = choices.FirstOrDefault(w => w.Id == _windowId) ?? choices[0];
                    _binding = true;
                    try { _windowId = selected.Id; WindowsList.ItemsSource = choices; WindowsList.SelectedItem = selected; }
                    finally { _binding = false; }
                    await RenderAsync();
                }
                var args = JsonSerializer.SerializeToElement(new RevisionPoll(_sessionId, session.Revision, 15000), OverviewJson.Default.RevisionPoll);
                json = await api.CallAsync("session.wait_change", args, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e)
        {
            _snapshot = null; _binding = true; WindowsList.ItemsSource = null; ShelfText.Text = string.Empty;
            Title = "Workspace overview"; StatusText.Text = "Workspace subscription stopped: " + e.Message;
            await ClearAsync();
        }
    }

    private async Task RenderAsync()
    {
        await _render.WaitAsync();
        try
        {
            if (_life.IsCancellationRequested || _snapshot is not { } session) return;
            Guid windowId = _windowId;
            var plan = LayoutProjection.Build(session, windowId);
            var wanted = plan.Slots.Select(s => s.Block.Id).ToHashSet();
            var obsolete = _panes.Keys.Where(k => !wanted.Contains(k)).ToArray();
            await Task.WhenAll(obsolete.Select(Remove).ToArray());
            if (_life.IsCancellationRequested || !ReferenceEquals(session, _snapshot) || windowId != _windowId) return;
            foreach (var slot in plan.Slots)
            {
                if (!_panes.TryGetValue(slot.Block.Id, out var pane))
                {
                    pane = new(_http, _connector); _panes.Add(slot.Block.Id, pane); Surface.Place(pane.Root, slot.Area, slot.Layer);
                    pane.Start(new(_endpoint!, _token, _sessionId, slot.Block.Id, slot.Block.Dimensions));
                }
                else Surface.Place(pane.Root, slot.Area, slot.Layer);
                pane.Refresh(slot.Block.Label);
            }
            ShelfText.Text = plan.Shelf.Count == 0 ? string.Empty : "Shelf · " + string.Join(" · ", plan.Shelf.Select(b => b.Label));
            StatusText.Text = $"{plan.Slots.Count} visible · {plan.Shelf.Count} shelved · revision {session.Revision}";
        }
        finally { _render.Release(); }
    }

    private Task Remove(Guid id)
    {
        var pane = _panes[id]; _panes.Remove(id); Surface.RemovePane(pane.Root); return pane.ReleaseAsync();
    }

    private async Task ClearAsync()
    {
        await _render.WaitAsync();
        try { await Task.WhenAll(_panes.Keys.ToArray().Select(Remove)); }
        finally { _render.Release(); }
    }
}
