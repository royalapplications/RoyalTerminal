// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Follows Ghostty graphics_command.zig, compared with xterm's addon-image
/// KittyGraphicsTypes/Handler. Ghostty's strict integer bounds, animation and
/// relative-placement keys go beyond xterm's current command catalog. Windows
/// Terminal's output parser does not implement Kitty graphics APC commands.
/// </summary>
public sealed class ManagedKittyGraphicsCommandTests
{
    [Fact]
    public void DenseFieldsPreserveFullUnsignedAndSignedRangesAndIgnoreUnknownKeys()
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse("a=T,i=4294967295,I=2,p=3,z=-2147483648,H=2147483647,V=-1,1=9,Q=10,N=1;YWJj"u8, 128, out var command));
        Assert.Equal('T', command.Action);
        Assert.Equal(uint.MaxValue, command.ImageId);
        Assert.Equal(2u, command.ImageNumber);
        Assert.Equal(3u, command.PlacementId);
        Assert.Equal(int.MinValue, command.GetSigned('z'));
        Assert.Equal(int.MaxValue, command.GetSigned('H'));
        Assert.Equal(-1, command.GetSigned('V'));
        Assert.Equal(10u, command.Get('Q'));
        Assert.Equal("abc"u8.ToArray(), command.Data.ToArray());
    }

    [Theory]
    [InlineData(";")]
    [InlineData("i=1")]
    [InlineData("a=f,f=999,s=0,v=4294967295,z=-1")]
    [InlineData("a=a,s=999,c=4294967295")]
    [InlineData("a=c,C=8,r=0,c=0")]
    [InlineData("a=d,d=R,x=10,y=2")]
    [InlineData("too_long_key=garbage;")]
    public void PermittedAndExecutionValidatedCommandsParse(string text)
        => Assert.True(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(text), 128, out _));

    [Theory]
    [InlineData("")]
    [InlineData("i=1,")]
    [InlineData("i=1,b")]
    [InlineData("i=-1")]
    [InlineData("i=4294967296")]
    [InlineData("z=2147483648")]
    [InlineData("z=-2147483649")]
    [InlineData("a=x")]
    [InlineData("a=t,t=x")]
    [InlineData("a=t,o=x")]
    [InlineData("a=d,d=u")]
    [InlineData("i=1;!!!!")]
    public void InvalidCommandsAreRejectedWithoutAllocatingUnboundedPayloads(string text)
        => Assert.False(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(text), 128, out _));

    [Fact]
    public void DirectChunkingQuietAndPayloadLimitsMatchGhostty()
    {
        Assert.True(ManagedKittyGraphicsCommand.TryParse("i=1,m=2,q=10;YWJj"u8, 4, out var direct));
        Assert.True(direct.MoreChunks);
        Assert.Equal(2, direct.Quiet);
        Assert.True(ManagedKittyGraphicsCommand.TryParse("i=1,t=s,m=1"u8, 0, out var shared));
        Assert.False(shared.MoreChunks);
        Assert.False(ManagedKittyGraphicsCommand.TryParse("i=1;YWJj"u8, 3, out _));
    }
}
