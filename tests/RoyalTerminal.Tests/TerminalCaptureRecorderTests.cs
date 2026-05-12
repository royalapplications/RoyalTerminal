// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Capture/replay recorder and serializer coverage.

using System.Text;
using System.Text.Json;
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
    public void Recorder_CapturesSingleExitEvent()
    {
        TerminalCaptureRecorder recorder = new();
        recorder.StartCapture(initialColumns: 80, initialRows: 24, transportId: TerminalTransportIds.Pty);

        recorder.CaptureExit(17);
        recorder.CaptureExit(23);

        TerminalCaptureSession session = recorder.StopCapture();

        Assert.Single(session.Events);
        Assert.Equal(TerminalCaptureEventKind.Exit, session.Events[0].Kind);
        Assert.Equal(17, session.Events[0].ExitCode);
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
    public void FormatRegistry_ResolvesBuiltInFormatsByIdAndExtension()
    {
        TerminalCaptureSessionFormatRegistry registry = TerminalCaptureSessionFormats.DefaultRegistry;

        ITerminalCaptureSessionFormat jsonFormat =
            registry.GetRequiredFormat(TerminalCaptureSessionFormats.RoyalTerminalJsonId);
        ITerminalCaptureSessionFormat asciicastFormat =
            registry.GetRequiredFormat(TerminalCaptureSessionFormats.AsciicastV3Id);

        Assert.Same(jsonFormat, registry.FindByFileName("session.rtcap.json"));
        Assert.Same(asciicastFormat, registry.FindByFileName("session.cast"));
    }

    [Fact]
    public async Task AsciicastV3Format_SavesCompatibleNdjson()
    {
        TerminalCaptureSession session = new()
        {
            CreatedUtc = DateTimeOffset.FromUnixTimeSeconds(1_504_467_315),
            InitialColumns = 80,
            InitialRows = 24,
            Events =
            [
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 248,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = Encoding.UTF8.GetBytes("\u001b[31mHello\u001b[0m\n"),
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 1001,
                    Kind = TerminalCaptureEventKind.Input,
                    Data = Encoding.UTF8.GetBytes("x"),
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 3051,
                    Kind = TerminalCaptureEventKind.Resize,
                    Columns = 90,
                    Rows = 30,
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 4592,
                    Kind = TerminalCaptureEventKind.Marker,
                    Label = "Chapter",
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 5479,
                    Kind = TerminalCaptureEventKind.Exit,
                    ExitCode = 0,
                },
            ],
        };

        await using MemoryStream stream = new();
        await TerminalCaptureSessionFormats.AsciicastV3.SaveAsync(session, stream);

        string[] lines = Encoding.UTF8
            .GetString(stream.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        using JsonDocument header = JsonDocument.Parse(lines[0]);
        Assert.Equal(3, header.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(80, header.RootElement.GetProperty("term").GetProperty("cols").GetInt32());
        Assert.Equal(24, header.RootElement.GetProperty("term").GetProperty("rows").GetInt32());
        Assert.Equal(1_504_467_315, header.RootElement.GetProperty("timestamp").GetInt64());

        AssertAsciicastEvent(lines[1], 0.248m, "o", "\u001b[31mHello\u001b[0m\n");
        AssertAsciicastEvent(lines[2], 0.753m, "i", "x");
        AssertAsciicastEvent(lines[3], 2.050m, "r", "90x30");
        AssertAsciicastEvent(lines[4], 1.541m, "m", "Chapter");
        AssertAsciicastEvent(lines[5], 0.887m, "x", "0");
    }

    [Fact]
    public async Task AsciicastV3Format_SavesSplitUtf8SequenceAtCompletionTime()
    {
        TerminalCaptureSession session = new()
        {
            InitialColumns = 80,
            InitialRows = 24,
            Events =
            [
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 10,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = [0xE2],
                },
                new TerminalCaptureEvent
                {
                    OffsetMilliseconds = 25,
                    Kind = TerminalCaptureEventKind.Output,
                    Data = [0x82, 0xAC],
                },
            ],
        };

        await using MemoryStream stream = new();
        await TerminalCaptureSessionFormats.AsciicastV3.SaveAsync(session, stream);

        string[] lines = Encoding.UTF8
            .GetString(stream.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        AssertAsciicastEvent(lines[1], 0.025m, "o", "\u20AC");
    }

    [Fact]
    public async Task AsciicastV3Format_LoadsOutputInputResizeMarkerAndExitEvents()
    {
        const string cast = """
                            {"version": 3, "term": {"cols": 80, "rows": 24}, "timestamp": 1504467315}
                            # event stream follows
                            [0.248, "o", "hello"]
                            [0.753, "i", "x"]
                            [2.050, "r", "90x30"]
                            [1.541, "m", "Chapter"]
                            [0.887, "x", "7"]
                            """;

        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(cast));
        TerminalCaptureSession session = await TerminalCaptureSessionFormats.AsciicastV3.LoadAsync(stream);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_504_467_315), session.CreatedUtc);
        Assert.Equal(80, session.InitialColumns);
        Assert.Equal(24, session.InitialRows);
        Assert.Equal(5, session.Events.Count);
        Assert.Equal(248, session.Events[0].OffsetMilliseconds);
        Assert.Equal(1001, session.Events[1].OffsetMilliseconds);
        Assert.Equal(3051, session.Events[2].OffsetMilliseconds);
        Assert.Equal(4592, session.Events[3].OffsetMilliseconds);
        Assert.Equal(5479, session.Events[4].OffsetMilliseconds);
        Assert.Equal("hello", Encoding.UTF8.GetString(session.Events[0].Data!));
        Assert.Equal("x", Encoding.UTF8.GetString(session.Events[1].Data!));
        Assert.Equal(TerminalCaptureEventKind.Resize, session.Events[2].Kind);
        Assert.Equal(90, session.Events[2].Columns);
        Assert.Equal(30, session.Events[2].Rows);
        Assert.Equal(TerminalCaptureEventKind.Marker, session.Events[3].Kind);
        Assert.Equal("Chapter", session.Events[3].Label);
        Assert.Equal(TerminalCaptureEventKind.Exit, session.Events[4].Kind);
        Assert.Equal(7, session.Events[4].ExitCode);
        Assert.Equal(5479, session.DurationMilliseconds);
    }

    [Fact]
    public async Task FormatRegistry_LoadAsync_UsesFileNameToLoadAsciicast()
    {
        const string cast = """
                            {"version": 3, "term": {"cols": 100, "rows": 40}}
                            [0.001, "o", "a"]
                            """;

        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(cast));
        TerminalCaptureSession session = await TerminalCaptureSessionFormats.DefaultRegistry
            .LoadAsync(stream, "sample.cast");

        Assert.Equal(100, session.InitialColumns);
        Assert.Equal(40, session.InitialRows);
        Assert.Single(session.Events);
        Assert.Equal("a", Encoding.UTF8.GetString(session.Events[0].Data!));
    }

    [Fact]
    public async Task FormatRegistry_LoadAsync_ProbesHeaderOnlyAsciicastWithoutFileName()
    {
        const string cast = """
                            {"version": 3, "term": {"cols": 120, "rows": 35}}
                            """;

        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(cast));
        TerminalCaptureSession session = await TerminalCaptureSessionFormats.DefaultRegistry.LoadAsync(stream);

        Assert.Equal(120, session.InitialColumns);
        Assert.Equal(35, session.InitialRows);
        Assert.Empty(session.Events);
    }

    [Fact]
    public async Task AsciicastV3Format_LoadAsync_RejectsInvalidUtf8()
    {
        byte[] bytes =
        [
            (byte)'{', (byte)'"', (byte)'v', (byte)'e', (byte)'r', (byte)'s', (byte)'i', (byte)'o', (byte)'n',
            (byte)'"', (byte)':', (byte)'3', (byte)',', (byte)'"', (byte)'t', (byte)'e', (byte)'r', (byte)'m',
            (byte)'"', (byte)':', (byte)'{', (byte)'"', (byte)'c', (byte)'o', (byte)'l', (byte)'s', (byte)'"',
            (byte)':', (byte)'8', (byte)'0', (byte)',', (byte)'"', (byte)'r', (byte)'o', (byte)'w', (byte)'s',
            (byte)'"', (byte)':', (byte)'2', (byte)'4', (byte)'}', (byte)'}', (byte)'\n',
            (byte)'[', (byte)'0', (byte)',', (byte)'"', (byte)'o', (byte)'"', (byte)',', (byte)'"', 0xFF,
            (byte)'"', (byte)']',
        ];

        await using MemoryStream stream = new(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await TerminalCaptureSessionFormats.AsciicastV3.LoadAsync(stream));
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

    private static void AssertAsciicastEvent(
        string line,
        decimal expectedInterval,
        string expectedCode,
        string expectedData)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(expectedInterval, root[0].GetDecimal());
        Assert.Equal(expectedCode, root[1].GetString());
        Assert.Equal(expectedData, root[2].GetString());
    }
}
