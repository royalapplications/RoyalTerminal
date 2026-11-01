// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

internal sealed record KeyBatch(Guid SessionId,Guid BlockId,string[] Keys);
internal sealed record EventRead(Guid? SessionId,Guid? BlockId,long? After,int TimeoutMs,int Limit=64);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KeyBatch)),JsonSerializable(typeof(EventRead)),JsonSerializable(typeof(JsonElement))]
internal partial class ToolsJson : JsonSerializerContext;

internal static class TerminalTools
{
    internal const string EventUsage="events [--all | -s SESSION [BLOCK] | SESSION [BLOCK]] [--json] [--after N] [--count N]";
    internal static async Task<int> RunAsync(string verb,string[] args,string? url,string? token,HttpClient http,TextWriter output,TextWriter error,CancellationToken ct)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(token))throw new ArgumentException("A workspace credential is required.");
            var api=new MultiplexingApiClient(http,new Uri(url??"http://127.0.0.1:7331"),token);
            var resolver=new CliTargetResolver(api);
            if(verb=="send-key")
            {
                if(args.Length is <3 or >66)throw new ArgumentException("send-key SESSION BLOCK KEY [KEY …] (1–64 keys)");
                var sid=await resolver.SessionAsync(args[0],ct); var bid=await resolver.BlockAsync(sid,args[1],ct);
                var input=JsonSerializer.SerializeToElement(new KeyBatch(sid,bid,args[2..]),ToolsJson.Default.KeyBatch);
                await output.WriteLineAsync((await api.CallAsync("terminal.send_keys",input,ct)).GetRawText()); return 0;
            }
            List<string> targets=[]; string? selected=null; bool all=false,envelope=false; long? after=null; int? count=null;
            for(int i=0;i<args.Length;i++)
            {
                string arg=args[i];
                switch(arg)
                {
                    case "--all" when !all: all=true; break;
                    case "--json" when !envelope: envelope=true; break;
                    case "-s" or "--session" when selected is null:
                        selected=Operand(ref i); break;
                    case "--after" when after is null:
                        if(!long.TryParse(Operand(ref i),NumberStyles.None,CultureInfo.InvariantCulture,out long n))throw new ArgumentException(EventUsage);
                        after=n; break;
                    case "--count" when count is null:
                        if(!int.TryParse(Operand(ref i),NumberStyles.None,CultureInfo.InvariantCulture,out int c)||c<1)throw new ArgumentException(EventUsage);
                        count=c; break;
                    default: if(arg.StartsWith('-'))throw new ArgumentException(EventUsage); targets.Add(arg); break;
                }
            }
            if(all&&(selected is not null||targets.Count!=0)||targets.Count>(selected is null?2:1))throw new ArgumentException(EventUsage);
            string? selector=selected??targets.FirstOrDefault(); Guid? session=selector is null?null:await resolver.SessionAsync(selector,ct);
            string? blockSelector=targets.ElementAtOrDefault(selected is null?1:0);
            Guid? block=blockSelector is null?null:await resolver.BlockAsync(session!.Value,blockSelector,ct);
            bool mixed=!block.HasValue&&(selected is not null||!session.HasValue);
            string method=all?"server.all_events":!session.HasValue?"server.events":block.HasValue?"terminal.events":mixed?"session.events":"session.terminal_events";
            while(true)
            {
                var request=JsonSerializer.SerializeToElement(new EventRead(session,block,after,after.HasValue?15000:0),ToolsJson.Default.EventRead);
                var page=await api.CallAsync(method,request,ct);
                long next=page.GetProperty("nextSequence").GetInt64(),expected=after??next;
                foreach(var item in page.GetProperty("events").EnumerateArray())
                {
                    if(after is null||item.GetProperty("sequence").GetInt64()!=checked(++expected))throw new InvalidDataException("Incomplete event journal.");
                    string line=!envelope?JsonSerializer.Serialize(item,ToolsJson.Default.JsonElement):mixed?ActivityEnvelope.Write(item):TerminalEventEnvelope.Write(session!.Value,block,item);
                    await output.WriteLineAsync(line); await output.FlushAsync(ct);
                    if(count.HasValue&&--count==0)return 0;
                }
                if(expected!=next)throw new InvalidDataException("Inconsistent event cursor.");
                after=next;if(page.GetProperty("ended").GetBoolean())return 0;
            }
            string Operand(ref int index) => ++index<args.Length && args[index].Length!=0 && !args[index].StartsWith('-') ? args[index] : throw new ArgumentException(EventUsage);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){return 130;}
        catch(Exception ex) when(ex is ArgumentException or UriFormatException or MultiplexerAutomationException){await error.WriteLineAsync(ex.Message);return 2;}
        catch(Exception ex) when(ex is IOException or HttpRequestException or JsonException or OperationCanceledException){await error.WriteLineAsync(ex.Message);return 1;}
    }
}
