// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private ManagedUnknownApcCapture _oscUnknownCapture;
    private bool _oscHasKnownSelector;
    private bool _oscIsUnknown;
    private int _oscSelector;

    private void ResetOscCapture()
    {
        _oscHasKnownSelector = _oscIsUnknown = false;
        _oscSelector = 0;
        _oscUnknownCapture.Reset(UnknownSequenceMaxBytes);
    }

    // Ghostty's selector trie commits to a known parser only at ';'. Malformed
    // payloads of that parser must never be reclassified as host extensions.
    private void ClassifyOscByte(byte value)
    {
        if (value == ';' && _oscBuffer.Count != 0 && IsKnownOscSelector(_oscSelector))
        {
            _oscHasKnownSelector = true;
            return;
        }
        if (value is >= (byte)'0' and <= (byte)'9' &&
            (_oscBuffer.Count == 0 || _oscSelector != 0))
        {
            int selector = _oscSelector * 10 + value - '0';
            if (IsKnownOscSelector(selector) || IsPartialOscSelector(selector))
            {
                _oscSelector = selector;
                return;
            }
        }
        BeginUnknownOsc();
    }

    private static bool IsKnownOscSelector(int value)
        => value is 0 or 1 or 2 or 4 or 5 or 7 or 8 or 9 or >= 10 and <= 19 or
            21 or 22 or 52 or 66 or 72 or 99 or 104 or 105 or >= 110 and <= 119 or
            133 or 777 or 1337 or 3008 or 5522 or 7501;

    private static bool IsPartialOscSelector(int value)
        => value is 3 or 6 or 30 or 55 or 75 or 77 or 300 or 552 or 750;

    private void BeginUnknownOsc()
    {
        if (UnknownSequenceMaxBytes == 0)
        {
            _oscBuffer.Clear();
            _isDiscardingOscPayload = true;
            return;
        }
        _oscIsUnknown = true;
        _oscUnknownCapture.Begin(UnknownSequenceMaxBytes);
        _oscUnknownCapture.Append(CollectionsMarshal.AsSpan(_oscBuffer));
        _oscBuffer.Clear();
    }

    private void AppendUnknownOsc(ReadOnlySpan<byte> bytes)
    {
        // OSC capture never resumes after an allocation/limit failure: the
        // reported bytes must remain a prefix of the original command.
        if (!_oscUnknownCapture.Truncated) _oscUnknownCapture.Append(bytes);
    }

    private bool FinishUnknownOsc(bool bellTerminator)
    {
        if (!_oscHasKnownSelector && !_oscIsUnknown && _oscBuffer.Count != 0 &&
            !IsKnownOscSelector(_oscSelector)) BeginUnknownOsc();
        if (!_oscIsUnknown) return false;
        try
        {
            if (UnknownSequenceCallback is { } callback)
            {
                byte[] content = _oscUnknownCapture.Finish(out bool truncated);
                callback(new(TerminalUnknownSequenceType.Osc, content, truncated,
                    bellTerminator ? TerminalOscTerminator.Bel : TerminalOscTerminator.St));
            }
        }
        finally { ResetOscCapture(); }
        return true;
    }
}
