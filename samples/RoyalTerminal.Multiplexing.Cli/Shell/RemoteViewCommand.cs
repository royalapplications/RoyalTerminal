// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Nodes;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Cli;

internal static class RemoteViewCommand
{
    internal const string Usage="views -s SESSION | focus -s SESSION [-C CLIENT] (-b BLOCK | -w WINDOW | --direction DIRECTION) | ui -s SESSION [-C CLIENT] ACTION | run/split -s SESSION -C CLIENT --focus=true [--wait] -- EXE ARGS";
    internal static bool Handles(string verb,string[] args) => verb is "views" or "focus" or "ui" || verb is "run" or "split" && args.TakeWhile(x=>x!="--").Any(x=>x is "-C" or "--client" or "--focus=true" or "--focus" || x.StartsWith("--client=",StringComparison.Ordinal));
    internal static async Task<int> RunAsync(string verb,string[] argv,string? endpoint,string? token,CliCurrentContext context,HttpClient http,TextWriter output,TextWriter error,CancellationToken ct)
    {
        if(argv is ["-h" or "--help"]){await output.WriteLineAsync(Usage);return 0;}
        try
        {
            var url=new Uri(endpoint??"http://127.0.0.1:7331/");if(url.Scheme!="https" && !(url.Scheme=="http"&&url.IsLoopback))throw new ArgumentException("Specify HTTPS or a loopback HTTP endpoint.");
            ArgumentException.ThrowIfNullOrWhiteSpace(token);var api=new MultiplexingApiClient(http,url,token);
            bool launching=verb is "run" or "split";List<string> rest=[];string? client=null;bool? focus=null;
            for(int i=0;i<argv.Length;i++){
                var a=argv[i];if(a=="--"){rest.AddRange(argv[i..]);break;}
                string key=a.Split('=',2)[0];string Value(){int eq=a.IndexOf('=');return eq>=0?a[(eq+1)..]:++i<argv.Length && argv[i]!="--"?argv[i]:throw new ArgumentException("Missing option value for "+key);}
                if(key is "-C" or "--client"){if(client is not null)throw new ArgumentException("Duplicate client selector.");client=Value();if(string.IsNullOrWhiteSpace(client))throw new ArgumentException("Empty client selector.");continue;}
                if(launching && key=="--focus"){if(focus.HasValue||!bool.TryParse(Value(),out bool f))throw new ArgumentException("Use --focus=true or --focus=false once.");focus=f;continue;}
                rest.Add(a);
                if(!a.Contains('=') && key is "--session" or "-s" or "--block" or "-b" or "--window" or "-w" or "--label" or "--cwd" or "--ratio" or "--split" or "--for" or "--direction")rest.Add(Value());
            }
            if(launching){
                var c=ShellSyntax.Parse(verb,[..rest]);if(focus==false || focus is null && client is null)return await ShellWorkflow.RunAsync(verb,[..rest],endpoint,token,context,http,Stream.Null,output,error,ct);
                var workspace=await ShellWorkspace.OpenAsync(api,c.Get("--session")??context.Session,ct);var receiver=await ResolveAsync(workspace,client,ct);
                List<string> launch=["-s",workspace.Id.ToString(),"--json"];
                foreach(var (k,v) in c.Options){if(k is "--json" or "--session" or "--wait" or "--for")continue;launch.Add(k);if(k!="--keep-open")launch.Add(v);}
                if(c.Tail.Length>0){launch.Add("--");launch.AddRange(c.Tail);}
                using var text=new StringWriter();int code=await ShellWorkflow.RunAsync(verb,[..launch],endpoint,token,context,http,Stream.Null,text,error,ct);if(code!=0)return code;
                var receipt=JsonNode.Parse(text.ToString())!.AsObject();string id=receipt["block_ids"]![0]!.GetValue<string>();
                try{var ticket=await Queue(workspace,receiver,new(){["name"]="pane.focus",["blockId"]=id},ct);receipt["client_sequence"]=ticket.GetProperty("sequence").GetInt64();}
                catch{await error.WriteLineAsync($"Block {id} already started in {workspace.Id}; focus was not confirmed. Do not retry the launch.");throw;}
                if(!c.Has("--wait")){await output.WriteLineAsync(c.Has("--json")?receipt.ToJsonString():id);return 0;}
                await error.WriteLineAsync($"Started {workspace.Id}/{id}. Focus queued.");List<string> wait=["-s",workspace.Id.ToString(),"-b",id];if(c.Has("--json"))wait.Add("--json");if(c.Get("--for") is {} limit)wait.AddRange(["--for",limit]);
                return await ShellWorkflow.RunAsync("wait",[..wait],endpoint,token,context,http,Stream.Null,output,error,ct);
            }
            string? s=null,b=null,w=null,d=null,action=null;
            for(int i=0;i<rest.Count;i++){
                var raw=rest[i];var kv=raw.Split('=',2);string Val()=>kv.Length==2?kv[1]:++i<rest.Count?rest[i]:throw new ArgumentException(Usage);
                switch(kv[0]){case "-s" or "--session" when s is null:s=Val();break;case "-b" or "--block" when b is null:b=Val();break;case "-w" or "--window" when w is null:w=Val();break;case "--direction" when d is null:d=Val();break;default:if(verb!="ui"||raw.StartsWith('-')||action is not null)throw new ArgumentException(Usage);action=raw;break;}}
            int targets=(b!=null?1:0)+(w!=null?1:0)+(d!=null?1:0);
            if(verb=="views" && (targets!=0||client is not null)||verb=="focus" && targets!=1||verb=="ui" && (action is null||targets!=0))throw new ArgumentException(Usage);
            var ws=await ShellWorkspace.OpenAsync(api,s??context.Session,ct);
            if(verb=="views"){await output.WriteLineAsync((await ws.CallAsync("client.views",new(),ct)).GetRawText());return 0;}
            var dest=await ResolveAsync(ws,client,ct);var command=new JsonObject{["name"]=action??(w is null?"pane.focus":"window.focus")};
            if(b is not null)command["blockId"]=ws.Block(b,null,null,false).ToString("D");if(w is not null)command["windowId"]=ws.Window(w)!.Value.ToString("D");if(d is not null)command["direction"]=d;
            await output.WriteLineAsync((await Queue(ws,dest,command,ct)).GetRawText());return 0;
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){return 130;}
        catch(MultiplexerAutomationException e){await error.WriteLineAsync(e.Message);return 3;}
        catch(Exception e)when(e is ArgumentException or FormatException){await error.WriteLineAsync(e.Message);return 2;}
        catch(Exception e)when(e is IOException or HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException){await error.WriteLineAsync(e.Message);return 1;}
    }
    private static async Task<Guid> ResolveAsync(ShellWorkspace ws,string? selector,CancellationToken ct)
    {
        var views=(await ws.CallAsync("client.views",new(),ct)).EnumerateArray().Select(v=>new MultiplexerTarget(v.GetProperty("id").GetString()!,v.GetProperty("label").GetString()!)).ToArray();
        selector??=Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_CLIENT");
        if(string.IsNullOrEmpty(selector))return views.Length==1?Guid.Parse(views[0].Id):throw new MultiplexerAutomationException(views.Length==0?MultiplexerAutomationError.TargetNotFound:MultiplexerAutomationError.AmbiguousTarget,"Select one opted-in UI with -C. Run views -s SESSION.");
        if(Guid.TryParse(selector,out var id))return views.Any(v=>Guid.Parse(v.Id)==id)?id:throw new MultiplexerAutomationException(MultiplexerAutomationError.TargetNotFound,"Client ID is stale or outside the selected session.");
        return Guid.Parse(MultiplexerTargetResolver.Resolve(selector,views).Id);
    }
    private static ValueTask<JsonElement> Queue(ShellWorkspace ws,Guid id,JsonObject action,CancellationToken ct)=>ws.CallAsync("client.queue",new(){["clientId"]=id.ToString(),["actions"]=new JsonArray(action)},ct);
}
