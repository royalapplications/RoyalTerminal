// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

/// <summary>One explicit method registry drives discovery, HTTP, CLI, and custom actions.</summary>
internal sealed partial class ApiCatalog
{
    private sealed record Entry(MethodDescription Description, string[] Properties, JsonElement ActionSchema,
        Func<ApiArguments, AccessGrant, CancellationToken, ValueTask<JsonElement>> Invoke);
    private readonly Dictionary<string, Entry> _methods = new(StringComparer.Ordinal);
    private readonly WorkspaceOperations _workspaces;
    private readonly AccessTokens _tokens;
    private readonly ClientRegistry _clients;

    public ApiCatalog(WorkspaceOperations workspaces, AccessTokens tokens, ClientRegistry clients)
    {
        _workspaces = workspaces;
        _tokens = tokens;
        _clients = clients;
        RegisterMethods();
        RegisterWorkspaceMethods();
    }

    public MethodList Describe() => new(_methods.Values.Select(x => x.Description).ToArray());
    public IReadOnlyList<MultiplexerActionMethod> DescribeActionMethods() => _methods.Values
        .Select(x => new MultiplexerActionMethod(x.Description.Name, x.ActionSchema)).ToArray();
    public MethodDescription Describe(string name) => _methods.TryGetValue(name, out Entry? entry)
        ? entry.Description : throw new ApiException(404, "method_not_found", "Unknown API method.");

    public ValueTask<JsonElement> InvokeAsync(string method, JsonElement arguments, AccessGrant grant, CancellationToken cancellationToken)
    {
        if (!_tokens.IsActive(grant)) throw new ApiException(403, "expired", "The capability expired or was revoked.");
        if (!_methods.TryGetValue(method, out Entry? entry)) throw new ApiException(404, "method_not_found", $"Unknown method '{method}'.");
        ApiArguments args = new(arguments);
        args.Validate(entry.Properties);
        return entry.Invoke(args, grant, cancellationToken);
    }

    public ScreenCapture Capture(Guid sessionId, Guid blockId, string format = "text")
    {
        var inspection = _workspaces.Inspection(sessionId, blockId);
        TerminalSnapshotExportFormat kind = format switch
        {
            "text" => TerminalSnapshotExportFormat.PlainText,
            "html" => TerminalSnapshotExportFormat.Html,
            "vt" => TerminalSnapshotExportFormat.StyledVt,
            _ => throw new ArgumentException("Capture format must be text, html, or vt."),
        };
        string content = inspection.Capture(kind);
        if (content.Length > 4 * 1024 * 1024) throw new ApiException(413, "capture_limit", "The capture exceeds the response budget.");
        return new(format, content, inspection.Title, inspection.WorkingDirectory);
    }

