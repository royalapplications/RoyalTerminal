// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty #14560 and OSC 7501 revision 0.3. WT/xterm.js have no matching
// status dispatcher; their cancellation/reset rules were compared separately.
public sealed class TerminalProgramStatusTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportsAndQueriesSurviveEveryInputSplit(bool native)
    {
        byte[] input = Encoding.UTF8.GetBytes("\u001b]7501;state=blocked:kind=permission:progress=42:id=deploy/eu:app=terraform:title=RVU:msg=QXBwbHk/\u001b\\\u001b]7501;?\a\u001b]7501;?\u001b\\");
        for (int split = 0; split <= input.Length; split++)
        {
            using IVtProcessor processor = Create(native);
            ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
            List<byte[]> replies = [];
            processor.ResponseCallback = replies.Add;
            processor.Process(input.AsSpan(0, split));
            processor.Process(input.AsSpan(split));
            Assert.Equal(new(TerminalProgramStatusState.Blocked, "deploy/eu", TerminalProgramStatusKind.Permission,
                42, "terraform", "EU", "Apply?"), Assert.Single(status.ProgramStatuses));
            Assert.Equal(["\u001b]7501;?\a", "\u001b]7501;?\u001b\\"], replies.Select(Encoding.UTF8.GetString));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportsReplaceRecordsAndInheritFromNearestAncestor(bool native)
    {
        using IVtProcessor processor = Create(native);
        ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
        Write(processor, "state=idle:app=root");
        Write(processor, "state=working:id=build:app=cargo:title=QnVpbGQ=:progress=30");
        Write(processor, "state=blocked:id=build/test:kind=question:msg=V2h5Pw==");
        Assert.Equal("cargo", status.GetProgramStatusApplication("build/test"));
        Write(processor, "state=done:id=build");
        Assert.Equal("root", status.GetProgramStatusApplication("build/test"));
        Assert.Equal(new(TerminalProgramStatusState.Done, "build"), status.ProgramStatuses[^1]);
        Write(processor, "state=done:id=builder");
        Write(processor, "state=clear:id=build");
        Assert.Equal(["", "builder"], status.ProgramStatuses.Select(record => record.Id));
        Write(processor, "state=clear");
        Assert.Empty(status.ProgramStatuses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LifecyclePreservesCompletedRecordsAndSoftResetRetainsAll(bool native)
    {
        using IVtProcessor processor = Create(native);
        ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
        int changes = 0;
        status.ProgramStatusChangedCallback = () => changes++;
        foreach (string state in new[] { "idle", "working", "done", "blocked", "error" })
            Write(processor, $"state={state}:id={state}");
        processor.Process("\u001b[?1049h\u001b[!p\u001b[?1049l"u8);
        Assert.Equal(5, status.ProgramStatuses.Count);
        Assert.Equal(5, changes);
        processor.Process("\u001b]133;A\a"u8);
        Assert.Equal(["idle", "done", "error"], status.ProgramStatuses.Select(record => record.Id));
        Write(processor, "state=blocked:id=child");
        status.NotifyProgramStatusProcessExit();
        Assert.Equal(3, status.ProgramStatuses.Count);
        Assert.Equal(8, changes);
        processor.Process("\u001bc"u8);
        Assert.Empty(status.ProgramStatuses);
        Assert.Equal(9, changes);
        Write(processor, "state=done");
        processor.Reset();
        Assert.Empty(status.ProgramStatuses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidReportsAreAtomicAndLimitsApplyToEveryOccurrence(bool native)
    {
        using IVtProcessor processor = Create(native);
        ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
        Write(processor, "state=idle:app=original");
        string[] invalid =
        [
            "msg=SGk=", "state=future", "state=clear:id=", "state=clear:id=a//b", "state=clear:id=/a",
            "state=clear:id=a/", "state=clear:id=a,b", "state=clear:id=a/b/c/d/e/f/g/h/i",
            "state=clear:id=" + new string('a', 33), "state=clear:id=bad//id:id=good",
            "state=done:msg=AA==", "state=done:msg=fw==", "state=done:msg=wp8=", "state=done:msg=/w==",
            "state=done:msg=a", "state=done:msg=SGk===", "state=done:title=AA==",
            "state=done:app=" + new string('a', 33) + ":app=short",
            "state=done:" + new string('k', 17) + "=x",
            "state=done:title=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('x', 193))),
            "state=done:msg=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('x', 2049))),
            "state=done:msg=" + new string('a', 2733) + ":msg=SGk=",
            "state=done:unknown=" + new string('x', 4096),
        ];
        foreach (string body in invalid)
        {
            Write(processor, body);
            Assert.Equal(new(TerminalProgramStatusState.Idle, App: "original"), Assert.Single(status.ProgramStatuses));
        }
        processor.Process("\u001b]7501;state=clear\u0018\u001b]7501;state=clear\u001a"u8);
        Assert.Single(status.ProgramStatuses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonzeroBase64PaddingBitsDoNotReplaceStoredStatus(bool native)
    {
        using IVtProcessor processor = Create(native);
        ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
        Write(processor, "state=idle:app=original");
        // Ghostty's RFC 4648 decoder rejects unused nonzero bits, even when
        // padding is omitted. Exercise both short and vector-sized payloads.
        foreach (string prefix in new[] { "", "QUJDQUJDQUJDQUJDQUJDQUJDQUJDQUJD" })
        foreach (string field in new[] { "title", "msg" })
        foreach (string encoded in new[] { "QR==", "QR", "QUJ=", "QUJ" })
        {
            Write(processor, $"state=done:{field}={prefix}{encoded}");
            Assert.Equal(new(TerminalProgramStatusState.Idle, App: "original"), Assert.Single(status.ProgramStatuses));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedPairsAreSkippedAndLastValidPairWins(bool native)
    {
        using IVtProcessor processor = Create(native);
        ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
        Write(processor, "state=working:broken:state=blocked:kind=auth:progress=50:progress=1_0:app=bad/name:msg=SGk=:msg=bad!data:unknown=allowed: title = VGVzdA ");
        Assert.Equal(new(TerminalProgramStatusState.Blocked, Kind: TerminalProgramStatusKind.Auth,
            Title: "Test", Message: "Hi"), Assert.Single(status.ProgramStatuses));
        Write(processor, "state=done:kind=permission:progress=42");
        Assert.Equal(new(TerminalProgramStatusState.Done), Assert.Single(status.ProgramStatuses));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordCapEvictsLeastRecentlyUpdatedWithoutClearingItsChildren(bool native)
    {
        using IVtProcessor processor = Create(native);
        ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
        Write(processor, "state=idle:id=parent");
        Write(processor, "state=idle:id=parent/child");
        for (int i = 2; i < 256; i++) Write(processor, $"state=idle:id=item{i}");
        Write(processor, "state=done:id=parent/child");
        Write(processor, "state=working:id=new");
        Assert.Equal(256, status.ProgramStatuses.Count);
        Assert.DoesNotContain(status.ProgramStatuses, record => record.Id == "parent");
        Assert.Contains(status.ProgramStatuses, record => record.Id == "parent/child");
        Assert.Equal("new", status.ProgramStatuses[^1].Id);
    }

    private static void Write(IVtProcessor processor, string body)
        => processor.Process(Encoding.UTF8.GetBytes($"\u001b]7501;{body}\a"));

    private static IVtProcessor Create(bool native)
    {
        Assert.SkipWhen(native && !GhosttyVtProcessor.IsAvailable(), "Native Ghostty is unavailable.");
        TerminalScreen screen = new(20, 4, 100);
        return native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    }
}
