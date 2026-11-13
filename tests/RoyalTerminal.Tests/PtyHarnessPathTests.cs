// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Xunit;

namespace RoyalTerminal.Tests;

public sealed class PtyHarnessPathTests
{
    [Theory]
    [InlineData("Debug/net10.0")]
    [InlineData("Release/net10.0")]
    [InlineData("Release/net10.0/linux-x64")]
    [InlineData("Release/net10.0/osx-arm64")]
    [InlineData("Debug/net10.0/osx-x64/")]
    public void ResolvePreservesConfigurationFrameworkAndRuntimeIdentifier(string outputSuffix)
    {
        string repository = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "royal-harness-path-tests"));
        string suffix = outputSuffix.Replace('/', Path.DirectorySeparatorChar);
        string testOutput = Path.Combine(repository, "tests", "RoyalTerminal.Tests", "bin", suffix);
        string executableName = OperatingSystem.IsWindows() ? "RoyalTerminal.PtyHarness.exe" : "RoyalTerminal.PtyHarness";
        string expected = Path.Combine(repository, "tests", "RoyalTerminal.PtyHarness", "bin", suffix, executableName);

        Assert.Equal(expected, PtyHarnessPath.Resolve(repository, testOutput));
    }

    [Fact]
    public void ResolveRejectsOutputOutsideTheTestBinDirectory()
    {
        string repository = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "royal-harness-path-tests"));
        string testOutput = Path.Combine(repository, "artifacts", "Release", "net10.0");

        Assert.Throws<ArgumentException>(() => PtyHarnessPath.Resolve(repository, testOutput));
    }
}
