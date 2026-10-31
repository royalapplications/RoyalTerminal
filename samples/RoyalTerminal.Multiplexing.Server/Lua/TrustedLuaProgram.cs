// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record LuaManifest(string Generation, string[] Actions);
internal sealed record LuaRun(string Mode, string Source, string? Name, ActionContext Context, JsonElement Arguments);
internal sealed record LuaReply(bool Ok, JsonElement? Value, string? Error = null);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LuaRun)), JsonSerializable(typeof(LuaReply)), JsonSerializable(typeof(LuaManifest))]
internal partial class LuaWire : JsonSerializerContext;

/// <summary>Publishes a complete validated init.lua generation. Invocation never reopens its source file.</summary>
internal sealed class TrustedLuaProgram
{
    private sealed record Generation(string Source, LuaManifest Manifest);
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();
    private readonly SemaphoreSlim _reload = new(1), _workers = new(4);
    private readonly string _file, _exe;
    private Generation? _current;
    internal TrustedLuaProgram(string file, string exe)
    {
        if (!Path.IsPathFullyQualified(file) || !Path.IsPathFullyQualified(exe)) throw new ArgumentException("Configure absolute paths for init.lua and the Lua 5.1 interpreter.");
        _file = file; _exe = exe;
    }
    internal LuaManifest Manifest => Volatile.Read(ref _current)?.Manifest ?? throw new InvalidOperationException("Missing Lua generation.");

