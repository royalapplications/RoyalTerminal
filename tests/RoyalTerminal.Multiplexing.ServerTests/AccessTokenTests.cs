using RoyalTerminal.Multiplexing.Server;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class AccessTokenTests
{
    private const string Administrator = "administrator-token-with-at-least-32-characters";

    [Fact]
    public void ScopedCapabilitiesCannotPromoteThemselvesAndRevokeImmediately()
    {
        AccessTokens tokens = new(Administrator);
        Guid session = Guid.NewGuid();
        ShareCreated share = tokens.Create(session, new("observer", 5));
        AccessGrant grant = Assert.IsType<AccessGrant>(tokens.Resolve(share.Token));
        Assert.False(grant.IsAdministrator);
        Assert.False(grant.CanControl);
        Assert.True(grant.CanRead(session));
        Assert.False(grant.CanRead(Guid.NewGuid()));
        Assert.Single(tokens.List(session).Shares);
        Assert.False(tokens.Revoke(Guid.NewGuid(), share.Id));
        Assert.True(tokens.Revoke(session, share.Id));
        Assert.False(tokens.IsActive(grant));
        Assert.Null(tokens.Resolve(share.Token));
        Assert.True(tokens.Resolve(Administrator)!.IsAdministrator);
    }

    [Fact]
    public void ExpiredCapabilitiesFailAndArePurged()
    {
        ManualClock clock = new();
        AccessTokens tokens = new(Administrator, clock);
        ShareCreated share = tokens.Create(Guid.NewGuid(), new("controller", 1));
        Assert.True(tokens.Resolve(share.Token)!.CanControl);
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Null(tokens.Resolve(share.Token));
        Assert.Empty(tokens.List(share.SessionId).Shares);
    }

    [Theory]
    [InlineData("owner", 60)]
    [InlineData("observer", 0)]
    [InlineData("controller", 1441)]
    public void InvalidRolesAndLifetimeAreRejected(string role, int lifetime)
        => Assert.Throws<ArgumentException>(() => new AccessTokens(Administrator).Create(Guid.NewGuid(), new(role, lifetime)));

    [Fact]
    public void ListenAddressesRequireDeliberateRemoteConfiguration()
    {
        ServerHost.ValidateListenAddress("http://127.0.0.1:7331", false);
        ServerHost.ValidateListenAddress("http://localhost:7331", false);
        Assert.Throws<ArgumentException>(() => ServerHost.ValidateListenAddress("http://0.0.0.0:7331", false));
        Assert.Throws<ArgumentException>(() => ServerHost.ValidateListenAddress("https://user:password@example.com/", true));
        Assert.Throws<ArgumentException>(() => ServerHost.ValidateListenAddress("https://example.com/path", true));
        ServerHost.ValidateListenAddress("https://example.com:7331", true);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
