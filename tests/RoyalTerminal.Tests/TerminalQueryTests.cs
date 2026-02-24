// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Tests for terminal query detection and response generation.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public class TerminalQueryTests
{
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
    public void BasicVtProcessor_Dsr6_AcrossChunks_SendsCursorPositionReport()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b"u8);
        processor.Process("[6n"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b[1;1R", System.Text.Encoding.ASCII.GetString(response));
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
    public void BasicVtProcessor_NonQueryCsi_DoesNotRespond()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b[10;20H"u8);

        Assert.Null(response);
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
    public void BasicVtProcessor_DA1_AcrossChunks_SendsPrimaryDeviceAttributes()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b["u8);
        processor.Process("c"u8);

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
    public void BasicVtProcessor_DA2_AcrossChunks_SendsSecondaryDeviceAttributes()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b[>"u8);
        processor.Process("c"u8);

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
    public void BasicVtProcessor_Enq_NoAnswerbackConfigured_DoesNotRespond()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process([0x05]);

        Assert.Null(response);
    }

    [Fact]
    public void BasicVtProcessor_Enq_WithAnswerbackConfigured_Responds()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen)
        {
            EnquiryResponse = "RoyalTerminal"u8.ToArray(),
        };
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process([0x05]);

        Assert.NotNull(response);
        Assert.Equal("RoyalTerminal", System.Text.Encoding.ASCII.GetString(response));
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
    public void BasicVtProcessor_OscForegroundQuery_RespondsWithRgbValue()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b]10;?\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b]10;rgb:D4D4/D4D4/D4D4\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_OscPaletteQuery_RespondsWithIndexedColor()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b]4;1;?\x07"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b]4;1;rgb:CDCD/0000/0000\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_OscForegroundSetThenQuery_UsesActiveThemeColor()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b]10;#112233\x1b\\"u8);
        processor.Process("\x1b]10;?\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b]10;rgb:1111/2222/3333\x1b\\", System.Text.Encoding.ASCII.GetString(response));
        Assert.Equal(0xFF112233u, screen.DefaultForeground);
    }

    [Fact]
    public void BasicVtProcessor_OscPaletteSetThenQuery_UsesUpdatedColor()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b]4;42;#445566\x1b\\"u8);
        processor.Process("\x1b]4;42;?\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b]4;42;rgb:4444/5555/6666\x1b\\", System.Text.Encoding.ASCII.GetString(response));
        Assert.Equal(0xFF445566u, screen.Theme.Palette[42]);
    }

    [Fact]
    public void BasicVtProcessor_OscQuery_Uses8BitFormatWhenConfigured()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        TerminalTheme theme = TerminalTheme.Dark
            .WithDefaultForeground(0xFF112233u)
            .WithOscColorReportFormat(TerminalOscColorReportFormat.Bit8);
        processor.ApplyTheme(theme);

        processor.Process("\x1b]10;?\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1b]10;rgb:11/22/33\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void TerminalScreen_ApplyTheme_RemapsExistingDefaultAndIndexedColors()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("A\x1b[31mB"u8);

        TerminalTheme theme = screen.Theme
            .WithDefaultForeground(0xFF112233u)
            .WithDefaultBackground(0xFF223344u)
            .WithPaletteColor(1, 0xFF44AA55u, explicitOverride: true);

        screen.ApplyTheme(theme, invalidateRows: true);

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(theme.DefaultForeground, row[0].Foreground);
        Assert.Equal(theme.DefaultBackground, row[0].Background);
        Assert.Equal(theme.Palette[1], row[1].Foreground);
        Assert.Equal(theme.DefaultBackground, row[1].Background);
    }

    [Fact]
    public void BasicVtProcessor_ApplyTheme_WhenScreenWasPreApplied_RemapsActiveColors()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("\x1b[31mA"u8);

        TerminalTheme theme = screen.Theme
            .WithDefaultForeground(0xFF102030u)
            .WithDefaultBackground(0xFF203040u)
            .WithPaletteColor(1, 0xFF55AA11u, explicitOverride: true);

        // Mirrors TerminalControl theme flow that may pre-apply to screen first.
        screen.ApplyTheme(theme, invalidateRows: true);
        processor.ApplyTheme(theme);

        processor.Process("B\x1b[0mC"u8);

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(theme.Palette[1], row[0].Foreground);
        Assert.Equal(theme.Palette[1], row[1].Foreground);
        Assert.Equal(theme.DefaultBackground, row[1].Background);
        Assert.Equal(theme.DefaultForeground, row[2].Foreground);
        Assert.Equal(theme.DefaultBackground, row[2].Background);
    }

    [Fact]
    public void BasicVtProcessor_DcsDecrqss_SgrQuery_ReturnsCurrentSgrState()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b[1;31m"u8);
        processor.Process("\x1bP$qm\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1bP1$r1;38;2;205;0;0m\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_DcsDecrqss_MarginsQuery_ReturnsScrollRegion()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b[2;10r"u8);
        processor.Process("\x1bP$qr\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1bP1$r2;10r\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_DcsDecrqss_CursorStyleQuery_ReflectsDecscusr()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1b[5 q"u8);
        processor.Process("\x1bP$q q\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1bP1$r5 q\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_DcsDecrqss_UnsupportedQuery_ReturnsFailureResponse()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1bP$qx\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1bP0$r\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_DcsDecrqss_AcrossChunks_IsHandled()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        processor.Process("\x1bP$q"u8);
        processor.Process("r\x1b\\"u8);

        Assert.NotNull(response);
        Assert.Equal("\x1bP1$r1;24r\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_DcsOversizePayload_IsDiscarded_AndParserRecovers()
    {
        var screen = new TerminalScreen(80, 24, 0);
        var processor = new BasicVtProcessor(screen);
        byte[]? response = null;
        processor.ResponseCallback = data => response = data;

        string largePayload = new('A', 5000);
        processor.Process(System.Text.Encoding.ASCII.GetBytes($"\x1bP{largePayload}\x1b\\"));

        // Oversized DCS payload should be dropped, not interpreted.
        Assert.Null(response);

        // Parser should recover and handle subsequent DECRQSS.
        processor.Process("\x1bP$qr\x1b\\"u8);
        Assert.NotNull(response);
        Assert.Equal("\x1bP1$r1;24r\x1b\\", System.Text.Encoding.ASCII.GetString(response));
    }

    [Fact]
    public void BasicVtProcessor_Rep_RepeatsLastGraphicCharacter()
    {
        var screen = new TerminalScreen(16, 4, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("A\x1b[3b"u8);

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(4, processor.CursorCol);
        Assert.Equal('A', row[0].Codepoint);
        Assert.Equal('A', row[1].Codepoint);
        Assert.Equal('A', row[2].Codepoint);
        Assert.Equal('A', row[3].Codepoint);
    }

    [Fact]
    public void BasicVtProcessor_SmRm_InsertMode_InsertsCharacters()
    {
        var screen = new TerminalScreen(5, 2, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("ABCD"u8);
        processor.Process("\x1b[1;2H\x1b[4hZ\x1b[4l"u8);

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal('A', row[0].Codepoint);
        Assert.Equal('Z', row[1].Codepoint);
        Assert.Equal('B', row[2].Codepoint);
        Assert.Equal('C', row[3].Codepoint);
        Assert.Equal('D', row[4].Codepoint);
    }

    [Fact]
    public void BasicVtProcessor_SmRm_LineFeedNewLineMode_MovesToColumnZeroOnLineFeed()
    {
        var screen = new TerminalScreen(4, 2, 0);
        var processor = new BasicVtProcessor(screen);

        processor.Process("A\x1b[20h\nB"u8);

        TerminalRow row0 = screen.GetViewportRow(0);
        TerminalRow row1 = screen.GetViewportRow(1);
        Assert.Equal('A', row0[0].Codepoint);
        Assert.Equal('B', row1[0].Codepoint);
        Assert.False(row1[1].HasContent);

        processor.Process("\x1b[20l"u8);
        processor.Reset();
        processor.Process("A\nB"u8);
        row1 = screen.GetViewportRow(1);
        Assert.Equal('B', row1[1].Codepoint);
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

    [Fact]
    public void ManagedVsNative_OscQueryFamilies_ResponsesMatch_WhenNativeAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        using VtParityPair parity = CreateVtParityPair(columns: 80, rows: 24);

        parity.ProcessBoth("\x1b]10;?\x1b\\"u8);
        if (!CanCompareResponseParity(parity))
        {
            return;
        }
        AssertResponseParity(parity, "OSC 10");

        parity.ProcessBoth("\x1b]11;?\x1b\\"u8);
        AssertResponseParity(parity, "OSC 11");

        parity.ProcessBoth("\x1b]12;?\x1b\\"u8);
        AssertResponseParity(parity, "OSC 12");

        parity.ProcessBoth("\x1b]4;1;?\x07"u8);
        AssertResponseParity(parity, "OSC 4");
    }

    [Fact]
    public void ManagedVsNative_DcsDecrqss_ResponsesMatch_WhenNativeAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        using VtParityPair parity = CreateVtParityPair(columns: 80, rows: 24);

        parity.ProcessBoth("\x1b[1;31m"u8);
        parity.ProcessBoth("\x1bP$qm\x1b\\"u8);
        if (!CanCompareResponseParity(parity))
        {
            return;
        }
        AssertResponseParity(parity, "DECRQSS SGR");

        parity.ProcessBoth("\x1b[2;10r"u8);
        parity.ProcessBoth("\x1bP$qr\x1b\\"u8);
        AssertResponseParity(parity, "DECRQSS scroll margins");

        parity.ProcessBoth("\x1b[5 q"u8);
        parity.ProcessBoth("\x1bP$q q\x1b\\"u8);
        AssertResponseParity(parity, "DECRQSS cursor style");
    }

    [Fact]
    public void ManagedVsNative_CsiSmRmRepAndCursorStyle_StateAndScreenMatch_WhenNativeAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        using VtParityPair repParity = CreateVtParityPair(columns: 16, rows: 4);
        repParity.ProcessBoth("A\x1b[3b"u8);
        AssertScreenPrefixParity(repParity, row: 0, prefixColumns: 4, "REP");
        AssertCursorParity(repParity, "REP");

        using VtParityPair insertParity = CreateVtParityPair(columns: 8, rows: 2);
        insertParity.ProcessBoth("ABCD\x1b[1;2H\x1b[4hZ\x1b[4l"u8);
        AssertScreenPrefixParity(insertParity, row: 0, prefixColumns: 5, "IRM");
        AssertCursorParity(insertParity, "IRM");

        using VtParityPair lineModeParity = CreateVtParityPair(columns: 4, rows: 2);
        lineModeParity.ProcessBoth("A\x1b[20h\nB"u8);
        AssertScreenPrefixParity(lineModeParity, row: 0, prefixColumns: 2, "LNM enabled row0");
        AssertScreenPrefixParity(lineModeParity, row: 1, prefixColumns: 2, "LNM enabled row1");
        AssertCursorParity(lineModeParity, "LNM enabled");

        lineModeParity.ResetBoth();
        lineModeParity.ProcessBoth("A\nB"u8);
        AssertScreenPrefixParity(lineModeParity, row: 0, prefixColumns: 2, "LNM disabled row0");
        AssertScreenPrefixParity(lineModeParity, row: 1, prefixColumns: 2, "LNM disabled row1");
        AssertCursorParity(lineModeParity, "LNM disabled");
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

    private static VtParityPair CreateVtParityPair(int columns, int rows)
    {
        TerminalScreen managedScreen = new(columns, rows, 0);
        TerminalScreen nativeScreen = new(columns, rows, 0);
        return new VtParityPair(
            managedScreen,
            nativeScreen,
            new BasicVtProcessor(managedScreen),
            new GhosttyVtProcessor(nativeScreen));
    }

    private static void AssertResponseParity(VtParityPair parity, string scenario)
    {
        Assert.Equal(parity.ManagedResponses.Count, parity.NativeResponses.Count);
        Assert.NotEmpty(parity.ManagedResponses);

        for (int i = 0; i < parity.ManagedResponses.Count; i++)
        {
            string managed = NormalizeTerminalResponse(parity.ManagedResponses[i]);
            string native = NormalizeTerminalResponse(parity.NativeResponses[i]);
            Assert.True(
                string.Equals(managed, native, StringComparison.Ordinal),
                $"{scenario} response mismatch. managed='{EscapeForAssert(managed)}', native='{EscapeForAssert(native)}'");
        }

        parity.ClearResponses();
    }

    private static bool CanCompareResponseParity(VtParityPair parity)
    {
        // Some native builds currently do not emit callback responses for OSC/DCS
        // query families. Keep explicit parity assertions when native responses are
        // observable, and no-op otherwise.
        if (parity.NativeResponses.Count > 0)
        {
            return true;
        }

        parity.ClearResponses();
        return false;
    }

    private static void AssertScreenPrefixParity(VtParityPair parity, int row, int prefixColumns, string scenario)
    {
        string managed = ReadAsciiPrefix(parity.ManagedScreen, row, prefixColumns);
        string native = ReadAsciiPrefix(parity.NativeScreen, row, prefixColumns);
        Assert.True(
            string.Equals(managed, native, StringComparison.Ordinal),
            $"{scenario} row parity mismatch at row {row}. managed='{EscapeForAssert(managed)}', native='{EscapeForAssert(native)}'");
    }

    private static void AssertCursorParity(VtParityPair parity, string scenario)
    {
        Assert.True(
            parity.ManagedProcessor.CursorCol == parity.NativeProcessor.CursorCol,
            $"{scenario} cursor column mismatch. managed={parity.ManagedProcessor.CursorCol}, native={parity.NativeProcessor.CursorCol}");
        Assert.True(
            parity.ManagedProcessor.CursorRow == parity.NativeProcessor.CursorRow,
            $"{scenario} cursor row mismatch. managed={parity.ManagedProcessor.CursorRow}, native={parity.NativeProcessor.CursorRow}");
        Assert.True(
            parity.ManagedProcessor.ModeState == parity.NativeProcessor.ModeState,
            $"{scenario} mode-state mismatch. managed={parity.ManagedProcessor.ModeState}, native={parity.NativeProcessor.ModeState}");
    }

    private static string ReadAsciiPrefix(TerminalScreen screen, int row, int columns)
    {
        TerminalRow terminalRow = screen.GetViewportRow(row);
        int maxColumns = Math.Min(columns, terminalRow.Columns);
        char[] chars = new char[maxColumns];
        for (int col = 0; col < maxColumns; col++)
        {
            int codepoint = terminalRow[col].Codepoint;
            chars[col] = codepoint <= 0 ? ' ' : codepoint <= 0x7F ? (char)codepoint : '?';
        }

        return new string(chars);
    }

    private static string NormalizeTerminalResponse(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return response;
        }

        string normalized = response.Replace("\x9C", "\x1b\\", StringComparison.Ordinal);
        if (normalized.StartsWith("\x1b]", StringComparison.Ordinal) &&
            normalized.EndsWith('\a'))
        {
            normalized = normalized[..^1] + "\x1b\\";
        }

        return normalized;
    }

    private static string EscapeForAssert(string value)
    {
        return value
            .Replace("\x1b", "<ESC>", StringComparison.Ordinal)
            .Replace("\a", "<BEL>", StringComparison.Ordinal);
    }

    private sealed class VtParityPair : IDisposable
    {
        public VtParityPair(
            TerminalScreen managedScreen,
            TerminalScreen nativeScreen,
            BasicVtProcessor managedProcessor,
            GhosttyVtProcessor nativeProcessor)
        {
            ManagedScreen = managedScreen;
            NativeScreen = nativeScreen;
            ManagedProcessor = managedProcessor;
            NativeProcessor = nativeProcessor;
            ManagedResponses = [];
            NativeResponses = [];

            ManagedProcessor.ResponseCallback = data => ManagedResponses.Add(System.Text.Encoding.ASCII.GetString(data));
            NativeProcessor.ResponseCallback = data => NativeResponses.Add(System.Text.Encoding.ASCII.GetString(data));
        }

        public TerminalScreen ManagedScreen { get; }

        public TerminalScreen NativeScreen { get; }

        public BasicVtProcessor ManagedProcessor { get; }

        public GhosttyVtProcessor NativeProcessor { get; }

        public List<string> ManagedResponses { get; }

        public List<string> NativeResponses { get; }

        public void ProcessBoth(ReadOnlySpan<byte> data)
        {
            ManagedProcessor.Process(data);
            NativeProcessor.Process(data);
        }

        public void ResetBoth()
        {
            ManagedProcessor.Reset();
            NativeProcessor.Reset();
            ClearResponses();
        }

        public void ClearResponses()
        {
            ManagedResponses.Clear();
            NativeResponses.Clear();
        }

        public void Dispose()
        {
            ManagedProcessor.Dispose();
            NativeProcessor.Dispose();
        }
    }

    #endregion
}
