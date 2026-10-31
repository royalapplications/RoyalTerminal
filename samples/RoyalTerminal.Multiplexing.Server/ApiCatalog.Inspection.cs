// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record AttentionCounts(int Idle = 0, int Working = 0, int Done = 0, int Blocked = 0, int Error = 0,
    int Permission = 0, int Question = 0, int Auth = 0)
{
    internal static AttentionCounts From(IReadOnlyList<TerminalProgramStatus> statuses)
    {
        int idle = 0, working = 0, done = 0, blocked = 0, error = 0, permission = 0, question = 0, auth = 0;
        foreach (var s in statuses)
        {
            switch (s.State)
            {
                case TerminalProgramStatusState.Idle: idle++; break;
                case TerminalProgramStatusState.Working: working++; break;
                case TerminalProgramStatusState.Done: done++; break;
                case TerminalProgramStatusState.Error: error++; break;
                case TerminalProgramStatusState.Blocked:
                    blocked++;
                    if (s.Kind == TerminalProgramStatusKind.Permission) permission++;
                    else if (s.Kind == TerminalProgramStatusKind.Question) question++;
                    else if (s.Kind == TerminalProgramStatusKind.Auth) auth++;
                    break;
            }
        }
        return new(idle, working, done, blocked, error, permission, question, auth);
    }
    internal AttentionCounts Plus(AttentionCounts b) => new(Idle + b.Idle, Working + b.Working, Done + b.Done,
        Blocked + b.Blocked, Error + b.Error, Permission + b.Permission, Question + b.Question, Auth + b.Auth);
}
internal sealed record BlockAttention(Guid BlockId, string Label, AttentionCounts? Counts);
internal sealed record WindowAttention(Guid WindowId, string Label, AttentionCounts Counts, IReadOnlyList<BlockAttention> Blocks);
internal sealed record SessionAttention(Guid SessionId, long LayoutRevision, int UnavailableBlocks, AttentionCounts Counts, IReadOnlyList<WindowAttention> Windows);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SessionAttention))]
[JsonSerializable(typeof(TerminalMetadataPage))]
internal sealed partial class WorkspaceInspectionJsonContext : JsonSerializerContext;

internal sealed partial class ApiCatalog
{
    private void RegisterInspectionMethods()
    {
        RegisterReader("terminal.events", "Read ordered bell, title, program status and child-exit events. Omit after for a start-at-now cursor. Continue from nextSequence.",
            ["sessionId:string!", "blockId:string!", "after:integer", "limit:integer", "timeoutMs:integer"], WorkspaceInspectionJsonContext.Default.TerminalMetadataPage, ReadMetadataAsync);
        RegisterReader("session.attention", "Count program-status records by window and block, including hidden terminals. A null counts value means inspection was unavailable, not idle.",
            ["sessionId:string!"], WorkspaceInspectionJsonContext.Default.SessionAttention, ReadAttentionAsync);
    }
    private void RegisterReader<T>(string name, string description, string[] fields, JsonTypeInfo<T> result,
        Func<ApiArguments, AccessGrant, CancellationToken, ValueTask<JsonElement>> handler)
    {
        var input = Schema(name, fields);
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer)) result.GetJsonSchemaAsNode().WriteTo(writer);
        using JsonDocument schema = JsonDocument.Parse(buffer.WrittenMemory);
        string[] names = fields.Select(f => f[..f.IndexOf(':')]).ToArray();
        _methods.Add(name, new(new(name, description, input, schema.RootElement.Clone(), ValidationNotes: "Session-scoped read. Terminal metadata cursors are unrelated to VT replay cursors and structural revisions."), names, ApiInputSchemas.ForActions(input), handler));
    }
    private async ValueTask<JsonElement> ReadMetadataAsync(ApiArguments args, AccessGrant grant, CancellationToken token)
    {
        Guid sessionId = Session(args, grant);
        long? after = null;
        if (args.Value.TryGetProperty("after", out JsonElement value))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long cursor) || cursor < 0) throw new ArgumentException("after must be a nonnegative 64-bit integer.");
            after = cursor;
        }
        var feed = _workspaces.Inspection(sessionId, args.Id("blockId")).Metadata;
        var page = await feed.ReadAsync(after, args.Integer("limit", 64), args.Integer("timeoutMs", 0), token);
        token.ThrowIfCancellationRequested();
        if (!_tokens.IsActive(grant)) throw new ApiException(403, "expired", "Access expired or was revoked while reading terminal events.");
        return Element(page, WorkspaceInspectionJsonContext.Default.TerminalMetadataPage);
    }
    private ValueTask<JsonElement> ReadAttentionAsync(ApiArguments args, AccessGrant grant, CancellationToken token)
    {
        var session = _workspaces.Broker.GetSession(Session(args, grant));
        var grouped = session.Blocks.ToLookup(b => b.WindowId);
        List<WindowAttention> windows = []; AttentionCounts total = new(); int unavailable = 0;
        foreach (var window in session.Windows)
        {
            List<BlockAttention> blocks = []; AttentionCounts sum = new();
            foreach (var b in grouped[window.Id])
            {
                token.ThrowIfCancellationRequested(); AttentionCounts? counts = null;
                try { counts = AttentionCounts.From(_workspaces.Inspection(session.Id, b.Id).ProgramStatuses); }
                catch (ObjectDisposedException) { }
                catch (MultiplexerException e) when (e.Code == MultiplexerErrorCode.NotFound) { }
                if (counts is null) unavailable++; else sum = sum.Plus(counts);
                blocks.Add(new(b.Id, b.Label, counts));
            }
            total = total.Plus(sum); windows.Add(new(window.Id, window.Label, sum, blocks));
        }
        if (!_tokens.IsActive(grant)) throw new ApiException(403, "expired", "Access expired or was revoked.");
        return Done(new SessionAttention(session.Id, session.Revision, unavailable, total, windows), WorkspaceInspectionJsonContext.Default.SessionAttention);
    }
}
