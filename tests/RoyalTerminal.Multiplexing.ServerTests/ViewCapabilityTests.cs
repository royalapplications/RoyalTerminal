using RoyalTerminal.Multiplexing.Server;
using Xunit;
namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class ViewCapabilityTests
{
    private static readonly Guid S = Guid.NewGuid();
    private static readonly AccessGrant G = new(Guid.NewGuid(), false, S, false, DateTimeOffset.MaxValue);
    [Theory]
    [InlineData("browser",1,8)] [InlineData("native",1,2)] [InlineData("native",2,5)] [InlineData("mobile",1,2)] [InlineData("mobile",2,5)] [InlineData("mobile",3,6)]
    public void EveryDiscoveredFormIsAdmittedAndUnlistedOnesAreRejected(string kind,int version,int count)
    {
        var hub=new ViewMailbox(TimeProvider.System,_=>true);var v=hub.Open(S,"client",kind,G,version).View;var catalog=hub.Describe(S,v.Id);
        Assert.Equal(count,catalog.Forms.Count);Assert.Equal(v.Id,catalog.ClientId);Assert.Equal(S,catalog.SessionId);Assert.Equal(version,catalog.NavigationVersion);
        long seq=0;foreach(var form in catalog.Forms)foreach(string value in form.AcceptedValues??[string.Empty]){
            var a=new ViewAction(form.Name,form.RequiredArgument=="blockId"?Guid.NewGuid():null,form.RequiredArgument=="windowId"?Guid.NewGuid():null,form.RequiredArgument=="direction"?value:null);
            Assert.Equal(++seq,hub.Publish(S,v.Id,[a]).Sequence);}
        Assert.Throws<ApiException>(()=>hub.Publish(S,v.Id,[new("window.focus",BlockId:Guid.NewGuid())]));
        Assert.Throws<ApiException>(()=>hub.Publish(S,v.Id,[new("block.input")])); Assert.Throws<ApiException>(()=>hub.Describe(Guid.NewGuid(),v.Id));
    }
    [Theory][InlineData("mobile")][InlineData("native")]
    public void OldReceiversRejectRelativeBatchesWithoutEnqueuingTheirPrefix(string kind)
    {
        var hub=new ViewMailbox(TimeProvider.System,_=>true);var old=hub.Open(S,"same",kind,G);var modern=hub.Open(S,"same",kind,G,2);
        var next=new ViewAction("pane.focus",Direction:"next");var absolute=new ViewAction("pane.focus",Guid.NewGuid());
        Assert.Throws<ApiException>(()=>hub.Publish(S,old.View.Id,[absolute,next]));Assert.Equal(1L,hub.Publish(S,old.View.Id,[absolute]).Sequence);
        Assert.Equal(1L,hub.Publish(S,modern.View.Id,[next]).Sequence);hub.Close(S,modern.View.Id,modern.ReaderKey,G);
        Assert.Throws<ApiException>(()=>hub.Describe(S,modern.View.Id));Assert.Throws<ArgumentException>(()=>hub.Open(S,"unknown",kind,G,4));
    }
}
