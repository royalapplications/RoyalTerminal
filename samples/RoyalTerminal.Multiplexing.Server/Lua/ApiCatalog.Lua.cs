// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
namespace RoyalTerminal.Multiplexing.Server;
internal sealed partial class ApiCatalog
{
    internal void ConfigureLua(WebApplicationBuilder builder)
    {
        RegisterTerminalCommands();
        Define("session.launch_layout","Launch a validated PTY layout in a new window or beside blockId.",["sessionId:string!","layout:object!","label:string","blockId:string","direction:string","side:string","ratio:number","expectedRevision:integer"],LayoutTemplateJson.Default.LayoutReceipt,LaunchLayout);
        string? file=builder.Configuration["ROYALTERMINAL_MUX_LUA_INIT"];
        TrustedLuaProgram? program=string.IsNullOrEmpty(file)?null:new TrustedLuaProgram(file,builder.Configuration["ROYALTERMINAL_MUX_LUA_EXECUTABLE"]??"/usr/bin/lua5.1")
        {
            Published=(m,changed)=>_workspaces.Broker.Activity.RecordConfiguration(m.Generation,changed?m.Peers.Hash:null)
        };
        Define("server.hosts","List configured peer labels, ordered endpoints and stable IDs. Never includes local token-file pins.",[],PeerWire.Default.WorkspacePeers,(_,grant,_)=>
        {ServerHttp.RequireAdministrator(grant);return Done(program?.Manifest.Peers??WorkspacePeers.Empty,PeerWire.Default.WorkspacePeers);});
        if(program is null)return;
        var router=new WorkspacePeerRouter(builder.Configuration["ROYALTERMINAL_MUX_PEER_PINS"]);
        builder.Services.AddSingleton<WorkspacePeerRouter>(_=>router);
        builder.Services.AddHostedService(sp=>new LuaStartup(program,sp.GetRequiredService<WorkspacePeerRouter>()));
        Define("server.call_host","Call a configured peer once using a separately pinned credential, never the caller's token.",["id:string!","method:string!","arguments:object!"],ServerJsonContext.Default.JsonElement,(a,g,t)=>
        {ServerHttp.RequireAdministrator(g);return router.CallAsync(program.Manifest.Peers,a.String("id"),a.String("method"),a.Object("arguments"),t);});
        Define("lua.actions","Read the active action and keyboard declaration generation.",[],LuaWire.Default.LuaManifest,(_,grant,_)=>{ServerHttp.RequireAdministrator(grant);return Done(program.Manifest,LuaWire.Default.LuaManifest);});
        Define("lua.check","Validate without publishing any host, action or keymap change.",[],LuaWire.Default.LuaManifest,async(_,grant,ct)=>{ServerHttp.RequireAdministrator(grant);return Element(await program.CheckAsync(ct),LuaWire.Default.LuaManifest);});
        Define("lua.reload","Atomically publish the validated operator configuration.",[],LuaWire.Default.LuaManifest,async(_,grant,ct)=>{ServerHttp.RequireAdministrator(grant);return Element(await program.ReloadAsync(ct),LuaWire.Default.LuaManifest);});
        Define("lua.invoke","Invoke a configured action, optionally conditional on generation.",["name:string!","arguments:object","generation:string","sessionId:string","windowId:string","blockId:string"],ServerJsonContext.Default.JsonElement,async(a,grant,ct)=>{
            ServerHttp.RequireAdministrator(grant);var args=a.Value.TryGetProperty("arguments",out _)?a.Object("arguments"):EmptyArguments;var gen=a.Value.TryGetProperty("generation",out _)?a.String("generation"):null;
            return await program.InvokeAsync(a.String("name"),Context(a),args,(m,i,t)=>InvokeAsync(m,i,grant,t),ct,gen);});
        Define("lua.key","Invoke an anonymous keybinding callback against its originating generation.",["id:integer!","generation:string!","sessionId:string","windowId:string","blockId:string"],ServerJsonContext.Default.JsonElement,async(a,grant,ct)=>{
            ServerHttp.RequireAdministrator(grant);return await program.InvokeAsync(a.Integer("id",-1).ToString(CultureInfo.InvariantCulture),Context(a),EmptyArguments,(m,i,t)=>InvokeAsync(m,i,grant,t),ct,a.String("generation"),binding:true);});
    }
    private static ActionContext Context(ApiArguments a)=>new(a.OptionalId("sessionId"),a.OptionalId("blockId"),a.OptionalId("windowId"));
    private static readonly JsonElement EmptyArguments=JsonSerializer.SerializeToElement(new Dictionary<string,string>(),EmptyMapJson.Default.DictionaryStringString);
    private void Define(string name,string description,string[] fields,JsonTypeInfo result,Func<ApiArguments,AccessGrant,CancellationToken,ValueTask<JsonElement>> handler,string notes="Administrator only. External effects are not rolled back or retried.")
    {
        var input=Schema(name,fields);using var output=JsonDocument.Parse(result.GetJsonSchemaAsNode().ToJsonString());
        _methods.Add(name,new(new(name,description,input,output.RootElement.Clone(),ValidationNotes:notes),fields.Select(f=>f[..f.IndexOf(':')]).ToArray(),ApiInputSchemas.ForActions(input),handler));
    }
    // The container owns the router. Resolving it here ensures deterministic disposal, including startup failure.
    private sealed class LuaStartup(TrustedLuaProgram program,WorkspacePeerRouter router):IHostedService
    {
        public async Task StartAsync(CancellationToken ct){GC.KeepAlive(router);_=await program.ReloadAsync(ct);}
        public Task StopAsync(CancellationToken ct)=>Task.CompletedTask;
    }
}
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string,string>))]
internal partial class EmptyMapJson:System.Text.Json.Serialization.JsonSerializerContext;
