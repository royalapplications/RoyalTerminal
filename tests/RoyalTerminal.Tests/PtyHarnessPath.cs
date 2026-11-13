// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Tests;

/// <summary>Maps the test project's SDK output layout to the matching harness output.</summary>
internal static class PtyHarnessPath
{
    internal static string Resolve(string repositoryDirectory, string testOutputDirectory)
    {
        string testsDirectory = Path.Combine(repositoryDirectory, "tests");
        string testBinDirectory = Path.Combine(testsDirectory, "RoyalTerminal.Tests", "bin");
        string outputSuffix = Path.GetRelativePath(testBinDirectory, testOutputDirectory);
        if (Path.IsPathRooted(outputSuffix) || outputSuffix == ".." ||
            outputSuffix.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Test output must be inside the test project's bin directory.", nameof(testOutputDirectory));

        string executableName = OperatingSystem.IsWindows() ? "RoyalTerminal.PtyHarness.exe" : "RoyalTerminal.PtyHarness";
        // Preserve the entire suffix: configuration/framework[/runtime identifier].
        return Path.GetFullPath(Path.Combine(testsDirectory, "RoyalTerminal.PtyHarness", "bin", outputSuffix, executableName));
    }
}
