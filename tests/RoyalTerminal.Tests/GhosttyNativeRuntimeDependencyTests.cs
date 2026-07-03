// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - NuGet runtime.json metadata coverage for Ghostty native assets.

using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyNativeRuntimeDependencyTests
{
    private const string ConsumerTargetFramework = "net10.0";

    private static readonly (string Rid, string ExpectedNativePackage)[] RuntimePackageSelections =
    [
        ("linux-arm64", "RoyalApps.RoyalTerminal.GhosttySharp.Native.Linux64"),
        ("linux-x64", "RoyalApps.RoyalTerminal.GhosttySharp.Native.Linux64"),
        ("osx-arm64", "RoyalApps.RoyalTerminal.GhosttySharp.Native.OSX"),
        ("osx-x64", "RoyalApps.RoyalTerminal.GhosttySharp.Native.OSX"),
        ("win-arm64", "RoyalApps.RoyalTerminal.GhosttySharp.Native.Win64"),
        ("win-x64", "RoyalApps.RoyalTerminal.GhosttySharp.Native.Win64"),
    ];

    private static readonly string[] ManagedRuntimePackages =
    [
        "RoyalApps.RoyalTerminal.GhosttySharp",
        "RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty",
    ];

    private static readonly string[] ExternalPackageStubs =
    [
        "SkiaSharp",
        "SkiaSharp.NativeAssets.Linux",
        "SkiaSharp.NativeAssets.macOS",
        "SkiaSharp.NativeAssets.WebAssembly",
        "SkiaSharp.NativeAssets.Win32",
        "System.Security.Cryptography.ProtectedData",
    ];

    [Theory]
    [InlineData("RoyalApps.RoyalTerminal.GhosttySharp", "src/RoyalTerminal.GhosttySharp")]
    [InlineData("RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty", "src/RoyalTerminal.Rendering.Interop.Ghostty")]
    public void PackageProject_OptsIntoGeneratedRuntimeJson(string packageId, string projectDirectory)
    {
        string repoRoot = FindRepositoryRoot();
        string projectName = Path.GetFileName(projectDirectory);
        string projectPath = Path.Combine(repoRoot, projectDirectory, projectName + ".csproj");

        XDocument project = XDocument.Load(projectPath);
        XElement optInProperty = Assert.Single(
            project.Descendants(),
            element => element.Name.LocalName == "RoyalTerminalGenerateGhosttyNativeRuntimeJson");
        XElement packageIdProperty = Assert.Single(
            project.Descendants(),
            element => element.Name.LocalName == "PackageId");

        Assert.Equal(packageId, packageIdProperty.Value);
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
            element => element.Name.LocalName == "MakeDir"
                    && string.Equals(
                        element.Attribute("Directories")?.Value,
                        "$(IntermediateOutputPath)",
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

    [Fact]
    public void AvaloniaAppPackage_DoesNotReferencePlatformNativeProjects()
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.Avalonia.App",
            "RoyalTerminal.Avalonia.App.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] projectReferenceIncludes = project
            .Descendants()
            .Where(static element => element.Name.LocalName == "ProjectReference")
            .Select(static element => element.Attribute("Include")?.Value)
            .Where(static include => include is not null)
            .Select(static include => include!)
            .ToArray();
        string[] importProjects = project
            .Descendants()
            .Where(static element => element.Name.LocalName == "Import")
            .Select(static element => element.Attribute("Project")?.Value)
            .Where(static import => import is not null)
            .Select(static import => import!)
            .ToArray();

        Assert.Contains(
            projectReferenceIncludes,
            include => include.Contains("RoyalTerminal.Terminal.Vt.Ghostty", StringComparison.Ordinal));
        Assert.DoesNotContain(
            projectReferenceIncludes,
            include => include.Contains("RoyalTerminal.GhosttySharp.Native", StringComparison.Ordinal));
        Assert.DoesNotContain(
            importProjects,
            import => import.Contains("RoyalTerminal.GhosttySharp.Native", StringComparison.Ordinal));
    }

    [Fact]
    public void AvaloniaAppPackage_DoesNotOwnDesktopOrFluentThemeBootstrap()
    {
        string repoRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.Avalonia.App",
            "RoyalTerminal.Avalonia.App.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] packageReferences = project
            .Descendants()
            .Where(static element => element.Name.LocalName == "PackageReference")
            .Select(static element => element.Attribute("Include")?.Value)
            .Where(static packageId => packageId is not null)
            .Select(static packageId => packageId!)
            .ToArray();

        Assert.DoesNotContain("Avalonia.Desktop", packageReferences);
        Assert.DoesNotContain("Avalonia.Themes.Fluent", packageReferences);
    }

    [Theory]
    [InlineData("RoyalApps.RoyalTerminal.GhosttySharp")]
    [InlineData("RoyalApps.RoyalTerminal.Rendering.Interop.Ghostty")]
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
        AssertRuntimeDependency(runtimes, "linux-arm64", packageId, "RoyalApps.RoyalTerminal.GhosttySharp.Native.Linux64");
        AssertRuntimeDependency(runtimes, "linux-x64", packageId, "RoyalApps.RoyalTerminal.GhosttySharp.Native.Linux64");
        AssertRuntimeDependency(runtimes, "osx-arm64", packageId, "RoyalApps.RoyalTerminal.GhosttySharp.Native.OSX");
        AssertRuntimeDependency(runtimes, "osx-x64", packageId, "RoyalApps.RoyalTerminal.GhosttySharp.Native.OSX");
        AssertRuntimeDependency(runtimes, "win-arm64", packageId, "RoyalApps.RoyalTerminal.GhosttySharp.Native.Win64");
        AssertRuntimeDependency(runtimes, "win-x64", packageId, "RoyalApps.RoyalTerminal.GhosttySharp.Native.Win64");

        Assert.Equal(6, runtimes.EnumerateObject().Count());
        Assert.Contains(
            target.Descendants(),
            element => string.Equals(
                element.Name.LocalName,
                "_RoyalTerminalGhosttyNativePackageVersion",
                StringComparison.Ordinal)
                    && string.Equals(element.Value, "$(PackageVersion)", StringComparison.Ordinal));
    }

    [Fact]
    public void NuGetRestore_SelectsExpectedNativePackageForEverySupportedRid()
    {
        string repoRoot = FindRepositoryRoot();
        string packageVersion = ReadPackageVersion(repoRoot);
        string testRoot = Path.Combine(Path.GetTempPath(), "RoyalTerminal.NuGetRuntimeRestore." + Guid.NewGuid().ToString("N"));
        string feedDirectory = Path.Combine(testRoot, "feed");
        string packageCacheDirectory = Path.Combine(testRoot, "packages");

        try
        {
            Directory.CreateDirectory(feedDirectory);
            Directory.CreateDirectory(packageCacheDirectory);

            foreach (string packageId in ExternalPackageStubs)
            {
                PackStubPackage(
                    testRoot,
                    feedDirectory,
                    packageId,
                    ReadCentralPackageVersion(repoRoot, packageId));
            }

            string appHostPackageVersion = ReadAppHostPackageVersion(repoRoot);
            foreach ((string rid, _) in RuntimePackageSelections)
            {
                PackStubPackage(
                    testRoot,
                    feedDirectory,
                    "Microsoft.NETCore.App.Host." + rid,
                    appHostPackageVersion);
            }

            PackProject(repoRoot, feedDirectory, "src/RoyalTerminal.Rendering.Contracts/RoyalTerminal.Rendering.Contracts.csproj");
            PackProject(repoRoot, feedDirectory, "src/RoyalTerminal.Terminal/RoyalTerminal.Terminal.csproj");
            PackProject(
                repoRoot,
                feedDirectory,
                "src/RoyalTerminal.GhosttySharp.Native.Linux64/RoyalTerminal.GhosttySharp.Native.Linux64.csproj");
            PackProject(
                repoRoot,
                feedDirectory,
                "src/RoyalTerminal.GhosttySharp.Native.OSX/RoyalTerminal.GhosttySharp.Native.OSX.csproj");
            PackProject(
                repoRoot,
                feedDirectory,
                "src/RoyalTerminal.GhosttySharp.Native.Win64/RoyalTerminal.GhosttySharp.Native.Win64.csproj");
            PackProject(repoRoot, feedDirectory, "src/RoyalTerminal.GhosttySharp/RoyalTerminal.GhosttySharp.csproj");
            PackProject(
                repoRoot,
                feedDirectory,
                "src/RoyalTerminal.Rendering.Interop.Ghostty/RoyalTerminal.Rendering.Interop.Ghostty.csproj");

            foreach (string managedPackage in ManagedRuntimePackages)
            {
                foreach ((string rid, string expectedNativePackage) in RuntimePackageSelections)
                {
                    string consumerDirectory = Path.Combine(testRoot, "consumer-" + managedPackage + "-" + rid);
                    Directory.CreateDirectory(consumerDirectory);
                    string consumerProjectPath = Path.Combine(consumerDirectory, "Consumer.csproj");
                    File.WriteAllText(
                        consumerProjectPath,
                        CreateConsumerProject(managedPackage, packageVersion));

                    RunDotNet(
                        repoRoot,
                        "restore",
                        consumerProjectPath,
                        "-r",
                        rid,
                        "--packages",
                        packageCacheDirectory,
                        "--source",
                        feedDirectory,
                        "--no-cache",
                        "--force-evaluate");

                    string assetsPath = Path.Combine(consumerDirectory, "obj", "project.assets.json");
                    Assert.True(
                        File.Exists(assetsPath),
                        $"Restore did not produce assets file for '{managedPackage}' and '{rid}'.");

                    using FileStream stream = File.OpenRead(assetsPath);
                    using JsonDocument assets = JsonDocument.Parse(stream);
                    AssertRestoredRuntimePackage(
                        assets.RootElement,
                        managedPackage,
                        rid,
                        expectedNativePackage,
                        packageVersion);
                }
            }
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
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

    private static void AssertRestoredRuntimePackage(
        JsonElement assets,
        string managedPackage,
        string rid,
        string expectedNativePackage,
        string packageVersion)
    {
        string managedLibraryName = managedPackage + "/" + packageVersion;
        string expectedLibraryName = expectedNativePackage + "/" + packageVersion;
        JsonElement libraries = assets.GetProperty("libraries");
        Assert.True(
            libraries.TryGetProperty(managedLibraryName, out _),
            $"Expected '{managedLibraryName}' in restored NuGet libraries for '{rid}'.");
        Assert.True(
            libraries.TryGetProperty(expectedLibraryName, out _),
            $"Expected '{expectedLibraryName}' in restored NuGet libraries for '{rid}'.");

        foreach (string nativePackage in RuntimePackageSelections
            .Select(static selection => selection.ExpectedNativePackage)
            .Distinct(StringComparer.Ordinal))
        {
            string libraryName = nativePackage + "/" + packageVersion;
            if (string.Equals(nativePackage, expectedNativePackage, StringComparison.Ordinal))
            {
                continue;
            }

            Assert.False(
                libraries.TryGetProperty(libraryName, out _),
                $"Did not expect '{libraryName}' in restored NuGet libraries for '{rid}'.");
        }

        JsonElement targets = assets.GetProperty("targets");
        JsonElement ridTarget = targets
            .EnumerateObject()
            .Single(property => property.Name.EndsWith("/" + rid, StringComparison.Ordinal))
            .Value;
        Assert.True(
            ridTarget.TryGetProperty(managedLibraryName, out _),
            $"Expected '{managedLibraryName}' in restored NuGet target graph for '{rid}'.");
        Assert.True(
            ridTarget.TryGetProperty(expectedLibraryName, out _),
            $"Expected '{expectedLibraryName}' in restored NuGet target graph for '{rid}'.");
    }

    private static void PackProject(string repoRoot, string outputDirectory, string projectRelativePath)
    {
        RunDotNet(
            repoRoot,
            "pack",
            Path.Combine(repoRoot, projectRelativePath),
            "-c",
            "Release",
            "-o",
            outputDirectory,
            "-p:RestoreSources=" + outputDirectory);
    }

    private static void PackStubPackage(string testRoot, string outputDirectory, string packageId, string packageVersion)
    {
        string projectDirectory = Path.Combine(testRoot, "stub-" + packageId);
        Directory.CreateDirectory(projectDirectory);
        string projectPath = Path.Combine(projectDirectory, packageId + ".csproj");
        File.WriteAllText(projectPath, CreateStubProject(packageId, packageVersion));
        File.WriteAllText(
            Path.Combine(projectDirectory, "Marker.cs"),
            """
            namespace RoyalTerminal.Tests.StubPackage;

            public sealed class Marker
            {
            }
            """);

        RunDotNet(
            testRoot,
            "pack",
            projectPath,
            "-c",
            "Release",
            "-o",
            outputDirectory);
    }

    private static string RunDotNet(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            string.Join(" ", startInfo.ArgumentList.ToArray()) + Environment.NewLine + output + error);

        return output;
    }

    private static string CreateConsumerProject(string packageId, string packageVersion)
    {
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{ConsumerTargetFramework}}</TargetFramework>
                <OutputType>Library</OutputType>
                <SelfContained>false</SelfContained>
                <UseAppHost>false</UseAppHost>
                <EnableRuntimePackDownload>false</EnableRuntimePackDownload>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{packageId}}" Version="{{packageVersion}}" />
              </ItemGroup>
            </Project>
            """;
    }

    private static string CreateStubProject(string packageId, string packageVersion)
    {
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{ConsumerTargetFramework}}</TargetFramework>
                <PackageId>{{packageId}}</PackageId>
                <Version>{{packageVersion}}</Version>
                <Authors>RoyalTerminal Tests</Authors>
                <Description>Local restore-only stub package for NuGet runtime dependency tests.</Description>
                <PackageLicenseExpression>MIT</PackageLicenseExpression>
                <IsPackable>true</IsPackable>
              </PropertyGroup>
            </Project>
            """;
    }

    private static string ReadPackageVersion(string repoRoot)
    {
        string propsPath = Path.Combine(repoRoot, "Directory.Build.props");
        XDocument props = XDocument.Load(propsPath);
        string versionPrefix = Assert.Single(
            props.Descendants(),
            element => element.Name.LocalName == "VersionPrefix").Value;
        string versionSuffix = Assert.Single(
            props.Descendants(),
            element => element.Name.LocalName == "VersionSuffix").Value;

        return string.IsNullOrWhiteSpace(versionSuffix)
            ? versionPrefix
            : versionPrefix + "-" + versionSuffix;
    }

    private static string ReadCentralPackageVersion(string repoRoot, string packageId)
    {
        string propsPath = Path.Combine(repoRoot, "Directory.Packages.props");
        XDocument props = XDocument.Load(propsPath);
        XElement packageVersion = Assert.Single(
            props.Descendants(),
            element => element.Name.LocalName == "PackageVersion"
                    && string.Equals(element.Attribute("Include")?.Value, packageId, StringComparison.Ordinal));

        return packageVersion.Attribute("Version")?.Value
            ?? throw new InvalidOperationException("PackageVersion is missing a Version attribute for " + packageId + ".");
    }

    private static string ReadAppHostPackageVersion(string repoRoot)
    {
        string output = RunDotNet(
            repoRoot,
            "msbuild",
            Path.Combine(repoRoot, "tests", "RoyalTerminal.Tests", "RoyalTerminal.Tests.csproj"),
            "-getItem:KnownAppHostPack");

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement knownAppHostPacks = document.RootElement.GetProperty("Items").GetProperty("KnownAppHostPack");
        JsonElement netCoreAppHostPack = Assert.Single(
            knownAppHostPacks.EnumerateArray(),
            item => string.Equals(
                    item.GetProperty("Identity").GetString(),
                    "Microsoft.NETCore.App",
                    StringComparison.Ordinal)
                && string.Equals(
                    item.GetProperty("TargetFramework").GetString(),
                    ConsumerTargetFramework,
                    StringComparison.Ordinal));

        return netCoreAppHostPack.GetProperty("AppHostPackVersion").GetString()
            ?? throw new InvalidOperationException(
                "KnownAppHostPack is missing AppHostPackVersion for " + ConsumerTargetFramework + ".");
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
