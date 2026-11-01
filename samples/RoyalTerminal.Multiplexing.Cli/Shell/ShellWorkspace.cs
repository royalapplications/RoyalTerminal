// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Nodes;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

/// <summary>Resolve nested selectors against one authorized snapshot. A stale ID must never retarget input.</summary>
internal sealed class ShellWorkspace(MultiplexingApiClient api, JsonElement view)
{
    internal Guid Id { get; } = view.GetProperty("id").GetGuid();
    internal long Revision => view.GetProperty("revision").GetInt64();
    internal JsonElement View => view;
    internal static async Task<ShellWorkspace> OpenAsync(MultiplexingApiClient api, string? selector, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(selector)) throw new ArgumentException("Select a session with -s or ROYALTERMINAL_MUX_SESSION.");
        Guid id = await new CliTargetResolver(api).SessionAsync(selector, ct);
        var snapshot = await api.SendAsync(HttpMethod.Get, $"api/sessions/{id:D}", cancellationToken:ct);
        if (snapshot.GetProperty("id").GetGuid() != id) throw new InvalidDataException("Unexpected session identity.");
        return new(api, snapshot);
    }
    internal Guid? Window(string? selector) => selector is null ? null : Resolve(selector,view.GetProperty("windows").EnumerateArray());
    internal Guid Block(string? selector, Guid? window, string? inherited, bool tiledOnly)
    {
        var all = view.GetProperty("blocks").EnumerateArray();
        var scoped = all.Where(b => !window.HasValue || b.GetProperty("windowId").GetGuid() == window);
        if (selector is not null) return Resolve(selector,scoped);
        if (!string.IsNullOrEmpty(inherited))
        {
            if (!Guid.TryParse(inherited,out var id) || id==Guid.Empty) throw new ArgumentException("Invalid inherited block ID. Supply -b explicitly.");
            var owned = all.FirstOrDefault(b => b.GetProperty("id").GetGuid()==id);
            if (owned.ValueKind==JsonValueKind.Undefined) throw Missing("The inherited block no longer exists. Supply -b explicitly.");
            if (!window.HasValue || owned.GetProperty("windowId").GetGuid()==window) return id;
            // An explicit different window overrides a valid inherited block; require an unambiguous candidate there.
        }
        if (tiledOnly) scoped = scoped.Where(b=>b.GetProperty("placement").GetString()=="Tiled");
        Guid[] candidates = scoped.Select(b=>b.GetProperty("id").GetGuid()).Take(2).ToArray();
        return candidates.Length switch { 1=>candidates[0],0=>throw Missing("No eligible block in this scope."),
            _=>throw new MultiplexerAutomationException(MultiplexerAutomationError.AmbiguousTarget,"Select a block with -b; more than one matches this scope.") };
    }
    private static Guid Resolve(string selector,IEnumerable<JsonElement> items)
    {
        if (Guid.TryParse(selector,out var id))
        {
            if (id!=Guid.Empty && items.Any(x=>x.GetProperty("id").GetGuid()==id)) return id;
            throw Missing("The selected identity does not exist in this scope.");
        }
        var candidates = items.Select(x=>new MultiplexerTarget(x.GetProperty("id").GetGuid().ToString("D"),x.GetProperty("label").GetString()!)).ToArray();
        return Guid.Parse(MultiplexerTargetResolver.Resolve(selector,candidates).Id);
    }
    private static MultiplexerAutomationException Missing(string message)=>new(MultiplexerAutomationError.TargetNotFound,message);
    internal ValueTask<JsonElement> CallAsync(string method, JsonObject fields, CancellationToken ct)
    { fields.Add("sessionId",Id.ToString("D")); return CallAsync(api,method,fields,ct); }
    internal static async ValueTask<JsonElement> CallAsync(MultiplexingApiClient api,string method,JsonObject fields,CancellationToken ct)
    { using var json=JsonDocument.Parse(fields.ToJsonString()); return await api.CallAsync(method,json.RootElement,ct); }
}
