// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record LuaManifest(string Generation, string[] Actions, JsonElement Keymap);
internal sealed record LuaRun(string Mode, string Source, string? Name, ActionContext Context, JsonElement Arguments);
internal sealed record LuaReply(bool Ok, JsonElement? Value, string? Error = null);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LuaRun)), JsonSerializable(typeof(LuaReply)), JsonSerializable(typeof(LuaManifest))]
internal partial class LuaWire : JsonSerializerContext;

internal sealed partial class TrustedLuaProgram
{
    private sealed record Generation(string Source, LuaManifest Manifest);
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new Dictionary<string,string>(), EmptyMapJson.Default.DictionaryStringString);
    private readonly SemaphoreSlim _reload = new(1), _workers = new(4);
    private readonly string _file, _exe;
    private Generation? _current;
    internal TrustedLuaProgram(string file, string exe)
    {
        if (!Path.IsPathFullyQualified(file) || !Path.IsPathFullyQualified(exe)) throw new ArgumentException("Configure absolute paths for init.lua and Lua 5.1.");
        _file = file; _exe = exe;
    }
    internal LuaManifest Manifest => Volatile.Read(ref _current)?.Manifest ?? throw new InvalidOperationException("Missing Lua generation.");
    internal async Task<LuaManifest> ReloadAsync(CancellationToken ct)
    {
        if (!await _reload.WaitAsync(0, ct)) throw new ApiException(409,"reload_in_progress","A Lua reload is already running.");
        try
        {
            await using var file = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            const int limit = 128 * 1024; var bytes = new byte[limit + 1];
            int count = await file.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream:false, cancellationToken:ct);
            if (count > limit) throw new ArgumentException("Lua configuration exceeds 128 KiB.");
            string source = new UTF8Encoding(false,true).GetString(bytes,0,count);
            var result = await ExecuteAsync(new("validate",source,null,new(),Empty),null,ct);
            var declared = result.GetProperty("actions"); var keys = result.GetProperty("keymap");
            if (declared.ValueKind != JsonValueKind.Array || declared.GetArrayLength() > 256 || keys.GetProperty("bindings").GetArrayLength() > 2048 || keys.GetProperty("modes").GetArrayLength() > 128) throw new InvalidDataException("Invalid Lua manifest.");
            var actions=declared.EnumerateArray().Select(n=>n.GetString()??throw new InvalidDataException("Invalid action name.")).ToArray();
            var manifest=new LuaManifest(Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0,count))),actions,keys.Clone());
            ct.ThrowIfCancellationRequested(); Volatile.Write(ref _current,new(source,manifest)); return manifest;
        }
        finally { _reload.Release(); }
    }
    internal async ValueTask<JsonElement> InvokeAsync(string name,ActionContext context,JsonElement args,Func<string,JsonElement,CancellationToken,ValueTask<JsonElement>> invoke,CancellationToken ct,string? expectedGeneration=null,bool binding=false)
    {
        var gen=Volatile.Read(ref _current)??throw new InvalidOperationException("Missing Lua generation.");
        if(expectedGeneration is not null && gen.Manifest.Generation!=expectedGeneration)throw new ApiException(409,"stale_lua_generation","Lua configuration changed. Reload bindings; no action was invoked.");
        if(binding)
        {
            if(expectedGeneration is null || !int.TryParse(name,out var id) || !gen.Manifest.Keymap.GetProperty("bindings").EnumerateArray().Any(b=>b.TryGetProperty("callback",out var n)&&n.GetInt32()==id))throw new ApiException(404,"binding_not_found","Unknown Lua callback.");
        }
        else if(!gen.Manifest.Actions.Contains(name,StringComparer.Ordinal))throw new ApiException(404,"lua_action_not_found","Unknown configured Lua action.");
        if (!await _workers.WaitAsync(0,ct)) throw new ApiException(429,"lua_busy","All workers are busy.");
        try { return await ExecuteAsync(new(binding?"binding":"invoke",gen.Source,name,context,args),invoke,ct); }
        finally { _workers.Release(); }
    }
}
