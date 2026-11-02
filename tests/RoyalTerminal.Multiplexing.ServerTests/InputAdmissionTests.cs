using Microsoft.AspNetCore.Http;
using RoyalTerminal.Multiplexing.Server;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class InputAdmissionTests
{
    [Theory]
    [InlineData("controller", "expired", true)]
    [InlineData("observer", "forbidden", false)]
    public async Task InputCannotOutliveItsCapability(string role, string code, bool expectRead)
    {
        var session = Guid.NewGuid();
        var tokens = new AccessTokens(AccessTokens.GenerateToken());
        var share = tokens.Create(session, new(role));
        using var body = new RevokingStream(() => tokens.Revoke(session, share.Id));
        var ctx = new DefaultHttpContext();
        ctx.Request.RouteValues["sessionId"] = session;
        ctx.Request.RouteValues["blockId"] = Guid.NewGuid();
        ctx.Request.Headers.Authorization = "Bearer " + share.Token;
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = body;
        bool sent = false;
        var error = await Assert.ThrowsAsync<ApiException>(() => WorkspaceRoutes.WriteInputAsync(ctx, tokens, (_, _, _) => sent = true));
        Assert.Equal(403, error.Status);
        Assert.Equal(code, error.Code);
        Assert.Equal(expectRead, body.WasRead);
        Assert.False(sent);
    }

    private sealed class RevokingStream(Action revoke) : MemoryStream("{\"data\":\"YQ==\"}"u8.ToArray())
    {
        internal bool WasRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (!WasRead) { WasRead = true; revoke(); }
            return base.ReadAsync(buffer, token);
        }
    }
}
