// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>One structurally consistent session layout and per-block parser checkpoints. No process recipes or credentials.</summary>
public sealed record WorkspaceArchiveImage(JsonElement Session, IReadOnlyList<MultiplexingCheckpoint> Blocks);

/// <summary>Offline, versioned archive framing. SHA-256 detects damage, not malicious replacement; no entries are extracted to filesystem paths.</summary>
public static class WorkspaceArchive
{
    private const int MaxBlocks = 128, MaxMetadata = 1024 * 1024, MaxBlock = 8 * 1024 * 1024, MaxFile = 128 * 1024 * 1024;
    private static readonly byte[] Magic = "RTMUX\0\u0001\n"u8.ToArray();

    /// <summary>Writes a bounded snapshot, fetching at most one block at a time. Publish the destination only after this method succeeds.</summary>
    public static async ValueTask WriteAsync(Stream destination, JsonElement session,
        Func<Guid, CancellationToken, ValueTask<MultiplexingCheckpoint>> capture, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(destination); ArgumentNullException.ThrowIfNull(capture);
        var (id, blocks) = ReadIdentity(session);
        byte[] metadata = System.Text.Encoding.UTF8.GetBytes(session.GetRawText());
        if (metadata.Length > MaxMetadata) throw Invalid("session metadata exceeds the quota");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int size = 32;
        async ValueTask Append(ReadOnlyMemory<byte> bytes)
        {
            if (bytes.Length > MaxFile - size) throw Invalid("archive exceeds the total quota");
            size += bytes.Length; hash.AppendData(bytes.Span); await destination.WriteAsync(bytes, token).ConfigureAwait(false);
        }
        await Append(Magic).ConfigureAwait(false);
        byte[] length = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, metadata.Length);
        await Append(length).ConfigureAwait(false); await Append(metadata).ConfigureAwait(false);
        foreach (var block in blocks)
        {
            token.ThrowIfCancellationRequested(); var cp = await capture(block, token).ConfigureAwait(false);
            if (cp.SessionId != id || cp.BlockId != block || cp.Sequence < 1 || cp.Binary.Length is < 1 or > MaxBlock)
                throw Invalid("checkpoint identity, cursor or length is invalid");
            byte[] header = new byte[33]; block.TryWriteBytes(header.AsSpan(0,16), bigEndian:true, out _);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), cp.Sequence);
            header[24] = cp.ExitCode.HasValue ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(25), cp.ExitCode.GetValueOrDefault());
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(29), cp.Binary.Length);
            await Append(header).ConfigureAwait(false); await Append(cp.Binary).ConfigureAwait(false);
        }
        await destination.WriteAsync(hash.GetHashAndReset(), token).ConfigureAwait(false);
    }

    /// <summary>Reads through the signature footer and physical EOF before exposing any authenticated-by-checksum data. Does not run a PTY or interpret escape sequences.</summary>
    public static async ValueTask<WorkspaceArchiveImage> ReadAsync(Stream source, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int left = MaxFile - 32;
        async ValueTask<byte[]> ReadHashed(int count)
        {
            if (count < 0 || count > left) throw Invalid("archive exceeds the total quota");
            left -= count;
            var bytes = GC.AllocateUninitializedArray<byte>(count);
            await source.ReadExactlyAsync(bytes, token).ConfigureAwait(false); hash.AppendData(bytes); return bytes;
        }
        var magic = await ReadHashed(Magic.Length).ConfigureAwait(false);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw Invalid("unknown archive format or version");
        var length = await ReadHashed(4).ConfigureAwait(false); int n = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (n is < 2 or > MaxMetadata) throw Invalid("metadata size out of range");
        using var doc = JsonDocument.Parse(await ReadHashed(n).ConfigureAwait(false));
        var (session, ids) = ReadIdentity(doc.RootElement);
        List<MultiplexingCheckpoint> result = new(ids.Length);
        foreach (var id in ids)
        {
            var header = await ReadHashed(33).ConfigureAwait(false);
            if (new Guid(header.AsSpan(0,16), bigEndian:true) != id) throw Invalid("wrong block identity or ordering");
            long seq = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16)); int code = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(25));
            int len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(29));
            if (seq < 1 || header[24] > 1 || (header[24] == 0 && code != 0) || len is < 1 or > MaxBlock) throw Invalid("invalid checkpoint header");
            result.Add(new(session,id,seq,header[24] == 1 ? code : null,await ReadHashed(len).ConfigureAwait(false)));
        }
        byte[] digest = new byte[32]; await source.ReadExactlyAsync(digest, token).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest,hash.GetHashAndReset())) throw Invalid("checksum mismatch");
        if (await source.ReadAsync(digest.AsMemory(0,1),token).ConfigureAwait(false) != 0) throw Invalid("trailing bytes");
        return new(doc.RootElement.Clone(), result.AsReadOnly());
    }

    private static (Guid Id, Guid[] Blocks) ReadIdentity(JsonElement doc)
    {
        if (doc.ValueKind != JsonValueKind.Object || !doc.TryGetProperty("id",out var s) || s.ValueKind != JsonValueKind.String || !s.TryGetGuid(out var session) || session == Guid.Empty ||
            !doc.TryGetProperty("revision",out var rev) || rev.ValueKind != JsonValueKind.Number || !rev.TryGetInt64(out var r) || r < 1 ||
            !doc.TryGetProperty("windows",out var w) || w.ValueKind != JsonValueKind.Array || w.GetArrayLength() is < 1 or > 128 ||
            !doc.TryGetProperty("blocks",out var b) || b.ValueKind != JsonValueKind.Array || b.GetArrayLength() > MaxBlocks) throw Invalid("invalid topology manifest");
        HashSet<Guid> unique = []; List<Guid> ids = new(b.GetArrayLength());
        foreach (var block in b.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("sessionId",out var owner) || owner.ValueKind != JsonValueKind.String || !owner.TryGetGuid(out var owningId) || owningId != session ||
                !block.TryGetProperty("id",out var id) || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var guid) || guid == Guid.Empty || !unique.Add(guid)) throw Invalid("invalid or duplicate block identity");
            ids.Add(guid);
        }
        return (session,ids.ToArray());
    }
    private static InvalidDataException Invalid(string message) => new($"Invalid workspace archive: {message}.");
}
