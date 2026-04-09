// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — regression checks for Windows arm64 native packaging metadata.

using System.Xml.Linq;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class WindowsArm64NativePackagingTests
{
    [Fact]
    public void WindowsNativePackageProject_PacksBothX64AndArm64RuntimeAssets()
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.GhosttySharp.Native.Win64",
            "RoyalTerminal.GhosttySharp.Native.Win64.csproj");

        XDocument project = XDocument.Load(projectPath);
        List<(string Include, string PackagePath)> runtimeEntries = project
            .Descendants()
            .Where(element => element.Name.LocalName == "None")
            .Select(element => (
                Include: element.Attribute("Include")?.Value ?? string.Empty,
                PackagePath: element.Attribute("PackagePath")?.Value ?? string.Empty))
            .ToList();

        Assert.Contains(
            runtimeEntries,
            entry => string.Equals(entry.Include, "runtimes/win-x64/native/ghostty-vt.dll", StringComparison.Ordinal)
                  && string.Equals(entry.PackagePath, "runtimes/win-x64/native", StringComparison.Ordinal));
        Assert.Contains(
            runtimeEntries,
            entry => string.Equals(entry.Include, "runtimes/win-x64/native/ghostty-renderer-capi.dll", StringComparison.Ordinal)
                  && string.Equals(entry.PackagePath, "runtimes/win-x64/native", StringComparison.Ordinal));
        Assert.Contains(
            runtimeEntries,
            entry => string.Equals(entry.Include, "runtimes/win-arm64/native/ghostty-vt.dll", StringComparison.Ordinal)
                  && string.Equals(entry.PackagePath, "runtimes/win-arm64/native", StringComparison.Ordinal));
        Assert.Contains(
            runtimeEntries,
            entry => string.Equals(entry.Include, "runtimes/win-arm64/native/ghostty-renderer-capi.dll", StringComparison.Ordinal)
                  && string.Equals(entry.PackagePath, "runtimes/win-arm64/native", StringComparison.Ordinal));
        Assert.DoesNotContain(
            runtimeEntries,
            entry => entry.Include.Contains(".lib", StringComparison.OrdinalIgnoreCase)
                  || entry.Include.Contains("ghostty.dll", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(entry.Include, "runtimes/win-x64/native/**", StringComparison.Ordinal)
                  || string.Equals(entry.Include, "runtimes/win-arm64/native/**", StringComparison.Ordinal)
                  || string.Equals(entry.Include, "runtimes/win-x64/native/*.dll", StringComparison.Ordinal)
                  || string.Equals(entry.Include, "runtimes/win-arm64/native/*.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsNativeTargets_ResolveArm64RuntimeByRidAndProcessArchitecture()
    {
        string repoRoot = FindRepositoryRoot();
        string buildTargetsPath = Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.GhosttySharp.Native.Win64",
            "build",
            "RoyalTerminal.GhosttySharp.Native.Win64.targets");
        string transitiveTargetsPath = Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.GhosttySharp.Native.Win64",
            "buildTransitive",
            "RoyalTerminal.GhosttySharp.Native.Win64.targets");

        AssertTargetsContainArm64RuntimeSelection(File.ReadAllText(buildTargetsPath));
        AssertTargetsContainArm64RuntimeSelection(File.ReadAllText(transitiveTargetsPath));
    }

    [Fact]
    public void CiAndReleaseWorkflows_IncludeWindowsArm64NativeArtifacts()
    {
        string repoRoot = FindRepositoryRoot();
        string ciWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "ci.yml");
        string releaseWorkflowPath = Path.Combine(repoRoot, ".github", "workflows", "release.yml");

        AssertWorkflowContainsWindowsArm64Markers(File.ReadAllText(ciWorkflowPath));
        AssertWorkflowContainsWindowsArm64Markers(File.ReadAllText(releaseWorkflowPath));
    }

    private static void AssertTargetsContainArm64RuntimeSelection(string targetsText)
    {
        Assert.Contains("runtimes/win-arm64/native/ghostty-vt.dll", targetsText, StringComparison.Ordinal);
        Assert.Contains("runtimes/win-arm64/native/ghostty-renderer-capi.dll", targetsText, StringComparison.Ordinal);
        Assert.Contains("'$(RuntimeIdentifier)' == 'win-arm64'", targetsText, StringComparison.Ordinal);
        Assert.Contains("ProcessArchitecture)' == 'Arm64'", targetsText, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimes/win-arm64/native/*", targetsText, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimes/win-x64/native/*", targetsText, StringComparison.Ordinal);
        Assert.DoesNotContain(".lib", targetsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghostty.dll", targetsText, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertWorkflowContainsWindowsArm64Markers(string workflowText)
    {
        Assert.Contains("native-win-arm64", workflowText, StringComparison.Ordinal);
        Assert.Contains("runtimes/win-arm64/native/", workflowText, StringComparison.Ordinal);
        Assert.Contains("-name \"ghostty-vt.dll\"", workflowText, StringComparison.Ordinal);
        Assert.Contains("-name \"ghostty-renderer-capi.dll\"", workflowText, StringComparison.Ordinal);
        Assert.DoesNotContain("find external/ghostty/zig-out -name \"*.lib\"", workflowText, StringComparison.Ordinal);
        Assert.DoesNotContain("find native/ghostty-renderer-capi/zig-out -name \"*ghostty-renderer-capi*.lib\"", workflowText, StringComparison.Ordinal);
        Assert.DoesNotContain("find external/ghostty/zig-out -name \"*.dll\"", workflowText, StringComparison.Ordinal);
        Assert.DoesNotContain("ghostty.dll", workflowText, StringComparison.Ordinal);
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
