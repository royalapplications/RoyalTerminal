// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Cli;

internal static class ConfigurationTools
{
    internal static bool Handles(ReadOnlySpan<string> args) => args is ["keymap", ..] or ["config", "check" or "reload", ..];
    internal static async Task<int> RunAsync(string[] args, string? endpoint, string? token, HttpClient http, TextWriter output, TextWriter error, CancellationToken ct)
    {
        try
        {
            bool bindings = args[0] == "keymap";
            string method = bindings ? "lua.actions" : args[1] == "check" ? "lua.check" : "lua.reload";
            if (bindings ? args.Length is not 1 and not 3 || args.Length == 3 && args[1] != "--mode" : args.Length != 2)
                throw new ArgumentException("Usage: keymap [--mode NAME] | config check | config reload. Configuration source is selected by the server, not uploaded.");
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            var api = new MultiplexingApiClient(http,new Uri(endpoint??"http://127.0.0.1:7331/"),token);
            using var empty=JsonDocument.Parse("{}");var manifest=await api.CallAsync(method,empty.RootElement,ct);
            if (!bindings) { await output.WriteLineAsync(manifest.GetRawText()); return 0; }
            var keymap=manifest.GetProperty("keymap");var entries=keymap.GetProperty("bindings"); string? mode=args.Length==3?args[2]:null;
            using var stream=new MemoryStream();
            using(var w=new Utf8JsonWriter(stream))
            {
                w.WriteStartObject(); w.WriteString("generation",manifest.GetProperty("generation").GetString());w.WritePropertyName("modes");keymap.GetProperty("modes").WriteTo(w);
                w.WriteStartArray("bindings");foreach(var b in entries.EnumerateArray())if(mode==null||mode==b.GetProperty("mode").GetString())b.WriteTo(w);
                w.WriteEndArray();w.WriteEndObject();
            }
            await output.WriteLineAsync(System.Text.Encoding.UTF8.GetString(stream.GetBuffer(),0,(int)stream.Length));return 0;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) {return 130;}
        catch(Exception e) when(e is ArgumentException or UriFormatException) {await error.WriteLineAsync(e.Message);return 2;}
        catch(MultiplexingApiException e) when(e.Code=="method_not_found") {await error.WriteLineAsync("The host has not enabled trusted Lua configuration or does not support this operation.");return 1;}
        catch(Exception e) when(e is HttpRequestException or IOException or JsonException or OperationCanceledException) {await error.WriteLineAsync(e.Message);return 1;}
    }
}
