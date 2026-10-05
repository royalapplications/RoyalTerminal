// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class KittyGraphicsProtocolParityTests
{
    public static IEnumerable<object[]> Conversations()
    {
        yield return ["frame-replies-and-silent-control", new[]
        {
            "a=T,f=32,i=1,p=1,s=1,v=1,C=1;/wAA/w==",
            "a=f,f=32,i=1,s=1,v=1,r=99,z=40;AAD//w==",
            "a=a,i=1,c=2", "a=c,i=1,r=1,c=2,w=1,h=1"
        }];
        yield return ["missing-frame-target", new[] { "a=f,f=32,i=99,r=8,s=1,v=1;/wAA/w==" }];
        yield return ["query-does-not-inherit-upload-quiet", new[]
        {
            "a=t,f=32,i=1,p=7,s=1,v=1,m=1,q=2;/wAA",
            "a=q,f=32,i=9,p=6,s=1,v=1;AAD//w==", "m=0;/w=="
        }];
        yield return ["query-without-id-is-silent", new[] { "a=q,f=32,I=9,s=1,v=1;AAD//w==" }];
        yield return ["conflicting-identifiers-do-not-inherit-upload-quiet", new[]
        {
            "a=t,f=32,i=1,p=7,s=1,v=1,m=1,q=2;/wAA",
            "a=p,i=1,I=2,p=3", "m=0;/w=="
        }];
        yield return ["frame-and-composition-by-number", new[]
        {
            "a=t,f=32,I=43,s=1,v=1;/wAA/w==",
            "a=f,f=32,I=43,s=1,v=1;AAD//w==", "a=c,I=43,r=1,c=2"
        }];
        yield return ["chunked-frame-cannot-target-changed-generation", new[]
        {
            "a=t,f=32,i=1,s=1,v=1;/wAA/w==", "a=f,f=32,i=1,s=1,v=1;AAD//w==",
            "a=f,f=32,i=1,s=1,v=1,m=1;AP8A", "a=a,i=1,c=2", "m=0;/w=="
        }];
        yield return ["invalid-frame-base-echoes-resolved-frame", new[]
        {
            "a=t,f=32,i=1,s=1,v=1;/wAA/w==", "a=f,f=32,i=1,s=1,v=1,c=99;AAD//w=="
        }];
        yield return ["virtual-parent-before-missing-image", new[]
        {
            "a=p,i=99,p=1,U=1,P=2", "a=p,I=99,p=2,U=2,P=3"
        }];
        yield return ["invalid-retransmission-retires-image", new[]
        {
            "a=T,i=1,p=1,s=1,v=1,C=1;/wAA/w==", "a=t,i=1,f=99,s=1,v=1;AAD//w==",
            "a=p,i=1,p=1,C=1"
        }];
        yield return ["frame-errors-before-and-after-decoding", new[]
        {
            "a=t,i=1,s=1,v=1;/wAA/w==", "a=f,i=1,r=99,f=99,s=1,v=1;AAD//w==",
            "a=f,i=1,r=99,s=1,v=1;AA==", "a=f,i=1,r=99,s=2,v=1;AAD//wAA//8=",
            "a=f,i=1,r=99,s=1,v=1,c=99;AAD//w=="
        }];
        yield return ["chunked-frame-error-has-not-resolved-requested-frame", new[]
        {
            "a=t,i=1,s=1,v=1;/wAA/w==", "a=f,i=1,r=99,s=1,v=1,m=1;AA==", "m=0;AA=="
        }];
        yield return ["transmit-like-continuations-follow-initial-action", new[]
        {
            "a=T,i=1,p=1,s=1,v=1,C=1,m=1;/wAA", "a=f,i=99,r=99,m=0;/w==",
            "a=f,i=1,r=99,s=1,v=1,m=1;AAD/", "a=T,i=99,p=99,m=0;/w==",
            "a=a,i=1,c=2", "a=p,i=99,C=1"
        }];
        yield return ["implicit-id-reservation-survives-aborted-load", new[]
        {
            "a=t,s=1,v=1,m=1;/wAA", "a=d,d=a", "a=T,s=1,v=1,C=1;AAD//w==",
            "a=p,i=2147483647,C=1", "a=p,i=2147483648,C=1"
        }];
        yield return ["rgb-promotion-refreshes-number-lookup-generation", new[]
        {
            "a=t,I=77,f=24,s=1,v=1;/wAA", "a=t,I=77,f=24,s=1,v=1;AAD/",
            "a=p,i=1,C=1", "a=p,I=77,C=1", "a=f,i=1,c=99,f=24,s=1,v=1;AP8A",
            "a=p,I=77,C=1"
        }];
        yield return ["rgb-promotion-cannot-retarget-numbered-chunked-frame", new[]
        {
            "a=t,I=77,f=24,s=2,v=1;/wAAAP8A", "a=t,I=77,f=24,s=1,v=1;AAD/",
            "a=f,I=77,f=24,s=1,v=1,m=1;AA==", "a=c,i=1,r=1,c=1,x=1,w=1,h=1",
            "m=0;AP8=", "a=p,I=77,C=1"
        }];
    }

    [Theory]
    [MemberData(nameof(Conversations))]
    public void RepliesMatchNativeGhostty(string name, string[] commands)
    {
        if (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
            Assert.Skip("Native Ghostty Kitty graphics runtime unavailable.");
        }
        TerminalScreen managedScreen = new(8, 3, 10);
        TerminalScreen nativeScreen = new(8, 3, 10);
        using BasicVtProcessor managed = new(managedScreen);
        using GhosttyVtProcessor native = new(nativeScreen);
        managed.NotifyResize(8, 3, 64, 48);
        native.NotifyResize(8, 3, 64, 48);
        List<string> actual = [];
        List<string> expected = [];
        managed.ResponseCallback = bytes => actual.Add(Encoding.ASCII.GetString(bytes));
        native.ResponseCallback = bytes => expected.Add(Encoding.ASCII.GetString(bytes));
        foreach (string command in commands)
        {
            byte[] sequence = Encoding.ASCII.GetBytes("\u001b_G" + command + "\u001b\\");
            managed.Process(sequence);
            native.Process(sequence);
            Assert.True(expected.SequenceEqual(actual),
                $"{name}: {command}\nExpected: {string.Join(" | ", expected)}\nActual: {string.Join(" | ", actual)}");
        }
    }
}
