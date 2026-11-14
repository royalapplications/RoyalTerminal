using Xunit;
namespace RoyalTerminal.Multiplexing.Tests;
public sealed class ScopedLookupTests
{
    [Fact]
    public async Task InspectOneBlockRequiresItsOwningSessionAndReportsCurrentPlacement()
    {
        await using var broker=new MultiplexerBroker(new FakeFactory());
        var a=broker.CreateSession("First");var b=broker.CreateSession("Second");
        var block=await broker.AddBlockAsync(a.Id,"Worker",MultiplexerBrokerTests.Options());
        Assert.Equal(MultiplexerPaneKind.Tiled,broker.GetBlock(a.Id,block.Id).Placement);
        Assert.Equal(MultiplexerErrorCode.NotFound,Assert.Throws<MultiplexerException>(()=>broker.GetBlock(b.Id,block.Id)).Code);
        broker.PlaceBlock(a.Id,block.Id,a.Windows[0].Id,MultiplexerPaneKind.Unplaced);
        Assert.Equal(MultiplexerPaneKind.Unplaced,broker.GetBlock(a.Id,block.Id).Placement);
        await broker.CloseBlockAsync(a.Id,block.Id);
        Assert.Equal(MultiplexerErrorCode.NotFound,Assert.Throws<MultiplexerException>(()=>broker.GetBlock(a.Id,block.Id)).Code);
    }
}