    internal async Task<LuaManifest> ReloadAsync(CancellationToken ct)
    {
        if (!await _reload.WaitAsync(0, ct)) throw new ApiException(409,"reload_in_progress","A Lua configuration reload is already running.");
        try
        {
            await using var file = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            const int limit=128*1024; var bytes=new byte[limit+1]; int count=await file.ReadAtLeastAsync(bytes,bytes.Length,throwOnEndOfStream:false,cancellationToken:ct);
            if (count>limit) throw new ArgumentException("Lua configuration exceeds 128 KiB.");
            string source=new UTF8Encoding(false,true).GetString(bytes,0,count);
            JsonElement names=await ExecuteAsync(new("validate",source,null,new(),Empty),null,ct);
            if (names.ValueKind!=JsonValueKind.Array || names.GetArrayLength()>256) throw new InvalidDataException("Invalid Lua action manifest.");
            var actions=names.EnumerateArray().Select(n=>n.GetString() ?? throw new InvalidDataException("Invalid Lua action name.")).ToArray();
            var manifest=new LuaManifest(Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0,count))),actions);
            ct.ThrowIfCancellationRequested(); Volatile.Write(ref _current,new(source,manifest)); return manifest;
        }
        finally { _reload.Release(); }
    }

    internal async ValueTask<JsonElement> InvokeAsync(string name,ActionContext context,JsonElement args,
        Func<string,JsonElement,CancellationToken,ValueTask<JsonElement>> invoke,CancellationToken ct)
    {
        var gen=Volatile.Read(ref _current) ?? throw new InvalidOperationException("Missing Lua generation.");
        if (!gen.Manifest.Actions.Contains(name,StringComparer.Ordinal)) throw new ApiException(404,"lua_action_not_found","Unknown configured Lua action.");
        if (!await _workers.WaitAsync(0,ct)) throw new ApiException(429,"lua_busy","All Lua workers are in use.");
        try { return await ExecuteAsync(new("invoke",gen.Source,name,context,args),invoke,ct); }
        finally { _workers.Release(); }
    }

    private async Task<JsonElement> ExecuteAsync(LuaRun request,Func<string,JsonElement,CancellationToken,ValueTask<JsonElement>>? invoke,CancellationToken caller)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(caller);
        deadline.CancelAfter(TimeSpan.FromSeconds(invoke is null ? 5 : 20)); var ct=deadline.Token;
        var start=new ProcessStartInfo(_exe) { WorkingDirectory=AppContext.BaseDirectory,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true };
        start.Environment.Clear(); if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SystemRoot") is string root) start.Environment["SystemRoot"]=root;
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Lua","config-worker.lua"));
        using var child=Process.Start(start) ?? throw new IOException("Unable to start the configured Lua interpreter.");
        var drain=child.StandardError.BaseStream.CopyToAsync(Stream.Null,ct);
        try
        {
            var output=new FramedReader(child.StandardOutput.BaseStream);
            await WriteAsync(child.StandardInput.BaseStream,JsonSerializer.SerializeToUtf8Bytes(request,LuaWire.Default.LuaRun),ct);
            int calls=0;
            while (true)
            {
                using var msg=await output.ReadAsync(ct); var obj=msg.RootElement;
                switch (obj.GetProperty("op").GetString())
                {
                    case "failed":
                        string where=obj.TryGetProperty("line",out var v) && v.TryGetInt32(out int line) ? $" at init.lua:{line}" : "";
                        throw new ApiException(422,"lua_failed","Configured Lua program failed"+where+".");
                    case "done":
                        var result=obj.GetProperty("value").Clone(); child.StandardInput.Close(); await child.WaitForExitAsync(ct);
                        if (child.ExitCode!=0) throw new IOException("Lua exited unsuccessfully."); return result;
                    case "call" when invoke is not null && ++calls<=32:
                        string method=obj.GetProperty("method").GetString() ?? string.Empty;
                        if (method.StartsWith("lua.",StringComparison.Ordinal)) throw new ApiException(422,"lua_recursion","A configured action cannot start another Lua worker.");
                        LuaReply reply;
                        try { reply=new(true,await invoke(method,obj.GetProperty("arguments"),ct)); }
                        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
                        { var (status,error)=ServerHttp.Classify(e); if(status>=500) throw; reply=new(false,null,error.Code); }
                        var frame=JsonSerializer.SerializeToUtf8Bytes(reply,LuaWire.Default.LuaReply);
                        if(frame.Length>262144) throw new ApiException(413,"lua_frame_limit","Host result exceeds the Lua frame limit; the call is not retried.");
                        await WriteAsync(child.StandardInput.BaseStream,frame,ct); break;
                    default: throw new ApiException(422,"lua_protocol","Invalid Lua operation or API call quota exceeded.");
                }
            }
        }
        catch(OperationCanceledException) when (!caller.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw new ApiException(408,"lua_timeout","Lua execution exceeded its deadline."); }
        finally
        {
            try { if (!child.HasExited) child.Kill(entireProcessTree:true); } catch(InvalidOperationException) { }
            await child.WaitForExitAsync(CancellationToken.None);
            try { await drain; } catch(OperationCanceledException) when(ct.IsCancellationRequested) { }
        }
    }
    private static async Task WriteAsync(Stream s,byte[] bytes,CancellationToken ct)
    { await s.WriteAsync(bytes,ct);await s.WriteAsync("\n"u8.ToArray(),ct);await s.FlushAsync(ct); }

    private sealed class FramedReader(Stream stream)
    {
        private readonly byte[] _read=new byte[4096]; private int _start,_end;
        internal async Task<JsonDocument> ReadAsync(CancellationToken ct)
        {
            var frame=new ArrayBufferWriter<byte>();
            while(true)
            {
                if(_start==_end){_start=0;_end=await stream.ReadAsync(_read,ct);if(_end==0)throw new IOException("Lua worker ended before completing its reply.");}
                int newline=Array.IndexOf(_read,(byte)'\n',_start,_end-_start);int end=newline<0?_end:newline;
                if(frame.WrittenCount+end-_start>262144)throw new ApiException(413,"lua_frame_limit","Lua frame exceeds 256 KiB.");
                frame.Write(_read.AsSpan(_start,end-_start));_start=end;
                if(newline>=0){++_start;return JsonDocument.Parse(frame.WrittenMemory);}
            }
        }
    }
}
