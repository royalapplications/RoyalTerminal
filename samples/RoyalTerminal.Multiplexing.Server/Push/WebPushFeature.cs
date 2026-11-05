// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Multiplexing.Server;

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PushSettings))][JsonSerializable(typeof(PushRequest))][JsonSerializable(typeof(PushReceipt))]
internal partial class PushJson : JsonSerializerContext;

internal static class WebPushFeature
{
    internal static void Configure(WebApplicationBuilder builder,WorkspaceOperations workspaces,AccessTokens tokens)
    {
        string? file=builder.Configuration["ROYALTERMINAL_MUX_PUSH_KEY"];
        if(string.IsNullOrEmpty(file))return;
        if(!Path.IsPathFullyQualified(file)||!File.Exists(file)||new FileInfo(file).Length>8192)throw new ArgumentException("Select an existing absolute private VAPID PEM file (at most 8 KiB).");
        string path=Path.GetFullPath(file),root=Path.GetFullPath(builder.Environment.WebRootPath??Path.Combine(AppContext.BaseDirectory,"wwwroot"));
        string relative=Path.GetRelativePath(root,path);
        if(!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative)) throw new ArgumentException("Never place the VAPID key under the web root.");
        if(!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.OtherRead|UnixFileMode.OtherWrite))!=0) throw new ArgumentException("Restrict the VAPID key to its owner (chmod 600).");
        string contact=builder.Configuration["ROYALTERMINAL_MUX_PUSH_CONTACT"]??throw new ArgumentException("Web Push requires an operator contact.");
        builder.Services.AddSingleton<VapidSender>(sp=>{
            var key=ECDsa.Create();try {key.ImportFromPem(File.ReadAllText(path));key.SignData(new byte[]{0},HashAlgorithmName.SHA256);
                return new(key,contact,new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false,PooledConnectionLifetime=TimeSpan.FromMinutes(5)});
            }catch{key.Dispose();throw;}});
        builder.Services.AddSingleton(sp=>new WorkspacePush(workspaces,tokens,sp.GetRequiredService<VapidSender>(),TimeProvider.System));
        builder.Services.AddHostedService(sp=>sp.GetRequiredService<WorkspacePush>());
    }
    internal static void Map(WebApplication app,AccessTokens tokens)
    {
        var push=app.Services.GetService<WorkspacePush>();
        app.MapGet("/api/push/settings",(HttpContext c)=>{_=ServerHttp.Authorize(c,tokens);return ServerHttp.WriteAsync(c,new PushSettings(push!=null,push?.PublicKey),PushJson.Default.PushSettings);});
        app.MapPost("/api/push/watch",async(HttpContext c)=>{var grant=ServerHttp.Authorize(c,tokens);if(push==null)throw new ApiException(404,"push_disabled","Background push is not configured.");var r=await ServerHttp.ReadAsync(c,PushJson.Default.PushRequest);var receipt=await push.SubscribeAsync(r,grant,c.RequestAborted);await ServerHttp.WriteAsync(c,receipt,PushJson.Default.PushReceipt,201);});
        app.MapDelete("/api/push/watch/{id:guid}",(HttpContext c)=>{var grant=ServerHttp.Authorize(c,tokens);push?.Remove(ServerHttp.RouteId(c,"id"),grant);return ServerHttp.WriteAsync(c,new ApiSuccess(),ServerJsonContext.Default.ApiSuccess);});
    }
}
