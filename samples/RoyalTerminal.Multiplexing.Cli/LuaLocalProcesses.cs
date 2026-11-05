// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoyalTerminal.Multiplexing.Cli;

/// <summary>Owns only processes explicitly launched by one trusted local script. Never part of the HTTP host.</summary>
internal sealed partial class LuaLocalProcesses : IDisposable
{
    private readonly Dictionary<int,Job> _jobs=[];
    private int _next;
    [LibraryImport("libc",EntryPoint="kill",SetLastError=true)]
    private static partial int PosixKill(int pid,int signal);

    internal async ValueTask<JsonNode> DispatchAsync(JsonElement f,CancellationToken ct)
    {
        string? op=f.GetProperty("operation").GetString();
        if(op=="start")
        {
            if(_jobs.Count>=16)throw new ArgumentException("Local process limit (16) reached.");
            JsonElement argv=f.GetProperty("argv"),o=f.GetProperty("options");
            if(argv.ValueKind!=JsonValueKind.Array||argv.GetArrayLength() is <1 or >256||o.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected argv and an options object.");
            ProcessStartInfo info=new(){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            foreach(var v in argv.EnumerateArray()){string a=v.GetString()??throw new ArgumentException("argv entries must be strings.");if(a.Contains('\0'))throw new ArgumentException("NUL in argv.");if(info.FileName.Length==0){ArgumentException.ThrowIfNullOrWhiteSpace(a);info.FileName=a;}else info.ArgumentList.Add(a);}
            if(o.TryGetProperty("cwd",out var cwd))info.WorkingDirectory=cwd.GetString()??throw new ArgumentException("Invalid cwd.");
            if(Bool(o,"clear_env",false))info.Environment.Clear();
            if(o.TryGetProperty("env",out var env))foreach(var p in env.EnumerateObject())
            { if(p.Name.Length==0||p.Name.IndexOfAny(['=','\0'])>=0)throw new ArgumentException("Invalid environment key.");if(p.Value.ValueKind==JsonValueKind.False)info.Environment.Remove(p.Name);else{var v=p.Value.GetString()??throw new ArgumentException("Expected an environment string or false.");if(v.Contains('\0'))throw new ArgumentException("NUL in environment value.");info.Environment[p.Name]=v;} }
            info.Environment.Remove("ROYALTERMINAL_MUX_TOKEN");info.Environment.Remove("LUA_INIT");
            double seconds=o.TryGetProperty("timeout",out var t)?t.GetDouble():1800;
            if(!double.IsFinite(seconds)||seconds<=0||seconds>1800)throw new ArgumentException("Expected timeout in (0,1800] seconds.");
            if(Bool(o,"detach",false))throw new ArgumentException("Detached local children are not supported; use a workspace block for long-lived work.");
            int id=checked(++_next); Job job=new(info,Bool(o,"stdout",true),Bool(o,"stderr",true),TimeSpan.FromSeconds(seconds));
            _jobs.Add(id,job);
            try { if(o.TryGetProperty("input",out var input))await job.WriteAsync(Convert.FromBase64String(input.GetString()!),ct);if(!Bool(o,"open",false))job.CloseInput();return new JsonObject{["id"]=id,["pid"]=job.Pid}; }
            catch { _jobs.Remove(id);job.Dispose();throw; }
        }
        int key=f.GetProperty("id").GetInt32();if(!_jobs.TryGetValue(key,out var j))throw new ArgumentException("Unknown local process handle.");
        switch(op)
        {
            case "poll":return await j.PollAsync(Math.Clamp(f.GetProperty("ms").GetInt32(),0,250),ct);
            case "write":await j.WriteAsync(Convert.FromBase64String(f.GetProperty("data").GetString()!),ct);break;
            case "close":j.CloseInput();break;
            case "signal":j.Signal(f.GetProperty("signal").GetInt32());break;
            case "running":return JsonValue.Create(!j.Exited)!;
            case "release":_jobs.Remove(key);j.Dispose();break;
            default:throw new ArgumentException("Unknown process operation.");
        }
        return JsonValue.Create(true)!;
    }
    private static bool Bool(JsonElement o,string p,bool fallback)=>o.TryGetProperty(p,out var v)?v.GetBoolean():fallback;
    public void Dispose(){foreach(var j in _jobs.Values)j.Dispose();_jobs.Clear();}

    private sealed class Job : IDisposable
    {
        private const int CaptureLimit=256*1024;
        private readonly object _gate=new();private readonly Process _p;private readonly Task _completion;
        private readonly MemoryStream[] _streams=[new(),new()];private readonly int[] _sent=[0,0];
        private readonly CancellationTokenSource _abort=new();private readonly Timer _deadline;
        private TaskCompletionSource _changed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestedSignal,_expired;private bool _inputClosed;private string? _error;
        internal Job(ProcessStartInfo info,bool stdout,bool stderr,TimeSpan deadline)
        {
            _p=Process.Start(info)??throw new IOException("Could not launch child process.");Pid=_p.Id;
            _deadline=new Timer(_=>{Interlocked.Exchange(ref _expired,1);try{Signal(15);}catch(Exception e)when(e is Win32Exception or InvalidOperationException or IOException){}},null,deadline,Timeout.InfiniteTimeSpan);
            _completion=ObserveAsync(stdout,stderr,deadline);
        }
        internal int Pid {get;}
        internal bool Exited=>_p.HasExited;
        internal void CloseInput(){if(_inputClosed)return;_inputClosed=true;_p.StandardInput.BaseStream.Close();}
        internal async Task WriteAsync(byte[] data,CancellationToken ct){if(_inputClosed)throw new IOException("Child stdin is closed.");using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,_abort.Token);await _p.StandardInput.BaseStream.WriteAsync(data,linked.Token);await _p.StandardInput.BaseStream.FlushAsync(linked.Token);}
        internal void Signal(int signal)
        {
            if(signal is not (1 or 2 or 9 or 15))throw new ArgumentException("Supported signals are HUP, INT, KILL and TERM.");
            if(Exited)return;
            if(OperatingSystem.IsWindows()) {if(signal is not(9 or 15))throw new ArgumentException("Only termination is supported on Windows.");Kill();return;}
            if(PosixKill(Pid,signal)!=0&&!Exited)throw new IOException("Could not deliver the signal.",new Win32Exception(Marshal.GetLastPInvokeError()));
            Volatile.Write(ref _requestedSignal,signal);
        }
        private void Kill(){try{if(!_p.HasExited)_p.Kill(entireProcessTree:true);}catch(InvalidOperationException)when(_p.HasExited){} }
        private async Task ObserveAsync(bool stdout,bool stderr,TimeSpan deadline)
        {
            Task io=Task.WhenAll(DrainAsync(0,_p.StandardOutput.BaseStream,stdout),DrainAsync(1,_p.StandardError.BaseStream,stderr),_p.WaitForExitAsync(_abort.Token));
            try{await io.WaitAsync(deadline+TimeSpan.FromSeconds(2),_abort.Token).ConfigureAwait(false);}
            catch(TimeoutException){Interlocked.Exchange(ref _expired,1);Kill();_abort.Cancel();}
            catch(OperationCanceledException)when(_abort.IsCancellationRequested){}
            catch(Exception e)when(e is IOException or InvalidOperationException){lock(_gate)_error??=e.Message;}
            finally{_deadline.Change(Timeout.Infinite,Timeout.Infinite); if(!io.IsCompleted){try{await io.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);}catch(Exception e)when(e is OperationCanceledException or TimeoutException or IOException){}} lock(_gate)Pulse();}
        }
        private async Task DrainAsync(int channel,Stream stream,bool keep)
        {
            byte[] buf=new byte[8192];while(true){int n=await stream.ReadAsync(buf,_abort.Token).ConfigureAwait(false);if(n==0)return;if(!keep)continue;
                bool overflow;lock(_gate){overflow=_streams[channel].Length+n>CaptureLimit;if(overflow)_error="Local process exceeded its 256 KiB per-stream output budget.";else _streams[channel].Write(buf,0,n);Pulse();}
                if(overflow){Kill();return;}}
        }
        private void Pulse(){var previous=_changed;_changed=new(TaskCreationOptions.RunContinuationsAsynchronously);previous.TrySetResult();}
        internal async ValueTask<JsonObject> PollAsync(int ms,CancellationToken ct)
        {
            Task? wait=null;lock(_gate){if(ms!=0&&!_completion.IsCompleted&&_streams[0].Length==_sent[0]&&_streams[1].Length==_sent[1])wait=_changed.Task;}
            if(wait is not null)try{await Task.WhenAny(wait,_completion).WaitAsync(TimeSpan.FromMilliseconds(ms),ct);}catch(TimeoutException){}
            lock(_gate){if(_error is not null)throw new IOException(_error);var page=new JsonObject();for(int i=0;i<2;i++){
                    int n=Math.Min(32768,(int)_streams[i].Length-_sent[i]);page[i==0?"out":"err"]=Convert.ToBase64String(_streams[i].GetBuffer(),_sent[i],n);_sent[i]+=n;}
                if(_completion.IsCompleted&&_p.HasExited&&_streams[0].Length==_sent[0]&&_streams[1].Length==_sent[1]){
                    int code=_p.ExitCode,sig=Volatile.Read(ref _requestedSignal);bool known=!OperatingSystem.IsWindows()&&sig!=0&&code==128+sig;
                    page["code"]=known?-1:code;page["signal"]=known?sig switch{1=>"SIGHUP",2=>"SIGINT",9=>"SIGKILL",_=>"SIGTERM"}:null;page["timed_out"]=Volatile.Read(ref _expired)!=0;}
                return page;}
        }
        public void Dispose(){_deadline.Dispose();Kill();_abort.Cancel();try{_completion.GetAwaiter().GetResult();}finally{_p.Dispose();_abort.Dispose();foreach(var s in _streams)s.Dispose();}}
    }
}
