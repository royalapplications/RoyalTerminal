// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty 5c9a3d8a9 rejects CR and LF in its mode-independent safety query.
// The shared host already classifies both as multiline, like Windows Terminal.
public sealed class TerminalPasteSafetyParityTests
{
    [Theory]
    [InlineData("echo hello", true)]
    [InlineData("echo hello\r", false)]
    [InlineData("echo hello\n", false)]
    [InlineData("echo hello\r\n", false)]
    [InlineData("one\rtwo", false)]
    [InlineData("\u001b[201~", false)]
    [InlineData("", true)]
    public void ManagedAndNativeSafetyQueriesRejectBothLineTerminators(string text, bool expected)
    {
        Assert.Equal(expected, TerminalPasteEncoder.IsSafe(text));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        Assert.Equal(expected, managed.IsPasteSafe(text));
        Assert.SkipWhen(!GhosttyVtProcessor.IsAvailable(), "Native Ghostty is unavailable.");
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        Assert.Equal(expected, native.IsPasteSafe(text));
        // This query does not inspect terminal modes; framing remains a
        // separate choice and preserves CR inside a bracketed paste.
        managed.Process("\u001b[?2004h"u8);
        native.Process("\u001b[?2004h"u8);
        Assert.Equal(expected, managed.IsPasteSafe(text));
        Assert.Equal(expected, native.IsPasteSafe(text));
    }

    [Fact]
    public void BracketedEncodingPreservesCarriageReturns()
    {
        Assert.Equal("\u001b[200~echo hello\r\u001b[201~"u8.ToArray(), TerminalPasteEncoder.Encode("echo hello\r", true));
    }
}
