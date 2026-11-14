// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers;
using System.Text;
using System.Text.Json;
namespace RoyalTerminal.Multiplexing.Cli;

internal static class TerminalEventEnvelope
{
    // The raw endpoint retains its independent cursor contract; --json presents the script-facing payload nested by scope.
    internal static string Write(Guid session, Guid? block, JsonElement item)
    {
        string name = item.GetProperty("name").GetString() ?? throw new InvalidDataException("An event has no name.");
        if (!name.StartsWith("terminal.",StringComparison.Ordinal)) throw new InvalidDataException("Expected a terminal event.");
        name = name[9..];
        if (name == "program_status_changed" && Get("reason") is { } why && why.GetString() == "removed") name="program_status_removed";
        Guid source = block ?? item.GetProperty("blockId").GetGuid();
        ArrayBufferWriter<byte> buffer=new();
        using (var w=new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();w.WriteString("type","session_event");w.WriteNumber("sequence",item.GetProperty("sequence").GetInt64());
            w.WriteStartObject("payload");w.WriteString("session_id",session);
            w.WriteStartObject("event");w.WriteString("type","block_event");w.WriteStartObject("payload");
            w.WriteString("block_id",source);w.WriteString("creator_name","com.superlogical.terminal");w.WriteString("name",name);
            w.WriteStartObject("payload");
            switch(name)
            {
                case "child_exited":
                    if(Get("exitCode") is { } exit){int code=exit.GetInt32();w.WriteNumber("exit_code",code);if(code!=0)w.WriteString("error",FormattableString.Invariant($"exit status {code}"));}
                    break;
                case "title_changed": Field("text","title");break;
                case "pwd_changed": Field("pwd","pwd");break;
                case "program_status_changed": Field("recordId","id");Field("record","record");Field("reason","reason");break;
                case "program_status_removed": w.WriteStartArray("ids");if(Get("recordId") is { } id)w.WriteStringValue(id.GetString());w.WriteEndArray();Field("reason","reason");break;
                case "desktop_notification": CopyObject("notification");break;
                case "progress_report": CopyObject("progressReport");break;
                case "process_changed": CopyObject("process",includeNull:true);break;
            }
            w.WriteEndObject();w.WriteEndObject();w.WriteEndObject();w.WriteEndObject();w.WriteEndObject();
            void Field(string from,string to){if(Get(from) is { } v){w.WritePropertyName(to);v.WriteTo(w);}}
            void CopyObject(string key,bool includeNull=false){if(Get(key) is {ValueKind:JsonValueKind.Object} o)foreach(var p in o.EnumerateObject())if(includeNull||p.Value.ValueKind!=JsonValueKind.Null)p.WriteTo(w);}
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
        JsonElement? Get(string key)=>item.TryGetProperty(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value:null;
    }
}
