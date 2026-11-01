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
        string? file=builder.Configuration["ROYALTERMINAL_MUX_LUA_INIT"];if(string.IsNullOrEmpty(file))return;
        var program=new TrustedLuaProgram(file,builder.Configuration["ROYALTERMINAL_MUX_LUA_EXECUTABLE"]??"/usr/bin/lua5.1");builder.Services.AddHostedService(_=>new LuaStartup(program));
        Define("lua.actions","Read the active action and keyboard declaration generation.",[],LuaWire.Default.LuaManifest,(_,grant,_)=>{ServerHttp.RequireAdministrator(grant);return Done(program.Manifest,LuaWire.Default.LuaManifest);});
        Define("lua.check","Validate the configured local file and return its proposed generation without publishing any change.",[],LuaWire.Default.LuaManifest,async(_,grant,ct)=>{ServerHttp.RequireAdministrator(grant);return Element(await program.CheckAsync(ct),LuaWire.Default.LuaManifest);});
        Define("lua.reload","Atomically reload the operator-selected local configuration file.",[],LuaWire.Default.LuaManifest,async(_,grant,ct)=>{ServerHttp.RequireAdministrator(grant);return Element(await program.ReloadAsync(ct),LuaWire.Default.LuaManifest);});
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
    private sealed class LuaStartup(TrustedLuaProgram program):IHostedService
    {public async Task StartAsync(CancellationToken ct)=>_=await program.ReloadAsync(ct);public Task StopAsync(CancellationToken ct)=>Task.CompletedTask;}
}
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string,string>))]
internal partial class EmptyMapJson:System.Text.Json.Serialization.JsonSerializerContext;
