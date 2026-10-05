// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalHyperlinkSafetyTests
{
    [Theory]
    [InlineData("http://example.com", TerminalHyperlinkDisposition.Allow)]
    [InlineData("HTTPS://example.com/a//b?q=one", TerminalHyperlinkDisposition.Allow)]
    [InlineData("https://example.com/é/😀", TerminalHyperlinkDisposition.Allow)]
    [InlineData("mailto:user@example.com", TerminalHyperlinkDisposition.Allow)]
    [InlineData("mailto:user@example.com?subject=hello", TerminalHyperlinkDisposition.Allow)]
    [InlineData("vscode://file/tmp/example.cs", TerminalHyperlinkDisposition.Confirm)]
    [InlineData("ssh://example.com", TerminalHyperlinkDisposition.Confirm)]
    [InlineData("custom:action?value=1", TerminalHyperlinkDisposition.Confirm)]
    [InlineData("file:///tmp/document.txt", TerminalHyperlinkDisposition.InspectFile)]
    [InlineData("file://localhost/tmp/document.txt", TerminalHyperlinkDisposition.InspectFile)]
    [InlineData("file:///tmp/payload.command", TerminalHyperlinkDisposition.InspectFile)]
    public void SchemesHaveExplicitDispatchRequirements(string target, TerminalHyperlinkDisposition expected)
    {
        TerminalHyperlinkRequest request = TerminalHyperlinkSafety.Classify(target);
        Assert.Equal(expected, request.Disposition);
        Assert.Equal(TerminalHyperlinkDenialReason.None, request.DenialReason);
        Assert.NotNull(request.Uri);
        Assert.Same(target, request.Target);
        Assert.Same(target, request.DisplayText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/tmp/file.txt")]
    [InlineData("../file.txt")]
    [InlineData("payload.command")]
    [InlineData(" https://example.com")]
    [InlineData("C:\\folder\\file.txt")]
    [InlineData("https:relative")]
    [InlineData("http:///missing-host")]
    [InlineData("https://example.com\\@other.test")]
    [InlineData("mailto:")]
    [InlineData("mailto:?subject=empty")]
    [InlineData("file://remote/tmp/a")]
    [InlineData("file:///tmp/a?query")]
    [InlineData("file:///tmp/a#fragment")]
    public void AmbiguousMalformedAndRemoteFileTargetsCannotDispatch(string target)
    {
        TerminalHyperlinkRequest request = TerminalHyperlinkSafety.Classify(target);
        Assert.Equal(TerminalHyperlinkDisposition.Deny, request.Disposition);
        Assert.NotEqual(TerminalHyperlinkDenialReason.None, request.DenialReason);
        Assert.Null(request.Uri);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(31)]
    [InlineData(127)]
    [InlineData(133)]
    [InlineData(159)]
    [InlineData(0x61C)]
    [InlineData(0x200B)]
    [InlineData(0x200F)]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    [InlineData(0x202A)]
    [InlineData(0x202E)]
    [InlineData(0x2060)]
    [InlineData(0x2066)]
    [InlineData(0x2069)]
    [InlineData(0xFEFF)]
    public void UnsafeScalarsAreRejectedBeforeUriNormalizationAndEscapedForDisplay(int scalar)
    {
        string target = "https://example.com/before" + char.ConvertFromUtf32(scalar) + "after";
        TerminalHyperlinkRequest request = TerminalHyperlinkSafety.Classify(target);
        Assert.Equal(TerminalHyperlinkDisposition.Deny, request.Disposition);
        Assert.Equal(TerminalHyperlinkDenialReason.UnsafeCharacters, request.DenialReason);
        Assert.Equal($"https://example.com/before\\u{{{scalar:X}}}after", request.DisplayText);
        Assert.Equal(target, request.Target);
    }

    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void InvalidHostUtf16IsNotSilentlyReplaced(int surrogate)
    {
        string target = "https://example.com/" + (char)surrogate;
        TerminalHyperlinkRequest request = TerminalHyperlinkSafety.Classify(target);
        Assert.Equal(TerminalHyperlinkDisposition.Deny, request.Disposition);
        Assert.Equal($"https://example.com/\\u{{{surrogate:X}}}", request.DisplayText);
    }

    [Fact]
    public void SafePreviewIsAllocationFreeAndDoesNotDecodePercentEscapes()
    {
        const string target = "https://example.com/a//b%20c/%E2%80%AE";
        _ = TerminalHyperlinkSafety.SanitizeDisplay(target);
        long before = GC.GetAllocatedBytesForCurrentThread();
        string display = target;
        for (int i = 0; i < 100; i++) display = TerminalHyperlinkSafety.SanitizeDisplay(target);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(target, display);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NullTargetsAreRejectedAtThePublicBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => TerminalHyperlinkSafety.Classify(null!));
        Assert.Throws<ArgumentNullException>(() => TerminalHyperlinkSafety.SanitizeDisplay(null!));
    }
}
