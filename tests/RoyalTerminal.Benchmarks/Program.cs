// Licensed under the MIT License.
// RoyalTerminal.Benchmarks — Render hot-path benchmark harness.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;

BenchmarkOptions options = BenchmarkOptions.Parse(args);

BenchmarkScenario[] scenarios =
[
    new("full-80x24", Columns: 80, Rows: 24, Iterations: 1_200, DirtyRowsPerFrame: 24, FullRedraw: true),
    new("full-160x48", Columns: 160, Rows: 48, Iterations: 700, DirtyRowsPerFrame: 48, FullRedraw: true),
    new("full-240x80", Columns: 240, Rows: 80, Iterations: 300, DirtyRowsPerFrame: 80, FullRedraw: true),
    new("dirty-160x48-r8", Columns: 160, Rows: 48, Iterations: 1_800, DirtyRowsPerFrame: 8, FullRedraw: false),
];

BenchmarkResult[] results = new BenchmarkResult[scenarios.Length];
for (int i = 0; i < scenarios.Length; i++)
{
    results[i] = RenderHotPathBenchmark.Run(scenarios[i]);
}

string report = BenchmarkReportWriter.CreateReport(results);
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

        double totalSeconds = (totalEnd - totalStart) / (double)Stopwatch.Frequency;
        double totalMs = totalSeconds * 1000.0;

        int rowsPerFrame = scenario.FullRedraw ? scenario.Rows : dirtyRows;
        double rowsPerSecond = (rowsPerFrame * (double)scenario.Iterations) / Math.Max(totalSeconds, 1e-9);

        long allocatedBytes = Math.Max(0, allocatedAfter - allocatedBefore);
        double allocPerFrame = allocatedBytes / (double)scenario.Iterations;

        double meanFrameMs = totalMs / scenario.Iterations;
        double p95FrameMs = PercentileMs(frameTicks, 0.95);

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

    private static double PercentileMs(long[] samples, double percentile)
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
    public static string CreateReport(ReadOnlySpan<BenchmarkResult> results)
    {
        StringBuilder sb = new();
        sb.AppendLine("# RoyalTerminal.GhosttySharp Render Hot-Path Baseline");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"CPU logical cores: {Environment.ProcessorCount}");
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

        return sb.ToString();
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
