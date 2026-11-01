// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
namespace RoyalTerminal.Multiplexing.Server;
internal sealed partial class ApiCatalog
{
    private void RegisterTerminalCommands()
    {
        RegisterBlockProtocol(); RegisterSessionTools(); RegisterActivityMethods(); RegisterRemoteViews();
        string[] fields=["sessionId:string!","blockId:string!","keys:array!"];
        RegisterReader("terminal.encode_keys","Encode named presses without writing; an existing controller can send the base64 result over its own leased socket.",fields,ServerJsonContext.Default.TerminalTextValue,(a,grant,ct)=>{
            Guid session=Session(a,grant);ct.ThrowIfCancellationRequested();var bytes=_workspaces.Inspection(session,a.Id("blockId")).EncodeKeyPresses(ReadKeyBatch(a));
            return Done(new TerminalTextValue(Convert.ToBase64String(bytes)),ServerJsonContext.Default.TerminalTextValue);});
        Define("terminal.send_keys","Encode a complete batch under the canonical keyboard mode and write it once.",fields,ServerJsonContext.Default.ApiSuccess,(a,grant,ct)=>{
            Guid session=Session(a,grant),block=a.Id("blockId");ct.ThrowIfCancellationRequested();if(!grant.CanControl)throw new ApiException(403,"forbidden","This capability is read only.");
            var names=ReadKeyBatch(a);var inspection=_workspaces.Inspection(session,block);var descriptor=_workspaces.GetBlock(session,block);
            using var controller=_workspaces.Broker.Attach(session,block,MultiplexerRole.Controller,descriptor.LastSequence);
            var bytes=inspection.EncodeKeyPresses(names);ct.ThrowIfCancellationRequested();if(!_tokens.IsActive(grant))throw new ApiException(403,"expired","The input grant expired.");controller.SendInput(bytes);return new ValueTask<JsonElement>(Success());
        },"Writes to a terminal; requires a session-scoped controller grant or administrator and an available exclusive input lease. Malformed batches cause no partial writes.");
    }
    private static string[] ReadKeyBatch(ApiArguments a)
    {
        if(!a.Value.TryGetProperty("keys",out var keys)||keys.ValueKind!=JsonValueKind.Array||keys.GetArrayLength() is <1 or >64)throw new ArgumentException("keys must be an array of 1–64 named presses.");
        return keys.EnumerateArray().Select(i=>i.ValueKind==JsonValueKind.String?i.GetString()!:throw new ArgumentException("Expected a named key.")).ToArray();
    }
}
