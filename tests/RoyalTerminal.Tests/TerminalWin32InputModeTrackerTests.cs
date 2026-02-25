// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Tests for win32-input-mode tracker behavior.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalWin32InputModeTrackerTests
{
    [Fact]
    public void Tracker_SetAndResetMode_UpdatesState()
    {
        TerminalWin32InputModeTracker tracker = new();

        bool changed = tracker.Process("\x1b[?9001h"u8);
        Assert.True(changed);
        Assert.True(tracker.Win32InputMode);

        changed = tracker.Process("\x1b[?9001l"u8);
        Assert.True(changed);
        Assert.False(tracker.Win32InputMode);
    }

    [Fact]
    public void Tracker_SplitSequenceAcrossChunks_IsHandled()
    {
        TerminalWin32InputModeTracker tracker = new();

        Assert.False(tracker.Process("\x1b[?90"u8));
        Assert.True(tracker.Process("01h"u8));
        Assert.True(tracker.Win32InputMode);
    }

    [Fact]
    public void Tracker_DecstrAndRis_ResetMode()
    {
        TerminalWin32InputModeTracker tracker = new();
        tracker.Process("\x1b[?9001h"u8);
        tracker.Process("\x1b[?1004h"u8);
        Assert.True(tracker.Win32InputMode);
        Assert.True(tracker.FocusEventMode);

        bool changed = tracker.Process("\x1b[!p"u8);
        Assert.True(changed);
        Assert.False(tracker.Win32InputMode);
        Assert.False(tracker.FocusEventMode);

        tracker.Process("\x1b[?9001h"u8);
        tracker.Process("\x1b[?1004h"u8);
        Assert.True(tracker.Win32InputMode);
        Assert.True(tracker.FocusEventMode);

        changed = tracker.Process("\u001bc"u8);
        Assert.True(changed);
        Assert.False(tracker.Win32InputMode);
        Assert.False(tracker.FocusEventMode);
    }

    [Fact]
    public void Tracker_FocusMode_SetAndReset_IsHandled()
    {
        TerminalWin32InputModeTracker tracker = new();

        bool changed = tracker.Process("\x1b[?1004h"u8);
        Assert.True(changed);
        Assert.True(tracker.FocusEventMode);

        changed = tracker.Process("\x1b[?1004l"u8);
        Assert.True(changed);
        Assert.False(tracker.FocusEventMode);
    }
}
