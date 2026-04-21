// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;
using Xunit;

namespace RoyalTerminal.IntegrationTests.TestInfrastructure;

/// <summary>
/// Skips native Ghostty integration tests when the libghostty-vt runtime is unavailable.
/// </summary>
public sealed class GhosttyNativeFactAttribute : FactAttribute
{
    private const string SkipReason =
        "libghostty-vt is not available in the current test environment.";

    public GhosttyNativeFactAttribute()
    {
        if (!GhosttyVtNative.IsAvailable())
        {
            Skip = SkipReason;
        }
    }
}
