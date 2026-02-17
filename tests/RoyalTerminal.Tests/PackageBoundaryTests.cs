// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — package boundary tests for backend split.

using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace RoyalTerminal.Tests;

[CollectionDefinition("ProjectBuildTests", DisableParallelization = true)]
public sealed class ProjectBuildTestsCollection
{
}

[Collection("ProjectBuildTests")]
public sealed class PackageBoundaryTests
{
    [Fact]
    public void RoyalTerminalAvalonia_References_DoNotIncludeGhosttyAssemblies()
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.Avalonia",
            "RoyalTerminal.Avalonia.csproj");

        XDocument project = XDocument.Load(projectPath);
        List<string> references = project
            .Descendants()
            .Where(element =>
                element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToList();
        List<string> imports = project
            .Descendants("Import")
            .Select(element => element.Attribute("Project")?.Value ?? string.Empty)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        Assert.DoesNotContain(
            references,
            include => include.Contains("GhosttySharp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            references,
            include => include.Contains("Terminal.Vt.Ghostty", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            references,
            include => include.Contains("Rendering.GhosttyInterop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            imports,
            include => include.Contains("Ghostty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RoyalTerminalAvalonia_CanBuildInIsolation_WithoutGhosttyProjects()
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.Avalonia",
            "RoyalTerminal.Avalonia.csproj");
        string logPath = Path.Combine(
            Path.GetTempPath(),
            $"RoyalTerminal.Avalonia.build.{Guid.NewGuid():N}.log");

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" --no-restore -nologo -v minimal -flp:logfile=\"{logPath}\";verbosity=minimal",
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet build process.");

        bool exited;
        string output;
        try
        {
            exited = process.WaitForExit(milliseconds: 120_000);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Ignore: process exited after timeout check.
                }

                process.WaitForExit();
            }

            output = File.Exists(logPath)
                ? File.ReadAllText(logPath)
                : string.Empty;
        }
        finally
        {
            File.Delete(logPath);
        }

        Assert.True(
            exited,
            $"Timed out building RoyalTerminal.Avalonia in isolation.{Environment.NewLine}{output}");
        Assert.True(
            process.ExitCode == 0,
            $"Build exited with code {process.ExitCode}.{Environment.NewLine}{output}");
        Assert.DoesNotContain("RoyalTerminal.GhosttySharp", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RoyalTerminal.Terminal.Vt.Ghostty", output, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RoyalTerminal.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
