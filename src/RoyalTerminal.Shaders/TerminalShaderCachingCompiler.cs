// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader compiler cache.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Caches full shader compilation results by deterministic request content.
/// </summary>
public sealed class TerminalShaderCachingCompiler : ITerminalShaderCompiler
{
    private readonly object _syncRoot = new();
    private readonly ITerminalShaderCompiler _innerCompiler;
    private readonly string _cacheNamespace;
    private readonly int _maxEntries;
    private readonly Dictionary<TerminalShaderCompilationCacheKey, TerminalShaderCompilationResult> _entries = [];
    private readonly Queue<TerminalShaderCompilationCacheKey> _insertionOrder = [];

    /// <summary>
    /// Initializes a new caching compiler wrapper.
    /// </summary>
    /// <param name="innerCompiler">Compiler to wrap.</param>
    /// <param name="cacheNamespace">Compiler-specific cache namespace.</param>
    /// <param name="maxEntries">Maximum cached entry count.</param>
    public TerminalShaderCachingCompiler(
        ITerminalShaderCompiler innerCompiler,
        string? cacheNamespace = null,
        int maxEntries = 128)
    {
        _innerCompiler = innerCompiler ?? throw new ArgumentNullException(nameof(innerCompiler));
        _cacheNamespace = string.IsNullOrWhiteSpace(cacheNamespace)
            ? innerCompiler.GetType().FullName ?? innerCompiler.GetType().Name
            : cacheNamespace.Trim();
        _maxEntries = Math.Max(1, maxEntries);
    }

    /// <summary>
    /// Gets the number of cached compilation results.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<TerminalShaderCompilationResult> CompileAsync(
        TerminalShaderCompilationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        TerminalShaderCompilationCacheKey key = TerminalShaderCompilationCacheKey.Create(request, _cacheNamespace);
        lock (_syncRoot)
        {
            if (_entries.TryGetValue(key, out TerminalShaderCompilationResult? cachedResult))
            {
                return cachedResult;
            }
        }

        TerminalShaderCompilationResult result = await _innerCompiler
            .CompileAsync(request, cancellationToken)
            .ConfigureAwait(false);

        lock (_syncRoot)
        {
            if (!_entries.ContainsKey(key))
            {
                _entries.Add(key, result);
                _insertionOrder.Enqueue(key);
                TrimToCapacity();
            }
        }

        return result;
    }

    /// <summary>
    /// Clears all cached compilation results.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _entries.Clear();
            _insertionOrder.Clear();
        }
    }

    private void TrimToCapacity()
    {
        while (_entries.Count > _maxEntries && _insertionOrder.Count > 0)
        {
            TerminalShaderCompilationCacheKey oldest = _insertionOrder.Dequeue();
            _entries.Remove(oldest);
        }
    }
}
