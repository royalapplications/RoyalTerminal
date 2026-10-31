// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Net;
using RoyalTerminal.Multiplexing.Inspection;
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
        bool allowRemote = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_ALLOW_REMOTE") == "1";
        ValidateListenAddress(url, allowRemote);
        string? configured = administratorToken ?? Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN");
        string token = configured ?? AccessTokens.GenerateToken();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        // Kestrel's explicitly configured endpoints override UseUrls. Enforce the same network policy
        // there, while permitting the personal service's explicit local Unix socket.
        foreach (IConfigurationSection section in builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            string? configuredUrl = section["Url"];
            if (configuredUrl is null) throw new ArgumentException("A configured Kestrel endpoint requires a URL.");
            if (configuredUrl.StartsWith("http://unix:/", StringComparison.Ordinal))
            {
                string path = configuredUrl["http://unix:".Length..];
                if (OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(path) || path.IndexOfAny(['?', '#', '\0']) >= 0) throw new ArgumentException("Invalid Unix socket endpoint.");
            }
            else ValidateListenAddress(configuredUrl, allowRemote);
        }
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
        MultiplexerBroker broker = new(inspections, new MultiplexerOptions
        {
            MaxDimension = 512,
            MaxColdReplayBytes = ColdReplayBudget(builder.Configuration["ROYALTERMINAL_MUX_COLD_REPLAY_MIB"]),
        });
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
        app.UseDefaultFiles(); app.UseStaticFiles();
        WorkspaceRoutes.Map(app, broker, workspaces, catalog, automation, streams, tokens);
        if (configured is null)
        {
            Console.WriteLine("RoyalTerminal experimental multiplexer. Keep this process running to retain sessions.");
            Console.WriteLine($"Configured endpoint: {url}; administrator token: {token}");
            Console.WriteLine("Set ROYALTERMINAL_MUX_TOKEN to reuse a token across server starts; terminal processes still end on server shutdown.");
        }
        return app;
    }

    internal static int ColdReplayBudget(string? value) => string.IsNullOrEmpty(value) ? 0 :
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int mib) && mib is >= 0 and <= 32
            ? mib * 1024 * 1024 : throw new ArgumentException("ROYALTERMINAL_MUX_COLD_REPLAY_MIB must be an integer from 0 through 32 per block.");

    internal static void ValidateListenAddress(string value, bool allowRemote)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new ArgumentException("Use one absolute HTTP(S) listening URL with no path, credentials, query, or fragment.");
        bool loopback = uri.Host == "localhost" || IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? ip) && IPAddress.IsLoopback(ip);
        if (!loopback && !allowRemote) throw new ArgumentException("Remote listening requires ROYALTERMINAL_MUX_ALLOW_REMOTE=1 and a trusted TLS deployment.");
    }
}
