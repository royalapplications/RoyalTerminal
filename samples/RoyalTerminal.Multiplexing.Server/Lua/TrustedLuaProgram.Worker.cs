// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;
namespace RoyalTerminal.Multiplexing.Server;

internal sealed partial class TrustedLuaProgram
{
    private readonly DetachedConfigJobs _detached = new();
    internal bool AllowExec { get; init; }
    internal Task StopJobsAsync(CancellationToken ct) => _detached.StopAsync(ct);
    private static readonly JsonElement ContractAccepted = JsonDocument.Parse("true").RootElement.Clone();

    private async Task<JsonElement> ExecuteAsync(LuaRun request,Func<string,JsonElement,CancellationToken,ValueTask<JsonElement>>? invoke,CancellationToken caller)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(caller,_detached.Stopping);
        deadline.CancelAfter(TimeSpan.FromSeconds(invoke is null ? 5 : 20));var ct=deadline.Token;
        var start=new ProcessStartInfo(_exe){WorkingDirectory=AppContext.BaseDirectory,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        start.Environment.Clear();if(OperatingSystem.IsWindows()&& Environment.GetEnvironmentVariable("SystemRoot") is string root)start.Environment["SystemRoot"]=root;
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Lua","config-worker.lua"));
        using var child=Process.Start(start)??throw new IOException("Unable to start the configured Lua interpreter.");
        using var jobs=new ConfigProcessScope(AllowExec,_detached);
        var drain=child.StandardError.BaseStream.CopyToAsync(Stream.Null,ct);
        try
        {
            var contracts=new LuaActionContracts();int contractCalls=0;
            var output=new FramedReader(child.StandardOutput.BaseStream);await WriteAsync(child.StandardInput.BaseStream,JsonSerializer.SerializeToUtf8Bytes(request,LuaWire.Default.LuaRun),ct);
            int calls=0,jobCalls=0;bool originRead=false;
            while(true)
            {
                using var msg=await output.ReadAsync(ct);var obj=msg.RootElement;LuaReply reply;
                switch(obj.GetProperty("op").GetString())
                {
                    case "failed":
                        string where=obj.TryGetProperty("line",out var v)&&v.TryGetInt32(out int line)?$" at init.lua:{line}":"";
                        throw new ApiException(422,"lua_failed","Configured Lua program failed"+where+".");
                    case "done":
                        var value=obj.GetProperty("value").Clone();child.StandardInput.Close();await child.WaitForExitAsync(ct);if(child.ExitCode!=0)throw new IOException("Lua exited unsuccessfully.");return value;
                    case "contract" when ++contractCalls<=4096:
                        try { contracts.Process(obj);reply=new(true,ContractAccepted); }
                        catch(Exception ex) when(ex is ArgumentException or MultiplexerAutomationException){reply=new(false,null,ex.Message);}
                        break;
                    case "job" when invoke is not null && ++jobCalls<=1024:
                        reply=await jobs.DispatchAsync(obj.GetProperty("frame"),ct);break;
                    case "call" when invoke is not null:
                        string method=obj.GetProperty("method").GetString()??string.Empty;
                        if(method=="client.invocation_origin" && !originRead)originRead=true;
                        else if(++calls>32)throw new ApiException(422,"lua_protocol","Host call quota exceeded.");
                        if(method.StartsWith("lua.",StringComparison.Ordinal))throw new ApiException(422,"lua_recursion","A configured action cannot start another Lua worker.");
                        try{reply=new(true,await invoke(method,obj.GetProperty("arguments"),ct));}
                        catch(Exception e) when(e is not OperationCanceledException and not OutOfMemoryException){var(status,error)=ServerHttp.Classify(e);if(status>=500)throw;reply=new(false,null,error.Code);}break;
                    default:throw new ApiException(422,"lua_protocol","Invalid Lua operation or call quota exceeded.");
                }
                var frame=JsonSerializer.SerializeToUtf8Bytes(reply,LuaWire.Default.LuaReply);if(frame.Length>262144)throw new ApiException(413,"lua_frame_limit","Host response exceeds the Lua frame budget. Effects are not retried.");await WriteAsync(child.StandardInput.BaseStream,frame,ct);
            }
        }
        catch(OperationCanceledException) when(!caller.IsCancellationRequested&&!_detached.Stopping.IsCancellationRequested&&deadline.IsCancellationRequested){throw new ApiException(408,"lua_timeout","Lua execution exceeded its deadline.");}
        finally{try{if(!child.HasExited)child.Kill(entireProcessTree:true);}catch(InvalidOperationException){}await child.WaitForExitAsync(CancellationToken.None);try{await drain;}catch(OperationCanceledException)when(ct.IsCancellationRequested){}}
    }
    private static async Task WriteAsync(Stream stream,byte[] bytes,CancellationToken ct){await stream.WriteAsync(bytes,ct);await stream.WriteAsync("\n"u8.ToArray(),ct);await stream.FlushAsync(ct);}
    private sealed class FramedReader(Stream stream)
    {
        private readonly byte[] _read=new byte[4096];private int _start,_end;
        internal async Task<JsonDocument> ReadAsync(CancellationToken ct)
        {
            var frame=new ArrayBufferWriter<byte>();
            while(true)
            {
                if(_start==_end){_start=0;_end=await stream.ReadAsync(_read,ct);if(_end==0)throw new IOException("Lua ended before completing its reply.");}
                int newline=Array.IndexOf(_read,(byte)'\n',_start,_end-_start);int end=newline<0?_end:newline;
                if(frame.WrittenCount+end-_start>262144)throw new ApiException(413,"lua_frame_limit","Lua frame exceeds 256 KiB.");
                frame.Write(_read.AsSpan(_start,end-_start));_start=end;if(newline>=0){++_start;return JsonDocument.Parse(frame.WrittenMemory);}
            }
        }
    }
}
