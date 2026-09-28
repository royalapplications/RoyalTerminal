// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class UnitTestBatchingTests
{
    [Theory]
    [InlineData("C")]
    [InlineData("en_US.UTF-8")]
    public async Task UnicodeNamesRemainDistinctAndTestProcessesKeepTheirLocale(string locale)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        (int exitCode, string output) = await RunBatches("valid", locale);
        Assert.True(exitCode == 0, output);
        Assert.Contains("Discovered 6 unit tests across 2 test methods.", output);
        Assert.Contains("Generated 2 unit test batches", output);
        Assert.Equal(2, output.Split("Executed mock batch", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("overlap", "filters overlap")]
    [InlineData("missing", "do not cover every discovered test")]
    [InlineData("unexpected", "match tests outside the discovered set")]
    [InlineData("empty", "does not match any discovered tests")]
    public async Task CoverageValidationStillRejectsInvalidBatches(string scenario, string error)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        (int exitCode, string output) = await RunBatches(scenario, "en_US.UTF-8");
        Assert.NotEqual(0, exitCode);
        Assert.Contains(error, output);
        Assert.DoesNotContain("Executed mock batch", output);
    }

    private static async Task<(int ExitCode, string Output)> RunBatches(string scenario, string locale)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RoyalTerminal.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(Path.GetTempPath(), "royalterminal-batching-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string executable = Path.Combine(directory, "dotnet");
            await File.WriteAllTextAsync(executable, FakeDotNet);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await File.WriteAllLinesAsync(Path.Combine(directory, "discovered.txt"),
            [
                "    Cases.Alpha(text: \"A\")",
                "    Cases.Alpha(text: \"A\uFE0F\")",
                "    Cases.Alpha(text: \"\U0001F600\")",
                "    Cases.Alpha(text: \"\U0001F600\uFE0F\")",
                "    Cases.Beta(text: \"\u754C\")",
                "    Cases.Beta(text: \"\u754C\uFE0F\")",
            ]);
            ProcessStartInfo start = new("/bin/bash")
            {
                WorkingDirectory = root.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(root.FullName, "scripts", "run-unit-test-batches.sh"));
            start.Environment["PATH"] = directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["LC_ALL"] = locale;
            start.Environment["BATCHING_EXPECTED_LOCALE"] = locale;
            start.Environment["BATCHING_FIXTURE_DIRECTORY"] = directory;
            start.Environment["BATCHING_SCENARIO"] = scenario;
            start.Environment["ROYALTERMINAL_TEST_PROJECT"] = "fixture.csproj";
            start.Environment["ROYALTERMINAL_TEST_RESULTS_DIR"] = Path.Combine(directory, "results");
            start.Environment["ROYALTERMINAL_TEST_BATCH_TARGET"] = "1";
            start.Environment["ROYALTERMINAL_VALIDATE_TEST_BATCH_COVERAGE"] = "true";
            start.Environment["ROYALTERMINAL_TEST_MAX_DUPLICATE_MATCHES"] = "0";
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start batch script.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return (process.ExitCode, await stdout + await stderr);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // Exercise the real discovery/filter/coverage/execution script without
    // recursively launching the enclosing xUnit suite. Unicode theory rows
    // differ only in selectors, which macOS linguistic collation can merge.
    private const string FakeDotNet = """
        #!/bin/bash
        set -eu
        [[ "$LC_ALL" == "$BATCHING_EXPECTED_LOCALE" ]] || { echo 'Test locale changed'; exit 91; }
        list=false
        filter=''
        while [[ $# -gt 0 ]]; do
          case "$1" in
            --list-tests) list=true ;;
            --filter) shift; filter="$1" ;;
          esac
          shift
        done
        if [[ "$list" != true ]]; then echo 'Executed mock batch'; exit 0; fi
        while IFS= read -r item; do
          if [[ -n "$filter" ]]; then
            method="${item%%(*}"
            method="${method#    }"
            [[ "$BATCHING_SCENARIO" == overlap || "$filter" == *"(FullyQualifiedName=$method)"* ]] || continue
            [[ "$BATCHING_SCENARIO" != empty || "$method" != Cases.Beta ]] || continue
            [[ "$BATCHING_SCENARIO" != missing || "$item" != '    Cases.Alpha(text: "A")' ]] || continue
          fi
          printf '%s\n' "$item"
        done < "$BATCHING_FIXTURE_DIRECTORY/discovered.txt"
        if [[ -n "$filter" && "$BATCHING_SCENARIO" == unexpected && "$filter" == *Cases.Alpha* ]]; then
          printf '%s\n' '    Cases.Unexpected'
        fi
        """;
}
