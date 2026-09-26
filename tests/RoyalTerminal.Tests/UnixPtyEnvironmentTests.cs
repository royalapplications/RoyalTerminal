// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class UnixPtyEnvironmentTests
{
    [Fact]
    public unsafe void ArgumentsPreserveUtf8EmptyValuesAndScriptPrefix()
    {
        using UnixPtyArguments arguments = new("/bin/sh", ["Zażółć 世界", "", "a'b $()"], "script path");
        string[] expected = ["/bin/sh", "script path", "Zażółć 世界", "", "a'b $()"];
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], Marshal.PtrToStringUTF8((nint)arguments.Pointer[i]));
        Assert.True(arguments.Pointer[expected.Length] == null);
        arguments.Dispose();
        arguments.Dispose();
        Assert.True(arguments.Pointer == null);
    }

    [Fact]
    public void ArgumentsRejectEmbeddedNul()
    {
        Assert.Throws<ArgumentException>(() => new UnixPtyArguments("bad\0file", []));
        Assert.Throws<ArgumentException>(() => new UnixPtyArguments("/bin/sh", ["bad\0argument"]));
    }

    [Fact]
    public unsafe void EnvironmentIsTerminatedUtf8AndDoesNotMutateParent()
    {
        const string key = "ROYALTERMINAL_ENVIRONMENT_TEST";
        string? original = Environment.GetEnvironmentVariable(key);
        using UnixPtyEnvironment environment = new(new Dictionary<string, string>
        {
            [key] = "Zażółć=世界", ["TERM"] = "royal-test", ["ROYALTERMINAL_EMPTY_TEST"] = "",
        });
        Dictionary<string, string> actual = new(StringComparer.Ordinal);
        for (int i = 0; environment.Pointer[i] != null; i++)
        {
            string item = Marshal.PtrToStringUTF8((nint)environment.Pointer[i])!;
            int separator = item.IndexOf('=');
            Assert.True(separator > 0);
            actual.Add(item[..separator], item[(separator + 1)..]);
        }
        Assert.Equal("Zażółć=世界", actual[key]);
        Assert.Equal("royal-test", actual["TERM"]);
        Assert.Equal("", actual["ROYALTERMINAL_EMPTY_TEST"]);
        Assert.Equal(original, Environment.GetEnvironmentVariable(key));
        environment.Dispose();
        Assert.True(environment.Pointer == null);
        environment.Dispose();
    }

    [Theory]
    [InlineData("", "value")]
    [InlineData("A=B", "value")]
    [InlineData("A\0B", "value")]
    [InlineData("A", "value\0")]
    public void EnvironmentRejectsMalformedOverrides(string key, string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new UnixPtyEnvironment(new Dictionary<string, string> { [key] = value }));
    }
}
