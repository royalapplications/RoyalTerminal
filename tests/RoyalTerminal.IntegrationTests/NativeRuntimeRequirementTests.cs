// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class NativeRuntimeRequirementTests
{
    [Fact]
    public void CiCannotPassBySkippingEveryNativeIntegrationTest()
    {
        if (Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") != "1") return;
        Assert.True(GhosttyVtNative.IsAvailable(),
            "This job requires the rebuilt Ghostty native runtime; skipped native tests are not platform sign-off.");
    }
}
