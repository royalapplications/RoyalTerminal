// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Benchmarks - Render hot-path benchmark harness.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Unicode;
using SkiaSharp;

BenchmarkOptions options = BenchmarkOptions.Parse(args);

BenchmarkScenario[] defaultRenderScenarios =
[
    new("full-80x24", Columns: 80, Rows: 24, Iterations: 1_200, DirtyRowsPerFrame: 24, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz),
    new("full-160x48", Columns: 160, Rows: 48, Iterations: 700, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz),
    new("full-240x80", Columns: 240, Rows: 80, Iterations: 300, DirtyRowsPerFrame: 80, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz),
    new("dirty-160x48-r8", Columns: 160, Rows: 48, Iterations: 1_800, DirtyRowsPerFrame: 8, FullRedraw: false, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz),
];
BenchmarkScenario[] complexRenderScenarios =
[
    new("complex-ligatures-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz, Workload: RenderWorkload.LigatureAscii, EnableLigatures: true),
    new("complex-combining-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz, Workload: RenderWorkload.CombiningMarks),
    new("complex-cjk-wide-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz, Workload: RenderWorkload.CjkWide),
    new("complex-emoji-symbols-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz, Workload: RenderWorkload.EmojiSymbols),
    new("complex-rtl-arabic-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.HarfBuzz, Workload: RenderWorkload.RtlArabic, TextDirectionMode: TextDirectionMode.RightToLeft),
];
BenchmarkScenario[] pretextRenderScenarios =
[
    new("pretext-full-160x48", Columns: 160, Rows: 48, Iterations: 700, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.Pretext),
    new("pretext-complex-ligatures-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.Pretext, Workload: RenderWorkload.LigatureAscii, EnableLigatures: true),
    new("pretext-complex-combining-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.Pretext, Workload: RenderWorkload.CombiningMarks),
    new("pretext-complex-cjk-wide-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.Pretext, Workload: RenderWorkload.CjkWide),
    new("pretext-complex-emoji-symbols-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.Pretext, Workload: RenderWorkload.EmojiSymbols),
    new("pretext-complex-rtl-arabic-160x48", Columns: 160, Rows: 48, Iterations: 500, DirtyRowsPerFrame: 48, FullRedraw: true, TextRenderPipeline: TerminalTextRenderPipeline.Pretext, Workload: RenderWorkload.RtlArabic, TextDirectionMode: TextDirectionMode.RightToLeft),
];
BenchmarkScenario[] renderScenarios = IsPretextBenchmarkAvailable()
    ? [.. defaultRenderScenarios, .. complexRenderScenarios, .. pretextRenderScenarios]
    : [.. defaultRenderScenarios, .. complexRenderScenarios];

TextHighlightBenchmarkScenario[] textHighlightScenarios =
[
    new(
        "highlight-realtime-no-match-literal-rules",
        Columns: 160,
        Rows: 48,
        Iterations: 1_000,
        DirtyRowsPerFrame: 48,
        FullRedraw: true,
        TextHighlightingMode: TerminalTextHighlightingMode.Realtime,
        RuleSet: TextHighlightRuleSet.LiteralTokens,
        Workload: TextHighlightWorkload.NoMatches,
        MutateRows: true),
    new(
        "highlight-realtime-sparse-log-rules",
        Columns: 160,
        Rows: 48,
        Iterations: 900,
        DirtyRowsPerFrame: 48,
        FullRedraw: true,
        TextHighlightingMode: TerminalTextHighlightingMode.Realtime,
        RuleSet: TextHighlightRuleSet.LogPatterns,
        Workload: TextHighlightWorkload.SparseMatches,
        MutateRows: true),
    new(
        "highlight-realtime-no-match-many-literals",
        Columns: 160,
        Rows: 48,
        Iterations: 700,
        DirtyRowsPerFrame: 48,
        FullRedraw: true,
        TextHighlightingMode: TerminalTextHighlightingMode.Realtime,
        RuleSet: TextHighlightRuleSet.ManyLiteralTokens,
        Workload: TextHighlightWorkload.NoMatches,
        MutateRows: true),
    new(
        "highlight-realtime-dense-log-rules",
        Columns: 160,
        Rows: 48,
        Iterations: 700,
        DirtyRowsPerFrame: 48,
        FullRedraw: true,
        TextHighlightingMode: TerminalTextHighlightingMode.Realtime,
        RuleSet: TextHighlightRuleSet.LogPatterns,
        Workload: TextHighlightWorkload.DenseMatches,
        MutateRows: true),
    new(
        "highlight-static-warm-cache",
        Columns: 160,
        Rows: 48,
        Iterations: 1_200,
        DirtyRowsPerFrame: 48,
        FullRedraw: true,
        TextHighlightingMode: TerminalTextHighlightingMode.Static,
        RuleSet: TextHighlightRuleSet.LogPatterns,
        Workload: TextHighlightWorkload.SparseMatches,
        MutateRows: false),
    new(
        "highlight-static-dirty-160x48-r8",
        Columns: 160,
        Rows: 48,
        Iterations: 1_800,
        DirtyRowsPerFrame: 8,
        FullRedraw: false,
        TextHighlightingMode: TerminalTextHighlightingMode.Static,
        RuleSet: TextHighlightRuleSet.LogPatterns,
        Workload: TextHighlightWorkload.SparseMatches,
        MutateRows: true),
];

BenchmarkResult[] renderResults = [];
TextHighlightBenchmarkResult[] textHighlightResults = [];
if (options.IncludeRender)
{
    renderResults = new BenchmarkResult[renderScenarios.Length];
    for (int i = 0; i < renderScenarios.Length; i++)
    {
        renderResults[i] = RenderHotPathBenchmark.Run(renderScenarios[i]);
    }

    textHighlightResults = new TextHighlightBenchmarkResult[textHighlightScenarios.Length];
    for (int i = 0; i < textHighlightScenarios.Length; i++)
    {
        textHighlightResults[i] = TextHighlightBenchmark.Run(textHighlightScenarios[i]);
    }
}

PtyIoFixtureSet? ptyIoFixtures = null;
PtyIoBenchmarkResult[] ptyIoResults = [];
VtParseBenchmarkResult[] vtParseResults = [];
if (options.IncludePtyIo || options.IncludeVtParse || options.GeneratePtyIoFixturesOnly)
{
    ptyIoFixtures = PtyIoFixtureGenerator.EnsureFixtures(options.FixtureDirectory, options.FixtureSizeMiB);
    if (!options.GeneratePtyIoFixturesOnly)
    {
        if (options.IncludePtyIo)
        {
            ptyIoResults = PtyIoBenchmark.Run(ptyIoFixtures.Value, options.PtyIoOptions);
        }

        if (options.IncludeVtParse)
        {
            vtParseResults = VtParseBenchmark.Run(ptyIoFixtures.Value, options.PtyIoOptions.Repeats);
        }
    }
}

string report = BenchmarkReportWriter.CreateReport(renderResults, textHighlightResults, ptyIoResults, vtParseResults, ptyIoFixtures);
Console.WriteLine(report);

if (!string.IsNullOrWhiteSpace(options.OutputPath))
{
    string outputPath = Path.GetFullPath(options.OutputPath);
    string? directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllText(outputPath, report, Encoding.UTF8);
    Console.WriteLine($"Saved report: {outputPath}");
}

static bool IsPretextBenchmarkAvailable()
{
    using SkiaTerminalRenderer renderer = new("Consolas", 14f);
    return renderer.IsPretextTextRenderPipelineAvailable;
}

internal readonly record struct BenchmarkScenario(
    string Name,
    int Columns,
    int Rows,
    int Iterations,
    int DirtyRowsPerFrame,
    bool FullRedraw,
    TerminalTextRenderPipeline TextRenderPipeline,
    RenderWorkload Workload = RenderWorkload.SimpleAscii,
    bool EnableLigatures = false,
    TextDirectionMode TextDirectionMode = TextDirectionMode.Auto);

internal enum RenderWorkload
{
    SimpleAscii,
    LigatureAscii,
    CombiningMarks,
    CjkWide,
    EmojiSymbols,
    RtlArabic,
}

internal readonly record struct BenchmarkResult(
    string Name,
    int Columns,
    int Rows,
    int Iterations,
    int DirtyRowsPerFrame,
    bool FullRedraw,
    TerminalTextRenderPipeline TextRenderPipeline,
    double RowsPerSecond,
    double AllocBytesPerFrame,
    double RenderAllocBytesPerFrame,
    double MeanFrameMs,
    double P95FrameMs,
    double TotalTimeMs);

internal enum TextHighlightRuleSet
{
    LiteralTokens,
    ManyLiteralTokens,
    LogPatterns,
}

internal enum TextHighlightWorkload
{
    NoMatches,
    SparseMatches,
    DenseMatches,
}

internal readonly record struct TextHighlightBenchmarkScenario(
    string Name,
    int Columns,
    int Rows,
    int Iterations,
    int DirtyRowsPerFrame,
    bool FullRedraw,
    TerminalTextHighlightingMode TextHighlightingMode,
    TextHighlightRuleSet RuleSet,
    TextHighlightWorkload Workload,
    bool MutateRows);

internal readonly record struct TextHighlightBenchmarkResult(
    string Name,
    int Columns,
    int Rows,
    int Iterations,
    int DirtyRowsPerFrame,
    bool FullRedraw,
    TerminalTextHighlightingMode TextHighlightingMode,
    TextHighlightRuleSet RuleSet,
    TextHighlightWorkload Workload,
    bool MutateRows,
    double RowsPerSecond,
    double AllocBytesPerFrame,
    double MeanFrameMs,
    double P95FrameMs,
    double TotalTimeMs);

internal readonly record struct BenchmarkOptions(
    string? OutputPath,
    bool IncludeRender,
    bool IncludePtyIo,
    bool IncludeVtParse,
    bool GeneratePtyIoFixturesOnly,
    string? FixtureDirectory,
    int FixtureSizeMiB,
    PtyIoBenchmarkOptions PtyIoOptions)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        string? outputPath = null;
        bool includeRender = true;
        bool includePtyIo = false;
        bool includeVtParse = false;
        bool generatePtyIoFixturesOnly = false;
        string? fixtureDirectory = null;
        int fixtureSizeMiB = 150;
        PtyIoBenchmarkMode ptyIoMode = PtyIoBenchmarkMode.Raw;
        int ptyIoRepeats = 1;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    outputPath = args[i + 1];
                    i++;
                }

                continue;
            }

            if (string.Equals(arg, "--skip-render", StringComparison.OrdinalIgnoreCase))
            {
                includeRender = false;
                continue;
            }

            if (string.Equals(arg, "--io", StringComparison.OrdinalIgnoreCase))
            {
                includePtyIo = true;
                continue;
            }

            if (string.Equals(arg, "--vt-parse", StringComparison.OrdinalIgnoreCase))
            {
                includeVtParse = true;
                continue;
            }

            if (string.Equals(arg, "--generate-io-fixtures", StringComparison.OrdinalIgnoreCase))
            {
                includePtyIo = true;
                generatePtyIoFixturesOnly = true;
                continue;
            }

            if (string.Equals(arg, "--fixtures", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    fixtureDirectory = args[i + 1];
                    i++;
                }

                continue;
            }

            if (string.Equals(arg, "--fixture-size-mb", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length &&
                    int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sizeMiB))
                {
                    fixtureSizeMiB = Math.Clamp(sizeMiB, 1, 4096);
                    i++;
                }

                continue;
            }

            if (string.Equals(arg, "--io-mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    ptyIoMode = ParsePtyIoMode(args[i + 1]);
                    i++;
                }

                continue;
            }

            if (string.Equals(arg, "--io-repeats", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length &&
                    int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeats))
                {
                    ptyIoRepeats = Math.Clamp(repeats, 1, 25);
                    i++;
                }
            }
        }

        return new BenchmarkOptions(
            outputPath,
            includeRender,
            includePtyIo,
            includeVtParse,
            generatePtyIoFixturesOnly,
            fixtureDirectory,
            fixtureSizeMiB,
            new PtyIoBenchmarkOptions(ptyIoMode, ptyIoRepeats));
    }

    private static PtyIoBenchmarkMode ParsePtyIoMode(string value)
    {
        if (string.Equals(value, "managed-vt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "vt", StringComparison.OrdinalIgnoreCase))
        {
            return PtyIoBenchmarkMode.ManagedVt;
        }

        if (string.Equals(value, "both", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return PtyIoBenchmarkMode.Both;
        }

        return PtyIoBenchmarkMode.Raw;
    }
}

internal readonly record struct PtyIoBenchmarkOptions(PtyIoBenchmarkMode Mode, int Repeats);

internal enum PtyIoBenchmarkMode
{
    Raw,
    ManagedVt,
    Both,
}

internal enum PtyIoProcessingMode
{
    Raw,
    ManagedVt,
}

internal readonly record struct PtyIoFixtureSet(
    string Directory,
    int SizeMiB,
    string AsciiPath,
    string UnicodePath,
    string CsiPath);

internal readonly record struct PtyIoBenchmarkResult(
    string Name,
    string Mode,
    string FilePath,
    long Bytes,
    int Repeats,
    int Batches,
    int LargestBatchBytes,
    double TotalTimeMs,
    double MiBPerSecond,
    double ChildRealTimeMs,
    double ChildMiBPerSecond,
    double CallbackTimeMs,
    long CallbackAllocatedBytes,
    bool TimedOut,
    string? SkippedReason);

internal readonly record struct VtPayloadProfile(
    long Bytes,
    long PrintableAsciiBytes,
    long ControlOrEscapeBytes,
    long NonAsciiBytes);

internal readonly record struct VtParseBenchmarkResult(
    string Name,
    string FilePath,
    long Bytes,
    int Repeats,
    double PrintableAsciiPercent,
    double ControlOrEscapePercent,
    double NonAsciiPercent,
    double TotalTimeMs,
    double MiBPerSecond,
    long AllocatedBytes);

internal static class PtyIoFixtureGenerator
{
    private const int BufferSize = 128 * 1024;

    public static PtyIoFixtureSet EnsureFixtures(string? directory, int sizeMiB)
    {
        string fixtureDirectory = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(Path.GetTempPath(), "royalterminal-io-fixtures")
            : Path.GetFullPath(directory);
        Directory.CreateDirectory(fixtureDirectory);

        long targetBytes = sizeMiB * 1024L * 1024L;
        string asciiPath = Path.Combine(fixtureDirectory, $"{sizeMiB}MB_ascii.txt");
        string unicodePath = Path.Combine(fixtureDirectory, $"{sizeMiB}MB_unicode.txt");
        string csiPath = Path.Combine(fixtureDirectory, $"{sizeMiB}MB_csi.txt");

        EnsureFixture(
            asciiPath,
            targetBytes,
            "RoyalTerminal ASCII throughput line 0123456789 abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ\r\n"u8);
        EnsureFixture(
            unicodePath,
            targetBytes,
            "RoyalTerminal Unicode throughput 日本語ログ Ελληνικά кириллица العربية עברית हिंदी emoji ✅🚀 row\r\n"u8);
        EnsureFixture(
            csiPath,
            targetBytes,
            "\x1b[31mRED\x1b[0m \x1b[32mGREEN\x1b[0m \x1b[1;34mBOLD_BLUE\x1b[0m \x1b[2K\x1b[12;24HCSI throughput row\r\n"u8);

        return new PtyIoFixtureSet(fixtureDirectory, sizeMiB, asciiPath, unicodePath, csiPath);
    }

    private static void EnsureFixture(string path, long targetBytes, ReadOnlySpan<byte> pattern)
    {
        FileInfo fileInfo = new(path);
        if (fileInfo.Exists && fileInfo.Length == targetBytes)
        {
            return;
        }

        byte[] buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
        int offset = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = pattern[offset];
            offset++;
            if (offset == pattern.Length)
            {
                offset = 0;
            }
        }

        using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);

        long remaining = targetBytes;
        while (remaining > 0)
        {
            int writeLength = (int)Math.Min(buffer.Length, remaining);
            stream.Write(buffer.AsSpan(0, writeLength));
            remaining -= writeLength;
        }
    }
}

internal static class PtyIoBenchmark
{
    private static readonly TimeSpan ScenarioTimeout = TimeSpan.FromSeconds(120);
    private static readonly Regex RealTimeRegex = new(
        @"real\s+(?<seconds>[0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PtyIoBenchmarkResult[] Run(PtyIoFixtureSet fixtures, PtyIoBenchmarkOptions options)
    {
        PtyIoBenchmarkScenario[] fixtureScenarios =
        [
            new("pty-cat-ascii", fixtures.AsciiPath),
            new("pty-cat-unicode", fixtures.UnicodePath),
            new("pty-cat-csi", fixtures.CsiPath),
        ];
        PtyIoProcessingMode[] modes = options.Mode switch
        {
            PtyIoBenchmarkMode.ManagedVt => [PtyIoProcessingMode.ManagedVt],
            PtyIoBenchmarkMode.Both => [PtyIoProcessingMode.Raw, PtyIoProcessingMode.ManagedVt],
            _ => [PtyIoProcessingMode.Raw],
        };

        PtyIoBenchmarkResult[] results = new PtyIoBenchmarkResult[fixtureScenarios.Length * modes.Length];
        int index = 0;
        for (int i = 0; i < fixtureScenarios.Length; i++)
        {
            for (int j = 0; j < modes.Length; j++)
            {
                results[index] = RunScenario(fixtureScenarios[i], modes[j], options.Repeats);
                index++;
            }
        }

        return results;
    }

    private static PtyIoBenchmarkResult RunScenario(PtyIoBenchmarkScenario scenario, PtyIoProcessingMode mode, int repeats)
    {
        FileInfo fileInfo = new(scenario.FilePath);
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return new PtyIoBenchmarkResult(
                scenario.Name,
                FormatMode(mode),
                scenario.FilePath,
                fileInfo.Length,
                repeats,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                TimedOut: false,
                SkippedReason: "PTY IO throughput benchmark currently targets the Unix PTY read pipeline.");
        }

        PtyIoBenchmarkResult[] results = new PtyIoBenchmarkResult[repeats];
        for (int i = 0; i < repeats; i++)
        {
            results[i] = RunScenarioOnce(scenario, mode, repeats, fileInfo);
        }

        return results
            .OrderBy(static result => result.MiBPerSecond)
            .ElementAt(results.Length / 2);
    }

    private static PtyIoBenchmarkResult RunScenarioOnce(
        PtyIoBenchmarkScenario scenario,
        PtyIoProcessingMode mode,
        int repeats,
        FileInfo fileInfo)
    {
        using IPty pty = new DefaultPtyFactory().Create();
        using IVtProcessor? processor = CreateProcessor(mode);
        using ManualResetEventSlim completed = new(false);
        string markerText = "__ROYALTERMINAL_IO_BENCH_DONE_" + Guid.NewGuid().ToString("N") + "__";
        byte[] marker = Encoding.ASCII.GetBytes(markerText);
        byte[] markerTail = new byte[Math.Max(marker.Length - 1, 0)];
        byte[] parseTail = new byte[16 * 1024];
        int parseTailLength = 0;
        object sync = new();
        long startTimestamp = 0;
        long endTimestamp = 0;
        long callbackTicks = 0;
        long callbackAllocatedBytes = 0;
        int batches = 0;
        int largestBatch = 0;

        pty.DataReceived += (data, length) =>
        {
            if (length <= 0 || Volatile.Read(ref startTimestamp) == 0)
            {
                return;
            }

            long callbackStart = Stopwatch.GetTimestamp();
            long callbackAllocationStart = GC.GetAllocatedBytesForCurrentThread();
            Interlocked.Increment(ref batches);
            UpdateLargestBatch(ref largestBatch, length);
            processor?.Process(data.AsSpan(0, length));

            ReadOnlySpan<byte> received = data.AsSpan(0, length);
            lock (sync)
            {
                bool hasMarker = ContainsMarker(received, marker, markerTail);
                UpdateTail(markerTail, received);
                AppendTail(parseTail, ref parseTailLength, received);
                if (hasMarker)
                {
                    Interlocked.CompareExchange(ref endTimestamp, Stopwatch.GetTimestamp(), 0);
                    completed.Set();
                }
            }

            long callbackAllocationEnd = GC.GetAllocatedBytesForCurrentThread();
            long callbackEnd = Stopwatch.GetTimestamp();
            Interlocked.Add(ref callbackTicks, callbackEnd - callbackStart);
            Interlocked.Add(ref callbackAllocatedBytes, Math.Max(0, callbackAllocationEnd - callbackAllocationStart));
        };

        pty.Start(shell: "/bin/sh", columns: 120, rows: 40, workingDirectory: Environment.CurrentDirectory);
        pty.Write("stty -echo\n");
        Thread.Sleep(200);
        long start = Stopwatch.GetTimestamp();
        Volatile.Write(ref startTimestamp, start);
        pty.Write("/usr/bin/time -p cat " + ShellQuote(scenario.FilePath) + "\n");
        pty.Write("printf '\\n" + markerText + "\\n'\n");

        bool finished = completed.Wait(ScenarioTimeout);
        long end = Volatile.Read(ref endTimestamp);
        if (end == 0)
        {
            end = Stopwatch.GetTimestamp();
        }

        pty.Stop();

        double totalMs = (end - start) * 1000.0 / Stopwatch.Frequency;
        double seconds = Math.Max(totalMs / 1000.0, 1e-9);
        double mibPerSecond = (fileInfo.Length / (1024.0 * 1024.0)) / seconds;
        double childRealTimeMs = ExtractChildRealMilliseconds(parseTail.AsSpan(0, parseTailLength));
        double childMiBPerSecond = childRealTimeMs > 0
            ? (fileInfo.Length / (1024.0 * 1024.0)) / (childRealTimeMs / 1000.0)
            : double.NaN;
        double callbackTimeMs = Volatile.Read(ref callbackTicks) * 1000.0 / Stopwatch.Frequency;

        return new PtyIoBenchmarkResult(
            scenario.Name,
            FormatMode(mode),
            scenario.FilePath,
            fileInfo.Length,
            repeats,
            Volatile.Read(ref batches),
            Volatile.Read(ref largestBatch),
            totalMs,
            mibPerSecond,
            childRealTimeMs,
            childMiBPerSecond,
            callbackTimeMs,
            Volatile.Read(ref callbackAllocatedBytes),
            TimedOut: !finished,
            SkippedReason: null);
    }

    private static IVtProcessor? CreateProcessor(PtyIoProcessingMode mode)
    {
        if (mode != PtyIoProcessingMode.ManagedVt)
        {
            return null;
        }

        return new BasicVtProcessor(new TerminalScreen(120, 40));
    }

    private static bool ContainsMarker(ReadOnlySpan<byte> received, ReadOnlySpan<byte> marker, byte[] tail)
    {
        if (received.IndexOf(marker) >= 0)
        {
            return true;
        }

        if (tail.Length == 0)
        {
            return false;
        }

        int maxPrefix = Math.Min(tail.Length, marker.Length - 1);
        for (int prefixLength = 1; prefixLength <= maxPrefix; prefixLength++)
        {
            int tailOffset = tail.Length - prefixLength;
            ReadOnlySpan<byte> tailPrefix = tail.AsSpan(tailOffset, prefixLength);
            ReadOnlySpan<byte> markerPrefix = marker[..prefixLength];
            if (!tailPrefix.SequenceEqual(markerPrefix))
            {
                continue;
            }

            int suffixLength = marker.Length - prefixLength;
            if (received.Length >= suffixLength &&
                received[..suffixLength].SequenceEqual(marker[prefixLength..]))
            {
                return true;
            }
        }

        return false;
    }

    private static void UpdateTail(byte[] tail, ReadOnlySpan<byte> received)
    {
        if (tail.Length == 0 || received.IsEmpty)
        {
            return;
        }

        if (received.Length >= tail.Length)
        {
            received[^tail.Length..].CopyTo(tail);
            return;
        }

        tail.AsSpan(received.Length).CopyTo(tail);
        received.CopyTo(tail.AsSpan(tail.Length - received.Length));
    }

    private static void AppendTail(byte[] tail, ref int length, ReadOnlySpan<byte> received)
    {
        if (tail.Length == 0 || received.IsEmpty)
        {
            return;
        }

        if (received.Length >= tail.Length)
        {
            received[^tail.Length..].CopyTo(tail);
            length = tail.Length;
            return;
        }

        int overflow = Math.Max(0, length + received.Length - tail.Length);
        if (overflow > 0)
        {
            tail.AsSpan(overflow, length - overflow).CopyTo(tail);
            length -= overflow;
        }

        received.CopyTo(tail.AsSpan(length));
        length += received.Length;
    }

    private static double ExtractChildRealMilliseconds(ReadOnlySpan<byte> tail)
    {
        string text = Encoding.UTF8.GetString(tail);
        MatchCollection matches = RealTimeRegex.Matches(text);
        if (matches.Count == 0)
        {
            return double.NaN;
        }

        Match match = matches[^1];
        string value = match.Groups["seconds"].Value;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            ? seconds * 1000.0
            : double.NaN;
    }

    private static string FormatMode(PtyIoProcessingMode mode)
    {
        return mode == PtyIoProcessingMode.ManagedVt ? "managed-vt" : "raw";
    }

    private static void UpdateLargestBatch(ref int largestBatch, int candidate)
    {
        while (true)
        {
            int current = Volatile.Read(ref largestBatch);
            if (candidate <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref largestBatch, candidate, current) == current)
            {
                return;
            }
        }
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}

internal readonly record struct PtyIoBenchmarkScenario(string Name, string FilePath);

internal static class VtParseBenchmark
{
    public static VtParseBenchmarkResult[] Run(PtyIoFixtureSet fixtures, int repeats)
    {
        PtyIoBenchmarkScenario[] scenarios =
        [
            new("vt-parse-ascii", fixtures.AsciiPath),
            new("vt-parse-unicode", fixtures.UnicodePath),
            new("vt-parse-csi", fixtures.CsiPath),
        ];

        VtParseBenchmarkResult[] results = new VtParseBenchmarkResult[scenarios.Length];
        for (int i = 0; i < scenarios.Length; i++)
        {
            results[i] = RunScenario(scenarios[i], repeats);
        }

        return results;
    }

    private static VtParseBenchmarkResult RunScenario(PtyIoBenchmarkScenario scenario, int repeats)
    {
        byte[] payload = File.ReadAllBytes(scenario.FilePath);
        VtPayloadProfile profile = AnalyzePayload(payload);
        VtParseBenchmarkResult[] results = new VtParseBenchmarkResult[repeats];
        for (int i = 0; i < repeats; i++)
        {
            results[i] = RunScenarioOnce(scenario, payload, profile, repeats);
        }

        return results
            .OrderBy(static result => result.MiBPerSecond)
            .ElementAt(results.Length / 2);
    }

    private static VtParseBenchmarkResult RunScenarioOnce(
        PtyIoBenchmarkScenario scenario,
        byte[] payload,
        VtPayloadProfile profile,
        int repeats)
    {
        TerminalScreen screen = new(120, 40);
        using BasicVtProcessor processor = new(screen);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        processor.Process(payload);
        long end = Stopwatch.GetTimestamp();
        long allocatedAfter = GC.GetTotalAllocatedBytes(precise: false);

        double totalMs = (end - start) * 1000.0 / Stopwatch.Frequency;
        double seconds = Math.Max(totalMs / 1000.0, 1e-9);
        double mibPerSecond = (payload.Length / (1024.0 * 1024.0)) / seconds;

        return new VtParseBenchmarkResult(
            scenario.Name,
            scenario.FilePath,
            payload.Length,
            repeats,
            Percent(profile.PrintableAsciiBytes, profile.Bytes),
            Percent(profile.ControlOrEscapeBytes, profile.Bytes),
            Percent(profile.NonAsciiBytes, profile.Bytes),
            totalMs,
            mibPerSecond,
            Math.Max(0, allocatedAfter - allocatedBefore));
    }

    private static VtPayloadProfile AnalyzePayload(ReadOnlySpan<byte> payload)
    {
        long printableAscii = 0;
        long controlOrEscape = 0;
        long nonAscii = 0;

        for (int i = 0; i < payload.Length; i++)
        {
            byte value = payload[i];
            if (value >= 0x20 && value < 0x7F)
            {
                printableAscii++;
            }
            else if (value < 0x20 || value == 0x7F)
            {
                controlOrEscape++;
            }
            else
            {
                nonAscii++;
            }
        }

        return new VtPayloadProfile(payload.Length, printableAscii, controlOrEscape, nonAscii);
    }

    private static double Percent(long value, long total)
    {
        return total > 0
            ? value * 100.0 / total
            : 0;
    }
}

internal static class RenderHotPathBenchmark
{
    private const float FontSize = 14f;

    public static BenchmarkResult Run(BenchmarkScenario scenario)
    {
        using SkiaTerminalRenderer renderer = new("Consolas", FontSize);
        renderer.TextRenderPipeline = scenario.TextRenderPipeline;
        renderer.EnableLigatures = scenario.EnableLigatures;
        renderer.TextDirectionMode = scenario.TextDirectionMode;

        TerminalCell[][]? baseRows = scenario.Workload == RenderWorkload.SimpleAscii
            ? null
            : CreateRenderRowTemplates(scenario.Workload, scenario.Columns, scenario.Rows, variant: 0);
        TerminalCell[][]? mutatedRows = scenario.Workload == RenderWorkload.SimpleAscii
            ? null
            : CreateRenderRowTemplates(scenario.Workload, scenario.Columns, scenario.Rows, variant: 1);

        TerminalScreen screen = new(scenario.Columns, scenario.Rows);
        InitializeScreen(screen, scenario, baseRows);

        int width = Math.Max(1, (int)Math.Ceiling(renderer.CellWidth * scenario.Columns));
        int height = Math.Max(1, (int)Math.Ceiling(renderer.CellHeight * scenario.Rows));

        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
        SKCanvas canvas = surface.Canvas;

        Warmup(renderer, screen, canvas, scenario, baseRows, mutatedRows);

        long[] frameTicks = new long[scenario.Iterations];

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long totalStart = Stopwatch.GetTimestamp();
        long renderAllocatedBytes = 0;

        int dirtyRows = Math.Clamp(scenario.DirtyRowsPerFrame, 1, scenario.Rows);

        for (int frame = 0; frame < scenario.Iterations; frame++)
        {
            MutateFrame(screen, frame, dirtyRows, scenario, baseRows, mutatedRows);

            long renderAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long frameStart = Stopwatch.GetTimestamp();
            if (scenario.FullRedraw)
            {
                renderer.RenderFull(canvas, screen);
            }
            else
            {
                renderer.Render(canvas, screen, forceFullRedraw: false);
            }

            long frameEnd = Stopwatch.GetTimestamp();
            long renderAllocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            frameTicks[frame] = frameEnd - frameStart;
            renderAllocatedBytes += Math.Max(0, renderAllocatedAfter - renderAllocatedBefore);
        }

        long totalEnd = Stopwatch.GetTimestamp();
        long allocatedAfter = GC.GetTotalAllocatedBytes(true);

        return CreateResult(
            scenario,
            dirtyRows,
            frameTicks,
            totalStart,
            totalEnd,
            allocatedBefore,
            allocatedAfter,
            renderAllocatedBytes);
    }

    private static BenchmarkResult CreateResult(
        BenchmarkScenario scenario,
        int dirtyRows,
        long[] frameTicks,
        long totalStart,
        long totalEnd,
        long allocatedBefore,
        long allocatedAfter,
        long renderAllocatedBytes)
    {
        double totalSeconds = (totalEnd - totalStart) / (double)Stopwatch.Frequency;
        double totalMs = totalSeconds * 1000.0;

        int rowsPerFrame = scenario.FullRedraw ? scenario.Rows : dirtyRows;
        double rowsPerSecond = (rowsPerFrame * (double)scenario.Iterations) / Math.Max(totalSeconds, 1e-9);

        long allocatedBytes = Math.Max(0, allocatedAfter - allocatedBefore);
        double allocPerFrame = allocatedBytes / (double)scenario.Iterations;
        double renderAllocPerFrame = renderAllocatedBytes / (double)scenario.Iterations;

        double meanFrameMs = totalMs / scenario.Iterations;
        double p95FrameMs = BenchmarkMath.PercentileMs(frameTicks, 0.95);

        return new BenchmarkResult(
            scenario.Name,
            scenario.Columns,
            scenario.Rows,
            scenario.Iterations,
            dirtyRows,
            scenario.FullRedraw,
            scenario.TextRenderPipeline,
            rowsPerSecond,
            allocPerFrame,
            renderAllocPerFrame,
            meanFrameMs,
            p95FrameMs,
            totalMs);
    }

    private static void Warmup(
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        SKCanvas canvas,
        BenchmarkScenario scenario,
        TerminalCell[][]? baseRows,
        TerminalCell[][]? mutatedRows)
    {
        int dirtyRows = Math.Clamp(scenario.DirtyRowsPerFrame, 1, scenario.Rows);
        int warmupFrames = Math.Min(120, Math.Max(30, scenario.Iterations / 10));

        for (int i = 0; i < warmupFrames; i++)
        {
            MutateFrame(screen, i, dirtyRows, scenario, baseRows, mutatedRows);
            if (scenario.FullRedraw)
            {
                renderer.RenderFull(canvas, screen);
            }
            else
            {
                renderer.Render(canvas, screen, forceFullRedraw: false);
            }
        }
    }

    private static void InitializeScreen(
        TerminalScreen screen,
        BenchmarkScenario scenario,
        TerminalCell[][]? baseRows)
    {
        if (scenario.Workload != RenderWorkload.SimpleAscii && baseRows is not null)
        {
            InitializeComplexScreen(screen, baseRows);
            return;
        }

        for (int rowIndex = 0; rowIndex < screen.ViewportRows; rowIndex++)
        {
            TerminalRow row = screen.GetViewportRow(rowIndex);
            Span<TerminalCell> cells = row.Cells;

            for (int col = 0; col < cells.Length; col++)
            {
                ref TerminalCell cell = ref cells[col];
                int codepoint = 'a' + ((rowIndex + col) % 26);

                cell.Codepoint = codepoint;
                cell.Foreground = (col & 1) == 0 ? 0xFFD4D4D4 : 0xFFB0B0B0;
                cell.Background = 0xFF1E1E1E;
                cell.Attributes = (col % 11 == 0)
                    ? CellAttributes.Bold
                    : (col % 13 == 0)
                        ? CellAttributes.Italic
                        : CellAttributes.None;
                cell.Width = 1;
            }

            row.IsDirty = true;
        }
    }

    private static void InitializeComplexScreen(TerminalScreen screen, TerminalCell[][] baseRows)
    {
        for (int rowIndex = 0; rowIndex < screen.ViewportRows; rowIndex++)
        {
            TerminalRow row = screen.GetViewportRow(rowIndex);
            CopyTemplateRow(baseRows[rowIndex], row);
            row.IsDirty = true;
        }
    }

    private static void MutateFrame(
        TerminalScreen screen,
        int frameIndex,
        int dirtyRows,
        BenchmarkScenario scenario,
        TerminalCell[][]? baseRows,
        TerminalCell[][]? mutatedRows)
    {
        if (scenario.Workload != RenderWorkload.SimpleAscii &&
            baseRows is not null &&
            mutatedRows is not null)
        {
            MutateComplexFrame(screen, frameIndex, dirtyRows, scenario, baseRows, mutatedRows);
            return;
        }

        int rows = screen.ViewportRows;
        int columns = screen.Columns;

        if (!scenario.FullRedraw)
        {
            for (int rowIndex = 0; rowIndex < rows; rowIndex++)
            {
                screen.GetViewportRow(rowIndex).IsDirty = false;
            }
        }

        int rowsToMutate = scenario.FullRedraw ? rows : dirtyRows;
        for (int rowIndex = 0; rowIndex < rowsToMutate; rowIndex++)
        {
            TerminalRow row = screen.GetViewportRow(rowIndex);
            Span<TerminalCell> cells = row.Cells;

            int col = (frameIndex + rowIndex * 7) % columns;
            int codepoint = 'A' + ((frameIndex + rowIndex) % 26);

            ref TerminalCell cell = ref cells[col];
            cell.Codepoint = codepoint;
            cell.Foreground = ((frameIndex + rowIndex) & 1) == 0 ? 0xFFFFFFFF : 0xFF80FF80;
            cell.Background = 0xFF1E1E1E;
            cell.Attributes = (frameIndex & 3) == 0 ? CellAttributes.Bold : CellAttributes.None;
            cell.Width = 1;
            row.IsDirty = true;
        }
    }

    private static void MutateComplexFrame(
        TerminalScreen screen,
        int frameIndex,
        int dirtyRows,
        BenchmarkScenario scenario,
        TerminalCell[][] baseRows,
        TerminalCell[][] mutatedRows)
    {
        int rows = screen.ViewportRows;

        if (!scenario.FullRedraw)
        {
            for (int rowIndex = 0; rowIndex < rows; rowIndex++)
            {
                screen.GetViewportRow(rowIndex).IsDirty = false;
            }
        }

        TerminalCell[][] sourceRows = (frameIndex & 1) == 0 ? mutatedRows : baseRows;
        int rowsToMutate = scenario.FullRedraw ? rows : dirtyRows;
        for (int i = 0; i < rowsToMutate; i++)
        {
            int rowIndex = scenario.FullRedraw ? i : (frameIndex + i) % rows;
            TerminalRow row = screen.GetViewportRow(rowIndex);
            CopyTemplateRow(sourceRows[rowIndex], row);
            row.IsDirty = true;
        }
    }

    private static TerminalCell[][] CreateRenderRowTemplates(
        RenderWorkload workload,
        int columns,
        int rows,
        int variant)
    {
        TerminalCell[][] rowTemplates = new TerminalCell[rows][];
        for (int rowIndex = 0; rowIndex < rows; rowIndex++)
        {
            rowTemplates[rowIndex] = CreateRenderRowTemplate(workload, columns, rowIndex, variant);
        }

        return rowTemplates;
    }

    private static TerminalCell[] CreateRenderRowTemplate(
        RenderWorkload workload,
        int columns,
        int rowIndex,
        int variant)
    {
        TerminalCell[] cells = new TerminalCell[columns];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = TerminalCell.Empty();
        }

        string text = CreateRenderWorkloadText(workload, rowIndex, variant);
        GraphemeEnumerator enumerator = new(text.AsSpan());
        int col = 0;
        while (col < columns && enumerator.MoveNext(out Grapheme grapheme))
        {
            ReadOnlySpan<char> graphemeText = text.AsSpan(grapheme.Offset, grapheme.Length);
            int width = Math.Clamp(TerminalCellWidthCalculator.GetCellWidth(graphemeText), 0, 2);
            if (width == 0)
            {
                continue;
            }

            if (col + width > columns)
            {
                break;
            }

            ref TerminalCell cell = ref cells[col];
            cell = CreateBenchmarkCell(grapheme.FirstCodepoint, graphemeText, rowIndex, col);
            cell.Width = (byte)width;

            if (width == 2)
            {
                cells[col + 1] = TerminalCell.Empty(cell.Foreground, cell.Background);
                cells[col + 1].Width = 0;
            }

            col += width;
        }

        return cells;
    }

    private static TerminalCell CreateBenchmarkCell(
        Codepoint firstCodepoint,
        ReadOnlySpan<char> graphemeText,
        int rowIndex,
        int column)
    {
        bool isSingleCodepoint = graphemeText.Length == (firstCodepoint.Value > 0xFFFF ? 2 : 1);
        uint foreground = ((rowIndex + (column / 12)) & 1) == 0 ? 0xFFD4D4D4 : 0xFF9FE8C7;

        return new TerminalCell
        {
            Codepoint = isSingleCodepoint ? (int)firstCodepoint.Value : 0,
            Grapheme = isSingleCodepoint ? null : new string(graphemeText),
            Foreground = foreground,
            Background = 0xFF1E1E1E,
            Attributes = (column % 37 == 0)
                ? CellAttributes.Bold
                : (column % 41 == 0)
                    ? CellAttributes.Italic
                    : CellAttributes.None,
            UnderlineStyle = TerminalUnderlineStyle.None,
            UnderlineColor = 0,
            HasUnderlineColor = false,
            Decorations = CellDecorations.None,
            HasBackground = true,
            HyperlinkId = 0,
            Width = 1,
        };
    }

    private static string CreateRenderWorkloadText(RenderWorkload workload, int rowIndex, int variant)
    {
        string prefix = ((rowIndex + variant) & 1) == 0 ? "main" : "alt";
        return workload switch
        {
            RenderWorkload.LigatureAscii =>
                $"{prefix} office affine efficient filesystem buffer => === != <= >= -> <- www ffi ffl fi fl iteration {rowIndex:D2} ",
            RenderWorkload.CombiningMarks =>
                $"{prefix} cafe\u0301 resume\u0301 nai\u0308ve coo\u0308perate jalapen\u0303o a\u0301e\u0301i\u0301o\u0301u\u0301 row {rowIndex:D2} ",
            RenderWorkload.CjkWide =>
                $"{prefix} 日本語ログ 接続成功 測定値 正常 東京大阪 字符宽度 渲染缓存 行 {rowIndex:D2} ",
            RenderWorkload.EmojiSymbols =>
                $"{prefix} status ✅ build ⚠️ deploy 🚀 cpu ▰▱ ◼︎ ◻︎ ♥︎ ★ ☆ ✦ row {rowIndex:D2} ",
            RenderWorkload.RtlArabic =>
                $"{prefix} مرحبا بالعالم حالة الاتصال ناجحة قياس الذاكرة وسرعة الرسم صف {rowIndex:D2} ",
            _ =>
                $"{prefix} simple ascii render row {rowIndex:D2} ",
        };
    }

    private static void CopyTemplateRow(TerminalCell[] source, TerminalRow row)
    {
        source.AsSpan(0, row.Columns).CopyTo(row.Cells);
    }
}

internal static class TextHighlightBenchmark
{
    private const float FontSize = 14f;

    public static TextHighlightBenchmarkResult Run(TextHighlightBenchmarkScenario scenario)
    {
        using SkiaTerminalRenderer renderer = new("Consolas", FontSize);
        renderer.TextHighlightingMode = scenario.TextHighlightingMode;
        renderer.SetTextHighlightRules(CreateRules(scenario.RuleSet));

        TerminalScreen screen = new(scenario.Columns, scenario.Rows);
        string[] baseRows = CreateRows(scenario.Workload, scenario.Columns, scenario.Rows, variant: 0);
        string[] mutatedRows = CreateRows(scenario.Workload, scenario.Columns, scenario.Rows, variant: 1);
        InitializeRows(screen, baseRows);

        int width = Math.Max(1, (int)Math.Ceiling(renderer.CellWidth * scenario.Columns));
        int height = Math.Max(1, (int)Math.Ceiling(renderer.CellHeight * scenario.Rows));

        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
        SKCanvas canvas = surface.Canvas;

        Warmup(renderer, screen, canvas, scenario, baseRows, mutatedRows);

        long[] frameTicks = new long[scenario.Iterations];

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long totalStart = Stopwatch.GetTimestamp();

        int dirtyRows = Math.Clamp(scenario.DirtyRowsPerFrame, 1, scenario.Rows);

        for (int frame = 0; frame < scenario.Iterations; frame++)
        {
            MutateRows(screen, frame, dirtyRows, scenario, baseRows, mutatedRows);

            long frameStart = Stopwatch.GetTimestamp();
            if (scenario.FullRedraw)
            {
                renderer.RenderFull(canvas, screen);
            }
            else
            {
                renderer.Render(canvas, screen, forceFullRedraw: false);
            }

            long frameEnd = Stopwatch.GetTimestamp();
            frameTicks[frame] = frameEnd - frameStart;
        }

        long totalEnd = Stopwatch.GetTimestamp();
        long allocatedAfter = GC.GetTotalAllocatedBytes(true);

        return CreateResult(
            scenario,
            dirtyRows,
            frameTicks,
            totalStart,
            totalEnd,
            allocatedBefore,
            allocatedAfter);
    }

    private static TextHighlightBenchmarkResult CreateResult(
        TextHighlightBenchmarkScenario scenario,
        int dirtyRows,
        long[] frameTicks,
        long totalStart,
        long totalEnd,
        long allocatedBefore,
        long allocatedAfter)
    {
        double totalSeconds = (totalEnd - totalStart) / (double)Stopwatch.Frequency;
        double totalMs = totalSeconds * 1000.0;

        int rowsPerFrame = scenario.FullRedraw ? scenario.Rows : dirtyRows;
        double rowsPerSecond = (rowsPerFrame * (double)scenario.Iterations) / Math.Max(totalSeconds, 1e-9);

        long allocatedBytes = Math.Max(0, allocatedAfter - allocatedBefore);
        double allocPerFrame = allocatedBytes / (double)scenario.Iterations;

        double meanFrameMs = totalMs / scenario.Iterations;
        double p95FrameMs = BenchmarkMath.PercentileMs(frameTicks, 0.95);

        return new TextHighlightBenchmarkResult(
            scenario.Name,
            scenario.Columns,
            scenario.Rows,
            scenario.Iterations,
            dirtyRows,
            scenario.FullRedraw,
            scenario.TextHighlightingMode,
            scenario.RuleSet,
            scenario.Workload,
            scenario.MutateRows,
            rowsPerSecond,
            allocPerFrame,
            meanFrameMs,
            p95FrameMs,
            totalMs);
    }

    private static void Warmup(
        SkiaTerminalRenderer renderer,
        TerminalScreen screen,
        SKCanvas canvas,
        TextHighlightBenchmarkScenario scenario,
        string[] baseRows,
        string[] mutatedRows)
    {
        int dirtyRows = Math.Clamp(scenario.DirtyRowsPerFrame, 1, scenario.Rows);
        int warmupFrames = Math.Min(120, Math.Max(30, scenario.Iterations / 10));

        for (int i = 0; i < warmupFrames; i++)
        {
            MutateRows(screen, i, dirtyRows, scenario, baseRows, mutatedRows);
            if (scenario.FullRedraw)
            {
                renderer.RenderFull(canvas, screen);
            }
            else
            {
                renderer.Render(canvas, screen, forceFullRedraw: false);
            }
        }
    }

    private static IReadOnlyList<TerminalTextHighlightRule> CreateRules(TextHighlightRuleSet ruleSet)
    {
        if (ruleSet == TextHighlightRuleSet.ManyLiteralTokens)
        {
            return CreateManyLiteralRules();
        }

        return ruleSet switch
        {
            TextHighlightRuleSet.LiteralTokens =>
            [
                new TerminalTextHighlightRule
                {
                    Name = "Log status literals",
                    Pattern = @"\b(ERROR|WARN|FAIL|FATAL)\b",
                    Foreground = 0xFFFFE6E6,
                    Background = 0xFF7F1D1D,
                },
                new TerminalTextHighlightRule
                {
                    Name = "BGP literal",
                    Pattern = @"%BGP-\d-\w*",
                    Foreground = 0xFFFFD08A,
                    Background = 0xFF3B1E00,
                },
                new TerminalTextHighlightRule
                {
                    Name = "Localhost",
                    Pattern = @"localhost",
                    Foreground = 0xFFBFDBFE,
                    Background = 0xFF1E3A8A,
                },
                new TerminalTextHighlightRule
                {
                    Name = "Prompt literal",
                    Pattern = @"PROMPT>",
                    Foreground = 0xFFA7F3D0,
                    Background = 0xFF064E3B,
                },
            ],
            _ =>
            [
                new TerminalTextHighlightRule
                {
                    Name = "Log levels",
                    Pattern = @"\b(ERROR|WARN|INFO|DEBUG|TRACE|FATAL)\b",
                    Foreground = 0xFFFFE6E6,
                    Background = 0xFF7F1D1D,
                },
                new TerminalTextHighlightRule
                {
                    Name = "IPv4 addresses",
                    Pattern = @"\b(?:25[0-5]|2[0-4]\d|1?\d?\d)(?:\.(?:25[0-5]|2[0-4]\d|1?\d?\d)){3}\b",
                    Foreground = 0xFF93C5FD,
                },
                new TerminalTextHighlightRule
                {
                    Name = "MAC addresses",
                    Pattern = @"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b",
                    Foreground = 0xFFFF4DFF,
                    Background = 0xFF3B003B,
                },
                new TerminalTextHighlightRule
                {
                    Name = "BGP",
                    Pattern = @"%BGP-\d-\w*",
                    Foreground = 0xFFFFD08A,
                    Background = 0xFF3B1E00,
                },
                new TerminalTextHighlightRule
                {
                    Name = "Prompt",
                    Pattern = @"^\w+@\w+:[^$#]+[$#]",
                    Foreground = 0xFF38BDF8,
                },
            ],
        };
    }

    private static IReadOnlyList<TerminalTextHighlightRule> CreateManyLiteralRules()
    {
        string[] tokens =
        [
            "ALERT", "AUDIT", "AUTHFAIL", "BACKPRESSURE", "CAPACITY", "CONGESTION", "DEADLOCK", "DEGRADED",
            "DROPPED", "EXCEPTION", "FAILOVER", "HOTSPOT", "INVARIANT", "LEADER", "MISCONFIG", "OVERFLOW",
            "PACKETLOSS", "PANIC", "QUORUM", "RECOVERY", "REJECTED", "RETRYING", "ROLLBACK", "SATURATED",
            "SPLITBRAIN", "STALLED", "THROTTLED", "TIMEOUT", "UNHEALTHY", "UNREACHABLE", "VIOLATION", "WATCHDOG",
        ];

        TerminalTextHighlightRule[] rules = new TerminalTextHighlightRule[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            rules[i] = new TerminalTextHighlightRule
            {
                Name = tokens[i],
                Pattern = tokens[i],
                Foreground = 0xFFFFE6E6,
                Background = 0xFF7F1D1D,
            };
        }

        return rules;
    }

    private static string[] CreateRows(TextHighlightWorkload workload, int columns, int rows, int variant)
    {
        string[] values = new string[rows];
        for (int row = 0; row < rows; row++)
        {
            values[row] = CreateRow(workload, columns, row, variant);
        }

        return values;
    }

    private static string CreateRow(TextHighlightWorkload workload, int columns, int row, int variant)
    {
        string text = workload switch
        {
            TextHighlightWorkload.NoMatches =>
                variant == 0
                    ? "worker heartbeat clean alpha beta gamma delta epsilon"
                    : "worker heartbeat clean theta sigma lambda omega",
            TextHighlightWorkload.DenseMatches =>
                CreateDenseMatchRow(row, variant),
            _ =>
                CreateSparseMatchRow(row, variant),
        };

        return text.Length >= columns ? text[..columns] : text.PadRight(columns);
    }

    private static string CreateSparseMatchRow(int row, int variant)
    {
        if ((row + variant) % 8 != 0)
        {
            return "service event accepted queue stable latency nominal";
        }

        return (row & 1) == 0
            ? "ERROR api01 accepted client 10.20.30.40 via 00:11:22:33:44:55"
            : "%BGP-5-ADJCHANGE neighbor 192.168.10.1 moved to Established";
    }

    private static string CreateDenseMatchRow(int row, int variant)
    {
        return ((row + variant) % 3) switch
        {
            0 => "ERROR edge01 rejected client 172.16.10.20 via AA:BB:CC:DD:EE:FF",
            1 => "WARN core02 %BGP-3-NOTIFICATION received from 10.0.0.2",
            _ => "INFO host01@router:/config# localhost resolved 127.0.0.1",
        };
    }

    private static void InitializeRows(TerminalScreen screen, ReadOnlySpan<string> rows)
    {
        for (int rowIndex = 0; rowIndex < screen.ViewportRows; rowIndex++)
        {
            WriteAsciiRow(screen.GetViewportRow(rowIndex), rows[rowIndex]);
        }
    }

    private static void MutateRows(
        TerminalScreen screen,
        int frameIndex,
        int dirtyRows,
        TextHighlightBenchmarkScenario scenario,
        string[] baseRows,
        string[] mutatedRows)
    {
        if (!scenario.FullRedraw)
        {
            for (int rowIndex = 0; rowIndex < screen.ViewportRows; rowIndex++)
            {
                screen.GetViewportRow(rowIndex).IsDirty = false;
            }
        }

        if (!scenario.MutateRows)
        {
            return;
        }

        int rowsToMutate = scenario.FullRedraw ? screen.ViewportRows : dirtyRows;
        for (int i = 0; i < rowsToMutate; i++)
        {
            int rowIndex = scenario.FullRedraw
                ? i
                : (frameIndex + i) % screen.ViewportRows;
            string[] source = (frameIndex & 1) == 0 ? mutatedRows : baseRows;
            WriteAsciiRow(screen.GetViewportRow(rowIndex), source[rowIndex]);
        }
    }

    private static void WriteAsciiRow(TerminalRow row, string text)
    {
        Span<TerminalCell> cells = row.Cells;
        int count = Math.Min(cells.Length, text.Length);
        for (int col = 0; col < cells.Length; col++)
        {
            ref TerminalCell cell = ref cells[col];
            cell.Codepoint = col < count ? text[col] : ' ';
            cell.Grapheme = null;
            cell.Foreground = 0xFFD4D4D4;
            cell.Background = 0xFF1E1E1E;
            cell.HasBackground = true;
            cell.Attributes = (col % 23 == 0) ? CellAttributes.Bold : CellAttributes.None;
            cell.Width = 1;
            cell.UnderlineColor = 0;
            cell.HasUnderlineColor = false;
        }

        row.IsDirty = true;
    }
}

internal static class BenchmarkMath
{
    public static double PercentileMs(long[] samples, double percentile)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        long[] sorted = new long[samples.Length];
        Array.Copy(samples, sorted, samples.Length);
        Array.Sort(sorted);

        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        index = Math.Clamp(index, 0, sorted.Length - 1);

        double ticks = sorted[index];
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}

internal static class BenchmarkReportWriter
{
    public static string CreateReport(
        ReadOnlySpan<BenchmarkResult> renderResults,
        ReadOnlySpan<TextHighlightBenchmarkResult> textHighlightResults,
        ReadOnlySpan<PtyIoBenchmarkResult> ptyIoResults,
        ReadOnlySpan<VtParseBenchmarkResult> vtParseResults,
        PtyIoFixtureSet? ptyIoFixtures)
    {
        StringBuilder sb = new();
        sb.AppendLine("# RoyalTerminal Benchmarks");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"CPU logical cores: {Environment.ProcessorCount}");
        sb.AppendLine();
        AppendRenderTable(sb, renderResults);
        sb.AppendLine();
        AppendTextHighlightTable(sb, textHighlightResults);
        sb.AppendLine();
        AppendPtyIoTable(sb, ptyIoResults, ptyIoFixtures);
        sb.AppendLine();
        AppendVtParseTable(sb, vtParseResults, ptyIoFixtures);
        return sb.ToString();
    }

    private static void AppendRenderTable(StringBuilder sb, ReadOnlySpan<BenchmarkResult> results)
    {
        sb.AppendLine("## Render Baseline");
        sb.AppendLine();
        if (results.IsEmpty)
        {
            sb.AppendLine("_Skipped._");
            return;
        }

        sb.AppendLine("| Scenario | Grid | Mode | Text pipeline | Iterations | Rows/frame | Rows/sec | Alloc/frame (B) | Render alloc/frame (B) | Mean frame (ms) | p95 frame (ms) | Total time (ms) |");
        sb.AppendLine("|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        for (int i = 0; i < results.Length; i++)
        {
            BenchmarkResult result = results[i];
            string mode = result.FullRedraw ? "full" : "dirty";

            sb.Append("| ").Append(result.Name)
                .Append(" | ").Append(result.Columns).Append('x').Append(result.Rows)
                .Append(" | ").Append(mode)
                .Append(" | ").Append(result.TextRenderPipeline)
                .Append(" | ").Append(result.Iterations)
                .Append(" | ").Append(result.DirtyRowsPerFrame)
                .Append(" | ").Append(Format(result.RowsPerSecond))
                .Append(" | ").Append(Format(result.AllocBytesPerFrame))
                .Append(" | ").Append(Format(result.RenderAllocBytesPerFrame))
                .Append(" | ").Append(Format(result.MeanFrameMs))
                .Append(" | ").Append(Format(result.P95FrameMs))
                .Append(" | ").Append(Format(result.TotalTimeMs))
                .AppendLine(" |");
        }
    }

    private static void AppendTextHighlightTable(
        StringBuilder sb,
        ReadOnlySpan<TextHighlightBenchmarkResult> results)
    {
        sb.AppendLine("## Regex Text Highlighting");
        sb.AppendLine();
        if (results.IsEmpty)
        {
            sb.AppendLine("_Skipped._");
            return;
        }

        sb.AppendLine("| Scenario | Grid | Render | Highlight mode | Rules | Workload | Mutates | Iterations | Rows/frame | Rows/sec | Alloc/frame (B) | Mean frame (ms) | p95 frame (ms) | Total time (ms) |");
        sb.AppendLine("|---|---:|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        for (int i = 0; i < results.Length; i++)
        {
            TextHighlightBenchmarkResult result = results[i];
            string renderMode = result.FullRedraw ? "full" : "dirty";

            sb.Append("| ").Append(result.Name)
                .Append(" | ").Append(result.Columns).Append('x').Append(result.Rows)
                .Append(" | ").Append(renderMode)
                .Append(" | ").Append(result.TextHighlightingMode)
                .Append(" | ").Append(result.RuleSet)
                .Append(" | ").Append(result.Workload)
                .Append(" | ").Append(result.MutateRows ? "yes" : "no")
                .Append(" | ").Append(result.Iterations)
                .Append(" | ").Append(result.DirtyRowsPerFrame)
                .Append(" | ").Append(Format(result.RowsPerSecond))
                .Append(" | ").Append(Format(result.AllocBytesPerFrame))
                .Append(" | ").Append(Format(result.MeanFrameMs))
                .Append(" | ").Append(Format(result.P95FrameMs))
                .Append(" | ").Append(Format(result.TotalTimeMs))
                .AppendLine(" |");
        }
    }

    private static void AppendPtyIoTable(
        StringBuilder sb,
        ReadOnlySpan<PtyIoBenchmarkResult> results,
        PtyIoFixtureSet? fixtures)
    {
        sb.AppendLine("## PTY IO Throughput");
        sb.AppendLine();
        if (fixtures is not null)
        {
            PtyIoFixtureSet value = fixtures.Value;
            sb.AppendLine($"Fixture directory: `{value.Directory}`");
            sb.AppendLine($"Fixture size: {value.SizeMiB} MiB each");
            sb.AppendLine();
        }

        if (results.IsEmpty)
        {
            sb.AppendLine("_Skipped. Pass `--io` to generate fixtures and run PTY IO throughput scenarios._");
            return;
        }

        sb.AppendLine("Child columns come from `/usr/bin/time -p cat` output captured through the PTY.");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Mode | File | Bytes | Repeats | Batches | Largest batch (B) | Terminal MiB/s | Terminal time (ms) | Child MiB/s | Child real (ms) | Callback time (ms) | Callback alloc/MiB | Status |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        for (int i = 0; i < results.Length; i++)
        {
            PtyIoBenchmarkResult result = results[i];
            string status = result.SkippedReason is not null
                ? "skipped: " + result.SkippedReason
                : result.TimedOut
                    ? "timed out"
                    : "ok";

            sb.Append("| ").Append(result.Name)
                .Append(" | ").Append(result.Mode)
                .Append(" | `").Append(result.FilePath).Append('`')
                .Append(" | ").Append(result.Bytes)
                .Append(" | ").Append(result.Repeats)
                .Append(" | ").Append(result.Batches)
                .Append(" | ").Append(result.LargestBatchBytes)
                .Append(" | ").Append(Format(result.MiBPerSecond))
                .Append(" | ").Append(Format(result.TotalTimeMs))
                .Append(" | ").Append(FormatPositiveOptional(result.ChildMiBPerSecond))
                .Append(" | ").Append(FormatPositiveOptional(result.ChildRealTimeMs))
                .Append(" | ").Append(Format(result.CallbackTimeMs))
                .Append(" | ").Append(Format(BytesPerMiB(result.CallbackAllocatedBytes, result.Bytes)))
                .Append(" | ").Append(status)
                .AppendLine(" |");
        }
    }

    private static void AppendVtParseTable(
        StringBuilder sb,
        ReadOnlySpan<VtParseBenchmarkResult> results,
        PtyIoFixtureSet? fixtures)
    {
        sb.AppendLine("## Managed VT Parse Throughput");
        sb.AppendLine();
        if (fixtures is not null)
        {
            PtyIoFixtureSet value = fixtures.Value;
            sb.AppendLine($"Fixture directory: `{value.Directory}`");
            sb.AppendLine($"Fixture size: {value.SizeMiB} MiB each");
            sb.AppendLine();
        }

        if (results.IsEmpty)
        {
            sb.AppendLine("_Skipped. Pass `--vt-parse` to run managed VT parser throughput scenarios._");
            return;
        }

        sb.AppendLine("This section excludes file IO and PTY kernel behavior; it measures `BasicVtProcessor.Process` directly.");
        sb.AppendLine();
        sb.AppendLine("| Scenario | File | Bytes | Repeats | Printable ASCII | Control/ESC | Non-ASCII | MiB/s | Total time (ms) | Alloc/MiB |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        for (int i = 0; i < results.Length; i++)
        {
            VtParseBenchmarkResult result = results[i];
            sb.Append("| ").Append(result.Name)
                .Append(" | `").Append(result.FilePath).Append('`')
                .Append(" | ").Append(result.Bytes)
                .Append(" | ").Append(result.Repeats)
                .Append(" | ").Append(Format(result.PrintableAsciiPercent)).Append('%')
                .Append(" | ").Append(Format(result.ControlOrEscapePercent)).Append('%')
                .Append(" | ").Append(Format(result.NonAsciiPercent)).Append('%')
                .Append(" | ").Append(Format(result.MiBPerSecond))
                .Append(" | ").Append(Format(result.TotalTimeMs))
                .Append(" | ").Append(Format(BytesPerMiB(result.AllocatedBytes, result.Bytes)))
                .AppendLine(" |");
        }
    }

    private static double BytesPerMiB(long bytes, long payloadBytes)
    {
        double mib = payloadBytes / (1024.0 * 1024.0);
        return mib > 0 ? bytes / mib : 0;
    }

    private static string FormatPositiveOptional(double value)
    {
        return double.IsFinite(value) && value > 0
            ? Format(value)
            : "n/a";
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
