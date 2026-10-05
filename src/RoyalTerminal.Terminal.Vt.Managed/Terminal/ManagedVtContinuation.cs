// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

/// <summary>Retains only bytes belonging to the current unfinished parser operation.</summary>
internal sealed class ManagedVtContinuation(int maximumBytes)
{
    private readonly List<byte> _bytes = [];
    private bool _broken;

    internal void Disable()
    {
        maximumBytes = 0;
        Reset();
    }

    internal void Track(ReadOnlySpan<byte> input, int newStart, bool ground)
    {
        if (maximumBytes == 0) return;
        if (ground)
        {
            Reset();
            return;
        }

        if (newStart >= 0)
        {
            Reset();
            input = input[newStart..];
        }

        if (_broken) return;
        if (input.Length > maximumBytes - _bytes.Count)
        {
            _bytes.Clear();
            _broken = true;
            return;
        }

        _bytes.AddRange(input);
    }

    internal ReadOnlySpan<byte> GetBytes()
    {
        if (maximumBytes == 0)
            throw new InvalidOperationException("Parser continuation retention is disabled.");
        if (_broken)
            throw new InvalidOperationException("The unfinished parser continuation exceeds its configured byte limit.");
        return CollectionsMarshal.AsSpan(_bytes);
    }

    internal void Reset()
    {
        _bytes.Clear();
        _broken = false;
    }
}
