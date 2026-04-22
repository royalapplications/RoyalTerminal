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

    [Fact]
    public void PackWorkflows_UseSharedPackScript()
    {
        string repoRoot = FindRepositoryRoot();
        string ciWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
        string releaseWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");

        string ciWorkflow = File.ReadAllText(ciWorkflowPath);
        string releaseWorkflow = File.ReadAllText(releaseWorkflowPath);

        Assert.Contains("bash scripts/pack-nuget.sh --configuration Release --output artifacts", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains(
            "bash scripts/pack-nuget.sh --configuration Release --output artifacts --version",
            releaseWorkflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackableProjectSet_IncludesInternalTransitivePackagesRequiredByPublishedPackages()
    {
        string repoRoot = FindRepositoryRoot();
        HashSet<string> packableProjectNames = EnumeratePackableProjects(repoRoot)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToHashSet(StringComparer.Ordinal);

        string[] requiredProjects =
        [
            "RoyalTerminal.Avalonia.Settings",
            "RoyalTerminal.Shaders",
            "RoyalTerminal.Rendering.Text",
            "RoyalTerminal.Terminal.Transport.Pipe",
            "RoyalTerminal.Terminal.Transport.Pty",
            "RoyalTerminal.Terminal.Transport.Raw",
            "RoyalTerminal.Terminal.Transport.Serial",
            "RoyalTerminal.Terminal.Transport.Ssh.Abstractions",
            "RoyalTerminal.Terminal.Transport.Ssh.SshNet",
            "RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent",
            "RoyalTerminal.Terminal.Transport.Telnet",
            "RoyalTerminal.Unicode",
        ];

        foreach (string requiredProject in requiredProjects)
        {
            Assert.Contains(requiredProject, packableProjectNames);
        }
    }

    [Fact]
    public void RoyalTerminalShaders_ProjectHasNoPackageOrProjectReferences()
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.Shaders",
            "RoyalTerminal.Shaders.csproj");

        XDocument project = XDocument.Load(projectPath);
        List<string> references = project
            .Descendants()
            .Where(element =>
                element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .ToList();

        Assert.Empty(references);
    }

    [Fact]
    public void RoyalTerminalShaders_SourceDoesNotUseTerminalOrRenderingTypes()
    {
        string repoRoot = FindRepositoryRoot();
        string shaderProjectRoot = Path.Combine(repoRoot, "src", "RoyalTerminal.Shaders");
        string[] forbiddenFragments =
        [
            "using Avalonia",
            "using SkiaSharp",
            "using RoyalTerminal.Avalonia",
            "using RoyalTerminal.Rendering",
            "using RoyalTerminal.Terminal",
            "namespace RoyalTerminal.Avalonia",
            "namespace RoyalTerminal.Rendering",
            "namespace RoyalTerminal.Terminal",
        ];

        foreach (string sourcePath in Directory.EnumerateFiles(shaderProjectRoot, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(sourcePath);
            foreach (string forbiddenFragment in forbiddenFragments)
            {
                Assert.DoesNotContain(forbiddenFragment, source, StringComparison.Ordinal);
            }
        }
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

    private static IEnumerable<string> EnumeratePackableProjects(string repoRoot)
    {
        string srcRoot = Path.Combine(repoRoot, "src");
        foreach (string projectPath in Directory.EnumerateFiles(srcRoot, "*.csproj", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            XDocument project = XDocument.Load(projectPath);
            bool isPackable = project
                .Descendants()
                .Where(element => element.Name.LocalName == "IsPackable")
                .Select(element => element.Value?.Trim())
                .All(value => !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase));

            if (isPackable)
            {
                yield return projectPath;
            }
        }
    }
}
