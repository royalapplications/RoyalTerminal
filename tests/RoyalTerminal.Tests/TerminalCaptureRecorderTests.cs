// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Capture/replay recorder and serializer coverage.

using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalCaptureRecorderTests
{
    [Fact]
    public void Recorder_CapturesOutputInputAndResizeEvents()
    {
        TerminalCaptureRecorder recorder = new();
        recorder.StartCapture(initialColumns: 80, initialRows: 24, transportId: TerminalTransportIds.Pty);

        recorder.CaptureOutput(Encoding.UTF8.GetBytes("hello"));
        recorder.CaptureInput(Encoding.UTF8.GetBytes("ls\r"));
        recorder.CaptureResize(columns: 120, rows: 40);

        TerminalCaptureSession session = recorder.StopCapture();

        Assert.Equal(TerminalTransportIds.Pty, session.TransportId);
        Assert.Equal(80, session.InitialColumns);
        Assert.Equal(24, session.InitialRows);
        Assert.Equal(3, session.Events.Count);
        Assert.Equal(TerminalCaptureEventKind.Output, session.Events[0].Kind);
        Assert.Equal(TerminalCaptureEventKind.Input, session.Events[1].Kind);
        Assert.Equal(TerminalCaptureEventKind.Resize, session.Events[2].Kind);
        Assert.Equal("hello", Encoding.UTF8.GetString(session.Events[0].Data!));
        Assert.Equal("ls\r", Encoding.UTF8.GetString(session.Events[1].Data!));
        Assert.Equal(120, session.Events[2].Columns);
        Assert.Equal(40, session.Events[2].Rows);
        Assert.True(session.DurationMilliseconds >= 0);
    }

    [Fact]
    public void Recorder_CreateSnapshot_DoesNotExposeMutablePayloadBuffers()
    {
        TerminalCaptureRecorder recorder = new();
        recorder.StartCapture(initialColumns: 80, initialRows: 24, transportId: TerminalTransportIds.Pty);
        recorder.CaptureOutput(Encoding.UTF8.GetBytes("abc"));

        TerminalCaptureSession snapshot = recorder.CreateSnapshot();
        snapshot.Events[0].Data![0] = (byte)'z';

        TerminalCaptureSession finalized = recorder.StopCapture();
        Assert.Equal("abc", Encoding.UTF8.GetString(finalized.Events[0].Data!));
    }

    [Fact]
    public async Task Serializer_RoundTripsCaptureSession()
    {
        TerminalCaptureSession original = new()
        {
            TransportId = TerminalTransportIds.Ssh,
            InitialColumns = 90,
            InitialRows = 30,
            Events =
            [
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 12,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = Encoding.UTF8.GetBytes("output"),
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 44,
                    Kind = TerminalCaptureEventKind.Input,
                    Data = Encoding.UTF8.GetBytes("input"),
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 55,
                    Kind = TerminalCaptureEventKind.Resize,
                    Columns = 100,
                    Rows = 35,
                },
            ],
        };

        await using MemoryStream stream = new();
        await TerminalCaptureSessionSerializer.SaveAsync(original, stream);
        stream.Position = 0;

        TerminalCaptureSession restored = await TerminalCaptureSessionSerializer.LoadAsync(stream);

        Assert.Equal(original.FormatVersion, restored.FormatVersion);
        Assert.Equal(original.TransportId, restored.TransportId);
        Assert.Equal(original.InitialColumns, restored.InitialColumns);
        Assert.Equal(original.InitialRows, restored.InitialRows);
        Assert.Equal(3, restored.Events.Count);
        Assert.Equal(TerminalCaptureEventKind.Output, restored.Events[0].Kind);
        Assert.Equal(TerminalCaptureEventKind.Input, restored.Events[1].Kind);
        Assert.Equal(TerminalCaptureEventKind.Resize, restored.Events[2].Kind);
        Assert.Equal("output", Encoding.UTF8.GetString(restored.Events[0].Data!));
        Assert.Equal("input", Encoding.UTF8.GetString(restored.Events[1].Data!));
        Assert.Equal(100, restored.Events[2].Columns);
        Assert.Equal(35, restored.Events[2].Rows);
        Assert.Equal(55, restored.DurationMilliseconds);
    }

    [Fact]
    public async Task Serializer_LoadAsync_RejectsInvalidResizeEvent()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "initialColumns": 80,
                              "initialRows": 24,
                              "events": [
                                {
                                  "offsetMilliseconds": 1,
                                  "kind": "Resize",
                                  "columns": 100,
                                  "rows": 0
                                }
                              ]
                            }
                            """;

        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await TerminalCaptureSessionSerializer.LoadAsync(stream));
    }

    [Fact]
    public async Task Serializer_LoadAsync_PreservesOrderForEqualOffsets()
    {
        const string json = """
                            {
                              "formatVersion": 1,
                              "initialColumns": 80,
                              "initialRows": 24,
                              "events": [
                                {
                                  "offsetMilliseconds": 5,
                                  "kind": "Output",
                                  "data": "QQ=="
                                },
                                {
                                  "offsetMilliseconds": 5,
                                  "kind": "Output",
                                  "data": "Qg=="
                                }
                              ]
                            }
                            """;

        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(json));
        TerminalCaptureSession session = await TerminalCaptureSessionSerializer.LoadAsync(stream);

        Assert.Equal(2, session.Events.Count);
        Assert.Equal("A", Encoding.UTF8.GetString(session.Events[0].Data!));
        Assert.Equal("B", Encoding.UTF8.GetString(session.Events[1].Data!));
    }

    [Fact]
    public void Session_DurationMilliseconds_ComputesWhenNotPrecomputed()
    {
        TerminalCaptureSession session = new()
        {
            Events =
            [
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 3,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = Encoding.UTF8.GetBytes("a"),
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 9,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = Encoding.UTF8.GetBytes("b"),
                },
            ],
        };

        Assert.Equal(9, session.DurationMilliseconds);
        Assert.Equal(9, session.DurationMilliseconds);
    }

    [Fact]
    public void Session_DurationMilliseconds_RecomputesWhenDurationIsNotPrecomputed()
    {
        TerminalCaptureSession session = new()
        {
            Events =
            [
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 2,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = Encoding.UTF8.GetBytes("x"),
                },
            ],
        };

        Assert.Equal(2, session.DurationMilliseconds);

        session.Events.Add(new TerminalCaptureEvent
        {
            OffsetMilliseconds = 11,
            Kind = TerminalCaptureEventKind.Output,
            Data = Encoding.UTF8.GetBytes("y"),
        });

        Assert.Equal(11, session.DurationMilliseconds);
    }
}
