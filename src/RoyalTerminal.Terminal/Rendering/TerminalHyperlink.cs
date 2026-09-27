// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Buffers.Binary;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Owned OSC 8 identity. The URI and explicit ID retain arbitrary protocol bytes;
/// <see cref="Uri"/> is the UTF-8 presentation view, not the identity key.
/// </summary>
public sealed class TerminalHyperlink
{
    private readonly byte[] _uri;
    private readonly byte[] _explicitId;
    private byte[]? _snapshotEncoding;

    internal TerminalHyperlink(ReadOnlySpan<byte> uri, ReadOnlySpan<byte> explicitId, uint implicitId)
    {
        _uri = uri.ToArray();
        _explicitId = explicitId.ToArray();
        ImplicitId = implicitId;
        Uri = Encoding.UTF8.GetString(uri);
    }

    /// <summary>Original URI bytes, owned by this immutable value.</summary>
    public ReadOnlySpan<byte> UriBytes => _uri;
    /// <summary>Original explicit ID bytes; empty for an implicit ID.</summary>
    public ReadOnlySpan<byte> ExplicitId => _explicitId;
    /// <summary>Whether the application supplied a nonempty explicit ID.</summary>
    public bool IsExplicit => _explicitId.Length != 0;
    /// <summary>Per-screen numeric identity, meaningful only without an explicit ID.</summary>
    public uint ImplicitId { get; }
    /// <summary>Decoded URI for presentation and link activation.</summary>
    public string Uri { get; }

    // Cached immutable protocol bytes may be shared by page/COW owners. This
    // does not cache page allocation IDs or retain a screen/registry reference.
    internal byte[] SnapshotEncoding
    {
        get
        {
            byte[]? existing = Volatile.Read(ref _snapshotEncoding);
            if (existing is not null) return existing;
            byte[] encoded = new byte[checked(9 + _explicitId.Length + _uri.Length)];
            encoded[0] = IsExplicit ? (byte)2 : (byte)1;
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(1), IsExplicit ? (uint)_explicitId.Length : ImplicitId);
            _explicitId.CopyTo(encoded, 5);
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(5 + _explicitId.Length), (uint)_uri.Length);
            _uri.CopyTo(encoded, 9 + _explicitId.Length);
            return Interlocked.CompareExchange(ref _snapshotEncoding, encoded, null) ?? encoded;
        }
    }
}

// Immutable collision chains can be shared by synchronized-output snapshots.
// Lookup accepts borrowed byte spans, so extracting every linked native cell
// does not allocate another identity or decode another URL.
internal sealed partial class TerminalHyperlinkRegistry
{
    private Dictionary<int, Entry> _buckets = [];
    private readonly Dictionary<int, TerminalHyperlink> _values = [];

    internal bool TryFind(ReadOnlySpan<byte> uri, ReadOnlySpan<byte> id, uint implicitId, out int token)
    {
        _buckets.TryGetValue(Hash(uri, id, implicitId), out Entry? entry);
        for (; entry is not null; entry = entry.Next)
        {
            TerminalHyperlink value = entry.Value;
            if ((value.IsExplicit || value.ImplicitId == implicitId) &&
                value.ExplicitId.SequenceEqual(id) && value.UriBytes.SequenceEqual(uri))
            { token = entry.Token; return true; }
        }
        token = 0;
        return false;
    }

    internal TerminalHyperlink Add(int token, ReadOnlySpan<byte> uri, ReadOnlySpan<byte> id, uint implicitId, bool collectible = false)
    {
        int hash = Hash(uri, id, implicitId);
        _buckets.TryGetValue(hash, out Entry? previous);
        TerminalHyperlink value = new(uri, id, id.IsEmpty ? implicitId : 0);
        Entry entry = new(token, value, previous);
        // Finish fallible preparation before publishing either index.
        _buckets.EnsureCapacity(_buckets.Count + 1);
        _values.EnsureCapacity(_values.Count + 1);
        if (collectible) _collectibleTokens.EnsureCapacity(_collectibleTokens.Count + 1);
        _values.Add(token, value);
        _buckets[hash] = entry;
        if (collectible) _collectibleTokens.Add(token);
        return value;
    }

    // Only pending, newly registered protocol identities may be discarded.
    // Existing/public registrations remain stable. CopyFrom shares immutable
    // chains, so removing an interior collision must rebuild its prefix.
    internal bool RemovePending(int token)
    {
        if (!_values.TryGetValue(token, out TerminalHyperlink? value)) return false;
        int hash = Hash(value.UriBytes, value.ExplicitId, value.ImplicitId);
        Entry? replacement = RemoveEntry(_buckets[hash], token);
        if (replacement is null) _buckets.Remove(hash);
        else _buckets[hash] = replacement;
        _values.Remove(token);
        _collectibleTokens.Remove(token);
        return true;
    }

    internal static Entry? RemoveEntry(Entry head, int token)
    {
        if (head.Token == token) return head.Next;
        List<Entry> prefix = [];
        Entry? current = head;
        while (current is not null && current.Token != token)
        {
            prefix.Add(current);
            current = current.Next;
        }
        if (current is null) throw new InvalidOperationException("Hyperlink registry indices disagree.");
        Entry? replacement = current.Next;
        for (int i = prefix.Count - 1; i >= 0; i--)
            replacement = new(prefix[i].Token, prefix[i].Value, replacement);
        return replacement;
    }

    internal bool TryGet(int token, out TerminalHyperlink? value) => _values.TryGetValue(token, out value);
    internal void Clear()
    {
        _values.Clear();
        _buckets.Clear();
        _collectibleTokens.Clear();
        CancelCollection();
        _nextCollectionThreshold = CollectionSlack;
    }
    internal void CopyFrom(TerminalHyperlinkRegistry source)
    {
        Clear();
        foreach (var pair in source._buckets) _buckets.Add(pair.Key, pair.Value);
        foreach (var pair in source._values) _values.Add(pair.Key, pair.Value);
        _collectibleTokens.UnionWith(source._collectibleTokens);
        _nextCollectionThreshold = source._nextCollectionThreshold;
    }

    private static int Hash(ReadOnlySpan<byte> uri, ReadOnlySpan<byte> id, uint implicitId)
    {
        HashCode hash = new();
        hash.AddBytes(uri);
        hash.Add(uri.Length);
        hash.AddBytes(id);
        hash.Add(id.IsEmpty ? implicitId : 0);
        return hash.ToHashCode();
    }

    internal sealed record Entry(int Token, TerminalHyperlink Value, Entry? Next);
}
