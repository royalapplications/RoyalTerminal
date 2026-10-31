// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class LuaScriptCli
{
    private const int MaxFrame=256*1024, MaxReply=4*1024*1024;
    public static async Task<int> RunAsync(string[] args,string? endpoint,string? token,HttpClient http,TextWriter diagnostics,
        CancellationToken cancellation,JsonObject? launch=null,TextWriter? output=null)
    {
        if(launch is null && (args.Length==0||args[0] is "--help" or "-h")){await diagnostics.WriteLineAsync("Usage: royalterminal-mux lua FILE [ARGS...] (requires Lua 5.1 and dkjson). Only run trusted local files.");return args.Length==0?2:0;}
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellation);lifetime.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            string path=launch is null?Path.GetFullPath(args[0]):"-";
            if(launch is null&&!File.Exists(path))throw new FileNotFoundException("Lua script was not found.",path);
            MultiplexingApiClient? client=string.IsNullOrWhiteSpace(token)?null:new(http,new Uri(endpoint??"http://127.0.0.1:7331/",UriKind.Absolute),token);
            ProcessStartInfo start=new(Environment.GetEnvironmentVariable("ROYALTERMINAL_LUA51")??(OperatingSystem.IsWindows()?"lua5.1.exe":"lua5.1"))
            {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,StandardInputEncoding=new UTF8Encoding(false),CreateNoWindow=true};
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"Lua","workspace.lua"));start.ArgumentList.Add(path);
            foreach(string argument in args.Skip(1))start.ArgumentList.Add(argument);
            start.Environment.Remove("ROYALTERMINAL_MUX_TOKEN");start.Environment.Remove("LUA_INIT");
            using Process process=Process.Start(start)??throw new IOException("Could not start Lua.");
            try
            {
                using var children=new LuaLocalProcesses();
                string? result=await PumpAsync(process.StandardOutput.BaseStream,process.StandardInput.BaseStream,client,children,launch,lifetime.Token);
                await process.WaitForExitAsync(lifetime.Token);
                if(process.ExitCode==0&&output is not null&&result is not null)await output.WriteLineAsync(result);
                return process.ExitCode;
            }
            finally
            {
                if(!process.HasExited){try{process.Kill(true);}catch(InvalidOperationException)when(process.HasExited){}await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}
            }
        }
        catch(OperationCanceledException)when(cancellation.IsCancellationRequested){await diagnostics.WriteLineAsync("Lua run cancelled; workspace processes were not stopped.");return 130;}
        catch(OperationCanceledException){await diagnostics.WriteLineAsync("The local script deadline expired.");return 124;}
        catch(Exception ex)when(ex is IOException or Win32Exception or ArgumentException or FormatException or InvalidOperationException or JsonException or HttpRequestException or TimeoutException or UnauthorizedAccessException)
        {await diagnostics.WriteLineAsync(ex.Message);return 1;}
    }
    private static async Task<string?> PumpAsync(Stream input,Stream output,MultiplexingApiClient? client,LuaLocalProcesses children,JsonObject? launch,CancellationToken token)
    {
        var clock=Stopwatch.StartNew();byte[] chunk=new byte[8192];using MemoryStream line=new();int messages=0;string? result=null;
        for(;;)
        {
            int n=await input.ReadAsync(chunk,token);if(n==0){if(line.Length!=0)throw new IOException("Truncated Lua RPC frame.");return result;}
            for(int start=0;start<n;)
            {
                int end=Array.IndexOf(chunk,(byte)'\n',start,n-start),length=(end<0?n:end)-start;
                if(line.Length+length>MaxFrame)throw new IOException("Lua RPC input exceeded 256 KiB.");line.Write(chunk,start,length);if(end<0)break;
                if(++messages>100000)throw new IOException("Lua RPC operation limit exceeded.");
                using var doc=JsonDocument.Parse(line.GetBuffer().AsMemory(0,(int)line.Length));line.SetLength(0);JsonElement f=doc.RootElement;
                if(f.ValueKind!=JsonValueKind.Object||!f.TryGetProperty("op",out var op)||op.ValueKind!=JsonValueKind.String)throw new IOException("Invalid Lua RPC envelope.");
                using MemoryStream response=new();using(var w=new Utf8JsonWriter(response))
                {
                    w.WriteStartObject();
                    try
                    {
                        switch(op.GetString())
                        {
                            case "setup": w.WritePropertyName("value");if(launch is null)w.WriteBooleanValue(false);else launch.WriteTo(w);break;
                            case "result": if(launch is null||result is not null)throw new ArgumentException("Unexpected script result.");result=f.GetProperty("value").GetRawText();w.WriteBoolean("value",true);break;
                            case "clock":w.WriteNumber("value",clock.Elapsed.TotalSeconds);break;
                            case "sleep":double seconds=f.GetProperty("seconds").GetDouble();if(!double.IsFinite(seconds)||seconds<0||seconds>1800)throw new ArgumentException("Invalid sleep duration.");await Task.Delay(TimeSpan.FromSeconds(seconds),token);w.WriteBoolean("value",true);break;
                            case "process":var payload=await children.DispatchAsync(f,token);w.WritePropertyName("value");payload.WriteTo(w);break;
                            case "call":
                                if(client is null)throw new ArgumentException("Set ROYALTERMINAL_MUX_TOKEN for workspace API calls.");
                                if(!f.TryGetProperty("method",out var method)||method.ValueKind!=JsonValueKind.String||!f.TryGetProperty("args",out var a)||a.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected method and object arguments.");
                                var value=await client.CallAsync(method.GetString()!,a,token);if(Encoding.UTF8.GetByteCount(value.GetRawText())>MaxReply-64)throw new IOException("Lua RPC result exceeded its budget.");w.WritePropertyName("value");value.WriteTo(w);break;
                            default:throw new ArgumentException("Unknown Lua RPC operation.");
                        }
                    }catch(Exception e)when(e is HttpRequestException or IOException or ArgumentException or Win32Exception or InvalidOperationException or FormatException or KeyNotFoundException or UnauthorizedAccessException){w.WriteString("error",e.Message);}
                    w.WriteEndObject();
                }
                if(response.Length>MaxReply)throw new IOException("Encoded Lua RPC reply exceeded four MiB.");response.WriteByte((byte)'\n');await output.WriteAsync(response.GetBuffer().AsMemory(0,(int)response.Length),token);await output.FlushAsync(token);start=end+1;
            }
        }
    }
}
