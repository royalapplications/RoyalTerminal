// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
namespace RoyalTerminal.Multiplexing.Server;
internal sealed partial class ApiCatalog
{
    private void RegisterTerminalCommands()
    {
        RegisterBlockProtocol();
        string[] fields=["sessionId:string!","blockId:string!","keys:array!"];
        RegisterReader("terminal.encode_keys","Encode a named-key batch without writing to the process. Clients holding an existing controller lease send the resulting base64 bytes over that same attachment.",fields,ServerJsonContext.Default.TerminalTextValue,(a,grant,ct)=>{
            Guid session=Session(a,grant);ct.ThrowIfCancellationRequested();var encoded=_workspaces.Inspection(session,a.Id("blockId")).EncodeKeyPresses(ReadKeyBatch(a));
            return Done(new TerminalTextValue(Convert.ToBase64String(encoded)),ServerJsonContext.Default.TerminalTextValue);});
        RegisterReader("terminal.send_keys","Write a named-key batch once using the authoritative terminal keyboard mode. Requires a control-capable grant and an available control lease.",fields,ServerJsonContext.Default.ApiSuccess,(a,grant,ct)=>{
            Guid session=Session(a,grant),block=a.Id("blockId");ct.ThrowIfCancellationRequested();if(!grant.CanControl)throw new ApiException(403,"forbidden","This capability is read only.");
            var names=ReadKeyBatch(a);var inspection=_workspaces.Inspection(session,block);var descriptor=_workspaces.GetBlock(session,block);
            using var controller=_workspaces.Broker.Attach(session,block,MultiplexerRole.Controller,descriptor.LastSequence);
            var bytes=inspection.EncodeKeyPresses(names);ct.ThrowIfCancellationRequested();if(!_tokens.IsActive(grant))throw new ApiException(403,"expired","The input grant expired.");controller.SendInput(bytes);return new ValueTask<JsonElement>(Success());});
    }
    private static string[] ReadKeyBatch(ApiArguments a)
    {
        if(!a.Value.TryGetProperty("keys",out var keys)||keys.ValueKind!=JsonValueKind.Array||keys.GetArrayLength() is <1 or >64)throw new ArgumentException("keys must be an array of 1–64 named presses.");
        return keys.EnumerateArray().Select(i=>i.ValueKind==JsonValueKind.String?i.GetString()!:throw new ArgumentException("Expected a named key.")).ToArray();
    }
}
