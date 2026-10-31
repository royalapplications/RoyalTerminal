// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed class WorkspaceOperations(MultiplexerBroker broker, InspectingTerminalTransportFactory inspections)
{
    private readonly ConcurrentDictionary<Guid, ITerminalTransportOptions> _options = new();
    public MultiplexerBroker Broker { get; } = broker;

    public async ValueTask<MultiplexerBlockDescriptor> CreateBlockAsync(Guid sessionId, NewBlock request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Length > 256 || request.Label.Any(char.IsControl)) throw new ArgumentException("Invalid block label.");
        if (request.Command is { } command && (string.IsNullOrWhiteSpace(command.FileName)
            || command.FileName.Contains('\0') || command.Arguments is null || command.Arguments.Count > 256
            || command.Arguments.Any(x => x is null || x.Length > 32768 || x.Contains('\0'))))
            throw new ArgumentException("Invalid executable or argument list.");
        if (request.WorkingDirectory is { } directory && (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)))
            throw new ArgumentException("The working directory must be an existing absolute directory.");
        TerminalSessionDimensions dimensions = request.Dimensions ?? new(100, 28, 0, 0);
        if (dimensions.Columns is < 2 or > 512 || dimensions.Rows is < 1 or > 256
            || dimensions.WidthPixels is < 0 or > 32768 || dimensions.HeightPixels is < 0 or > 32768)
            throw new ArgumentException("Terminal dimensions exceed the host limits (512 columns, 256 rows).");
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            // Local transports merge inherited variables with string overrides. Blank the
            // host capability explicitly; this does not isolate the child OS account.
            ["ROYALTERMINAL_MUX_TOKEN"] = string.Empty,
            ["ROYALTERMINAL_MUX_SESSION"] = sessionId.ToString(),
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
        };
        ITerminalTransportOptions options = request.Transport switch
        {
            "pty" => new PtyTransportOptions(request.Command, request.WorkingDirectory, environment, dimensions),
            "pipe" when request.Command is { } executable => new PipeTransportOptions(executable, request.WorkingDirectory, environment, true, dimensions),
            "pipe" => throw new ArgumentException("Pipe blocks require an executable and argument list."),
            _ => throw new ArgumentException("The experimental host exposes pty and pipe transports."),
        };
        MultiplexerBlockDescriptor block;
        if (request.SplitBlockId is { } target)
        {
            MultiplexerSplitOrientation orientation = request.Orientation switch
            {
                "horizontal" => MultiplexerSplitOrientation.Horizontal,
                "vertical" => MultiplexerSplitOrientation.Vertical,
                _ => throw new ArgumentException("Split orientation must be horizontal or vertical."),
            };
            block = await Broker.SplitBlockAsync(sessionId, target, orientation, request.Ratio, request.Label,
                options, request.ExpectedRevision, cancellationToken);
        }
        else if (request.WindowId is { } windowId)
            block = await Broker.AddBlockAsync(sessionId, windowId, request.Label, options, cancellationToken);
        else block = await Broker.AddBlockAsync(sessionId, request.Label, options, cancellationToken);
        _options[block.Id] = options;
        return block;
    }

    public async ValueTask CloseBlockAsync(Guid sessionId, Guid blockId)
    {
        // A rejected session/block pair must not erase another session's inspection.
        _ = GetBlock(sessionId, blockId);
        try { await Broker.CloseBlockAsync(sessionId, blockId); }
        finally { _options.TryRemove(blockId, out _); }
    }

    public async ValueTask CloseSessionAsync(Guid sessionId)
    {
        MultiplexerSessionDescriptor session = Broker.GetSession(sessionId);
        try { await Broker.CloseSessionAsync(sessionId); }
        finally { foreach (MultiplexerBlockDescriptor block in session.Blocks) _options.TryRemove(block.Id, out _); }
    }

    public void Input(Guid sessionId, Guid blockId, string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        if (bytes.Length > 64 * 1024) throw new ArgumentException("Input exceeds 64 KiB.");
        MultiplexerBlockDescriptor block = GetBlock(sessionId, blockId);
        using MultiplexerAttachment attachment = Broker.Attach(sessionId, blockId, MultiplexerRole.Controller, block.LastSequence);
        attachment.SendInput(bytes);
    }

    public MultiplexerBlockDescriptor GetBlock(Guid sessionId, Guid blockId)
        => Broker.GetSession(sessionId).Blocks.FirstOrDefault(x => x.Id == blockId)
            ?? throw new MultiplexerException(MultiplexerErrorCode.NotFound, "Block not found.");

    public InspectingTerminalTransport Inspection(Guid sessionId, Guid blockId)
    {
        _ = GetBlock(sessionId, blockId);
        if (!_options.TryGetValue(blockId, out ITerminalTransportOptions? options)
            || !inspections.TryGetInspection(options, out InspectingTerminalTransport? inspection))
            throw new MultiplexerException(MultiplexerErrorCode.NotFound, "Terminal inspection is unavailable.");
        return inspection;
    }
}
