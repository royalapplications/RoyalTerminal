// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Benchmarks - Render hot-path benchmark harness.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using SkiaSharp;

BenchmarkOptions options = BenchmarkOptions.Parse(args);

BenchmarkScenario[] renderScenarios =
[
    new("full-80x24", Columns: 80, Rows: 24, Iterations: 1_200, DirtyRowsPerFrame: 24, FullRedraw: true),
    new("full-160x48", Columns: 160, Rows: 48, Iterations: 700, DirtyRowsPerFrame: 48, FullRedraw: true),
    new("full-240x80", Columns: 240, Rows: 80, Iterations: 300, DirtyRowsPerFrame: 80, FullRedraw: true),
    new("dirty-160x48-r8", Columns: 160, Rows: 48, Iterations: 1_800, DirtyRowsPerFrame: 8, FullRedraw: false),
];

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

BenchmarkResult[] renderResults = new BenchmarkResult[renderScenarios.Length];
for (int i = 0; i < renderScenarios.Length; i++)
{
    renderResults[i] = RenderHotPathBenchmark.Run(renderScenarios[i]);
}

TextHighlightBenchmarkResult[] textHighlightResults = new TextHighlightBenchmarkResult[textHighlightScenarios.Length];
for (int i = 0; i < textHighlightScenarios.Length; i++)
{
    textHighlightResults[i] = TextHighlightBenchmark.Run(textHighlightScenarios[i]);
}

string report = BenchmarkReportWriter.CreateReport(renderResults, textHighlightResults);
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

internal readonly record struct BenchmarkScenario(
    string Name,
    int Columns,
    int Rows,
    int Iterations,
    int DirtyRowsPerFrame,
    bool FullRedraw);

internal readonly record struct BenchmarkResult(
    string Name,
    int Columns,
    int Rows,
    int Iterations,
    int DirtyRowsPerFrame,
    bool FullRedraw,
    double RowsPerSecond,
    double AllocBytesPerFrame,
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

internal readonly record struct BenchmarkOptions(string? OutputPath)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        string? outputPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length)
            {
                outputPath = args[i + 1];
                i++;
            }
        }

        return new BenchmarkOptions(outputPath);
    }
}

internal static class RenderHotPathBenchmark
{
    private const float FontSize = 14f;

    public static BenchmarkResult Run(BenchmarkScenario scenario)
    {
        using SkiaTerminalRenderer renderer = new("Consolas", FontSize);
        TerminalScreen screen = new(scenario.Columns, scenario.Rows);
        InitializeScreen(screen);

        int width = Math.Max(1, (int)Math.Ceiling(renderer.CellWidth * scenario.Columns));
        int height = Math.Max(1, (int)Math.Ceiling(renderer.CellHeight * scenario.Rows));

        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
        SKCanvas canvas = surface.Canvas;

        Warmup(renderer, screen, canvas, scenario);

        long[] frameTicks = new long[scenario.Iterations];

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        long totalStart = Stopwatch.GetTimestamp();

        int dirtyRows = Math.Clamp(scenario.DirtyRowsPerFrame, 1, scenario.Rows);

        for (int frame = 0; frame < scenario.Iterations; frame++)
        {
            MutateFrame(screen, frame, dirtyRows, scenario.FullRedraw);

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

    private static BenchmarkResult CreateResult(
        BenchmarkScenario scenario,
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

        return new BenchmarkResult(
            scenario.Name,
            scenario.Columns,
            scenario.Rows,
            scenario.Iterations,
            dirtyRows,
            scenario.FullRedraw,
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
        BenchmarkScenario scenario)
    {
        int dirtyRows = Math.Clamp(scenario.DirtyRowsPerFrame, 1, scenario.Rows);
        int warmupFrames = Math.Min(120, Math.Max(30, scenario.Iterations / 10));

        for (int i = 0; i < warmupFrames; i++)
        {
            MutateFrame(screen, i, dirtyRows, scenario.FullRedraw);
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

    private static void InitializeScreen(TerminalScreen screen)
    {
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

    private static void MutateFrame(TerminalScreen screen, int frameIndex, int dirtyRows, bool fullRedraw)
    {
        int rows = screen.ViewportRows;
        int columns = screen.Columns;

        if (!fullRedraw)
        {
            for (int rowIndex = 0; rowIndex < rows; rowIndex++)
            {
                screen.GetViewportRow(rowIndex).IsDirty = false;
            }
        }

        int rowsToMutate = fullRedraw ? rows : dirtyRows;
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
        ReadOnlySpan<TextHighlightBenchmarkResult> textHighlightResults)
    {
        StringBuilder sb = new();
        sb.AppendLine("# RoyalTerminal Render And Regex Highlighting Benchmarks");
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
        return sb.ToString();
    }

    private static void AppendRenderTable(StringBuilder sb, ReadOnlySpan<BenchmarkResult> results)
    {
        sb.AppendLine("## Render Baseline");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Grid | Mode | Iterations | Rows/frame | Rows/sec | Alloc/frame (B) | Mean frame (ms) | p95 frame (ms) | Total time (ms) |");
        sb.AppendLine("|---|---:|---|---:|---:|---:|---:|---:|---:|---:|");

        for (int i = 0; i < results.Length; i++)
        {
            BenchmarkResult result = results[i];
            string mode = result.FullRedraw ? "full" : "dirty";

            sb.Append("| ").Append(result.Name)
                .Append(" | ").Append(result.Columns).Append('x').Append(result.Rows)
                .Append(" | ").Append(mode)
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

    private static void AppendTextHighlightTable(
        StringBuilder sb,
        ReadOnlySpan<TextHighlightBenchmarkResult> results)
    {
        sb.AppendLine("## Regex Text Highlighting");
        sb.AppendLine();
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

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
