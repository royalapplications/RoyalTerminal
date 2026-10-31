using System.Text.Json;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop;

internal sealed class WorkspaceClient(HttpClient http) : IWorkspaceClient
{
    public async ValueTask<IReadOnlyList<WorkspaceSession>> ListAsync(Uri endpoint, string token, CancellationToken cancellationToken)
    {
        JsonElement response = await new MultiplexingApiClient(http, endpoint, token)
            .SendAsync(HttpMethod.Get, "api/sessions", cancellationToken: cancellationToken);
        return response.Deserialize(DesktopJsonContext.Default.WorkspaceSessionList)?.Sessions
            ?? throw new IOException("The host returned an invalid session list.");
    }

    public async ValueTask<WorkspaceSession> CreateSessionAsync(Uri endpoint, string token, string label, CancellationToken cancellationToken)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new WorkspaceLabel(label), DesktopJsonContext.Default.WorkspaceLabel);
        JsonElement response = await new MultiplexingApiClient(http, endpoint, token)
            .SendAsync(HttpMethod.Post, "api/sessions", payload, cancellationToken);
        return response.Deserialize(DesktopJsonContext.Default.WorkspaceSession)
            ?? throw new IOException("The host returned an invalid session.");
    }

    public async ValueTask<WorkspaceBlock> CreateShellAsync(Uri endpoint, string token, Guid sessionId, string label, CancellationToken cancellationToken)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new WorkspaceLabel(label), DesktopJsonContext.Default.WorkspaceLabel);
        JsonElement response = await new MultiplexingApiClient(http, endpoint, token)
            .SendAsync(HttpMethod.Post, $"api/sessions/{sessionId:D}/blocks", payload, cancellationToken);
        return response.Deserialize(DesktopJsonContext.Default.WorkspaceBlock)
            ?? throw new IOException("The host returned an invalid block.");
    }
}

internal sealed record WorkspaceSessionList(IReadOnlyList<WorkspaceSession> Sessions);
internal sealed record WorkspaceLabel(string Label);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkspaceSessionList))]
[JsonSerializable(typeof(WorkspaceSession))]
[JsonSerializable(typeof(WorkspaceBlock))]
[JsonSerializable(typeof(WorkspaceLabel))]
internal sealed partial class DesktopJsonContext : JsonSerializerContext;
