// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - NuGet runtime.json metadata coverage for Ghostty native assets.

using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyNativeRuntimeDependencyTests
{
    [Theory]
    [InlineData("RoyalTerminal.GhosttySharp", "src/RoyalTerminal.GhosttySharp")]
    [InlineData("RoyalTerminal.Rendering.Interop.Ghostty", "src/RoyalTerminal.Rendering.Interop.Ghostty")]
    public void PackageProject_OptsIntoGeneratedRuntimeJson(string packageId, string projectDirectory)
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(repoRoot, projectDirectory, packageId + ".csproj");

        XDocument project = XDocument.Load(projectPath);
        XElement optInProperty = Assert.Single(
            project.Descendants(),
            element => element.Name.LocalName == "RoyalTerminalGenerateGhosttyNativeRuntimeJson");

        Assert.Equal("true", optInProperty.Value);
    }

    [Fact]
    public void RuntimeJsonTarget_PacksGeneratedRuntimeJsonAtPackageRoot()
    {
        string repoRoot = FindRepositoryRoot();
        string targetsPath = Path.Combine(repoRoot, "Directory.Build.targets");
        XDocument targets = XDocument.Load(targetsPath);

        XElement extensionPoint = Assert.Single(
            targets.Descendants(),
            element => element.Name.LocalName == "TargetsForTfmSpecificContentInPackage"
                    && element.Value.Contains(
                        "GenerateRoyalTerminalGhosttyNativeRuntimeJson",
                        StringComparison.Ordinal));
        Assert.Contains(
            "GenerateRoyalTerminalGhosttyNativeRuntimeJson",
            extensionPoint.Value,
            StringComparison.Ordinal);

        XElement target = Assert.Single(
            targets.Descendants(),
            element => element.Name.LocalName == "Target"
                    && string.Equals(
                        element.Attribute("Name")?.Value,
                        "GenerateRoyalTerminalGhosttyNativeRuntimeJson",
                        StringComparison.Ordinal));

        Assert.Contains(
            target.Descendants(),
            element => element.Name.LocalName == "WriteLinesToFile"
                    && string.Equals(
                        element.Attribute("File")?.Value,
                        "$(_RoyalTerminalGhosttyRuntimeJsonPath)",
                        StringComparison.Ordinal));

        XElement packageItem = Assert.Single(
            target.Descendants(),
            element => element.Name.LocalName == "TfmSpecificPackageFile"
                    && string.Equals(
                        element.Attribute("Include")?.Value,
                        "$(_RoyalTerminalGhosttyRuntimeJsonPath)",
                        StringComparison.Ordinal));

        Assert.Equal("runtime.json", packageItem.Attribute("PackagePath")?.Value);
    }

    [Theory]
    [InlineData("RoyalTerminal.GhosttySharp")]
    [InlineData("RoyalTerminal.Rendering.Interop.Ghostty")]
    public void RuntimeJsonTarget_GeneratesSupportedRidMappings(string packageId)
    {
        string repoRoot = FindRepositoryRoot();
        string targetsPath = Path.Combine(repoRoot, "Directory.Build.targets");
        XDocument targets = XDocument.Load(targetsPath);
        XElement target = Assert.Single(
            targets.Descendants(),
            element => element.Name.LocalName == "Target"
                    && string.Equals(
                        element.Attribute("Name")?.Value,
                        "GenerateRoyalTerminalGhosttyNativeRuntimeJson",
                        StringComparison.Ordinal));
        string[] lines = target
            .Descendants()
            .Where(static element => element.Name.LocalName == "_RoyalTerminalGhosttyRuntimeJsonLine")
            .Select(static element => element.Attribute("Include")?.Value)
            .Where(static line => line is not null)
            .Select(line => line!
                .Replace("$(PackageId)", packageId, StringComparison.Ordinal)
                .Replace("$(_RoyalTerminalGhosttyNativePackageVersion)", "0.0.0-test", StringComparison.Ordinal))
            .ToArray();
        string json = string.Join(Environment.NewLine, lines);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement runtimes = document.RootElement.GetProperty("runtimes");
        AssertRuntimeDependency(runtimes, "linux-arm64", packageId, "RoyalTerminal.GhosttySharp.Native.Linux64");
        AssertRuntimeDependency(runtimes, "linux-x64", packageId, "RoyalTerminal.GhosttySharp.Native.Linux64");
        AssertRuntimeDependency(runtimes, "osx-arm64", packageId, "RoyalTerminal.GhosttySharp.Native.OSX");
        AssertRuntimeDependency(runtimes, "osx-x64", packageId, "RoyalTerminal.GhosttySharp.Native.OSX");
        AssertRuntimeDependency(runtimes, "win-arm64", packageId, "RoyalTerminal.GhosttySharp.Native.Win64");
        AssertRuntimeDependency(runtimes, "win-x64", packageId, "RoyalTerminal.GhosttySharp.Native.Win64");

        Assert.Equal(6, runtimes.EnumerateObject().Count());
        Assert.Contains(
            target.Descendants(),
            element => string.Equals(
                element.Name.LocalName,
                "_RoyalTerminalGhosttyNativePackageVersion",
                StringComparison.Ordinal)
                    && string.Equals(element.Value, "$(PackageVersion)", StringComparison.Ordinal));
    }

    private static void AssertRuntimeDependency(
        JsonElement runtimes,
        string rid,
        string packageId,
        string nativePackageId)
    {
        Assert.True(runtimes.TryGetProperty(rid, out JsonElement runtime), $"Missing runtime '{rid}'.");
        Assert.True(runtime.TryGetProperty(packageId, out JsonElement package), $"Missing package '{packageId}'.");
        Assert.True(
            package.TryGetProperty(nativePackageId, out JsonElement nativePackageVersion),
            $"Missing native package '{nativePackageId}'.");
        Assert.Equal("0.0.0-test", nativePackageVersion.GetString());
        Assert.Single(package.EnumerateObject());
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
