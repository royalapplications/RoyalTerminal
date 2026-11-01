// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Serialization;
namespace RoyalTerminal.Multiplexing.Server;

internal sealed record LuaManifest(string Generation, string[] Actions, JsonElement Keymap, WorkspacePeers Peers);
internal sealed record LuaRun(string Mode, string Source, string? Name, ActionContext Context, JsonElement Arguments, Dictionary<string,string>? Modules = null);
internal sealed record LuaReply(bool Ok, JsonElement? Value, string? Error = null);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LuaRun)),JsonSerializable(typeof(LuaReply)),JsonSerializable(typeof(LuaManifest))]
internal partial class LuaWire : JsonSerializerContext;

internal sealed partial class TrustedLuaProgram
{
    private sealed record Generation(LuaSourceSet Sources, LuaManifest Manifest);
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new Dictionary<string,string>(), EmptyMapJson.Default.DictionaryStringString);
    private readonly SemaphoreSlim _reload = new(1), _workers = new(4);
    private readonly string _file, _exe;
    // This is host-local configuration, never a path provided by a client or a Lua action.
    private readonly string? _moduleRoot = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_LUA_MODULES");
    private Generation? _current;
    internal Action<LuaManifest,bool>? Published { get; init; }
    internal TrustedLuaProgram(string file,string exe)
    {
        if (!Path.IsPathFullyQualified(file) || !Path.IsPathFullyQualified(exe)) throw new ArgumentException("Configure absolute paths for init.lua and Lua 5.1.");
        _file=file; _exe=exe;
    }
    internal LuaManifest Manifest => Volatile.Read(ref _current)?.Manifest ?? throw new InvalidOperationException("Missing Lua generation.");
    internal Task<LuaManifest> ReloadAsync(CancellationToken ct) => EvaluateFileAsync(publish:true,ct);
    internal Task<LuaManifest> CheckAsync(CancellationToken ct) => EvaluateFileAsync(publish:false,ct);
    private async Task<LuaManifest> EvaluateFileAsync(bool publish,CancellationToken ct)
    {
        if (!await _reload.WaitAsync(0,ct)) throw new ApiException(409,"reload_in_progress","Another Lua configuration evaluation is in progress.");
        try
        {
            var sources = await LuaSourceSet.ReadAsync(_file,_moduleRoot,ct);
            var result = await ExecuteAsync(new("validate",sources.Main,null,new(),Empty,sources.Modules),null,ct);
            var declared=result.GetProperty("actions"); var keys=result.GetProperty("keymap");
            if(declared.ValueKind!=JsonValueKind.Array || declared.GetArrayLength()>256 || keys.GetProperty("bindings").GetArrayLength()>2048 || keys.GetProperty("modes").GetArrayLength()>128)throw new InvalidDataException("Invalid Lua manifest.");
            var names=declared.EnumerateArray().Select(n=>n.GetString()??throw new InvalidDataException("Invalid action name.")).ToArray();
            var manifest=new LuaManifest(sources.Digest,names,keys.Clone(),WorkspacePeers.Parse(result.GetProperty("hosts")));
            ct.ThrowIfCancellationRequested();
            if(publish)
            {
                string previous=_current?.Manifest.Peers.Hash ?? WorkspacePeers.Empty.Hash;
                Volatile.Write(ref _current,new(sources,manifest));
                Published?.Invoke(manifest,previous!=manifest.Peers.Hash);
            }
            return manifest;
        }
        finally{_reload.Release();}
    }
    internal async ValueTask<JsonElement> InvokeAsync(string name,ActionContext context,JsonElement args,Func<string,JsonElement,CancellationToken,ValueTask<JsonElement>> invoke,CancellationToken ct,string? expectedGeneration=null,bool binding=false)
    {
        var gen=Volatile.Read(ref _current)??throw new InvalidOperationException("Missing Lua generation.");
        if(expectedGeneration!=null && gen.Manifest.Generation!=expectedGeneration)throw new ApiException(409,"stale_lua_generation","Configuration changed. No callback was executed.");
        if(binding)
        {
            if(expectedGeneration==null || !int.TryParse(name,out var id) || !gen.Manifest.Keymap.GetProperty("bindings").EnumerateArray().Any(b=>b.TryGetProperty("callback",out var c)&&c.GetInt32()==id))throw new ApiException(404,"binding_not_found","Unknown Lua callback.");
        }
        else if(!gen.Manifest.Actions.Contains(name,StringComparer.Ordinal))throw new ApiException(404,"lua_action_not_found","Unknown configured action.");
        if(!await _workers.WaitAsync(0,ct))throw new ApiException(429,"lua_busy","All Lua workers are busy.");
        try{return await ExecuteAsync(new(binding?"binding":"invoke",gen.Sources.Main,name,context,args,gen.Sources.Modules),invoke,ct);}finally{_workers.Release();}
    }
}
