// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Multiplexing.Server;

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkspaceActivityPage))]
[JsonSerializable(typeof(WorkspaceClientEvent))]
internal sealed partial class WorkspaceActivityJson : JsonSerializerContext;
internal sealed record WorkspaceClientEvent(string Principal,string Role,DateTimeOffset ConnectedAt);

internal sealed partial class ApiCatalog
{
    private void RegisterActivityMethods()
    {
        _clients.Activity = _workspaces.Broker.Activity;
        string[] cursor = ["after:integer", "limit:integer", "timeoutMs:integer"];
        Define("server.events", "Default server journal: session creation, destruction and view revision changes.", cursor, WorkspaceActivityJson.Default.WorkspaceActivityPage,
            (a,g,t)=>ReadActivity(a,g,t,session:false,all:false), "Administrator only. A cursor is local to this journal and broker lifetime.");
        Define("server.all_events", "Globally ordered workspace, client and admitted terminal metadata. Never includes raw terminal bytes or credentials.", cursor, WorkspaceActivityJson.Default.WorkspaceActivityPage,
            (a,g,t)=>ReadActivity(a,g,t,session:false,all:true), "Administrator only. Not a filtered copy of a session cursor.");
        RegisterReader("session.events", "Mixed activity for one authorized session, including short-lived windows, terminal metadata and client attachments.", ["sessionId:string!",..cursor], WorkspaceActivityJson.Default.WorkspaceActivityPage,
            (a,g,t)=>ReadActivity(a,g,t,session:true,all:false));
    }

    private async ValueTask<JsonElement> ReadActivity(ApiArguments a,AccessGrant grant,CancellationToken ct,bool session,bool all)
    {
        Guid? id=null;
        if(session) id=Session(a,grant); else ServerHttp.RequireAdministrator(grant);
        long? after=null;
        if(a.Value.TryGetProperty("after",out var value))
        {
            if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt64(out long n)||n<0)throw new ArgumentException("after must be a nonnegative 64-bit integer.");
            after=n;
        }
        var page=await _workspaces.Broker.Activity.ReadAsync(id,all,after,a.Integer("limit",64),a.Integer("timeoutMs",0),ct);
        ct.ThrowIfCancellationRequested();
        if(!_tokens.IsActive(grant))throw new ApiException(403,"expired","The activity grant expired or was revoked while waiting.");
        return Element(page,WorkspaceActivityJson.Default.WorkspaceActivityPage);
    }
}
