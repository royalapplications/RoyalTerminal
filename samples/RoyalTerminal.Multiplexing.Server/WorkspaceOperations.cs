// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Collections.Concurrent;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed class WorkspaceOperations(MultiplexerBroker broker, InspectingTerminalTransportFactory inspections)
{
    private readonly ConcurrentDictionary<Guid, ITerminalTransportOptions> _options = new();
    private readonly Dictionary<Guid, SessionTerminalJournal> _journals = [];
    public MultiplexerBroker Broker { get; } = broker;

    internal SessionTerminalJournal Events(Guid sessionId)
    {
        lock (_journals)
        {
            _ = Broker.GetSession(sessionId);
            if (!_journals.TryGetValue(sessionId, out var journal)) _journals.Add(sessionId, journal = new(Broker.Activity,sessionId));
            return journal;
        }
    }
    internal static ITerminalTransportOptions Prepare(Guid sessionId, NewBlock request)
    {
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Length > 256 || request.Label.Any(char.IsControl)) throw new ArgumentException("Invalid block label.");
        if (request.Command is { } command && (string.IsNullOrWhiteSpace(command.FileName) || command.FileName.Contains('\0') || command.Arguments is null || command.Arguments.Count > 256 || command.Arguments.Any(x => x is null || x.Length > 32768 || x.Contains('\0')))) throw new ArgumentException("Invalid executable or argument list.");
        if (request.WorkingDirectory is { } directory && (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))) throw new ArgumentException("The working directory must be an existing absolute directory.");
        TerminalSessionDimensions dimensions = request.Dimensions ?? new(100, 28, 0, 0);
        if (dimensions.Columns is < 2 or > 512 || dimensions.Rows is < 1 or > 256 || dimensions.WidthPixels is < 0 or > 32768 || dimensions.HeightPixels is < 0 or > 32768) throw new ArgumentException("Terminal dimensions exceed the host limits (512 columns, 256 rows).");
        Dictionary<string,string> environment = new(StringComparer.Ordinal) { ["ROYALTERMINAL_MUX_TOKEN"] = string.Empty, ["ROYALTERMINAL_MUX_SESSION"] = sessionId.ToString(), ["TERM"] = "xterm-256color", ["COLORTERM"] = "truecolor" };
        return request.Transport switch
        {
            "pty" => new PtyTransportOptions(request.Command, request.WorkingDirectory, environment, dimensions),
            "pipe" when request.Command is { } executable => new PipeTransportOptions(executable, request.WorkingDirectory, environment, true, dimensions),
            "pipe" => throw new ArgumentException("Pipe blocks require an executable and argument list."),
            _ => throw new ArgumentException("The experimental host exposes pty and pipe transports."),
        };
    }
    public async ValueTask<MultiplexerBlockDescriptor> CreateBlockAsync(Guid sessionId, NewBlock request, CancellationToken cancellationToken,
        MultiplexerPaneKind placement = MultiplexerPaneKind.Tiled, MultiplexerFloatBounds? bounds = null)
    {
        var options = Prepare(sessionId,request);
        var journal = Events(sessionId);
        MultiplexerBlockDescriptor block;
        if (request.SplitBlockId is { } target)
        {
            if (placement != MultiplexerPaneKind.Tiled || bounds is not null) throw new ArgumentException("A split must create a tiled block.");
            var orientation = request.Orientation switch { "horizontal" => MultiplexerSplitOrientation.Horizontal, "vertical" => MultiplexerSplitOrientation.Vertical, _ => throw new ArgumentException("Split orientation must be horizontal or vertical.") };
            block = await Broker.SplitBlockAsync(sessionId,target,orientation,request.Ratio,request.Label,options,request.ExpectedRevision,cancellationToken);
        }
        else block = await Broker.CreatePlacedBlockAsync(sessionId,request.WindowId,placement,bounds,request.Label,options,request.ExpectedRevision,cancellationToken);
        _options[block.Id] = options;
        try { journal.Include(block.Id, Inspection(sessionId, block.Id).Metadata); }
        catch
        {
            _options.TryRemove(block.Id,out _);
            try { await Broker.CloseBlockAsync(sessionId, block.Id); } catch (MultiplexerException e) when (e.Code == MultiplexerErrorCode.NotFound) { }
            throw;
        }
        return block;
    }
    public async ValueTask CloseBlockAsync(Guid sessionId, Guid blockId)
    {
        _ = GetBlock(sessionId,blockId);
        try { await Broker.CloseBlockAsync(sessionId,blockId); }
        finally { _options.TryRemove(blockId,out _); }
    }
    internal async ValueTask CloseFloatingLayerAsync(Guid sessionId, Guid key, long revision)
    {
        var s=Broker.GetSession(sessionId);
        if(s.Revision!=revision)throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The floating layer changed.");
        Guid layer=s.Blocks.FirstOrDefault(b=>b.Id==key)?.FloatingLayerId??key;
        var members=s.Blocks.Where(b=>b.FloatingLayerId==layer || b.Id==key).Select(b=>b.Id).ToArray();
        try { await Broker.CloseFrameAsync(sessionId,key,revision); }
        catch (MultiplexerException) { throw; }
        catch { foreach(var id in members)_options.TryRemove(id,out _);throw; }
        foreach(var id in members)_options.TryRemove(id,out _);
    }
    public async ValueTask CloseSessionAsync(Guid sessionId)
    {
        var session = Broker.GetSession(sessionId);
        try { await Broker.CloseSessionAsync(sessionId); }
        finally
        {
            foreach (var block in session.Blocks) _options.TryRemove(block.Id,out _);
            lock (_journals) if (_journals.Remove(sessionId, out var journal)) journal.Complete();
        }
    }
    public void Input(Guid sessionId, Guid blockId, string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        if (bytes.Length > 64 * 1024) throw new ArgumentException("Input exceeds 64 KiB.");
        var block = GetBlock(sessionId,blockId);
        using var attachment = Broker.Attach(sessionId,blockId,MultiplexerRole.Controller,block.LastSequence);
        attachment.SendInput(bytes);
    }
    public MultiplexerBlockDescriptor GetBlock(Guid sessionId,Guid blockId) => Broker.GetSession(sessionId).Blocks.FirstOrDefault(x=>x.Id==blockId) ?? throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Block not found.");
    public InspectingTerminalTransport Inspection(Guid sessionId,Guid blockId)
    {
        _ = GetBlock(sessionId,blockId);
        if (!_options.TryGetValue(blockId,out var options) || !inspections.TryGetInspection(options,out var inspection)) throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Terminal inspection is unavailable.");
        return inspection;
    }
}
