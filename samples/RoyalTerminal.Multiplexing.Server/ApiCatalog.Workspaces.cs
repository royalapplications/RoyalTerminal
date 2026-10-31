// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed partial class ApiCatalog
{
    private void RegisterWorkspaceMethods()
    {
        RegisterInspectionMethods();
        Add("block.place", "Move without restarting. kind: tiled, floating or unplaced. Floating coordinates use ten-thousandths of the home window.",
            ["sessionId:string!", "blockId:string!", "windowId:string!", "kind:string!", "relativeTo:string", "orientation:string", "ratio:number", "before:boolean", "bounds:object", "expectedRevision:integer"], Place);
        Add("block.swap", "Exchange two placements in one session, including across windows.",
            ["sessionId:string!", "firstId:string!", "secondId:string!", "expectedRevision:integer"], (a, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            return Done(_workspaces.Broker.SwapBlocks(a.Id("sessionId"), a.Id("firstId"), a.Id("secondId"), a.Revision()), ServerJsonContext.Default.MultiplexerSessionDescriptor);
        });
        Add("session.move_window", "Move a window to a one-based display position.",
            ["sessionId:string!", "windowId:string!", "position:integer!", "expectedRevision:integer"], (a, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            return Done(_workspaces.Broker.ReorderWindow(a.Id("sessionId"), a.Id("windowId"), a.Integer("position", 0), a.Revision()), ServerJsonContext.Default.MultiplexerSessionDescriptor);
        });
        Add("session.close_window", "Close a window without stopping processes. Nonempty windows require preserveIn; the last window cannot be closed.",
            ["sessionId:string!", "windowId:string!", "preserveIn:string", "expectedRevision:integer"], (a, grant, _) =>
        {
            ServerHttp.RequireAdministrator(grant);
            return Done(_workspaces.Broker.CloseWindow(a.Id("sessionId"), a.Id("windowId"), a.OptionalId("preserveIn"), a.Revision()), ServerJsonContext.Default.MultiplexerSessionDescriptor);
        });
        Add("session.wait_change", "Long-poll structural changes. Returns a current snapshot on timeout; equal revision means no change. Does not observe output or process exit.",
            ["sessionId:string!", "afterRevision:integer!", "timeoutMs:integer"], WaitForChangeAsync);
    }

    private ValueTask<JsonElement> Place(ApiArguments a, AccessGrant grant, CancellationToken cancellationToken)
    {
        ServerHttp.RequireAdministrator(grant); cancellationToken.ThrowIfCancellationRequested();
        MultiplexerPaneKind kind = a.String("kind") switch { "tiled" => MultiplexerPaneKind.Tiled, "floating" => MultiplexerPaneKind.Floating, "unplaced" => MultiplexerPaneKind.Unplaced, _ => throw new ArgumentException("Unknown placement kind.") };
        MultiplexerSplitOrientation orientation = a.String("orientation", "horizontal") switch { "horizontal" => MultiplexerSplitOrientation.Horizontal, "vertical" => MultiplexerSplitOrientation.Vertical, _ => throw new ArgumentException("Unknown split orientation.") };
        bool before = a.Value.TryGetProperty("before", out JsonElement item) && (item.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new ArgumentException("before must be a boolean.") });
        MultiplexerFloatBounds? bounds = null;
        if (a.Value.TryGetProperty("bounds", out _))
        {
            ApiArguments r = new(a.Object("bounds")); r.Validate("x", "y", "width", "height");
            bounds = new(r.Integer("x", -1), r.Integer("y", -1), r.Integer("width", -1), r.Integer("height", -1));
        }
        return Done(_workspaces.Broker.PlaceBlock(a.Id("sessionId"), a.Id("blockId"), a.Id("windowId"), kind,
            a.OptionalId("relativeTo"), orientation, a.Number("ratio", 0.5), before, bounds, a.Revision()), ServerJsonContext.Default.MultiplexerSessionDescriptor);
    }

    private async ValueTask<JsonElement> WaitForChangeAsync(ApiArguments a, AccessGrant grant, CancellationToken token)
    {
        Guid id = Session(a, grant);
        if (!a.Value.TryGetProperty("afterRevision", out JsonElement revision) || revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out long after) || after < 0)
            throw new ArgumentException("afterRevision must be a nonnegative 64-bit integer.");
        int ms = a.Integer("timeoutMs", 15000);
        if (ms is < 1 or > 25000) throw new ArgumentOutOfRangeException(nameof(a), "timeoutMs must be between 1 and 25000.");
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ms);
        MultiplexerSessionDescriptor result;
        try { result = await _workspaces.Broker.WaitForSessionChangeAsync(id, after, deadline.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested) { result = _workspaces.Broker.GetSession(id); }
        token.ThrowIfCancellationRequested();
        if (!_tokens.IsActive(grant)) throw new ApiException(403, "expired", "The capability expired or was revoked while waiting.");
        return Element(result, ServerJsonContext.Default.MultiplexerSessionDescriptor);
    }

    private static JsonElement? WorkspaceExample(string name)
    {
        const string session = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", window = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", block = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
        string? example = name switch
        {
            "block.place" => $$$"""{"sessionId":"{{{session}}}","windowId":"{{{window}}}","blockId":"{{{block}}}","kind":"floating","bounds":{"x":1000,"y":1500,"width":6000,"height":5000}}""",
            "block.swap" => $$"""{"sessionId":"{{session}}","firstId":"{{block}}","secondId":"dddddddd-dddd-4ddd-8ddd-dddddddddddd"}""",
            "session.move_window" => $$"""{"sessionId":"{{session}}","windowId":"{{window}}","position":1}""",
            "session.close_window" => $$"""{"sessionId":"{{session}}","windowId":"{{window}}"}""",
            "session.wait_change" => $$"""{"sessionId":"{{session}}","afterRevision":1,"timeoutMs":15000}""",
            _ => null,
        };
        if (example is null) return null;
        using JsonDocument doc = JsonDocument.Parse(example); return doc.RootElement.Clone();
    }

    private static void WriteWorkspaceConstraint(Utf8JsonWriter w, string method, string property)
    {
        if (method == "block.place" && property is "kind" or "orientation")
        {
            w.WriteStartArray("enum");
            string[] values = property == "kind" ? ["tiled", "floating", "unplaced"] : ["horizontal", "vertical"];
            foreach (string value in values) w.WriteStringValue(value);
            w.WriteEndArray();
        }
        if (method == "block.place" && property == "bounds")
        {
            string[] keys = ["x", "y", "width", "height"];
            w.WriteBoolean("additionalProperties", false); w.WriteStartArray("required"); foreach (string key in keys) w.WriteStringValue(key); w.WriteEndArray();
            w.WriteStartObject("properties"); foreach (string key in keys) { w.WriteStartObject(key); w.WriteString("type", "integer"); w.WriteNumber("minimum", key is "width" or "height" ? 1 : 0); w.WriteNumber("maximum", 10000); w.WriteEndObject(); } w.WriteEndObject();
        }
        if (method == "block.place" && property == "ratio") { w.WriteNumber("exclusiveMinimum", 0); w.WriteNumber("exclusiveMaximum", 1); }
        if (method == "session.wait_change" && property == "afterRevision" || method == "terminal.events" && property == "after") w.WriteNumber("minimum", 0);
        if (method == "session.wait_change" && property == "timeoutMs") { w.WriteNumber("minimum", 1); w.WriteNumber("maximum", 25000); }
        if (method == "terminal.events" && property == "timeoutMs") { w.WriteNumber("minimum", 0); w.WriteNumber("maximum", 25000); }
        if (method == "terminal.events" && property == "limit") { w.WriteNumber("minimum", 1); w.WriteNumber("maximum", 128); }
        if (method == "session.move_window" && property == "position") w.WriteNumber("minimum", 1);
    }
}
