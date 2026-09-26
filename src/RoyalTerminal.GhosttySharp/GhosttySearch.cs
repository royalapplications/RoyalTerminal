// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Managed lifetime wrapper for Ghostty's incremental, full-scrollback terminal search.
/// </summary>
public sealed class GhosttySearch : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>Creates an idle search bound to <paramref name="terminal"/>.</summary>
    public GhosttySearch(GhosttyTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(
            GhosttyVtNative.SearchNew(nint.Zero, out _handle, terminal.Handle),
            "ghostty_search_new");
    }

    /// <summary>Gets whether the native search handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Sets or replaces the UTF-8 search needle; null or empty clears it.</summary>
    public unsafe void SetNeedle(string? needle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] bytes = string.IsNullOrEmpty(needle) ? [] : Encoding.UTF8.GetBytes(needle);
        fixed (byte* pointer = bytes)
        {
            GhosttyVtNative.GhosttyString value = new((nint)pointer, (nuint)bytes.Length);
            ThrowIfFailed(
                GhosttyVtNative.SearchSet(
                    _handle,
                    GhosttyVtNative.GhosttySearchOption.Needle,
                    &value),
                "ghostty_search_set(needle)");
        }
    }

    /// <summary>Advances the search using only search-owned state.</summary>
    public GhosttyVtNative.GhosttySearchStatus Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(GhosttyVtNative.SearchTick(_handle, out GhosttyVtNative.GhosttySearchStatus status),
            "ghostty_search_tick");
        return status;
    }

    /// <summary>Copies a bounded amount of current terminal state into the search.</summary>
    public void Feed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(GhosttyVtNative.SearchFeed(_handle), "ghostty_search_feed");
    }

    /// <summary>Synchronously feeds and searches until caught up.</summary>
    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(GhosttyVtNative.SearchRun(_handle), "ghostty_search_run");
    }

    /// <summary>Selects the next, older match; returns false when there are no matches.</summary>
    public bool SelectNext() => Select(GhosttyVtNative.GhosttySearchOption.SelectNext);

    /// <summary>Selects the previous, newer match; returns false when there are no matches.</summary>
    public bool SelectPrevious() => Select(GhosttyVtNative.GhosttySearchOption.SelectPrevious);

    /// <summary>Sets whether selecting a match scrolls it into view.</summary>
    public unsafe void SetSelectionScroll(GhosttyVtNative.GhosttySearchScroll value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttySearchScroll copy = value;
        ThrowIfFailed(
            GhosttyVtNative.SearchSet(
                _handle,
                GhosttyVtNative.GhosttySearchOption.SelectScroll,
                &copy),
            "ghostty_search_set(select_scroll)");
    }

    /// <summary>Gets the current incremental search status.</summary>
    public GhosttyVtNative.GhosttySearchStatus GetStatus()
        => GetValue<GhosttyVtNative.GhosttySearchStatus>(GhosttyVtNative.GhosttySearchData.Status);

    /// <summary>Gets the current search needle, or null when the search is idle.</summary>
    public string? GetNeedle()
    {
        if (!TryGetValue(
                GhosttyVtNative.GhosttySearchData.Needle,
                out GhosttyVtNative.GhosttyString value))
        {
            return null;
        }

        return value.ToUtf8String();
    }

    /// <summary>Gets the total active-screen matches found so far.</summary>
    public nuint GetTotalMatches()
        => GetValue<nuint>(GhosttyVtNative.GhosttySearchData.TotalMatches);

    /// <summary>Gets the selected match index, or null when nothing is selected.</summary>
    public nuint? GetSelectedIndex()
        => TryGetValue(GhosttyVtNative.GhosttySearchData.SelectedIndex, out nuint value) ? value : null;

    /// <summary>Gets the current selected match snapshot, or null when nothing is selected.</summary>
    public GhosttyVtNative.GhosttySelectionRange? GetSelectedMatch()
        => TryGetValue(GhosttyVtNative.GhosttySearchData.SelectedMatch,
            out GhosttyVtNative.GhosttySelectionRange value) ? value : null;

    /// <summary>Copies all active-screen matches, ordered newest to oldest.</summary>
    public GhosttyVtNative.GhosttySelectionRange[] GetMatches()
        => GetMatches(GhosttyVtNative.GhosttySearchData.Matches);

    /// <summary>Copies the cached matches intersecting pages around the viewport.</summary>
    public GhosttyVtNative.GhosttySelectionRange[] GetViewportMatches()
        => GetMatches(GhosttyVtNative.GhosttySearchData.ViewportMatches);

    /// <summary>Gets the current selected-match viewport scrolling policy.</summary>
    public GhosttyVtNative.GhosttySearchScroll GetSelectionScroll()
        => GetValue<GhosttyVtNative.GhosttySearchScroll>(GhosttyVtNative.GhosttySearchData.SelectScroll);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != nint.Zero)
        {
            GhosttyVtNative.SearchFree(_handle);
            _handle = nint.Zero;
        }
    }

    private unsafe bool Select(GhosttyVtNative.GhosttySearchOption option)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.SearchSet(_handle, option, null);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            return false;
        }

        ThrowIfFailed(result, $"ghostty_search_set({option})");
        return true;
    }

    private unsafe GhosttyVtNative.GhosttySelectionRange[] GetMatches(
        GhosttyVtNative.GhosttySearchData data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttySelectionBuffer buffer = default;
        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.SearchGet(_handle, data, &buffer);
        if (probe == GhosttyVtNative.GhosttyResult.Success && buffer.Length == 0)
        {
            return [];
        }

        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, $"ghostty_search_get({data} probe)");
        }

        GhosttyVtNative.GhosttySelectionRange[] matches = new GhosttyVtNative.GhosttySelectionRange[
            checked((int)buffer.Length)];
        fixed (GhosttyVtNative.GhosttySelectionRange* pointer = matches)
        {
            buffer.Pointer = pointer;
            buffer.Capacity = (nuint)matches.Length;
            ThrowIfFailed(GhosttyVtNative.SearchGet(_handle, data, &buffer), $"ghostty_search_get({data})");
        }

        if (buffer.Length == (nuint)matches.Length)
        {
            return matches;
        }

        Array.Resize(ref matches, checked((int)buffer.Length));
        return matches;
    }

    private unsafe T GetValue<T>(GhosttyVtNative.GhosttySearchData data) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T value = default;
        ThrowIfFailed(GhosttyVtNative.SearchGet(_handle, data, &value), $"ghostty_search_get({data})");
        return value;
    }

    private unsafe bool TryGetValue<T>(GhosttyVtNative.GhosttySearchData data, out T value) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T resultValue = default;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.SearchGet(_handle, data, &resultValue);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            value = default;
            return false;
        }

        ThrowIfFailed(result, $"ghostty_search_get({data})");
        value = resultValue;
        return true;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed with {result}.");
        }
    }
}
