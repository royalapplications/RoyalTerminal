// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;
using System.Text;
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Server;

/// <summary>Outbound routes require a private credential pinned to a stable host ID and endpoint. Only the authentication preflight can fail over.</summary>
internal sealed class WorkspacePeerRouter : IDisposable
{
    private sealed record Route(Uri Origin,string TokenFile,HttpClient Http);
    private readonly Dictionary<(string Id,string Address),Route> _routes = [];
    private readonly SemaphoreSlim _available = new(16);

    internal WorkspacePeerRouter(string? pinFile)
    {
        if(string.IsNullOrEmpty(pinFile)) return;
        if(!Path.IsPathFullyQualified(pinFile)) throw new ArgumentException("Peer pins require an absolute local filename.");
        using var input=new FileStream(pinFile,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        byte[] source=new byte[128*1024+1];int n=input.ReadAtLeast(source,source.Length,throwOnEndOfStream:false);
        if(n==source.Length)throw new InvalidDataException("Peer pins exceed 128 KiB.");
        var pins=JsonSerializer.Deserialize(source.AsSpan(0,n),PeerWire.Default.PeerCredentialPinArray);
        if(pins is null||pins.Length>256)throw new InvalidDataException("Invalid peer pins.");
        try
        {
            foreach(var pin in pins)
            {
                if(pin is null || pin.Id is null || pin.Endpoint is null || string.IsNullOrWhiteSpace(pin.TokenFile) || !Path.IsPathFullyQualified(pin.TokenFile))throw new InvalidDataException("Incomplete peer credential pin.");
                var key=(WorkspacePeers.Name(pin.Id),WorkspacePeers.Normalize(pin.Endpoint));
                if(_routes.ContainsKey(key))throw new InvalidDataException("Duplicate peer credential pin.");
                HttpMessageHandler handler;Uri origin;
                if(key.Item2.StartsWith("unix:",StringComparison.Ordinal))
                {
                    using var local=new UnixWorkspaceEndpoint(key.Item2[5..]);handler=local.CreateHandler();origin=new("http://localhost/");
                }
                else
                {
                    handler=new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false,ConnectTimeout=TimeSpan.FromSeconds(3),PooledConnectionLifetime=TimeSpan.FromMinutes(3)};
                    origin=new(key.Item2);
                }
                _routes.Add(key,new(origin,pin.TokenFile,new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan}));
            }
        }
        catch { Dispose(); throw; }
    }
    internal async ValueTask<JsonElement> CallAsync(WorkspacePeers peers,string id,string method,JsonElement args,CancellationToken ct)
    {
        var peer=peers.Hosts.FirstOrDefault(p=>p.Id==id)??throw new ApiException(404,"unknown_peer","Unknown workspace peer identity.");
        if(method.Length is 0 or >160||method.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ('.' or '_')))throw new ArgumentException("Invalid peer method name.");
        if(method is "server.call_host" or "lua.invoke" or "lua.key")throw new ApiException(403,"peer_recursion","Nested forwarding and remote Lua execution are not available through peer routing.");
        if(!await _available.WaitAsync(0,ct))throw new ApiException(429,"peer_busy","Too many simultaneous peer calls.");
        try
        {
            bool pinned=false;
            foreach(var address in peer.Endpoints)
            {
                if(!_routes.TryGetValue((id,address),out var route))continue;
                pinned=true;var token=await ReadTokenAsync(route.TokenFile,ct);
                var client=new MultiplexingApiClient(route.Http,route.Origin,token,1024*1024);
                using(var probe=CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    probe.CancelAfter(TimeSpan.FromSeconds(3));
                    try
                    {
                        var capability=await client.SendAsync(HttpMethod.Get,"api/context",cancellationToken:probe.Token);
                        if(capability.ValueKind!=JsonValueKind.Object || !capability.TryGetProperty("administrator",out var admin) || admin.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            throw new InvalidDataException();
                    }
                    catch(OperationCanceledException) when(!ct.IsCancellationRequested){continue;}
                    catch(MultiplexingApiException e){throw Remote(e);}
                    catch(HttpRequestException e) when(e.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError){continue;}
                    catch(Exception e) when(e is HttpRequestException or IOException or JsonException){throw new ApiException(502,"peer_handshake_failed","Peer negotiation failed; no application call was dispatched.");}
                }
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
                try {return await client.CallAsync(method,args,deadline.Token);}
                catch(MultiplexingApiException e){throw Remote(e);}
                catch(Exception e) when(!ct.IsCancellationRequested && e is (OperationCanceledException or IOException or HttpRequestException or JsonException))
                {throw new ApiException(502,"peer_outcome_unknown","Peer call did not return a complete response. It may have run and was not retried.");}
            }
            throw pinned?new ApiException(503,"peer_unavailable","No pinned endpoint answered the preflight."):new ApiException(403,"peer_unpinned","No local credential is pinned for this host and its declared endpoints.");
        }
        finally{_available.Release();}
    }
    private static ApiException Remote(MultiplexingApiException error)
    {
        int code=(int)(error.StatusCode??HttpStatusCode.BadGateway);
        return new(code is >=400 and <=599?code:502,"peer_rejected","The peer returned an error. The call was not retried.");
    }
    private static async Task<string> ReadTokenAsync(string path,CancellationToken ct)
    {
        try
        {
            await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            const UnixFileMode nonOwner=UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute;
            if(!OperatingSystem.IsWindows()&&(File.GetUnixFileMode(file.SafeFileHandle)&nonOwner)!=0)throw new ApiException(503,"peer_credential_unavailable","Peer credential file must be private to its owner.");
            byte[] data=new byte[515];int n=await file.ReadAtLeastAsync(data,data.Length,false,ct);
            while(n>0&&data[n-1] is 10 or 13)n--;
            if(n is <32 or >512 || data.AsSpan(0,n).ContainsAnyExceptInRange((byte)'!',(byte)'~'))throw new InvalidDataException();
            return Encoding.ASCII.GetString(data,0,n);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){throw new ApiException(503,"peer_credential_unavailable","Cannot read a valid local peer credential.");}
    }
    public void Dispose(){foreach(var route in _routes.Values)route.Http.Dispose();_routes.Clear();_available.Dispose();}
}
