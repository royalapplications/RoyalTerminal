// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.App.Services.Links;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class NativeHyperlinkFileProbeTests
{
    [Fact]
    public async Task RegularDocumentAndOrdinaryDirectoryCanOpen()
    {
        using Fixture fixture = new();
        string path = fixture.Write("notes.txt", "Ordinary notes");
        TerminalHyperlinkRequest file = await Inspect(path);
        TerminalHyperlinkRequest directory = await Inspect(fixture.Root);
        Assert.Equal(TerminalHyperlinkDisposition.Allow, file.Disposition);
        Assert.Equal(TerminalHyperlinkDisposition.Allow, directory.Disposition);
        Assert.Equal(file.DisplayText, file.Uri!.LocalPath);
        Assert.EndsWith("notes.txt", file.DisplayText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("command", "Ordinary text")]
    [InlineData("desktop", "[Desktop Entry]\nType=Application\nExec=false")]
    [InlineData("sh", "#!/bin/sh\necho example")]
    [InlineData("py", "print('example')")]
    public async Task ContainersAndNonExecutableScriptsCannotOpen(string extension, string content)
    {
        using Fixture fixture = new();
        string path = fixture.Write("payload." + extension, content);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        TerminalHyperlinkRequest result = await Inspect(path);
        Assert.Equal(TerminalHyperlinkDisposition.Deny, result.Disposition);
        Assert.Equal(TerminalHyperlinkDenialReason.UnsafeFile, result.DenialReason);
    }

    [Fact]
    public async Task ApplicationDirectoryCannotOpenEvenWithoutExecutablePermission()
    {
        using Fixture fixture = new();
        string path = Path.Combine(fixture.Root, "Payload.app");
        Directory.CreateDirectory(path);
        TerminalHyperlinkRequest result = await Inspect(path);
        Assert.Equal(TerminalHyperlinkDenialReason.UnsafeFile, result.DenialReason);
    }

    [Fact]
    public async Task UnixExecutableBitOverridesInnocentName()
    {
        if (OperatingSystem.IsWindows()) return;
        using Fixture fixture = new();
        string path = fixture.Write("notes.txt", "Ordinary text");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Equal(TerminalHyperlinkDenialReason.UnsafeFile, (await Inspect(path)).DenialReason);
    }

    [Fact]
    public async Task UnixSymlinkClassificationAndPreviewUseEffectiveObject()
    {
        if (OperatingSystem.IsWindows()) return;
        using Fixture fixture = new();
        string payload = fixture.Write("payload.command", "Ordinary text");
        string alias = Path.Combine(fixture.Root, "notes.txt");
        File.CreateSymbolicLink(alias, payload);
        TerminalHyperlinkRequest result = await Inspect(alias);
        Assert.Equal(TerminalHyperlinkDenialReason.UnsafeFile, result.DenialReason);
        Assert.EndsWith("payload.command", result.DisplayText, StringComparison.Ordinal);
        Assert.Equal(new Uri(alias).AbsoluteUri, result.Target);
    }

    [Fact]
    public async Task UnixMissingLeafStillDisplaysResolvedParent()
    {
        if (OperatingSystem.IsWindows()) return;
        using Fixture fixture = new();
        string actual = Path.Combine(fixture.Root, "actual");
        Directory.CreateDirectory(actual);
        string alias = Path.Combine(fixture.Root, "alias");
        Directory.CreateSymbolicLink(alias, actual);
        TerminalHyperlinkRequest result = await Inspect(Path.Combine(alias, "missing.txt"));
        Assert.Equal(TerminalHyperlinkDenialReason.InaccessibleFile, result.DenialReason);
        Assert.EndsWith("/actual/missing.txt", result.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnixFifoIsRejectedWithoutWaitingForAWriter()
    {
        if (OperatingSystem.IsWindows()) return;
        using Fixture fixture = new();
        string path = Path.Combine(fixture.Root, "fifo");
        Assert.Equal(0, mkfifo(path, 0x180 /* 0600 */));
        TerminalHyperlinkRequest result = await Inspect(path).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TerminalHyperlinkDenialReason.InaccessibleFile, result.DenialReason);
    }

    [Fact]
    public async Task MissingTargetCannotOpen()
    {
        using Fixture fixture = new();
        Assert.Equal(TerminalHyperlinkDenialReason.InaccessibleFile, (await Inspect(Path.Combine(fixture.Root, "missing.txt"))).DenialReason);
    }

    [Fact]
    public async Task WindowsExecutableRenamedAsTextIsStillBlocked()
    {
        if (!OperatingSystem.IsWindows()) return;
        using Fixture fixture = new();
        string source = Environment.ProcessPath ?? throw new InvalidOperationException("Test process image is unavailable.");
        string path = Path.Combine(fixture.Root, "notes.txt");
        File.Copy(source, path);
        Assert.Equal(TerminalHyperlinkDenialReason.UnsafeFile, (await Inspect(path)).DenialReason);
    }

    [Fact]
    public async Task WindowsLongLocalPathUsesExtendedLengthInspection()
    {
        if (!OperatingSystem.IsWindows()) return;
        using Fixture fixture = new();
        string directory = Path.Combine(fixture.Root, new string('a', 120), new string('b', 120));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "notes.txt");
        File.WriteAllText(path, "Ordinary notes");
        TerminalHyperlinkRequest result = await Inspect(path);
        Assert.Equal(TerminalHyperlinkDisposition.Allow, result.Disposition);
        Assert.Equal(path, result.Uri!.LocalPath);
    }

    private static Task<TerminalHyperlinkRequest> Inspect(string path)
        => new DesktopHyperlinkFileInspector(new NativeHyperlinkFileProbe()).InspectAsync(
            TerminalHyperlinkSafety.Classify(new Uri(path).AbsoluteUri), CancellationToken.None).AsTask();

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("royalterminal-hyperlinks-").FullName;
        internal string Write(string name, string text)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, text);
            return path;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] private static partial int mkfifo(string path, uint mode);
}
