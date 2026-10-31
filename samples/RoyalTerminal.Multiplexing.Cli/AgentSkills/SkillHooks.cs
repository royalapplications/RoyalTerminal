// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class SkillHooks
{
    private const string Label="RoyalTerminal: report local workspace identity";
    internal static string Plan(string path,string action,bool dry)
    {
        string? original=SkillFiles.Read(path);
        var doc=original is null?new JsonObject(): JsonNode.Parse(original, documentOptions:new JsonDocumentOptions {AllowDuplicateProperties=false}) as JsonObject ?? throw new JsonException("Invalid settings object: "+path);
        var hooks=Object(doc,"hooks");var start=Array(hooks,"SessionStart");
        var expected=new JsonObject{["type"]="command",["command"]=Command(),["statusMessage"]=Label};
        int matches=0;bool current=false;
        foreach(var group in start.OfType<JsonObject>()) if(group["hooks"] is JsonArray handlers)
            foreach(var entry in handlers.OfType<JsonObject>()) if(IsOurs(entry)) {matches++;current|=JsonNode.DeepEquals(entry,expected);}
        string state=matches==0?"missing":matches==1&&current?"current":"outdated";
        if(action=="status" || action=="setup"&&state=="current" || action=="remove"&&matches==0) return state;
        if(action=="remove"||matches>0)
        {
            for(int i=start.Count-1;i>=0;i--) if(start[i] is JsonObject g && g["hooks"] is JsonArray handlers)
            {
                bool removed=false;
                for(int j=handlers.Count-1;j>=0;j--) if(handlers[j] is JsonObject h && IsOurs(h)){handlers.RemoveAt(j);removed=true;}
                if(removed&&handlers.Count==0&&g.Count==1)start.RemoveAt(i);
            }
        }
        if(action=="setup") start.Add(new JsonObject{["hooks"]=new JsonArray(expected)});
        if(start.Count==0)hooks.Remove("SessionStart");if(hooks.Count==0)doc.Remove("hooks");
        if(!dry)SkillFiles.Change(path,original,doc.ToJsonString(new JsonSerializerOptions{WriteIndented=true,TypeInfoResolver=System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine()})+"\n");
        return action=="remove"?"remove":matches==0?"create":"update";
    }
    private static bool IsOurs(JsonObject h)=>h.Count==3&&h["type"]?.ToString()=="command"&&h["statusMessage"]?.ToString()==Label&&h["command"] is JsonValue;
    private static JsonObject Object(JsonObject o,string key)
    { if(!o.ContainsKey(key))o[key]=new JsonObject();return o[key] as JsonObject??throw new JsonException("Expected an object for "+key); }
    private static JsonArray Array(JsonObject o,string key)
    { if(!o.ContainsKey(key))o[key]=new JsonArray();return o[key] as JsonArray??throw new JsonException("Expected an array for "+key); }
    private static string Command()
    {
        string exe=Environment.ProcessPath??throw new IOException("Cannot resolve the CLI launcher.");
        var parts=new List<string>{exe};
        if(Path.GetFileNameWithoutExtension(exe).Equals("dotnet",StringComparison.OrdinalIgnoreCase))parts.Add(typeof(SkillHooks).Assembly.Location);
        parts.Add("skill");parts.Add("context");
        if(parts.Any(p=>p.Length==0||p.Contains('\0')||p.Contains('\r')||p.Contains('\n')))throw new IOException("Invalid launcher path.");
        if(OperatingSystem.IsWindows())
        {
            string body="& "+string.Join(' ',parts.Select(p=>"'"+p.Replace("'","''")+"'"));
            return "powershell.exe -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(body));
        }
        return string.Join(' ',parts.Select(p=>"'"+p.Replace("'","'\\''")+"'"));
    }
}
