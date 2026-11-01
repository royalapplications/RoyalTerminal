// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
namespace RoyalTerminal.Multiplexing.Server;
internal sealed partial class ApiCatalog
{
    internal void ConfigureLua(WebApplicationBuilder builder)
    {
        RegisterTerminalCommands();
        Define("session.launch_layout","Launch a validated tree of PTYs. Use blockId for a split or omit it for a new window.",["sessionId:string!","layout:object!","label:string","blockId:string","direction:string","side:string","ratio:number","expectedRevision:integer"],LayoutTemplateJson.Default.LayoutReceipt,LaunchLayout);
        string? file=builder.Configuration["ROYALTERMINAL_MUX_LUA_INIT"];if(string.IsNullOrEmpty(file))return;
        var program=new TrustedLuaProgram(file,builder.Configuration["ROYALTERMINAL_MUX_LUA_EXECUTABLE"]??"/usr/bin/lua5.1");builder.Services.AddHostedService(_=>new LuaStartup(program));
        Define("lua.actions","List trusted server actions and their configuration digest.",[],LuaWire.Default.LuaManifest,(_,grant,_)=>{ServerHttp.RequireAdministrator(grant);return Done(program.Manifest,LuaWire.Default.LuaManifest);});
        Define("lua.reload","Validate and atomically replace callbacks from the startup-selected file. No source accepted.",[],LuaWire.Default.LuaManifest,async(_,grant,ct)=>{ServerHttp.RequireAdministrator(grant);return Element(await program.ReloadAsync(ct),LuaWire.Default.LuaManifest);});
        Define("lua.invoke","Run a local operator-configured callback.",["name:string!","arguments:object","sessionId:string","windowId:string","blockId:string"],ServerJsonContext.Default.JsonElement,async(a,grant,ct)=>{ServerHttp.RequireAdministrator(grant);var args=a.Value.TryGetProperty("arguments",out _)?a.Object("arguments"):EmptyArguments;ActionContext context=new(a.OptionalId("sessionId"),a.OptionalId("blockId"),a.OptionalId("windowId"));return await program.InvokeAsync(a.String("name"),context,args,(method,input,token)=>InvokeAsync(method,input,grant,token),ct);});
    }
    private static readonly JsonElement EmptyArguments=JsonSerializer.SerializeToElement(new Dictionary<string,string>(),EmptyMapJson.Default.DictionaryStringString);
    private void Define(string name,string description,string[] fields,JsonTypeInfo result,Func<ApiArguments,AccessGrant,CancellationToken,ValueTask<JsonElement>> handler)
    {
        var input=Schema(name,fields);using var output=JsonDocument.Parse(result.GetJsonSchemaAsNode().ToJsonString());
        _methods.Add(name,new(new(name,description,input,output.RootElement.Clone(),ValidationNotes:"Administrator only. External process effects are not rolled back or retried."),fields.Select(f=>f[..f.IndexOf(':')]).ToArray(),ApiInputSchemas.ForActions(input),handler));
    }
    private sealed class LuaStartup(TrustedLuaProgram program):IHostedService
    {public async Task StartAsync(CancellationToken ct)=>_=await program.ReloadAsync(ct);public Task StopAsync(CancellationToken ct)=>Task.CompletedTask;}
}
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string,string>))]
internal partial class EmptyMapJson:System.Text.Json.Serialization.JsonSerializerContext;
