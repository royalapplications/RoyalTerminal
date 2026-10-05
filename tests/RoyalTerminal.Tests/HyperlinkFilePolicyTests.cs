// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.App.Services.Links;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class HyperlinkFilePolicyTests
{
    [Theory]
    [InlineData("action")]
    [InlineData("app")]
    [InlineData("applescript")]
    [InlineData("class")]
    [InlineData("command")]
    [InlineData("desktop")]
    [InlineData("inetloc")]
    [InlineData("jar")]
    [InlineData("mobileconfig")]
    [InlineData("mpkg")]
    [InlineData("pkg")]
    [InlineData("scpt")]
    [InlineData("terminal")]
    [InlineData("tool")]
    [InlineData("url")]
    [InlineData("webloc")]
    [InlineData("workflow")]
    public void GhosttyDangerousContainersAreBlockedCaseInsensitively(string extension)
    {
        Assert.True(HyperlinkFilePolicy.IsUnsafeExtension("payload." + extension, windows: false));
        Assert.True(HyperlinkFilePolicy.IsUnsafeExtension("payload." + extension.ToUpperInvariant(), windows: false));
        Assert.True(HyperlinkFilePolicy.IsUnsafeExtension("payload." + extension, windows: true));
    }

    [Theory]
    [InlineData("exe")]
    [InlineData("dll")]
    [InlineData("com")]
    [InlineData("bat")]
    [InlineData("cmd")]
    [InlineData("ps1")]
    [InlineData("vbs")]
    [InlineData("js")]
    [InlineData("hta")]
    [InlineData("lnk")]
    [InlineData("msi")]
    [InlineData("appref-ms")]
    [InlineData("py")]
    [InlineData("sh")]
    public void WindowsExecutableAndScriptAssociationsAreBlocked(string extension)
        => Assert.True(HyperlinkFilePolicy.IsUnsafeExtension("payload." + extension, windows: true));

    [Theory]
    [InlineData((int)HyperlinkFileKind.Regular, false, false, "note.txt", TerminalHyperlinkDenialReason.None)]
    [InlineData((int)HyperlinkFileKind.Directory, true, false, "folder", TerminalHyperlinkDenialReason.None)]
    [InlineData((int)HyperlinkFileKind.Regular, true, false, "note.txt", TerminalHyperlinkDenialReason.UnsafeFile)]
    [InlineData((int)HyperlinkFileKind.Regular, false, true, "note.txt", TerminalHyperlinkDenialReason.UnsafeFile)]
    [InlineData((int)HyperlinkFileKind.Directory, true, true, "folder", TerminalHyperlinkDenialReason.UnsafeFile)]
    [InlineData((int)HyperlinkFileKind.Directory, true, false, "Editor.app", TerminalHyperlinkDenialReason.UnsafeFile)]
    [InlineData((int)HyperlinkFileKind.Other, false, false, "fifo", TerminalHyperlinkDenialReason.InaccessibleFile)]
    public void CanonicalMetadataControlsDispatch(int kind, bool executable, bool unsafeType,
        string name, TerminalHyperlinkDenialReason reason)
    {
        const string target = "file:///innocent.txt";
        string canonical = "/resolved/" + name;
        TerminalHyperlinkRequest result = HyperlinkFilePolicy.Apply(TerminalHyperlinkSafety.Classify(target),
            new(canonical, (HyperlinkFileKind)kind, executable, unsafeType), windows: false);
        Assert.Equal(reason, result.DenialReason);
        Assert.Equal(reason == TerminalHyperlinkDenialReason.None ? TerminalHyperlinkDisposition.Allow : TerminalHyperlinkDisposition.Deny, result.Disposition);
        Assert.Equal(target, result.Target);
        Assert.Equal(canonical, result.DisplayText);
    }

    [Theory]
    [InlineData("/tmp/a b#c?d%20é😀.txt", false)]
    [InlineData("/tmp/a\u202Eb.txt", false)]
    [InlineData("C:\\Temp\\a b#c%20é😀.txt", true)]
    public void CanonicalPathEscapingPreservesReservedCharacters(string path, bool windows)
    {
        TerminalHyperlinkRequest result = HyperlinkFilePolicy.Apply(TerminalHyperlinkSafety.Classify("file:///alias"),
            new(path, HyperlinkFileKind.Regular, false, false), windows);
        Assert.Equal(TerminalHyperlinkDisposition.Allow, result.Disposition);
        if (!windows || OperatingSystem.IsWindows()) Assert.Equal(path, result.Uri!.LocalPath);
        else Assert.Equal(path.Replace('\\', '/'), Uri.UnescapeDataString(result.Uri!.AbsolutePath.TrimStart('/')));
        Assert.Equal(string.Empty, result.Uri.Query);
        Assert.Equal(string.Empty, result.Uri.Fragment);
        Assert.Equal(TerminalHyperlinkSafety.SanitizeDisplay(path), result.DisplayText);
    }

    [Theory]
    [InlineData("relative", false)]
    [InlineData("//remote/path", false)]
    [InlineData("/tmp/a\0b", false)]
    [InlineData("\\\\server\\share\\a", true)]
    [InlineData("\\\\?\\C:\\Temp\\a", true)]
    [InlineData("C:relative", true)]
    [InlineData("C:\\Temp\\file.txt:payload.exe", true)]
    [InlineData("C:\\Temp\\NUL", true)]
    [InlineData("C:\\Temp\\NUL.txt", true)]
    [InlineData("C:\\Temp\\COM1", true)]
    [InlineData("C:\\Temp\\LPT\u00B2.txt", true)]
    [InlineData("C:\\Temp\\con .txt", true)]
    [InlineData("C:\\Temp\\CONIN$", true)]
    [InlineData("C:\\Temp\\payload.cmd.", true)]
    [InlineData("C:\\Temp\\payload.cmd ", true)]
    public void NonLocalOrAmbiguousPathsCannotReachLaunch(string path, bool windows)
    {
        Assert.False(HyperlinkFilePolicy.IsLocalPath(path, windows));
        TerminalHyperlinkRequest result = HyperlinkFilePolicy.Apply(TerminalHyperlinkSafety.Classify("file:///alias"),
            new(path, HyperlinkFileKind.Regular, false, false), windows);
        Assert.Equal(TerminalHyperlinkDisposition.Deny, result.Disposition);
    }

    [Theory]
    [InlineData("text/x-python", true)]
    [InlineData("application/x-shellscript", true)]
    [InlineData("text/javascript", true)]
    [InlineData("text/plain", false)]
    [InlineData("application/pdf", false)]
    [InlineData(null, false)]
    public void ScriptMimeFamiliesDoNotRejectOrdinaryText(string? type, bool expected)
        => Assert.Equal(expected, LinuxHyperlinkFileProbe.IsScriptType(type));
}
