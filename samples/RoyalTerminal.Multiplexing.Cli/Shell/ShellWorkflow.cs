// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class ShellWorkflow
{
    internal static async Task<int> RunAsync(string verb, string[] args, string? endpoint, string? token, CliCurrentContext context,
        HttpClient http, Stream stdin, TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (args is ["--help" or "-h"]) { await output.WriteLineAsync(ShellSyntax.Usage); return 0; }
        try
        {
            var command = ShellSyntax.Parse(verb,args);
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Set ROYALTERMINAL_MUX_TOKEN to an authorized credential.");
            var origin = new Uri(endpoint ?? "http://127.0.0.1:7331/",UriKind.Absolute);
            if (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback)) throw new ArgumentException("Use HTTPS for a remote workspace.");
            var api = new MultiplexingApiClient(http,origin,token);
            if (verb == "new") return await NewAsync(api,command,output,error,ct);
            var ws = await ShellWorkspace.OpenAsync(api,command.Get("--session") ?? context.Session,ct);
            var window = ws.Window(command.Get("--window"));
            var selected = command.Get("--block") ?? (verb is "wait" or "capture" ? command.Words.FirstOrDefault() : null);
            // The launch window in the environment is historical. A moved block is identified by ID, never by that old window.
            string? inherited = Guid.TryParse(context.Session,out var parent) && parent == ws.Id ? context.Block : null;
            if (verb == "wait" && selected is null && (window.HasValue || command.Has("--session")))
                return await WaitAsync(api,ws,window,null,command,output,ct);
            Guid? block = verb == "run" ? null : ws.Block(selected,window,inherited,verb == "split");
            if (command.Launch) return await LaunchAsync(api,ws,command,block,output,error,ct);
            if (verb == "wait") return await WaitAsync(api,ws,null,block,command,output,ct);
            var fields = new JsonObject { ["blockId"]=block!.Value.ToString("D") };
            JsonElement result;
            switch (verb)
            {
                case "capture":
                    fields["args"] = new JsonObject { ["format"]=command.Get("--format") ?? "text",["trim"]=command.Has("--trim"),["unwrap"]=command.Has("--unwrap") };
                    result = await ws.CallAsync("com.superlogical.terminal.format", fields,ct);
                    if (command.Has("--json")) await output.WriteLineAsync(result.GetRawText());
                    else await output.WriteAsync(result.GetProperty("content").GetString());
                    return 0;
                case "send":
                    byte[] bytes = command.Words.Length == 0 ? await ReadInputAsync(stdin,ct) : EncodeInput(command.Words[0]);
                    fields["args"] = new JsonObject { ["data"]=Convert.ToBase64String(bytes) };
                    result = await ws.CallAsync("com.superlogical.terminal.write",fields,ct); break;
                case "send-key":
                    fields["keys"] = Strings(command.Words);
                    result = await ws.CallAsync("terminal.send_keys",fields,ct); break;
                default: throw new ArgumentException("Unknown shell workflow.");
            }
            if (command.Has("--json")) await output.WriteLineAsync(result.GetRawText());
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { await error.WriteLineAsync("Interrupted. No stop or retry was sent."); return 130; }
        catch (OperationCanceledException) { await error.WriteLineAsync("Deadline expired. No stop or retry was sent."); return 1; }
        catch (MultiplexerAutomationException e) { await error.WriteLineAsync(e.Message); return e.Code is MultiplexerAutomationError.AmbiguousTarget or MultiplexerAutomationError.TargetNotFound ? 3 : 2; }
        catch (MultiplexingApiException e) { await error.WriteLineAsync($"{e.Message} {e.Code}"); return e.StatusCode == HttpStatusCode.NotFound && e.Code == "NotFound" ? 3 : 1; }
        catch (HttpRequestException e) { await error.WriteLineAsync(e.Message); return 4; }
        catch (Exception e) when (e is ArgumentException or FormatException) { await error.WriteLineAsync(e.Message); return 2; }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { await error.WriteLineAsync(e.Message); return 1; }
    }

    private static async Task<int> NewAsync(MultiplexingApiClient api, ShellSyntax c, TextWriter output, TextWriter error, CancellationToken ct)
    {
        string name = c.Words.FirstOrDefault() ?? "Workspace";
        var born = await ShellWorkspace.CallAsync(api,"session.create",new(){["label"]=name},ct);
        var ws = new ShellWorkspace(api,born);
        var wid = born.GetProperty("windows")[0].GetProperty("id").GetGuid();
        var block = new JsonObject{["label"]=c.Get("--label") ?? "Shell",["transport"]="pty",["windowId"]=wid.ToString("D"),["expectedRevision"]=ws.Revision};
        if (c.Get("--cwd") is {} cwd) block["workingDirectory"] = cwd;
        if (c.Tail.Length != 0) block["command"] = new JsonObject{["fileName"]=c.Tail[0],["arguments"]=Strings(c.Tail[1..])};
        JsonElement child;
        try { child = await ws.CallAsync("block.create",new(){["block"]=block},ct); }
        catch { await error.WriteLineAsync($"Session {ws.Id:D} was created. Inspect it before retrying; it was not deleted."); throw; }
        return await LaunchedAsync(api,ws,c,wid,child.GetProperty("id").GetGuid(),null,null,output,error,ct);
    }

    private static async Task<int> LaunchAsync(MultiplexingApiClient api, ShellWorkspace ws, ShellSyntax c, Guid? anchor, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var opts = new JsonObject(); if (c.Get("--cwd") is {} cwd) opts["cwd"] = cwd;
        if (c.Tail.Length != 0) opts["command"] = Strings(c.Tail);
        string label = c.Get("--label") ?? "Shell";
        var args = new JsonObject{["expectedRevision"]=ws.Revision,["label"]=label,["layout"]=new JsonObject{["flavor"]="com.superlogical.terminal.shell",["label"]=label,["options"]=opts}};
        if (anchor.HasValue)
        {
            var side = c.Get("--split") ?? "right"; bool first = side is "above" or "left";
            args["blockId"] = anchor.Value.ToString("D"); args["side"] = first ? "before" : "after";
            args["direction"] = side is "left" or "right" ? "horizontal" : "vertical";
            args["ratio"] = first ? c.Share : 1 - c.Share;
        }
        var result = await ws.CallAsync("session.launch_layout",args,ct);
        return await LaunchedAsync(api,ws,c,result.GetProperty("windowId").GetGuid(),result.GetProperty("blockIds")[0].GetGuid(),anchor,result.GetProperty("revision").GetInt64(),output,error,ct);
    }
    private static async Task<int> LaunchedAsync(MultiplexingApiClient api, ShellWorkspace ws, ShellSyntax c, Guid window, Guid block, Guid? anchor, long? revision, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var receipt = new JsonObject{["session_id"]=ws.Id.ToString("D"),["window_id"]=window.ToString("D"),["block_ids"]=new JsonArray(JsonValue.Create(block.ToString("D")))};
        if (anchor.HasValue) receipt["anchor_block_id"]=anchor.Value.ToString("D"); if (revision.HasValue) receipt["revision"]=revision.Value;
        if (c.Has("--wait"))
        {
            await error.WriteLineAsync($"Started {ws.Id:D}/{block:D}. Resume with wait -s {ws.Id:D} -b {block:D}.");
            return await WaitAsync(api,ws,null,block,c,output,ct);
        }
        await output.WriteLineAsync(c.Has("--json") ? receipt.ToJsonString() : (c.Verb == "new" ? ws.Id : block).ToString("D")); return 0;
    }
    private static async Task<int> WaitAsync(MultiplexingApiClient api, ShellWorkspace ws, Guid? window, Guid? block, ShellSyntax c, TextWriter output, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (c.Deadline is {} deadline) lifetime.CancelAfter(deadline);
        var stop = lifetime.Token; var view = ws.View;
        if (block is Guid bid)
        {
            // Fence first, then inspect the current descriptor. An exit between those reads is observed in at least one of them.
            JsonObject Read(long? after) { var q=new JsonObject{["blockId"]=bid.ToString("D")}; if(after.HasValue){q["after"]=after.Value;q["timeoutMs"]=20000;q["limit"]=128;} return q; }
            var page = await ws.CallAsync("terminal.events",Read(null),stop);
            view = await api.SendAsync(HttpMethod.Get,$"api/sessions/{ws.Id:D}",cancellationToken:stop);
            var child = view.GetProperty("blocks").EnumerateArray().FirstOrDefault(b => b.GetProperty("id").GetGuid() == bid);
            if (child.ValueKind == JsonValueKind.Undefined) throw new IOException("Block removed before exit status was observed.");
            int code;
            if (child.GetProperty("state").GetString() == "Exited") code = child.GetProperty("exitCode").GetInt32();
            else for (;;)
            {
                page = await ws.CallAsync("terminal.events",Read(page.GetProperty("nextSequence").GetInt64()),stop);
                var exit = page.GetProperty("events").EnumerateArray().FirstOrDefault(e => e.GetProperty("name").GetString() == "terminal.child_exited");
                if (exit.ValueKind != JsonValueKind.Undefined) {code = exit.GetProperty("exitCode").GetInt32(); break;}
                if (page.GetProperty("ended").GetBoolean()) throw new IOException("Terminal closed without an observed process exit.");
            }
            if(c.Has("--json")) await output.WriteLineAsync(new JsonObject{["session_id"]=ws.Id.ToString("D"),["block_id"]=bid.ToString("D"),["exit_code"]=code}.ToJsonString());
            return code;
        }
        while (!window.HasValue || view.GetProperty("windows").EnumerateArray().Any(w=>w.GetProperty("id").GetGuid()==window))
        {
            try { view = await ws.CallAsync("session.wait_change",new(){["afterRevision"]=view.GetProperty("revision").GetInt64(),["timeoutMs"]=20000},stop); }
            catch (MultiplexingApiException e) when(e.StatusCode==HttpStatusCode.NotFound && e.Code=="NotFound")
            {
                // The long-poll's asynchronous error can race revocation. Confirm absence through a newly authorized request.
                try { view = await api.SendAsync(HttpMethod.Get,$"api/sessions/{ws.Id:D}",cancellationToken:stop); }
                catch(MultiplexingApiException confirmation) when(confirmation.StatusCode==HttpStatusCode.NotFound && confirmation.Code=="NotFound") { break; }
            }
        }
        if(c.Has("--json")) await output.WriteLineAsync(new JsonObject{["session_id"]=ws.Id.ToString("D"),["window_id"]=window?.ToString("D"),["closed"]=true}.ToJsonString());
        return 0;
    }
    private const int InputLimit=65536;
    private static byte[] EncodeInput(string text)
    { if(Encoding.UTF8.GetByteCount(text)>InputLimit)throw new ArgumentException("Input exceeds 64 KiB; no bytes sent.");return Encoding.UTF8.GetBytes(text); }
    internal static async Task<byte[]> ReadInputAsync(Stream input,CancellationToken ct)
    {
        var buffer=new byte[InputLimit+1];int count=0;
        while(count<buffer.Length){int got=await input.ReadAsync(buffer.AsMemory(count),ct);if(got==0)return buffer[..count];count+=got;}
        throw new ArgumentException("Input exceeds 64 KiB; no bytes sent.");
    }
    private static JsonArray Strings(IEnumerable<string> values) {var a=new JsonArray();foreach(var s in values)a.Add(s);return a;}
}
