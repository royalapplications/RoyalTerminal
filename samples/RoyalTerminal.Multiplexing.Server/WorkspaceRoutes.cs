// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;

namespace RoyalTerminal.Multiplexing.Server;

internal static class WorkspaceRoutes
{
    internal static void Map(WebApplication app, MultiplexerBroker broker, WorkspaceOperations workspaces,
        ApiCatalog catalog, AutomationService automation, BlockStreamEndpoint streams, AccessTokens tokens)
    {
        app.MapGet("/healthz", (HttpContext context) => ServerHttp.WriteAsync(context, new ApiSuccess(), ServerJsonContext.Default.ApiSuccess));
        app.MapGet("/api/context", (HttpContext context) =>
        {
            AccessGrant grant = ServerHttp.Authorize(context, tokens);
            return ServerHttp.WriteAsync(context, new CapabilityDescription(grant.IsAdministrator, grant.SessionId, grant.CanControl), ServerJsonContext.Default.CapabilityDescription);
        });
        app.MapGet("/api/methods", (HttpContext context) =>
        {
            _ = ServerHttp.Authorize(context, tokens);
            return ServerHttp.WriteAsync(context, catalog.Describe(), ServerJsonContext.Default.MethodList);
        });
        app.MapGet("/api/config", (HttpContext context) =>
        {
            _ = ServerHttp.Authorize(context, tokens, administrator: true);
            return ServerHttp.WriteAsync(context, automation.Current, MultiplexerAutomationJsonContext.Default.MultiplexerResolvedConfiguration);
        });
        app.MapPut("/api/config", async (HttpContext context) =>
        {
            _ = ServerHttp.Authorize(context, tokens, administrator: true);
            byte[] bytes = await ServerHttp.ReadBytesAsync(context, 1024 * 1024);
            MultiplexerResolvedConfiguration result = await automation.UpdateAsync(bytes, context.RequestAborted);
            await ServerHttp.WriteAsync(context, result, MultiplexerAutomationJsonContext.Default.MultiplexerResolvedConfiguration);
        });
        app.MapPost("/api/actions/{actionId}/invoke", async (HttpContext context) =>
        {
            AccessGrant grant = ServerHttp.Authorize(context, tokens, administrator: true);
            ActionContext request = await ServerHttp.ReadAsync(context, ServerJsonContext.Default.ActionContext);
            string id = context.Request.RouteValues["actionId"]?.ToString() ?? throw new ArgumentException("An action ID is required.");
            MultiplexerActionResult result = await automation.RunAsync(id, request, grant, context.RequestAborted);
            await ServerHttp.WriteAsync(context, result, MultiplexerAutomationJsonContext.Default.MultiplexerActionResult);
        });
        app.MapPost("/api/call", async (HttpContext context) =>
        {
            AccessGrant grant = ServerHttp.Authorize(context, tokens);
            ApiCall request = await ServerHttp.ReadAsync(context, ServerJsonContext.Default.ApiCall);
            if (string.IsNullOrWhiteSpace(request.Method)) throw new ArgumentException("Method is required.");
            JsonElement result = await catalog.InvokeAsync(request.Method, request.Arguments, grant, context.RequestAborted);
            await ServerHttp.WriteAsync(context, result, ServerJsonContext.Default.JsonElement);
        });
        app.MapGet("/api/sessions", (HttpContext context) =>
        {
            AccessGrant grant = ServerHttp.Authorize(context, tokens);
            return ServerHttp.WriteAsync(context, new SessionList(broker.ListSessions().Where(x => grant.CanRead(x.Id)).ToArray()), ServerJsonContext.Default.SessionList);
        });
        app.MapPost("/api/sessions", async (HttpContext context) =>
        {
            _ = ServerHttp.Authorize(context, tokens, administrator: true);
            NewSession request = await ServerHttp.ReadAsync(context, ServerJsonContext.Default.NewSession);
            await ServerHttp.WriteAsync(context, broker.CreateSession(request.Label), ServerJsonContext.Default.MultiplexerSessionDescriptor, 201);
        });
        app.MapGet("/api/sessions/{sessionId}", (HttpContext context) =>
        {
            Guid sessionId = ServerHttp.RouteId(context, "sessionId");
            _ = ServerHttp.Authorize(context, tokens, sessionId);
            return ServerHttp.WriteAsync(context, broker.GetSession(sessionId), ServerJsonContext.Default.MultiplexerSessionDescriptor);
        });
        app.MapDelete("/api/sessions/{sessionId}", async (HttpContext context) =>
        {
            _ = ServerHttp.Authorize(context, tokens, administrator: true);
            await workspaces.CloseSessionAsync(ServerHttp.RouteId(context, "sessionId"));
            await ServerHttp.WriteAsync(context, new ApiSuccess(), ServerJsonContext.Default.ApiSuccess);
        });
        app.MapPost("/api/sessions/{sessionId}/blocks", async (HttpContext context) =>
        {
            _ = ServerHttp.Authorize(context, tokens, administrator: true);
            NewBlock request = await ServerHttp.ReadAsync(context, ServerJsonContext.Default.NewBlock);
            MultiplexerBlockDescriptor block = await workspaces.CreateBlockAsync(ServerHttp.RouteId(context, "sessionId"), request, context.RequestAborted);
            await ServerHttp.WriteAsync(context, block, ServerJsonContext.Default.MultiplexerBlockDescriptor, 201);
        });
        app.MapDelete("/api/sessions/{sessionId}/blocks/{blockId}", async (HttpContext context) =>
        {
            _ = ServerHttp.Authorize(context, tokens, administrator: true);
            await workspaces.CloseBlockAsync(ServerHttp.RouteId(context, "sessionId"), ServerHttp.RouteId(context, "blockId"));
            await ServerHttp.WriteAsync(context, new ApiSuccess(), ServerJsonContext.Default.ApiSuccess);
        });
        app.MapPost("/api/sessions/{sessionId}/blocks/{blockId}/input", async (HttpContext context) =>
        {
            Guid sessionId = ServerHttp.RouteId(context, "sessionId");
            AccessGrant grant = ServerHttp.Authorize(context, tokens, sessionId);
            if (!grant.CanControl) throw new ApiException(403, "forbidden", "This capability is read only.");
            InputRequest input = await ServerHttp.ReadAsync(context, ServerJsonContext.Default.InputRequest);
            workspaces.Input(sessionId, ServerHttp.RouteId(context, "blockId"), input.Data);
            await ServerHttp.WriteAsync(context, new ApiSuccess(), ServerJsonContext.Default.ApiSuccess);
        });
        app.MapGet("/api/sessions/{sessionId}/blocks/{blockId}/capture", (HttpContext context) =>
        {
            Guid sessionId = ServerHttp.RouteId(context, "sessionId");
            _ = ServerHttp.Authorize(context, tokens, sessionId);
            ScreenCapture capture = catalog.Capture(sessionId, ServerHttp.RouteId(context, "blockId"), context.Request.Query["format"].FirstOrDefault() ?? "text");
            return ServerHttp.WriteAsync(context, capture, ServerJsonContext.Default.ScreenCapture);
        });
        app.MapGet("/api/sessions/{sessionId}/blocks/{blockId}/stream", streams.HandleAsync);
    }
}
