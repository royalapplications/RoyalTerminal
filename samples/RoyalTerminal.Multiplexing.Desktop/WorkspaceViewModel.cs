using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Reactive;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop;

/// <summary>Framework-independent presentation state for the experimental remote workspace.</summary>
public sealed class WorkspaceViewModel : ReactiveObject, IDisposable
{
    private readonly IWorkspaceClient _client;
    private readonly IWorkspaceTerminal _terminal;
    private readonly CancellationTokenSource _lifetime = new();
    private string _endpoint = "http://127.0.0.1:7331/";
    private string _token = string.Empty;
    private string _sessionId = string.Empty;
    private string _blockId = string.Empty;
    private string _newSessionLabel = "Research";
    private string _status = "Detached · server processes remain running";
    private string _error = string.Empty;
    private bool _observer = true;
    private bool _isBusy;
    private bool _isAttached;
    private bool _hasControl;
    private bool _disposed;
    private decimal _columns = 120;
    private decimal _rows = 32;
    private WorkspaceSession? _selectedSession;
    private WorkspaceBlock? _selectedBlock;

    internal WorkspaceViewModel(IWorkspaceClient client, IWorkspaceTerminal terminal)
    {
        _client = client;
        _terminal = terminal;
        _terminal.StatusChanged += OnStatusChanged;
        IObservable<bool> idle = this.WhenAnyValue(x => x.IsBusy).Select(busy => !busy);
        RefreshCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(RefreshAsync), idle);
        NewSessionCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(NewSessionAsync), idle);
        NewShellCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(NewShellAsync), idle);
        AttachCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(AttachAsync), idle);
        DetachCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(async () => await _terminal.DetachAsync()), idle);
        ResizeCommand = ReactiveCommand.Create(() =>
        {
            try
            {
                Error = string.Empty;
                _terminal.RequestDimensions(new(checked((int)Columns), checked((int)Rows), 0, 0));
            }
            catch (Exception exception) { Error = exception.Message; }
        }, this.WhenAnyValue(x => x.IsBusy, x => x.HasControl, (busy, control) => !busy && control));
    }

    /// <summary>Gets the authenticated session list.</summary>
    public ObservableCollection<WorkspaceSession> Sessions { get; } = [];
    /// <summary>Gets blocks belonging to the selected session.</summary>
    public ObservableCollection<WorkspaceBlock> Blocks { get; } = [];
    /// <summary>Gets or sets the server URL; changing it does not change an active attachment.</summary>
    public string Endpoint { get => _endpoint; set => this.RaiseAndSetIfChanged(ref _endpoint, value); }
    /// <summary>Gets or sets the in-memory bearer credential, never persisted by this sample.</summary>
    public string Token { get => _token; set => this.RaiseAndSetIfChanged(ref _token, value); }
    /// <summary>Gets or sets an explicit session identity, also usable with scoped sharing credentials.</summary>
    public string SessionId { get => _sessionId; set => this.RaiseAndSetIfChanged(ref _sessionId, value); }
    /// <summary>Gets or sets an explicit block identity.</summary>
    public string BlockId { get => _blockId; set => this.RaiseAndSetIfChanged(ref _blockId, value); }
    /// <summary>Gets or sets the label for a newly created session.</summary>
    public string NewSessionLabel { get => _newSessionLabel; set => this.RaiseAndSetIfChanged(ref _newSessionLabel, value); }
    /// <summary>Gets or sets whether the next attachment is read only.</summary>
    public bool Observer { get => _observer; set => this.RaiseAndSetIfChanged(ref _observer, value); }
    /// <summary>Gets or sets the explicitly requested remote column count.</summary>
    public decimal Columns { get => _columns; set => this.RaiseAndSetIfChanged(ref _columns, value); }
    /// <summary>Gets or sets the explicitly requested remote row count.</summary>
    public decimal Rows { get => _rows; set => this.RaiseAndSetIfChanged(ref _rows, value); }
    /// <summary>Gets the latest attachment status.</summary>
    public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }
    /// <summary>Gets the last user-visible operation error.</summary>
    public string Error { get => _error; private set => this.RaiseAndSetIfChanged(ref _error, value); }
    /// <summary>Gets whether a management operation is active.</summary>
    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    /// <summary>Gets whether the terminal is currently attached.</summary>
    public bool IsAttached { get => _isAttached; private set => this.RaiseAndSetIfChanged(ref _isAttached, value); }
    /// <summary>Gets whether this attachment owns input and resize.</summary>
    public bool HasControl { get => _hasControl; private set => this.RaiseAndSetIfChanged(ref _hasControl, value); }

    /// <summary>Gets or sets the selected session and populates its block list.</summary>
    public WorkspaceSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSession, value);
            Blocks.Clear();
            if (value is not null)
            {
                SessionId = value.Id.ToString("D");
                foreach (WorkspaceBlock block in value.Blocks) { Blocks.Add(block); }
            }
            SelectedBlock = Blocks.FirstOrDefault();
        }
    }

    /// <summary>Gets or sets the selected block and updates explicit identity and requested dimensions.</summary>
    public WorkspaceBlock? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedBlock, value);
            if (value is not null)
            {
                BlockId = value.Id.ToString("D");
                Columns = value.Dimensions.Columns;
                Rows = value.Dimensions.Rows;
            }
            else { BlockId = string.Empty; }
        }
    }

    /// <summary>Reloads the authorized session list.</summary>
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    /// <summary>Creates a durable session.</summary>
    public ReactiveCommand<Unit, Unit> NewSessionCommand { get; }
    /// <summary>Creates a default server shell in the selected session.</summary>
    public ReactiveCommand<Unit, Unit> NewShellCommand { get; }
    /// <summary>Attaches the chosen identity with the requested observer/controller role.</summary>
    public ReactiveCommand<Unit, Unit> AttachCommand { get; }
    /// <summary>Detaches without terminating the server process.</summary>
    public ReactiveCommand<Unit, Unit> DetachCommand { get; }
    /// <summary>Requests an explicit shared-grid change when this client has control.</summary>
    public ReactiveCommand<Unit, Unit> ResizeCommand { get; }

    private async Task RefreshAsync()
    {
        Uri endpoint = ValidateConnection();
        string selectedSession = SessionId;
        string selectedBlock = BlockId;
        IReadOnlyList<WorkspaceSession> sessions = await _client.ListAsync(endpoint, Token, _lifetime.Token);
        Sessions.Clear();
        foreach (WorkspaceSession session in sessions) { Sessions.Add(session); }
        SelectedSession = Sessions.FirstOrDefault(session => session.Id.ToString("D") == selectedSession) ?? Sessions.FirstOrDefault();
        SelectedBlock = Blocks.FirstOrDefault(block => block.Id.ToString("D") == selectedBlock) ?? Blocks.FirstOrDefault();
    }

    private async Task NewSessionAsync()
    {
        Uri endpoint = ValidateConnection();
        WorkspaceSession session = await _client.CreateSessionAsync(endpoint, Token, NewSessionLabel, _lifetime.Token);
        SessionId = session.Id.ToString("D");
        await RefreshAsync();
        Status = "Session created · add a shell to start work";
    }

    private async Task NewShellAsync()
    {
        Uri endpoint = ValidateConnection();
        Guid sessionId = ParseId(SessionId);
        WorkspaceBlock block = await _client.CreateShellAsync(endpoint, Token, sessionId, "Shell", _lifetime.Token);
        BlockId = block.Id.ToString("D");
        await RefreshAsync();
        Status = "Shell started on server · attach to view it";
    }

    private async Task AttachAsync()
    {
        Uri endpoint = ValidateConnection();
        MultiplexingTransportOptions options = new(endpoint, Token, ParseId(SessionId), ParseId(BlockId),
            new(checked((int)Columns), checked((int)Rows), 0, 0)) { Observer = Observer };
        Status = "Attaching · replaying ordered terminal history";
        await _terminal.AttachAsync(options, _lifetime.Token);
        UpdateAttachmentState();
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        if (IsBusy) { return; }
        IsBusy = true;
        Error = string.Empty;
        try { await operation(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { Error = exception.Message; }
        finally { UpdateAttachmentState(); IsBusy = false; }
    }

    private Uri ValidateConnection()
    {
        if (string.IsNullOrWhiteSpace(Token)) { throw new ArgumentException("Enter the administrator or scoped sharing token."); }
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme is not ("http" or "https")) { throw new ArgumentException("Enter an absolute HTTP(S) server URL."); }
        return endpoint;
    }

    private static Guid ParseId(string value) => Guid.TryParse(value, out Guid id) && id != Guid.Empty
        ? id : throw new ArgumentException("Select a session and block, or enter their GUIDs.");
    private void OnStatusChanged(string status) { Status = status; UpdateAttachmentState(); }
    private void UpdateAttachmentState() { IsAttached = _terminal.IsAttached; HasControl = _terminal.HasControl; }

    /// <summary>Cancels outstanding operations and releases presentation subscriptions.</summary>
    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _lifetime.Cancel();
        _terminal.StatusChanged -= OnStatusChanged;
        RefreshCommand.Dispose(); NewSessionCommand.Dispose(); NewShellCommand.Dispose();
        AttachCommand.Dispose(); DetachCommand.Dispose(); ResizeCommand.Dispose();
        _lifetime.Dispose();
    }
}
