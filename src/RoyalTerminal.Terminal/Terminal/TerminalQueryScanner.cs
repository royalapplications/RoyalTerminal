// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Lightweight CSI query sequence detector.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Terminal query types that require a response written back to the PTY.
/// </summary>
public enum TerminalQuery
{
    /// <summary>DSR operating status (ESC[5n) → response ESC[0n</summary>
    DeviceStatusOk,

    /// <summary>DSR cursor position (ESC[6n) → response ESC[row;colR</summary>
    CursorPositionReport,

    /// <summary>DA1 primary device attributes (ESC[c or ESC[0c) → response ESC[?62;22c</summary>
    PrimaryDeviceAttributes,

    /// <summary>DA2 secondary device attributes (ESC[>c or ESC[>0c) → response ESC[>1;10;0c</summary>
    SecondaryDeviceAttributes,

    /// <summary>ENQ (0x05) → empty or configurable response</summary>
    Enquiry,
}

/// <summary>
/// Scans raw VT data for query sequences (DSR, DA, ENQ) that the readonly
/// native terminal stream ignores. Handles sequences split across data chunks
/// by maintaining parser state between calls.
/// </summary>
public sealed class TerminalQueryScanner
{
    private enum State
    {
        Ground,
        Escape,     // Saw ESC (0x1B)
        CsiEntry,   // Saw ESC [
        CsiParam,   // Collecting numeric params
    }

    private State _state = State.Ground;
    private int _param;
    private bool _hasParam;
    private char _intermediate; // '>' for DA2, '\0' for normal CSI
    private readonly Queue<TerminalQuery> _pending = new();

    /// <summary>
    /// Scans a span of raw terminal output bytes for query sequences.
    /// Any detected queries are enqueued and can be retrieved via <see cref="TryDequeue"/>.
    /// </summary>
    public void Scan(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            switch (_state)
            {
                case State.Ground:
                    if (b == 0x1B)
                        _state = State.Escape;
                    else if (b == 0x05) // ENQ
                        _pending.Enqueue(TerminalQuery.Enquiry);
                    break;

                case State.Escape:
                    if (b == (byte)'[')
                    {
                        _state = State.CsiEntry;
                        _param = 0;
                        _hasParam = false;
                        _intermediate = '\0';
                    }
                    else
                    {
                        // Not a CSI sequence — go back to ground
                        // (but check if this byte is itself an ESC)
                        _state = b == 0x1B ? State.Escape : State.Ground;
                    }
                    break;

                case State.CsiEntry:
                    if (b is >= (byte)'0' and <= (byte)'9')
                    {
                        _param = b - '0';
                        _hasParam = true;
                        _state = State.CsiParam;
                    }
                    else if (b == (byte)'>' || b == (byte)'?')
                    {
                        _intermediate = (char)b;
                        _state = State.CsiParam;
                    }
                    else if (b is >= 0x40 and <= 0x7E)
                    {
                        // Final byte with no params
                        DispatchCsi((char)b);
                    }
                    else if (b == 0x1B)
                    {
                        _state = State.Escape;
                    }
                    else
                    {
                        _state = State.Ground;
                    }
                    break;

                case State.CsiParam:
                    if (b is >= (byte)'0' and <= (byte)'9')
                    {
                        _param = _param * 10 + (b - '0');
                        _hasParam = true;
                    }
                    else if (b == (byte)';')
                    {
                        // Multiple params — we only care about single-param queries
                        // so just continue accumulating (reset for next param)
                        _param = 0;
                        _hasParam = false;
                    }
                    else if (b is >= 0x40 and <= 0x7E)
                    {
                        // Final byte
                        DispatchCsi((char)b);
                    }
                    else if (b == 0x1B)
                    {
                        _state = State.Escape;
                    }
                    else
                    {
                        _state = State.Ground;
                    }
                    break;
            }
        }
    }

    private void DispatchCsi(char finalByte)
    {
        _state = State.Ground;

        switch (finalByte)
        {
            case 'n' when _intermediate == '\0':
                // DSR — Device Status Report
                var p = _hasParam ? _param : 0;
                if (p == 5)
                    _pending.Enqueue(TerminalQuery.DeviceStatusOk);
                else if (p == 6)
                    _pending.Enqueue(TerminalQuery.CursorPositionReport);
                break;

            case 'c' when _intermediate == '>':
                // DA2 — Secondary Device Attributes
                _pending.Enqueue(TerminalQuery.SecondaryDeviceAttributes);
                break;

            case 'c' when _intermediate == '\0':
                // DA1 — Primary Device Attributes (ESC[c or ESC[0c)
                var da1Param = _hasParam ? _param : 0;
                if (da1Param == 0)
                    _pending.Enqueue(TerminalQuery.PrimaryDeviceAttributes);
                break;
        }
    }

    /// <summary>
    /// Tries to dequeue the next detected query.
    /// </summary>
    public bool TryDequeue(out TerminalQuery query)
    {
        return _pending.TryDequeue(out query);
    }

    /// <summary>
    /// Clears all pending queries without generating responses.
    /// </summary>
    public void ClearPending()
    {
        _pending.Clear();
    }
}
