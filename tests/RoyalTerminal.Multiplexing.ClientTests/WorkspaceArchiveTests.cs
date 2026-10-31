using System.Buffers.Binary;
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class WorkspaceArchiveTests
{
    private readonly Guid _s = Guid.NewGuid(), _b = Guid.NewGuid();
    private JsonElement Topology() { using var d=JsonDocument.Parse($$"""{"id":"{{_s}}","revision":9,"windows":[{"id":"{{Guid.NewGuid()}}"}],"blocks":[{"id":"{{_b}}","sessionId":"{{_s}}"}]}""");return d.RootElement.Clone(); }
    private async Task<byte[]> Encode()
    {
        using var output=new MemoryStream();await WorkspaceArchive.WriteAsync(output,Topology(),(id,ct)=>ValueTask.FromResult(new MultiplexingCheckpoint(_s,id,987,37,new byte[]{27,91,46})),TestContext.Current.CancellationToken);return output.ToArray();
    }
    [Fact]
    public async Task FooterCoversTopologyCursorAndPayload()
    {
        byte[] data=await Encode();var ct=TestContext.Current.CancellationToken;
        using var stream=new MemoryStream(data);var image=await WorkspaceArchive.ReadAsync(stream,ct);
        Assert.Equal(9,image.Session.GetProperty("revision").GetInt32());var cp=Assert.Single(image.Blocks);
        Assert.Equal(_b,cp.BlockId);Assert.Equal(37,cp.ExitCode);Assert.Equal(987,cp.Sequence);Assert.Equal(new byte[]{27,91,46},cp.Binary.ToArray());
        foreach(int pos in new[]{0,data.Length-33,data.Length-1})
        {var mutated=(byte[])data.Clone();mutated[pos]^=1;using var bad=new MemoryStream(mutated);await Assert.ThrowsAsync<InvalidDataException>(()=>WorkspaceArchive.ReadAsync(bad,ct).AsTask());}
        using var trailing=new MemoryStream(data.Concat(new byte[]{0}).ToArray());await Assert.ThrowsAsync<InvalidDataException>(()=>WorkspaceArchive.ReadAsync(trailing,ct).AsTask());
        using var shortFile=new MemoryStream(data[..^1]);await Assert.ThrowsAsync<EndOfStreamException>(()=>WorkspaceArchive.ReadAsync(shortFile,ct).AsTask());
    }
    [Fact]
    public async Task ForgedLengthsAndWrongCaptureIdentityAreRejectedBeforePublication()
    {
        var ct=TestContext.Current.CancellationToken;var data=await Encode();BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8),int.MaxValue);
        using var input=new MemoryStream(data);await Assert.ThrowsAsync<InvalidDataException>(()=>WorkspaceArchive.ReadAsync(input,ct).AsTask());
        using var output=new MemoryStream();await Assert.ThrowsAsync<InvalidDataException>(()=>WorkspaceArchive.WriteAsync(output,Topology(),(_,_)=>ValueTask.FromResult(new MultiplexingCheckpoint(Guid.NewGuid(),_b,1,null,new byte[]{1})),ct).AsTask());
    }
}
