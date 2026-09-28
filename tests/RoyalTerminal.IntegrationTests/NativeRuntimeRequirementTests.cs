// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class NativeRuntimeRequirementTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("unexpected", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    public void AvailabilityIsSuppressedOnlyByExplicitOptOut(string? disableProbe, bool expected)
    {
        // Pure policy inputs avoid changing process-wide environment while other
        // native integration tests run. CI/platform identity is not an opt-out.
        Assert.Equal(expected, GhosttyVtNative.ShouldSkipNativeAvailabilityProbe(disableProbe));
    }

    [Fact]
    public void CiCannotPassBySkippingEveryNativeIntegrationTest()
    {
        if (Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") != "1") return;
        Assert.True(GhosttyVtNative.IsAvailable(),
            "This job requires the rebuilt Ghostty native runtime; skipped native tests are not platform sign-off.");
    }
}