    private void RegisterMethods()
    {
        Add("server.version", "Describe this independent experimental protocol and its durability boundary.", [],
            (_, _, _) => Done(new ServerVersion("RoyalTerminal Multiplexer", "0.2.0-experimental", 1,
                "Processes survive client disconnects while this server remains running."), ServerJsonContext.Default.ServerVersion));
        Add("session.list", "List sessions visible to this capability.", [], (_, grant, _) =>
            Done(new SessionList(_workspaces.Broker.ListSessions().Where(x => grant.CanRead(x.Id)).ToArray()), ServerJsonContext.Default.SessionList));
        Add("session.create", "Create a session with an empty window.", ["label:string!"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            return Done(_workspaces.Broker.CreateSession(args.String("label")), ServerJsonContext.Default.MultiplexerSessionDescriptor);
        });
        Add("session.view", "Read a session, revision, windows, layouts, and block lifecycle.", ["sessionId:string!"], (args, grant, _) =>
            Done(_workspaces.Broker.GetSession(Session(args, grant)), ServerJsonContext.Default.MultiplexerSessionDescriptor));
        Add("session.set_label", "Rename a session with optional optimistic revision checking.",
            ["sessionId:string!", "label:string!", "expectedRevision:integer"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            return Done(_workspaces.Broker.RenameSession(args.Id("sessionId"), args.String("label"), args.Revision()), ServerJsonContext.Default.MultiplexerSessionDescriptor);
        });
        Add("session.close", "Explicitly terminate every process and remove the session.", ["sessionId:string!"], async (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant); await _workspaces.CloseSessionAsync(args.Id("sessionId")); return Success();
        });
        Add("session.new_window", "Create an empty tab in a session.", ["sessionId:string!", "label:string!", "expectedRevision:integer"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            return Done(_workspaces.Broker.CreateWindow(args.Id("sessionId"), args.String("label"), args.Revision()), ServerJsonContext.Default.MultiplexerWindowDescriptor);
        });
        Add("session.set_window_label", "Rename a window.", ["sessionId:string!", "windowId:string!", "label:string!", "expectedRevision:integer"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            return Done(_workspaces.Broker.RenameWindow(args.Id("sessionId"), args.Id("windowId"), args.String("label"), args.Revision()), ServerJsonContext.Default.MultiplexerWindowDescriptor);
        });
        Add("session.set_block_label", "Rename a block without replacing its process.", ["sessionId:string!", "blockId:string!", "label:string!", "expectedRevision:integer"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            return Done(_workspaces.Broker.RenameBlock(args.Id("sessionId"), args.Id("blockId"), args.String("label"), args.Revision()), ServerJsonContext.Default.MultiplexerBlockDescriptor);
        });
        Add("session.set_layout", "Replace a binary split tree containing each tiled block in this window exactly once.",
            ["sessionId:string!", "windowId:string!", "layout:object!", "expectedRevision:integer"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            MultiplexerLayoutNode layout = JsonSerializer.Deserialize(args.Object("layout"), ServerJsonContext.Default.MultiplexerLayoutNode) ?? throw new ArgumentException("A layout is required.");
            return Done(_workspaces.Broker.SetWindowLayout(args.Id("sessionId"), args.Id("windowId"), layout, args.Revision()), ServerJsonContext.Default.MultiplexerWindowDescriptor);
        });
        Add("block.create", "Start a PTY or pipe process in a window, optionally splitting an existing block.", ["sessionId:string!", "block:object!"], async (args, grant, cancellationToken) =>
        {
            ServerHttp.RequireAdministrator(grant);
            NewBlock request = JsonSerializer.Deserialize(args.Object("block"), ServerJsonContext.Default.NewBlock) ?? throw new ArgumentException("A block definition is required.");
            return Element(await _workspaces.CreateBlockAsync(args.Id("sessionId"), request, cancellationToken), ServerJsonContext.Default.MultiplexerBlockDescriptor);
        });
        Add("block.close", "Terminate one process and remove its placement.", ["sessionId:string!", "blockId:string!"], async (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant); await _workspaces.CloseBlockAsync(args.Id("sessionId"), args.Id("blockId")); return Success();
        });
        Add("block.input", "Send base64-encoded input once, acquiring temporary exclusive control.", ["sessionId:string!", "blockId:string!", "data:string!"], (args, grant, _) =>
        {
            Guid sessionId = Session(args, grant);
            if (!grant.CanControl) throw new ApiException(403, "forbidden", "This capability is read only.");
            _workspaces.Input(sessionId, args.Id("blockId"), args.String("data")); return new(Success());
        });
        Add("terminal.format", "Capture text, HTML, or styled VT from the server's authoritative terminal.", ["sessionId:string!", "blockId:string!", "format:string"], (args, grant, _) =>
            Done(Capture(Session(args, grant), args.Id("blockId"), args.String("format", "text")), ServerJsonContext.Default.ScreenCapture));
        Add("terminal.status", "Read bounded OSC 7501 program-status records.", ["sessionId:string!", "blockId:string!"], (args, grant, _) =>
            Done(_workspaces.Inspection(Session(args, grant), args.Id("blockId")).ProgramStatuses.ToArray(), ServerJsonContext.Default.TerminalProgramStatusArray));
        Add("terminal.title", "Read the terminal-reported title as untrusted plain text.", ["sessionId:string!", "blockId:string!"], (args, grant, _) =>
            Done(new TerminalTextValue(_workspaces.Inspection(Session(args, grant), args.Id("blockId")).Title), ServerJsonContext.Default.TerminalTextValue));
        Add("terminal.pwd", "Read the terminal-reported working-directory URI without accessing that path.", ["sessionId:string!", "blockId:string!"], (args, grant, _) =>
            Done(new TerminalTextValue(_workspaces.Inspection(Session(args, grant), args.Id("blockId")).WorkingDirectory), ServerJsonContext.Default.TerminalTextValue));
        Add("client.list", "List currently attached clients visible to this capability.", [], (_, grant, _) => Done(_clients.List(grant), ServerJsonContext.Default.ClientList));
        Add("client.disconnect", "Detach a client without terminating its process.", ["clientId:string!"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant); return Done(new ApiSuccess(_clients.Disconnect(args.Id("clientId"))), ServerJsonContext.Default.ApiSuccess);
        });
        Add("share.create", "Mint a scoped, expiring observer or controller capability. Token is returned once.", ["sessionId:string!", "role:string", "lifetimeMinutes:integer"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant); Guid id = args.Id("sessionId"); _workspaces.Broker.GetSession(id);
            return Done(_tokens.Create(id, new(args.String("role", "observer"), args.Integer("lifetimeMinutes", 60))), ServerJsonContext.Default.ShareCreated);
        });
        Add("share.list", "List capability metadata without disclosing tokens.", ["sessionId:string!"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant); return Done(_tokens.List(args.Id("sessionId")), ServerJsonContext.Default.ShareList);
        });
        Add("share.revoke", "Revoke a capability and detach its live clients.", ["sessionId:string!", "shareId:string!"], (args, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant); Guid id = args.Id("shareId"); bool revoked = _tokens.Revoke(args.Id("sessionId"), id);
            if (revoked) _clients.Revoke(id); return Done(new ApiSuccess(revoked), ServerJsonContext.Default.ApiSuccess);
        });
    }

    private static Guid Session(ApiArguments arguments, AccessGrant grant)
    {
        Guid id = arguments.Id("sessionId"); ServerHttp.RequireSession(grant, id); return id;
    }

    private void Add(string name, string description, string[] fields,
        Func<ApiArguments, AccessGrant, CancellationToken, ValueTask<JsonElement>> handler)
    {
        string[] names = fields.Select(x => x[..x.IndexOf(':')]).ToArray();
        JsonElement input = Schema(name, fields);
        _methods.Add(name, new(new(name, description, input, OutputSchema(name), RequestExample: WorkspaceExample(name),
            ValidationNotes: "Unknown and duplicate top-level arguments are rejected. Nested contracts reject unknown fields. Session IDs are UUIDs. Layout tree invariants and process options are validated by the server. Action preflight uses a bounded schema projection; recursive descendants are validated during invocation. Mutations require an administrator except block.input."), names, ApiInputSchemas.ForActions(input), handler));
    }

    private static JsonElement OutputSchema(string method)
    {
        JsonTypeInfo type = method switch
        {
            "server.version" => ServerJsonContext.Default.ServerVersion,
            "session.list" => ServerJsonContext.Default.SessionList,
            "session.create" or "session.view" or "session.set_label" or "block.place" or "block.swap" or "session.move_window" or "session.close_window" or "session.wait_change" => ServerJsonContext.Default.MultiplexerSessionDescriptor,
            "session.new_window" or "session.set_window_label" or "session.set_layout" => ServerJsonContext.Default.MultiplexerWindowDescriptor,
            "session.set_block_label" or "block.create" => ServerJsonContext.Default.MultiplexerBlockDescriptor,
            "terminal.format" => ServerJsonContext.Default.ScreenCapture,
            "terminal.status" => ServerJsonContext.Default.TerminalProgramStatusArray,
            "terminal.title" or "terminal.pwd" => ServerJsonContext.Default.TerminalTextValue,
            "client.list" => ServerJsonContext.Default.ClientList,
            "share.create" => ServerJsonContext.Default.ShareCreated,
            "share.list" => ServerJsonContext.Default.ShareList,
            _ => ServerJsonContext.Default.ApiSuccess,
        };
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer)) type.GetJsonSchemaAsNode().WriteTo(writer);
        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory); return document.RootElement.Clone();
    }

    private static JsonElement Schema(string method, string[] fields)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject(); writer.WriteString("type", "object"); writer.WriteBoolean("additionalProperties", false);
            writer.WriteStartObject("properties");
            foreach (string field in fields)
            {
                int colon = field.IndexOf(':'); string property = field[..colon];
                if (ApiInputSchemas.Nested(method, property) is { } nested)
                { writer.WritePropertyName(property); nested.WriteTo(writer); }
                else
                {
                    writer.WriteStartObject(property);
                    if (property == "expectedRevision")
                    {
                        writer.WriteStartArray("type"); writer.WriteStringValue("integer"); writer.WriteStringValue("null"); writer.WriteEndArray(); writer.WriteNumber("minimum", 0);
                    }
                    else writer.WriteString("type", field[(colon + 1)..].TrimEnd('!'));
                    WriteWorkspaceConstraint(writer, method, property);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndObject(); writer.WriteStartArray("required");
            foreach (string field in fields) if (field.EndsWith('!')) writer.WriteStringValue(field[..field.IndexOf(':')]);
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory); return document.RootElement.Clone();
    }

    private static ValueTask<JsonElement> Done<T>(T value, JsonTypeInfo<T> type) => new(Element(value, type));
    private static JsonElement Element<T>(T value, JsonTypeInfo<T> type) => JsonSerializer.SerializeToElement(value, type);
    private static JsonElement Success() => Element(new ApiSuccess(), ServerJsonContext.Default.ApiSuccess);
}
