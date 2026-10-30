// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;

internal static class HistoryCompressionBenchmark
{
    internal static void Run()
    {
        if (!GhosttyVtProcessor.IsAvailable()) throw new InvalidOperationException("Native comparison requires libghostty-vt.");
        PerformanceMeasurement.Header();
        foreach (int columns in new[] { 80, 240 })
        foreach (bool graphemes in new[] { false, true })
        {
            TerminalRow source = new(columns);
            for (int i = 0; i < columns; i++)
            {
                source[i].Codepoint = 'a' + i % 26;
                if (graphemes) source[i].Grapheme = ((char)('a' + i % 26)).ToString() + "\u0301";
            }
            string scenario = $"row-{columns}-{(graphemes ? "graphemes" : "ascii")}";
            long checksum = 0;
            PerformanceMeasurement.Run(scenario + "-compress", "managed", 2000,
                () =>
                {
                    TerminalRow row = source.CreateStateCopy();
                    if (!row.TryCompressCells()) throw new InvalidOperationException("Fixture must be compressible.");
                    checksum += (long)row.CompressedCellBytes;
                }, () => checksum);
            TerminalRow compact = source.CreateStateCopy();
            if (!compact.TryCompressCells()) throw new InvalidOperationException("Fixture must be compressible.");
            PerformanceMeasurement.Run(scenario + "-restore", "managed", 2000,
                () => checksum += compact.CreateStateCopy().ReadOnlyCells[^1].Codepoint, () => checksum);
        }

        byte[] snapshot = CreateSnapshot();
        foreach (bool compress in new[] { false, true })
        {
            long rows = 0;
            PerformanceMeasurement.Run(compress ? "snapshot-compressed" : "snapshot-plain", "managed", 5,
                () =>
                {
                    using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(snapshot, new() { CompressHistory = compress });
                    rows += restored.Screen.TotalRows;
                }, () => rows);
            using (ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(snapshot, new() { CompressHistory = compress }))
            {
                TerminalScreenMemoryUsage memory = restored.Processor.GetMemoryUsage().Primary;
                Console.WriteLine($"# managed compress={compress}: logical={memory.LogicalBytes} resident={memory.ResidentBytes} compressed={memory.CompressedBytes}");
            }
            PerformanceMeasurement.Run(compress ? "snapshot-compressed" : "snapshot-plain", "native-core", 5,
                () =>
                {
                    using GhosttySnapshotDecoder decoder = new(snapshot);
                    decoder.SetCompressHistory(compress);
                    using GhosttyTerminal restored = decoder.Decode();
                    rows += (long)restored.GetTotalRows();
                }, () => rows);
            using GhosttySnapshotDecoder diagnosticDecoder = new(snapshot);
            diagnosticDecoder.SetCompressHistory(compress);
            using GhosttyTerminal diagnostic = diagnosticDecoder.Decode();
            var native = diagnostic.GetMemoryUsage();
            Console.WriteLine($"# native compress={compress}: supported={native.CompressionSupported} logical={native.PrimaryVirtualBytes} resident={native.PrimaryResidentBytes} compressed={native.PrimaryCompressedBytes}");
        }
    }

    private static byte[] CreateSnapshot()
    {
        using BasicVtProcessor source = new(new TerminalScreen(80, 24, 2000));
        for (int i = 0; i < 1200; i++)
            source.Process(Encoding.UTF8.GetBytes($"\u001b]8;id=item;https://example.test/{i % 7}\a\u001b[1;38;5;42mBuild {i:D5}: 界e\u0301😀 ready\u001b]8;;\a\r\n"));
        return source.GetBinarySnapshot();
    }
}
