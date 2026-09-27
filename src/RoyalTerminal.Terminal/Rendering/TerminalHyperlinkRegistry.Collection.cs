// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

internal sealed partial class TerminalHyperlinkRegistry
{
    internal const int CollectionSlack = 256;
    private readonly HashSet<int> _collectibleTokens = [];
    private int _nextCollectionThreshold = CollectionSlack;
    // Preparation scratch is instance-local, never copied with COW state. Clear
    // reference-bearing scratch on every exit, including failed preparation.
    private HashSet<int>? _collectionCandidates;
    private Dictionary<int, Entry>? _collectionBuckets;
    private List<Entry>? _collectionPrefix;

    internal void PreservePublicToken(int token) => _collectibleTokens.Remove(token);

    internal HashSet<int>? BeginCollection(bool force)
    {
        if (_collectibleTokens.Count == 0 || !force && _collectibleTokens.Count < _nextCollectionThreshold) return null;
        HashSet<int> candidates = _collectionCandidates ??= [];
        candidates.Clear();
        candidates.UnionWith(_collectibleTokens);
        return candidates;
    }

    internal void PrepareCollection(HashSet<int> candidates)
    {
        if (candidates.Count == 0) return;
        Dictionary<int, Entry> prepared = _collectionBuckets ??= [];
        prepared.Clear();
        prepared.EnsureCapacity(_buckets.Count);
        foreach ((int hash, Entry head) in _buckets)
        {
            Entry? firstRemoved = head;
            while (firstRemoved is not null && !candidates.Contains(firstRemoved.Token)) firstRemoved = firstRemoved.Next;
            if (firstRemoved is null) { prepared.Add(hash, head); continue; }
            // The common singleton bucket needs no replacement node allocation.
            if (head.Next is null) continue;
            Entry? replacement = RemoveEntries(head, candidates, _collectionPrefix ??= []);
            if (replacement is not null) prepared.Add(hash, replacement);
        }
    }

    internal static Entry? RemoveEntries(Entry head, HashSet<int> removed, List<Entry> prefix)
    {
        prefix.Clear();
        try
        {
            bool changed = false;
            for (Entry? entry = head; entry is not null; entry = entry.Next)
            {
                if (removed.Contains(entry.Token)) changed = true;
                else prefix.Add(entry);
            }
            if (!changed) return head;
            Entry? replacement = null;
            for (int index = prefix.Count - 1; index >= 0; index--)
                replacement = new(prefix[index].Token, prefix[index].Value, replacement);
            return replacement;
        }
        finally { prefix.Clear(); }
    }

    internal int CommitCollection(HashSet<int> candidates, Dictionary<int, string> urls)
    {
        int removed = candidates.Count;
        if (removed != 0)
        {
            // Everything fallible has completed; publish both indices together.
            (_buckets, _collectionBuckets) = (_collectionBuckets!, _buckets);
            foreach (int token in candidates)
            {
                _values.Remove(token);
                _collectibleTokens.Remove(token);
                urls.Remove(token);
            }
        }
        // Amortize full-buffer scans for live-link-heavy scrollback. Dead entries
        // are pressure-pruned, like Ghostty's zero-reference set entries, not on
        // every text write. Collection never rewinds the token watermark;
        // the existing integer-exhaustion rollover policy remains unchanged.
        _nextCollectionThreshold = (int)Math.Min(int.MaxValue, (long)_collectibleTokens.Count * 2 + CollectionSlack);
        return removed;
    }

    internal void CancelCollection()
    {
        _collectionCandidates?.Clear();
        _collectionBuckets?.Clear();
        _collectionPrefix?.Clear();
    }
}
