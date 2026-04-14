// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Reusable TerminalCaptureRuntime behavior tests.

using System.Text;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Capture;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalCaptureRuntimeTests
{
    [AvaloniaFact]
    public async Task TerminalCaptureRuntime_CapturesInputAndOutput()
    {
        TerminalControl control = new();
        control.AttachEndpoint(new FakeTerminalEndpoint());
        TerminalCaptureRuntime runtime = new(control);

        try
        {
            runtime.StartCapture();
            control.SendInput("ls\r");
            control.WriteOutput(Encoding.UTF8.GetBytes("hello\n"));
            TerminalCaptureSession session = runtime.StopCapture();

            Assert.NotNull(session);
            Assert.True(session.Events.Count >= 2);
            Assert.Contains(session.Events, e => e.Kind == TerminalCaptureEventKind.Input);
            Assert.Contains(session.Events, e => e.Kind == TerminalCaptureEventKind.Output);
        }
        finally
        {
            runtime.Dispose();
            await HeadlessTerminalTestCleanup.CleanupControlAsync(
                control,
                control.DetachEndpoint);
        }
    }

    [AvaloniaFact]
    public async Task TerminalCaptureRuntime_LoadReplayAndSeek_UpdatesTimeline()
    {
        TerminalControl control = new();
        TerminalCaptureRuntime runtime = new(control);
        TerminalCaptureSession replay = new()
        {
            InitialColumns = 80,
            InitialRows = 24,
            Events =
            [
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 0,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = Encoding.UTF8.GetBytes("start"),
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 200,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = Encoding.UTF8.GetBytes("end"),
                },
            ],
        };

        try
        {
            runtime.LoadReplay(replay, "sample.rtcap.json");
            Assert.True(runtime.IsReplayEnabled);
            Assert.Equal("sample.rtcap.json", runtime.ReplaySourceLabel);
            Assert.Equal(0.2, runtime.ReplayDurationSeconds, 3);

            runtime.SeekReplay(0.15);
            Assert.Equal(0.15, runtime.ReplayPositionSeconds, 2);

            runtime.SeekReplay(99);
            Assert.Equal(0.2, runtime.ReplayPositionSeconds, 3);
        }
        finally
        {
            runtime.Dispose();
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    private sealed class FakeTerminalEndpoint : ITerminalEndpoint
    {
        public void SendText(ReadOnlySpan<byte> utf8)
        {
            _ = utf8;
        }

        public void SetFocus(bool focused)
        {
            _ = focused;
        }

        public void SetSize(int widthPx, int heightPx)
        {
            _ = widthPx;
            _ = heightPx;
        }
    }
}
