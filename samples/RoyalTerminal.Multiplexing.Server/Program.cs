// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;
using RoyalTerminal.Terminal.Transport.Pty;

namespace RoyalTerminal.Multiplexing.Server;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        await using WebApplication app = ServerHost.Build(args);
        await app.RunAsync();
    }
}

internal static class ServerHost
{
    public static WebApplication Build(string[] args, ITerminalTransportFactory? transportFactory = null,
        string? administratorToken = null, string? listenUrl = null)
    {
        string url = listenUrl ?? Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_URL") ?? "http://127.0.0.1:7331";
        ValidateListenAddress(url, Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_ALLOW_REMOTE") == "1");
        string? configured = administratorToken ?? Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN");
        string token = configured ?? AccessTokens.GenerateToken();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        builder.WebHost.UseUrls(url);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 256 * 1024;
            options.Limits.MaxRequestHeaderCount = 64;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });
        AccessTokens tokens = new(token);
        InspectingTerminalTransportFactory inspections = new(transportFactory ?? new CompositeTerminalTransportFactory(
            [new PtyTerminalTransportProvider(), new PipeTerminalTransportProvider()]));
        MultiplexerBroker broker = new(inspections, new MultiplexerOptions { MaxDimension = 512 });
        WorkspaceOperations workspaces = new(broker, inspections);
        ClientRegistry clients = new();
        ApiCatalog catalog = new(workspaces, tokens, clients);
        AutomationService automation = new(catalog, Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_CONFIG"));
        BlockStreamEndpoint streams = new(broker, tokens, clients);
        builder.Services.AddSingleton(broker);
        builder.Services.AddSingleton(workspaces);
        builder.Services.AddSingleton(catalog);
        WebApplication app = builder.Build();
        app.Lifetime.ApplicationStopping.Register(() => broker.DisposeAsync().AsTask().GetAwaiter().GetResult());
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            try
            {
                if (!ServerHttp.IsSameOrigin(context.Request)) throw new ApiException(403, "origin", "Cross-origin requests are disabled.");
                await next(context);
            }
            catch (Exception error) when (!context.Response.HasStarted && error is not OutOfMemoryException)
            {
                (int status, ApiError api) = ServerHttp.Classify(error);
                if (status == 500) app.Logger.LogError(error, "Multiplexer request failed");
                await ServerHttp.WriteAsync(context, api, ServerJsonContext.Default.ApiError, status);
            }
        });
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.UseDefaultFiles();
        app.UseStaticFiles();
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
        if (configured is null)
        {
            Console.WriteLine("RoyalTerminal experimental multiplexer. Keep this process running to retain sessions.");
            Console.WriteLine($"Open {url} and enter this administrator token: {token}");
            Console.WriteLine("Set ROYALTERMINAL_MUX_TOKEN to reuse a token across server starts; terminal processes still end on server shutdown.");
        }
        return app;
    }

    internal static void ValidateListenAddress(string value, bool allowRemote)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new ArgumentException("Use one absolute HTTP(S) listening URL with no path, credentials, query, or fragment.");
        bool loopback = uri.Host == "localhost" || IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? ip) && IPAddress.IsLoopback(ip);
        if (!loopback && !allowRemote)
            throw new ArgumentException("Remote listening requires ROYALTERMINAL_MUX_ALLOW_REMOTE=1 and a trusted TLS deployment.");
    }
}
