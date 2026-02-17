// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Tests for terminal query detection and response generation.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

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
    public void BasicVtProcessor_Bel_InvokesBellCallback()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        int bellCount = 0;
        processor.BellCallback = () => bellCount++;

        processor.Process([0x07]);

        Assert.Equal(1, bellCount);
    }

    [Fact]
    public void BasicVtProcessor_OscTitle_BelTerminator_InvokesTitleCallback()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        string? title = null;
        processor.TitleCallback = value => title = value;

        processor.Process("\x1b]2;phase-7-title\x07"u8);

        Assert.Equal("phase-7-title", title);
    }

    [Fact]
    public void BasicVtProcessor_OscTitle_StTerminatorAcrossChunks_InvokesTitleCallback()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        string? title = null;
        processor.TitleCallback = value => title = value;

        processor.Process("\x1b]0;phase-7-"u8);
        processor.Process("split\x1b\\"u8);

        Assert.Equal("phase-7-split", title);
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
    public void BasicVtProcessor_ModeState_TracksApplicationKeypad_AndRaisesModeChanged()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        int modeChangedCount = 0;
        TerminalModeState lastState = processor.ModeState;

        processor.ModeChanged += (_, state) =>
        {
            modeChangedCount++;
            lastState = state;
        };

        processor.Process("\x1b="u8); // DECKPAM

        Assert.True(processor.ApplicationKeypad);
        Assert.True(processor.ModeState.ApplicationKeypad);
        Assert.Equal(1, modeChangedCount);
        Assert.True(lastState.ApplicationKeypad);

        processor.Process("\x1b>"u8); // DECKPNM

        Assert.False(processor.ApplicationKeypad);
        Assert.False(processor.ModeState.ApplicationKeypad);
        Assert.Equal(2, modeChangedCount);
        Assert.False(lastState.ApplicationKeypad);
    }

    [Fact]
    public void BasicVtProcessor_Reset_RestoresDefaultModeState()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        int modeChangedCount = 0;
        processor.ModeChanged += (_, _) => modeChangedCount++;

        processor.Process("\x1b=\x1b[?1h\x1b[?25l\x1b[?2004h\x1b[?1049h"u8);
        Assert.True(processor.ApplicationKeypad);
        Assert.True(processor.ApplicationCursorKeys);
        Assert.False(processor.CursorVisible);
        Assert.True(processor.BracketedPaste);
        Assert.True(processor.AlternateScreen);

        processor.Reset();

        Assert.False(processor.ApplicationKeypad);
        Assert.False(processor.ApplicationCursorKeys);
        Assert.True(processor.CursorVisible);
        Assert.False(processor.BracketedPaste);
        Assert.False(processor.AlternateScreen);
        Assert.Equal(
            new TerminalModeState(
                CursorVisible: true,
                ApplicationCursorKeys: false,
                ApplicationKeypad: false,
                AlternateScreen: false,
                BracketedPaste: false),
            processor.ModeState);
        Assert.Equal(2, modeChangedCount);
    }

    [Fact]
    public void BasicVtProcessor_Ris_RaisesModeChangedOnce()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        processor.Process("\x1b="u8); // enable application keypad first
        Assert.True(processor.ApplicationKeypad);

        int modeChangedCount = 0;
        processor.ModeChanged += (_, _) => modeChangedCount++;

        processor.Process("\u001bc"u8); // RIS

        Assert.Equal(1, modeChangedCount);
        Assert.False(processor.ApplicationKeypad);
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
        Assert.Equal(1, row[0].Width);
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
        Assert.Equal(2, processor.CursorCol);
        Assert.Equal(0, processor.CursorRow);
        Assert.Equal(0x1F468, row[0].Codepoint);
        Assert.Equal(familyEmoji, row[0].Grapheme);
        Assert.Equal(2, row[0].Width);
        Assert.Equal(0, row[1].Codepoint);
        Assert.Equal(0, row[1].Width);
    }

    [Fact]
    public void BasicVtProcessor_RegionalIndicatorPair_AppendsToSingleCellGrapheme()
    {
        const string canadaFlag = "\U0001F1E8\U0001F1E6";

        var screen = new TerminalScreen(16, 4, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process(System.Text.Encoding.UTF8.GetBytes(canadaFlag));

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(2, processor.CursorCol);
        Assert.Equal(0, processor.CursorRow);
        Assert.Equal(0x1F1E8, row[0].Codepoint);
        Assert.Equal(canadaFlag, row[0].Grapheme);
        Assert.Equal(2, row[0].Width);
        Assert.Equal(0, row[1].Codepoint);
        Assert.Equal(0, row[1].Width);
    }

    [Fact]
    public void BasicVtProcessor_RegionalIndicatorTriplet_SplitsAfterFirstPair()
    {
        const string triplet = "\U0001F1E8\U0001F1E6\U0001F1FA";

        var screen = new TerminalScreen(16, 4, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process(System.Text.Encoding.UTF8.GetBytes(triplet));

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(4, processor.CursorCol);
        Assert.Equal(0x1F1E8, row[0].Codepoint);
        Assert.Equal("\U0001F1E8\U0001F1E6", row[0].Grapheme);
        Assert.Equal(2, row[0].Width);
        Assert.Equal(0, row[1].Width);
        Assert.Equal(0x1F1FA, row[2].Codepoint);
        Assert.Null(row[2].Grapheme);
        Assert.Equal(2, row[2].Width);
        Assert.Equal(0, row[3].Width);
    }

    [Fact]
    public void BasicVtProcessor_KeycapSequence_AppendsToSingleCellGrapheme()
    {
        const string keycap = "#\uFE0F\u20E3";

        var screen = new TerminalScreen(16, 4, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process(System.Text.Encoding.UTF8.GetBytes(keycap));

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(2, processor.CursorCol);
        Assert.Equal('#', row[0].Codepoint);
        Assert.Equal(keycap, row[0].Grapheme);
        Assert.Equal(2, row[0].Width);
        Assert.Equal(0, row[1].Codepoint);
        Assert.Equal(0, row[1].Width);
    }

    [Fact]
    public void BasicVtProcessor_OverwriteWideCharSpacer_ClearsLeadingWideCell()
    {
        var screen = new TerminalScreen(16, 4, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("中"u8);
        processor.Process("\bA"u8);

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(2, processor.CursorCol);
        Assert.Equal(0, row[0].Codepoint);
        Assert.Equal(1, row[0].Width);
        Assert.Equal('A', row[1].Codepoint);
        Assert.Equal(1, row[1].Width);
    }

    [Fact]
    public void BasicVtProcessor_CombiningMarkAtLineEnd_DoesNotWrapBeforeAppend()
    {
        var screen = new TerminalScreen(2, 2, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("AB\u0301"u8);

        TerminalRow row0 = screen.GetViewportRow(0);
        TerminalRow row1 = screen.GetViewportRow(1);
        Assert.Equal(2, processor.CursorCol);
        Assert.Equal(0, processor.CursorRow);
        Assert.Equal("B\u0301", row0[1].Grapheme);
        Assert.False(row1[0].HasContent);
    }

    [Fact]
    public void BasicVtProcessor_DeleteCharacterFromWideLead_NormalizesRow()
    {
        var screen = new TerminalScreen(8, 2, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("中A"u8);
        processor.Process("\r\x1b[P"u8);

        TerminalRow row = screen.GetViewportRow(0);
        AssertNoBrokenWideCells(row);
        Assert.Equal('A', row[1].Codepoint);
    }

    [Fact]
    public void BasicVtProcessor_EraseFromWideSpacer_NormalizesRow()
    {
        var screen = new TerminalScreen(8, 2, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("中A"u8);
        processor.Process("\x1b[1;2H\x1b[K"u8); // CUP row1,col2 then EL0

        TerminalRow row = screen.GetViewportRow(0);
        AssertNoBrokenWideCells(row);
        Assert.False(row[0].HasContent);
    }

    private static void AssertNoBrokenWideCells(TerminalRow row)
    {
        for (int col = 0; col < row.Columns; col++)
        {
            TerminalCell current = row[col];
            if (current.Width == 2)
            {
                Assert.True(col + 1 < row.Columns, $"Wide leader at col {col} reaches beyond row.");
                TerminalCell trailing = row[col + 1];
                Assert.Equal(0, trailing.Width);
                Assert.False(trailing.HasContent);
                continue;
            }

            if (current.Width == 0)
            {
                Assert.True(col > 0 && row[col - 1].Width == 2, $"Orphan spacer at col {col}.");
            }
        }
    }

    #endregion
}
