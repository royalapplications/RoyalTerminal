// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers;
using System.Text;
using System.Text.Json;
namespace RoyalTerminal.Multiplexing.Cli;

internal static class ActivityEnvelope
{
    internal static string Write(JsonElement e)
    {
        string type=e.GetProperty("type").GetString()!;
        bool local=e.GetProperty("scope").GetString()=="session";
        Guid? session=e.TryGetProperty("sessionId",out var sid)&&sid.ValueKind==JsonValueKind.String?sid.GetGuid():null;
        ArrayBufferWriter<byte> buffer=new();
        using(var w=new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();w.WriteString("type",local?"session_event":type);w.WriteNumber("sequence",e.GetProperty("sequence").GetInt64());
            if(type=="block_event")
            {
                using var block=JsonDocument.Parse(TerminalEventEnvelope.Write(session??throw new InvalidDataException("Missing session"),e.GetProperty("blockId").GetGuid(),e.GetProperty("detail")));
                w.WritePropertyName("payload");block.RootElement.GetProperty("payload").WriteTo(w);
            }
            else
            {
                if(local){w.WriteStartObject("payload");Field("sessionId","session_id");w.WriteStartObject("event");w.WriteString("type",type);}
                w.WriteStartObject("payload");
                if(type=="session_created"){w.WriteStartObject("session");Field("sessionId","session_id");Field("label","label");w.WriteEndObject();}
                else if(type=="session_destroyed")Field("sessionId","session_id");
                else if(type=="client_joined"){w.WriteStartObject("client");Client();w.WriteEndObject();}
                else if(type.StartsWith("client_",StringComparison.Ordinal))Client();
                else{Field("label","label");Field("windowId","window_id");Field("blockId","block_id");Field("generation","generation");Field("hash","hash");if(type=="session_view_changed")Field("revision","revision");}
                w.WriteEndObject();if(local){w.WriteEndObject();w.WriteEndObject();}
            }
            w.WriteEndObject();
            void Client(){Field("clientId","client_id");if(e.TryGetProperty("detail",out var d)){w.WriteString("principal",d.GetProperty("principal").GetString());w.WriteString("role",d.GetProperty("role").GetString());w.WriteString("connected_at",d.GetProperty("connectedAt").GetDateTimeOffset());}}
            void Field(string key,string name){if(e.TryGetProperty(key,out var v)&&v.ValueKind!=JsonValueKind.Null){w.WritePropertyName(name);v.WriteTo(w);}}
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
