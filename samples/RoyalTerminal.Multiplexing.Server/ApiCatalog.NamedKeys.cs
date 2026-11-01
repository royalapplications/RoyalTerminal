// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
namespace RoyalTerminal.Multiplexing.Server;
internal sealed partial class ApiCatalog
{
    private void RegisterTerminalCommands()
    {
        RegisterReader("terminal.send_keys","Encode a batch using the authoritative terminal keyboard mode, then write it once. Controller capability and an available control lease are required.",["sessionId:string!","blockId:string!","keys:array!"],ServerJsonContext.Default.ApiSuccess,(a,grant,ct)=>
        {
            Guid session=Session(a,grant),block=a.Id("blockId");ct.ThrowIfCancellationRequested();
            if(!grant.CanControl)throw new ApiException(403,"forbidden","This capability is read only.");
            if(!a.Value.TryGetProperty("keys",out var keys)||keys.ValueKind!=JsonValueKind.Array||keys.GetArrayLength() is <1 or >64)throw new ArgumentException("keys must be a bounded array.");
            var names=keys.EnumerateArray().Select(i=>i.ValueKind==JsonValueKind.String?i.GetString()!:throw new ArgumentException("Expected a named key.")).ToArray();
            var inspection=_workspaces.Inspection(session,block);var descriptor=_workspaces.GetBlock(session,block);
            using var controller=_workspaces.Broker.Attach(session,block,MultiplexerRole.Controller,descriptor.LastSequence);
            var bytes=inspection.EncodeKeyPresses(names);ct.ThrowIfCancellationRequested();
            if(!_tokens.IsActive(grant))throw new ApiException(403,"expired","The input grant expired.");
            controller.SendInput(bytes);return new ValueTask<JsonElement>(Success());
        });
    }
}
