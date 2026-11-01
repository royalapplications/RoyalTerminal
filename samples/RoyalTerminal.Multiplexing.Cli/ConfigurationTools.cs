// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Cli;

internal static class ConfigurationTools
{
    internal static bool Handles(ReadOnlySpan<string> a)=>a is ["keymap",..] or ["hosts",..] or ["-S" or "--server",..] or ["config","check" or "reload",..] or ["actions","--lua" or "--describe",..];
    internal static async Task<int> RunAsync(string[] args,string? endpoint,string? token,HttpClient http,TextWriter output,TextWriter error,CancellationToken ct)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            var api=new MultiplexingApiClient(http,new Uri(endpoint??"http://127.0.0.1:7331/"),token);
            using var empty=JsonDocument.Parse("{}");
            if(args[0] is "hosts" or "-S" or "--server")return await PeerCommand.RunAsync(args,api,empty.RootElement,Console.In,output,ct);
            if(args[0]=="actions")
            {
                if(args is not ["actions","--lua"] and not ["actions","--describe",_])throw new ArgumentException("Usage: actions --lua | actions --describe NAME");
                var manifest=await api.CallAsync("lua.actions",empty.RootElement,ct);
                if(!manifest.TryGetProperty("actionDetails",out var details))throw new ArgumentException("This host does not advertise typed action descriptors.");
                if(args.Length==3){if(!details.TryGetProperty(args[2],out var action))throw new ArgumentException("Unknown configured Lua action.");await output.WriteLineAsync(action.GetRawText());}
                else await output.WriteLineAsync(details.GetRawText());
                return 0;
            }
            bool bindings=args[0]=="keymap";string method=bindings?"lua.actions":args[1]=="check"?"lua.check":"lua.reload";
            if(bindings?args.Length is not 1 and not 3 || args.Length==3 && args[1]!="--mode":args.Length!=2)throw new ArgumentException("Usage: keymap [--mode NAME] | config check | config reload.");
            var configuration=await api.CallAsync(method,empty.RootElement,ct);
            if(!bindings){await output.WriteLineAsync(configuration.GetRawText());return 0;}
            var keymap=configuration.GetProperty("keymap");var entries=keymap.GetProperty("bindings");string? mode=args.Length==3?args[2]:null;
            using var stream=new MemoryStream();using(var w=new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();w.WriteString("generation",configuration.GetProperty("generation").GetString());w.WritePropertyName("modes");keymap.GetProperty("modes").WriteTo(w);
                w.WriteStartArray("bindings");foreach(var b in entries.EnumerateArray())if(mode==null||mode==b.GetProperty("mode").GetString())b.WriteTo(w);
                w.WriteEndArray();w.WriteEndObject();
            }
            await output.WriteLineAsync(System.Text.Encoding.UTF8.GetString(stream.GetBuffer(),0,(int)stream.Length));return 0;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){return 130;}
        catch(Exception e) when(e is ArgumentException or UriFormatException){await error.WriteLineAsync(e.Message);return 2;}
        catch(MultiplexingApiException e)when(e.Code=="method_not_found"){await error.WriteLineAsync("The host does not support this operation or has not enabled trusted configuration.");return 1;}
        catch(Exception e) when(e is HttpRequestException or IOException or JsonException or OperationCanceledException or UnauthorizedAccessException){await error.WriteLineAsync(e.Message);return 1;}
    }
}
