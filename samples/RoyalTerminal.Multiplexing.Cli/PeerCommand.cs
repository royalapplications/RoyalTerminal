// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Cli;
internal sealed record PeerCall(string Id,string Method,JsonElement Arguments);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PeerCall))]
internal sealed partial class PeerCliJson : JsonSerializerContext;
internal static class PeerCommand
{
    internal const string Usage="hosts [--json] | -S LABEL sessions | -S LABEL api call METHOD [JSON | @FILE | -]";
    internal static async Task<int> RunAsync(string[] args,MultiplexingApiClient api,JsonElement empty,TextReader input,TextWriter output,CancellationToken ct)
    {
        bool list=args is ["hosts"] or ["hosts","--json"];
        bool sessions=args is ["-S" or "--server",_,"sessions"];
        bool call=args is ["-S" or "--server",_,"api","call",_] or ["-S" or "--server",_,"api","call",_,_];
        if(!list&&!sessions&&!call)throw new ArgumentException(Usage);
        var catalog=await api.CallAsync("server.hosts",empty,ct);var hosts=catalog.GetProperty("hosts");
        if(list){await output.WriteLineAsync(catalog.GetRawText());return 0;}
        string? id=null;foreach(var item in hosts.EnumerateArray())if(item.GetProperty("label").GetString()==args[1]){id=item.GetProperty("id").GetString();break;}
        if(id is null)throw new ArgumentException("Unknown host label.");
        var values=empty;
        if(args.Length==6)
        {
            string raw=args[5];
            if(raw=="-")raw=await Read(input,ct);
            else if(raw.StartsWith('@')){using var f=new StreamReader(raw[1..],new UTF8Encoding(false,true));raw=await Read(f,ct);}
            if(Encoding.UTF8.GetByteCount(raw)>128*1024)throw new ArgumentException("Peer arguments exceed 128 KiB.");
            using var d=JsonDocument.Parse(raw);if(d.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Peer arguments must be an object.");values=d.RootElement.Clone();
        }
        var request=JsonSerializer.SerializeToElement(new PeerCall(id,sessions?"session.list":args[4],values),PeerCliJson.Default.PeerCall);
        var result=await api.CallAsync("server.call_host",request,ct);await output.WriteLineAsync(result.GetRawText());return 0;
    }
    private static async Task<string> Read(TextReader r,CancellationToken ct)
    {var result=new StringBuilder();char[] b=new char[4096];int n;while((n=await r.ReadAsync(b,ct))!=0){if(result.Length+n>128*1024)throw new ArgumentException("Peer arguments exceed 128 KiB.");result.Append(b,0,n);}return result.ToString();}
}
