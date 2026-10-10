// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Native package targets copy only their own libraries, not other packages' NativeLibrary items.

using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class GhosttyNativeRuntimeDependencyTests
{
    private const string ForeignMetadataName = "RoyalTerminalTestForeign";

    private static readonly string[] NativeTargetsFolders = ["build", "buildTransitive"];

    private static readonly NativeTargetsPackage[] NativeTargetsPackages =
    [
        new("Win64", "Windows", ["win-x64", "win-arm64"], ["ghostty-vt.dll", "ghostty-renderer-capi.dll"]),
        new("OSX", "OSX", ["osx-x64", "osx-arm64"], ["libghostty-vt.dylib", "libghostty-renderer-capi.dylib"]),
        new("Linux64", "Linux", ["linux-x64", "linux-arm64"], ["libghostty-vt.so", "libghostty-renderer-capi.so"]),
    ];

    public static TheoryData<string, string, string> NativeTargetsSelections()
    {
        TheoryData<string, string, string> data = new();
        foreach (NativeTargetsPackage package in NativeTargetsPackages)
        {
            foreach (string folder in NativeTargetsFolders)
            {
                foreach (string rid in package.Rids)
                {
                    data.Add(package.Name, folder, rid);
                }
            }
        }

        return data;
    }

    // Item evaluation only. Each targets file is gated on the build host's OS, so the
    // gate is replaced with "true" to evaluate every package on every host. The real
    // gate and the real copy are covered by NativeTargets_HostBuild_CopiesOnlyOwnLibraries.
    [Theory]
    [MemberData(nameof(NativeTargetsSelections))]
    public void NativeTargets_Evaluation_CopiesOnlyOwnLibraries(string packageName, string folder, string rid)
    {
        NativeTargetsPackage package = NativeTargetsPackages.Single(candidate => candidate.Name == packageName);
        string repoRoot = FindRepositoryRoot();
        string testRoot = CreateNativeTargetsTestRoot();

        try
        {
            string hostGate = $"Condition=\"$([MSBuild]::IsOSPlatform('{package.HostOs}'))\"";
            string targets = File.ReadAllText(package.TargetsPath(repoRoot, folder));
            Assert.Contains(hostGate, targets, StringComparison.Ordinal);
            string targetsPath = WriteNativeTargetsFixture(
                testRoot, package, folder, targets.Replace(hostGate, "Condition=\"true\"", StringComparison.Ordinal));
            ForeignInputs foreign = WriteForeignInputs(testRoot);
            string projectPath = WriteNativeTargetsConsumer(testRoot, targetsPath, foreign);

            using JsonDocument document = JsonDocument.Parse(RunDotNet(
                testRoot,
                "msbuild",
                projectPath,
                "-getItem:NativeLibrary",
                "-getItem:None",
                "-p:RuntimeIdentifier=" + rid));
            JsonElement items = document.RootElement.GetProperty("Items");
            string[] expectedOwn = package.Libraries
                .Select(library => Path.Combine("runtimes", rid, "native", library))
                .Order(StringComparer.Ordinal)
                .ToArray();

            JsonElement[] nativeLibraries = ReadItems(items, "NativeLibrary");
            Assert.Equal(
                expectedOwn,
                nativeLibraries
                    .Where(static item => !IsForeign(item))
                    .Select(PackageRelativePath)
                    .Order(StringComparer.Ordinal));
            Assert.Equal(
                foreign.Identities.Order(StringComparer.Ordinal),
                nativeLibraries
                    .Where(static item => IsForeign(item))
                    .Select(static item => Metadata(item, "Identity"))
                    .Order(StringComparer.Ordinal));

            JsonElement[] copied = ReadItems(items, "None")
                .Where(static item => Metadata(item, "CopyToOutputDirectory").Length > 0)
                .OrderBy(PackageRelativePath, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedOwn, copied.Select(PackageRelativePath));
            foreach (JsonElement item in copied)
            {
                Assert.Equal("PreserveNewest", Metadata(item, "CopyToOutputDirectory"));
                Assert.Equal(Metadata(item, "Filename") + Metadata(item, "Extension"), Metadata(item, "Link"));
                Assert.Equal("false", Metadata(item, "Visible"));
            }
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    // A real build on this host, with the targets files unchanged. CI runs it on
    // Windows, macOS and Linux, so each package's own host gate is exercised there.
    [Theory]
    [InlineData("build")]
    [InlineData("buildTransitive")]
    public void NativeTargets_HostBuild_CopiesOnlyOwnLibraries(string folder)
    {
        NativeTargetsPackage package = NativeTargetsPackages.Single(
            static candidate => OperatingSystem.IsOSPlatform(candidate.HostOs));
        string hostRid = package.Rids.Single(static rid => rid.EndsWith(
            "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            StringComparison.Ordinal));
        string repoRoot = FindRepositoryRoot();
        string testRoot = CreateNativeTargetsTestRoot();

        try
        {
            string targetsPath = WriteNativeTargetsFixture(
                testRoot, package, folder, File.ReadAllText(package.TargetsPath(repoRoot, folder)));
            ForeignInputs foreign = WriteForeignInputs(testRoot);
            string projectPath = WriteNativeTargetsConsumer(testRoot, targetsPath, foreign);
            string outputDirectory = Path.Combine(testRoot, "out");

            // Before the fix, the bare foreign inputs failed this build with MSB3030.
            RunDotNet(testRoot, "build", projectPath, "-o", outputDirectory);

            foreach (string library in package.Libraries)
            {
                // Each fixture library holds its RID, so this also checks the host architecture's copy won.
                Assert.Equal(hostRid, File.ReadAllText(Path.Combine(outputDirectory, library)));
            }

            foreach (string foreignFile in foreign.Identities.Select(static identity => Path.GetFileName(identity)))
            {
                Assert.False(
                    File.Exists(Path.Combine(outputDirectory, foreignFile)),
                    $"Did not expect foreign NativeLibrary input '{foreignFile}' in the build output.");
            }
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string CreateNativeTargetsTestRoot()
    {
        string testRoot = Path.Combine(
            Path.GetTempPath(), "RoyalTerminal.NativeTargetsCopy." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        return testRoot;
    }

    private static string WriteNativeTargetsFixture(
        string testRoot,
        NativeTargetsPackage package,
        string folder,
        string targets)
    {
        string packageRoot = Path.Combine(testRoot, "package");
        foreach (string rid in package.Rids)
        {
            string nativeDirectory = Path.Combine(packageRoot, "runtimes", rid, "native");
            Directory.CreateDirectory(nativeDirectory);
            foreach (string library in package.Libraries)
            {
                File.WriteAllText(Path.Combine(nativeDirectory, library), rid);
            }
        }

        string targetsDirectory = Path.Combine(packageRoot, folder);
        Directory.CreateDirectory(targetsDirectory);
        string targetsPath = Path.Combine(targetsDirectory, Path.GetFileName(package.TargetsPath(string.Empty, folder)));
        File.WriteAllText(targetsPath, targets);
        return targetsPath;
    }

    // Linker inputs another package (such as Sentry) adds to NativeLibrary: bare system
    // import libraries that are not files, and static libraries that are. Half are declared
    // before the native targets are imported and half after.
    private static ForeignInputs WriteForeignInputs(string testRoot)
    {
        string foreignDirectory = Path.Combine(testRoot, "foreign");
        Directory.CreateDirectory(foreignDirectory);
        string sentry = Path.Combine(foreignDirectory, "libsentry-native.a");
        string unwind = Path.Combine(foreignDirectory, "libunwind.a");
        File.WriteAllText(sentry, "foreign");
        File.WriteAllText(unwind, "foreign");
        return new ForeignInputs(["dbghelp.lib", sentry], ["winhttp.lib", unwind]);
    }

    private static string WriteNativeTargetsConsumer(string testRoot, string targetsPath, ForeignInputs foreign)
    {
        string consumerDirectory = Path.Combine(testRoot, "consumer");
        Directory.CreateDirectory(consumerDirectory);
        string projectPath = Path.Combine(consumerDirectory, "Consumer.csproj");
        File.WriteAllText(
            projectPath,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{{ConsumerTargetFramework}}</TargetFramework>
                <OutputType>Library</OutputType>
                <EnableDefaultItems>false</EnableDefaultItems>
              </PropertyGroup>
              <ItemGroup>
            {{ForeignItems(foreign.BeforeImport)}}
              </ItemGroup>
              <Import Project="{{targetsPath}}" />
              <ItemGroup>
            {{ForeignItems(foreign.AfterImport)}}
              </ItemGroup>
            </Project>
            """);
        return projectPath;
    }

    private static string ForeignItems(string[] identities) => string.Join(
        Environment.NewLine,
        identities.Select(static identity =>
            $"    <NativeLibrary Include=\"{identity}\" {ForeignMetadataName}=\"true\" />"));

    private static JsonElement[] ReadItems(JsonElement items, string itemType) =>
        items.TryGetProperty(itemType, out JsonElement list) ? list.EnumerateArray().ToArray() : [];

    private static bool IsForeign(JsonElement item) => Metadata(item, ForeignMetadataName) == "true";

    private static string Metadata(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;

    // The path from the fixture package root, e.g. runtimes/win-x64/native/ghostty-vt.dll. Comparing
    // from there avoids differences in how the temp directory itself is spelled (8.3 names, symlinks).
    private static string PackageRelativePath(JsonElement item)
    {
        string fullPath = Metadata(item, "FullPath");
        string runtimes = Path.DirectorySeparatorChar + "runtimes" + Path.DirectorySeparatorChar;
        int start = fullPath.LastIndexOf(runtimes, StringComparison.Ordinal);
        return start < 0 ? fullPath : fullPath[(start + 1)..];
    }

    private sealed record NativeTargetsPackage(string Name, string HostOs, string[] Rids, string[] Libraries)
    {
        public string TargetsPath(string repoRoot, string folder) => Path.Combine(
            repoRoot,
            "src",
            "RoyalTerminal.GhosttySharp.Native." + Name,
            folder,
            "RoyalTerminal.GhosttySharp.Native." + Name + ".targets");
    }

    private sealed record ForeignInputs(string[] BeforeImport, string[] AfterImport)
    {
        public IEnumerable<string> Identities => BeforeImport.Concat(AfterImport);
    }
}
