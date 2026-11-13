// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using Xunit;

namespace RoyalTerminal.Tests;

[Collection("ProjectBuildTests")]
public sealed class WindowsNotificationBridgeBuildTests
{
    [Fact]
    public async Task ConcurrentBuildsCompileOnceAndRebuildAfterHeaderChanges()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "The notification bridge requires the Windows SDK.");
        string vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        Assert.SkipWhen(!File.Exists(vswhere), "Visual C++ build tools are not installed.");

        string repositoryRoot = FindRepositoryRoot();
        string testRoot = Path.Combine(repositoryRoot, "test-results", "notification-build", Guid.NewGuid().ToString("N"));
        string scriptDirectory = Path.Combine(testRoot, "scripts");
        string sourceDirectory = Path.Combine(testRoot, "native", "windows-notifications");
        Directory.CreateDirectory(scriptDirectory);
        Directory.CreateDirectory(sourceDirectory);
        try
        {
            string script = Path.Combine(scriptDirectory, "build-windows-notifications.ps1");
            File.Copy(Path.Combine(repositoryRoot, "scripts", "build-windows-notifications.ps1"), script);
            foreach (string source in new[] { "notifications.cpp", "notification-platform.h" })
            {
                File.Copy(Path.Combine(repositoryRoot, "native", "windows-notifications", source), Path.Combine(sourceDirectory, source));
            }

            // Separate processes model framework builds sharing the same native outputs.
            string[] firstBuilds = await Task.WhenAll(RunBuildAsync(script), RunBuildAsync(script), RunBuildAsync(script));
            Assert.Equal(1, CountCompilerInvocations(firstBuilds));
            string binary = Path.Combine(testRoot, "src", "RoyalTerminal.Avalonia.App", "runtimes", "win-x64", "native", "royalterminal-notifications.dll");
            Assert.True(File.Exists(binary));
            DateTime originalTimestamp = File.GetLastWriteTimeUtc(binary);

            string[] unchangedBuilds = await Task.WhenAll(RunBuildAsync(script), RunBuildAsync(script));
            Assert.Equal(0, CountCompilerInvocations(unchangedBuilds));
            Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(binary));

            File.SetLastWriteTimeUtc(Path.Combine(sourceDirectory, "notification-platform.h"), DateTime.UtcNow);
            string[] changedBuilds = await Task.WhenAll(RunBuildAsync(script), RunBuildAsync(script));
            Assert.Equal(1, CountCompilerInvocations(changedBuilds));
            Assert.True(File.GetLastWriteTimeUtc(binary) > originalTimestamp);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async Task<string> RunBuildAsync(string script)
    {
        ProcessStartInfo startInfo = new("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[] { "-NoProfile", "-File", script, "-Arch", "x64" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        string output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, output);
        return output;
    }

    private static int CountCompilerInvocations(string[] outputs)
    {
        int count = 0;
        foreach (string output in outputs)
        {
            foreach (string line in output.Split('\n'))
            {
                if (line.Trim() == "notifications.cpp") count++;
            }
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoyalTerminal.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
