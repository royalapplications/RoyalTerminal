// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class LuaDoCommand
{
    internal const string Usage = "do FILE|-|-e CODE [--action NAME] [key=value ... | --args JSON | --args @FILE]";
    internal static async Task<int> RunAsync(string[] argv, string? endpoint, string? token, HttpClient http,
        TextReader stdin, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (argv.Length == 0 || argv[0] is "-h" or "--help") { await stderr.WriteLineAsync(Usage); return argv.Length == 0 ? 2 : 0; }
        try
        {
            int i = 1;
            string source = argv[0] switch
            {
                "-e" when argv.Length >= 2 => argv[i++], "-e" => throw new ArgumentException("-e requires Lua source."),
                "-" => await ReadBoundedAsync(stdin,ct), _ => await ReadFileAsync(argv[0],ct),
            };
            var fields = new JsonObject(); bool whole = false; string? action = null;
            for (; i < argv.Length; ++i)
            {
                if (argv[i] == "--action") { if (action is not null || ++i == argv.Length || string.IsNullOrWhiteSpace(argv[i])) throw new ArgumentException("Expected one action name."); action=argv[i]; continue; }
                if (argv[i] == "--args")
                {
                    if (whole || fields.Count != 0 || ++i == argv.Length) throw new ArgumentException("--args requires a single JSON object, not key=value pairs.");
                    string text=argv[i].StartsWith('@')?await ReadFileAsync(argv[i][1..],ct):argv[i];
                    using var json=JsonDocument.Parse(text); if(json.RootElement.ValueKind!=JsonValueKind.Object)throw new ArgumentException("--args must be an object.");
                    foreach(var p in json.RootElement.EnumerateObject()) { if(fields.ContainsKey(p.Name))throw new ArgumentException("Duplicate argument: "+p.Name);fields.Add(p.Name,JsonNode.Parse(p.Value.GetRawText())); }
                    whole=true;continue;
                }
                int eq=argv[i].IndexOf('='); if(whole||eq<1)throw new ArgumentException("Expected key=value or --args JSON.");
                string key=argv[i][..eq], value=argv[i][(eq+1)..];if(fields.ContainsKey(key))throw new ArgumentException("Duplicate argument: "+key);
                JsonNode? scalar=JsonValue.Create(value);
                if(value is "true" or "false" or "null")scalar=JsonNode.Parse(value);
                else if(double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n))scalar=JsonValue.Create(n);
                fields.Add(key,scalar);
            }
            if(Encoding.UTF8.GetByteCount(source)>262144 || Encoding.UTF8.GetByteCount(fields.ToJsonString())>262144)throw new ArgumentException("Lua source and arguments each have a 256 KiB limit.");
            var plan=new JsonObject { ["source"]=source,["name"]=argv[0] is "-e" or "-" ? "=(workspace)" : "@"+Path.GetFullPath(argv[0]),["args"]=fields,["action"]=action };
            return await LuaScriptCli.RunAsync([],endpoint,token,http,stderr,ct,plan,stdout);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){return 130;}
        catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException or JsonException){await stderr.WriteLineAsync(ex.Message);return 2;}
    }
    private static async Task<string> ReadFileAsync(string path,CancellationToken ct)
    { using var reader=new StreamReader(path,new UTF8Encoding(false,true),detectEncodingFromByteOrderMarks:true);return await ReadBoundedAsync(reader,ct); }
    private static async Task<string> ReadBoundedAsync(TextReader reader,CancellationToken ct)
    { var result=new StringBuilder();char[] chunk=new char[4096];int n;while((n=await reader.ReadAsync(chunk.AsMemory(),ct))!=0){if(result.Length+n>262144)throw new ArgumentException("Input exceeds 256 KiB.");result.Append(chunk,0,n);}return result.ToString(); }
}
