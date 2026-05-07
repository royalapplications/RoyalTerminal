// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Windows ConPTY environment parity tests.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class WindowsPtyEnvironmentTests
{
    [Fact]
    public void CreateEnvironmentVariables_AddsWindowsTerminalIdentityAndWslEnv()
    {
        Guid sessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Guid profileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Dictionary<string, string?> baseEnvironment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Path"] = "C:\\Windows\\System32",
            ["WSLENV"] = "EXISTING/u",
        };
        Dictionary<string, string> userEnvironment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ROYALTERMINAL_TOKEN"] = "%SystemRoot%",
            ["PATH"] = "C:\\Custom",
        };

        SortedDictionary<string, string> variables = WindowsPtyEnvironment.CreateEnvironmentVariables(
            baseEnvironment,
            userEnvironment,
            sessionId,
            profileId);

        Assert.Equal("11111111-2222-3333-4444-555555555555", variables["WT_SESSION"]);
        Assert.Equal("{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}", variables["WT_PROFILE_ID"]);
        Assert.Equal(
            "WT_SESSION:WT_PROFILE_ID:ROYALTERMINAL_TOKEN:EXISTING/u",
            variables["WSLENV"]);
        Assert.Equal("C:\\Custom", variables["PATH"]);
        Assert.Equal(
            Environment.ExpandEnvironmentVariables("%SystemRoot%"),
            variables["ROYALTERMINAL_TOKEN"]);
        Assert.False(variables.ContainsKey("TERM"));
        Assert.False(variables.ContainsKey("COLORTERM"));
        Assert.False(variables.ContainsKey("TERM_PROGRAM"));
    }

    [Fact]
    public void CreateEnvironmentVariables_DoesNotDuplicateExistingWslEnvNames()
    {
        Guid sessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Guid profileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Dictionary<string, string?> baseEnvironment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["WSLENV"] = "WT_SESSION:PATH/u:EXISTING/l",
        };
        Dictionary<string, string> userEnvironment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["EXISTING"] = "updated",
            ["PATH"] = "C:\\Custom",
        };

        SortedDictionary<string, string> variables = WindowsPtyEnvironment.CreateEnvironmentVariables(
            baseEnvironment,
            userEnvironment,
            sessionId,
            profileId);

        Assert.Equal("WT_PROFILE_ID:WT_SESSION:PATH/u:EXISTING/l", variables["WSLENV"]);
        Assert.Equal("updated", variables["EXISTING"]);
        Assert.Equal("C:\\Custom", variables["PATH"]);
    }
}
