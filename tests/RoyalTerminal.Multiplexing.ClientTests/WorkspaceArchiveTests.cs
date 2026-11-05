using System.Buffers.Binary;
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class WorkspaceArchiveTests
{
    private readonly Guid _session = Guid.NewGuid(), _block = Guid.NewGuid();

    private JsonElement Topology()
    {
        using var json = JsonDocument.Parse($$"""{"id":"{{_session}}","revision":9,"windows":[{"id":"{{Guid.NewGuid()}}"}],"blocks":[{"id":"{{_block}}","sessionId":"{{_session}}"}]}""");
        return json.RootElement.Clone();
    }

    private async Task<byte[]> EncodeAsync(CancellationToken token)
    {
        using var output = new MemoryStream();
        await WorkspaceArchive.WriteAsync(output, Topology(), (id, _) =>
            ValueTask.FromResult(new MultiplexingCheckpoint(_session, id, 987, 37, new byte[] { 27, 91, 46 })), token);
        return output.ToArray();
    }

    [Fact]
    public async Task FooterCoversTopologyCursorAndPayload()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        byte[] data = await EncodeAsync(token);
        using var stream = new MemoryStream(data);
        var image = await WorkspaceArchive.ReadAsync(stream, token);
        Assert.Equal(9, image.Session.GetProperty("revision").GetInt32());
        var checkpoint = Assert.Single(image.Blocks);
        Assert.Equal(_block, checkpoint.BlockId);
        Assert.Equal(37, checkpoint.ExitCode);
        Assert.Equal(987L, checkpoint.Sequence);
        Assert.Equal(new byte[] { 27, 91, 46 }, checkpoint.Binary.ToArray());

        foreach (int offset in new[] { 0, data.Length - 33, data.Length - 1 })
        {
            var mutated = (byte[])data.Clone();
            mutated[offset] ^= 1;
            using var bad = new MemoryStream(mutated);
            await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceArchive.ReadAsync(bad, token).AsTask());
        }
        using var trailing = new MemoryStream(data.Concat(new byte[] { 0 }).ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceArchive.ReadAsync(trailing, token).AsTask());
        using var truncated = new MemoryStream(data[..^1]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => WorkspaceArchive.ReadAsync(truncated, token).AsTask());
    }

    [Fact]
    public async Task ForgedLengthsAndWrongCaptureIdentityAreRejectedBeforePublication()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        var data = await EncodeAsync(token);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), int.MaxValue);
        using var input = new MemoryStream(data);
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceArchive.ReadAsync(input, token).AsTask());
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => WorkspaceArchive.WriteAsync(output, Topology(), (_, _) =>
            ValueTask.FromResult(new MultiplexingCheckpoint(Guid.NewGuid(), _block, 1, null, new byte[] { 1 })), token).AsTask());
    }
}
