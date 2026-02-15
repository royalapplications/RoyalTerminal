// Licensed under the MIT License.
// Tests for terminal query detection and response generation.

using GhosttySharp.Avalonia.Rendering;
using GhosttySharp.Avalonia.Terminal;
using Xunit;

namespace GhosttySharp.Tests;

public class TerminalQueryTests
{
    #region TerminalQueryScanner Tests

    [Fact]
    public void Scanner_DetectsDsr6_CursorPositionReport()
    {
        var scanner = new TerminalQueryScanner();
        scanner.Scan("\x1b[6n"u8);

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.CursorPositionReport, query);
        Assert.False(scanner.TryDequeue(out _));
    }

    [Fact]
    public void Scanner_DetectsDsr5_OperatingStatus()
    {
        var scanner = new TerminalQueryScanner();
        scanner.Scan("\x1b[5n"u8);

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.DeviceStatusOk, query);
    }

    [Fact]
    public void Scanner_DetectsDA1_PrimaryDeviceAttributes()
    {
        var scanner = new TerminalQueryScanner();
        scanner.Scan("\x1b[c"u8);

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.PrimaryDeviceAttributes, query);
    }

    [Fact]
    public void Scanner_DetectsDA1WithParam0()
    {
        var scanner = new TerminalQueryScanner();
        scanner.Scan("\x1b[0c"u8);

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.PrimaryDeviceAttributes, query);
    }

    [Fact]
    public void Scanner_DetectsDA2_SecondaryDeviceAttributes()
    {
        var scanner = new TerminalQueryScanner();
        scanner.Scan("\x1b[>c"u8);

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.SecondaryDeviceAttributes, query);
    }

    [Fact]
    public void Scanner_DetectsENQ()
    {
        var scanner = new TerminalQueryScanner();
        scanner.Scan([0x05]);

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.Enquiry, query);
    }

    [Fact]
    public void Scanner_HandlesSplitSequence_EscThenBracket6n()
    {
        var scanner = new TerminalQueryScanner();

        // Split ESC[6n across two chunks
        scanner.Scan("\x1b"u8);       // ESC only
        scanner.Scan("[6n"u8);        // rest

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.CursorPositionReport, query);
    }

    [Fact]
    public void Scanner_HandlesSplitSequence_EscBracketThen6n()
    {
        var scanner = new TerminalQueryScanner();

        scanner.Scan("\x1b["u8);      // ESC [
        scanner.Scan("6n"u8);         // param + final

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.CursorPositionReport, query);
    }

    [Fact]
    public void Scanner_HandlesSplitSequence_EscBracket6ThenN()
    {
        var scanner = new TerminalQueryScanner();

        scanner.Scan("\x1b[6"u8);     // ESC [ 6
        scanner.Scan("n"u8);          // final byte

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.CursorPositionReport, query);
    }

    [Fact]
    public void Scanner_IgnoresNonQueryCsi()
    {
        var scanner = new TerminalQueryScanner();

        // CSI H (cursor position set) — not a query
        scanner.Scan("\x1b[10;20H"u8);

        Assert.False(scanner.TryDequeue(out _));
    }

    [Fact]
    public void Scanner_DetectsMultipleQueries()
    {
        var scanner = new TerminalQueryScanner();
        scanner.Scan("\x1b[6n\x1b[5n\x1b[c"u8);

        Assert.True(scanner.TryDequeue(out var q1));
        Assert.Equal(TerminalQuery.CursorPositionReport, q1);

        Assert.True(scanner.TryDequeue(out var q2));
        Assert.Equal(TerminalQuery.DeviceStatusOk, q2);

        Assert.True(scanner.TryDequeue(out var q3));
        Assert.Equal(TerminalQuery.PrimaryDeviceAttributes, q3);

        Assert.False(scanner.TryDequeue(out _));
    }

    [Fact]
    public void Scanner_QueryMixedWithText()
    {
        var scanner = new TerminalQueryScanner();
        // Normal text, then DSR query, then more text
        scanner.Scan("Hello\x1b[6nWorld"u8);

        Assert.True(scanner.TryDequeue(out var query));
        Assert.Equal(TerminalQuery.CursorPositionReport, query);
        Assert.False(scanner.TryDequeue(out _));
    }

    [Fact]
    public void Scanner_ClearPending_RemovesAll()
    {
        var scanner = new TerminalQueryScanner();
        scanner.Scan("\x1b[6n\x1b[5n"u8);
        scanner.ClearPending();

        Assert.False(scanner.TryDequeue(out _));
    }

    [Fact]
    public void Scanner_IgnoresDA1WithNonZeroParam()
    {
        var scanner = new TerminalQueryScanner();
        // ESC[1c — not a standard DA1 query
        scanner.Scan("\x1b[1c"u8);

        Assert.False(scanner.TryDequeue(out _));
    }

    #endregion

    #region BasicVtProcessor DSR Response Tests

    [Fact]
    public void BasicVtProcessor_Dsr6_SendsCursorPositionReport()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);

        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        // Move cursor to row 5, col 10 (0-based → response should be 6;11)
        processor.Process("\x1b[6;11H"u8); // CUP to row 6, col 11 (1-based)
        processor.Process("\x1b[6n"u8);     // DSR cursor position query

        Assert.NotNull(response);
        Assert.Equal("\x1b[6;11R", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_Dsr5_SendsOperatingStatus()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);

        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b[5n"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b[0n", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_DA1_SendsPrimaryDeviceAttributes()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);

        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b[c"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b[?62;22c", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_DA2_SendsSecondaryDeviceAttributes()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);

        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b[>c"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b[>1;10;0c", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_NoCallback_DoesNotThrow()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        // No ResponseCallback set

        // Should not throw
        processor.Process("\x1b[6n"u8);
    }

    [Fact]
    public void BasicVtProcessor_Dsr6_AtOrigin_Reports1_1()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);

        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        // Cursor starts at origin (0,0) → response should be 1;1
        processor.Process("\x1b[6n"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b[1;1R", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_CombiningMark_AppendsToPreviousCellGrapheme()
    {
        var screen = new TerminalScreen(16, 4, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("e\u0301"u8);

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(1, processor.CursorCol);
        Assert.Equal(0, processor.CursorRow);
        Assert.Equal('e', row[0].Codepoint);
        Assert.Equal("e\u0301", row[0].Grapheme);
        Assert.Equal(0, row[1].Codepoint);
    }

    [Fact]
    public void BasicVtProcessor_ZwjSequence_AppendsToSingleCellGrapheme()
    {
        const string familyEmoji = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";

        var screen = new TerminalScreen(16, 4, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process(System.Text.Encoding.UTF8.GetBytes(familyEmoji));

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(1, processor.CursorCol);
        Assert.Equal(0, processor.CursorRow);
        Assert.Equal(0x1F468, row[0].Codepoint);
        Assert.Equal(familyEmoji, row[0].Grapheme);
        Assert.Equal(0, row[1].Codepoint);
    }

    #endregion
}
