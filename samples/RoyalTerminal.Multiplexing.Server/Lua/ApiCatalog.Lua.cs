// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed partial class ApiCatalog
{
    // Called once before the JSON automation configuration is schema-checked. No remote path selection.
    internal void ConfigureLua(WebApplicationBuilder builder)
    {
        string? file = builder.Configuration["ROYALTERMINAL_MUX_LUA_INIT"];
        if (string.IsNullOrEmpty(file)) return;
        string executable = builder.Configuration["ROYALTERMINAL_MUX_LUA_EXECUTABLE"] ?? "/usr/bin/lua5.1";
        var program = new TrustedLuaProgram(file, executable);
        builder.Services.AddHostedService(_ => new LuaStartup(program));
        Define("lua.actions", "List trusted server-side callbacks and their configuration digest.", [], LuaWire.Default.LuaManifest,
            (_, grant, _) => { ServerHttp.RequireAdministrator(grant); return Done(program.Manifest, LuaWire.Default.LuaManifest); });
        Define("lua.reload", "Validate and atomically replace callbacks from the startup-selected file. No source is accepted over HTTP.", [], LuaWire.Default.LuaManifest,
            async (_, grant, ct) => { ServerHttp.RequireAdministrator(grant); return Element(await program.ReloadAsync(ct), LuaWire.Default.LuaManifest); });
        Define("lua.invoke", "Execute one locally configured callback by name, passing structured arguments and optional workspace context.",
            ["name:string!", "arguments:object", "sessionId:string", "windowId:string", "blockId:string"], ServerJsonContext.Default.JsonElement,
            async (a, grant, ct) =>
            {
                ServerHttp.RequireAdministrator(grant);
                JsonElement args = a.Value.TryGetProperty("arguments", out _) ? a.Object("arguments") : EmptyArguments;
                ActionContext context = new(a.OptionalId("sessionId"), a.OptionalId("blockId"), a.OptionalId("windowId"));
                return await program.InvokeAsync(a.String("name"), context, args, (method, input, token) => InvokeAsync(method, input, grant, token), ct);
            });
    }

    private static readonly JsonElement EmptyArguments = JsonSerializer.SerializeToElement(new Dictionary<string,string>(), EmptyMapJson.Default.DictionaryStringString);

    private void Define(string name, string description, string[] fields, JsonTypeInfo result,
        Func<ApiArguments,AccessGrant,CancellationToken,ValueTask<JsonElement>> handler)
    {
        var input=Schema(name, fields);
        using var output=JsonDocument.Parse(result.GetJsonSchemaAsNode().ToJsonString());
        _methods.Add(name,new(new(name,description,input,output.RootElement.Clone(),ValidationNotes:"Administrator only. Trusted local Lua code; completed API effects are not rolled back or retried."),
            fields.Select(f=>f[..f.IndexOf(':')]).ToArray(),ApiInputSchemas.ForActions(input),handler));
    }

    private sealed class LuaStartup(TrustedLuaProgram program) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken) => _ = await program.ReloadAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string,string>))]
internal partial class EmptyMapJson : System.Text.Json.Serialization.JsonSerializerContext;
