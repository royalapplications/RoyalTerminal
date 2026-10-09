// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyOctoberApiTests
{
    [GhosttyNativeFact]
    public unsafe void ProgramStatusUsesPublicCallbacksAndPreservesReplyTerminator()
    {
        using GhosttyTerminal terminal = new(20, 4);
        StringBuilder replies = new();
        List<string> events = [];
        List<(GhosttyVtNative.GhosttyTerminalProgramStatus Report, string Id, string App, string Title, string Message)> reports = [];
        GhosttyVtNative.GhosttyTerminalWritePtyCallback write = (_, _, data, length) =>
            replies.Append(Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)data, (int)length)));
        GhosttyVtNative.GhosttyTerminalProgramStatusCallback status = (_, _, report) =>
        {
            reports.Add((*report, report->Id.ToUtf8String(), report->App.ToUtf8String(),
                report->Title.ToUtf8String(), report->Message.ToUtf8String()));
            events.Add(report->State == GhosttyVtNative.GhosttyProgramStatusState.Clear
                ? "clear" : report->Message.ToUtf8String());
        };
        GhosttyVtNative.GhosttyTerminalSemanticPromptCallback prompt = (_, _, value) =>
        {
            if (value->Kind == GhosttyVtNative.GhosttySemanticPromptKind.PromptStart)
                events.Add("prompt");
        };
        GhosttyVtNative.GhosttyTerminalResetCallback reset = (_, _) => events.Add("reset");
        terminal.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(write));
        terminal.Write("\u001b]7501;?\a"u8);
        Assert.Empty(replies.ToString());
        terminal.SetProgramStatusCallback(Marshal.GetFunctionPointerForDelegate(status));
        terminal.SetSemanticPromptCallback(Marshal.GetFunctionPointerForDelegate(prompt));
        terminal.SetResetCallback(Marshal.GetFunctionPointerForDelegate(reset));
        terminal.Write("\u001b]7501;?\a\u001b]7501;?\u001b\\"u8);
        Assert.Equal("\u001b]7501;?\a\u001b]7501;?\u001b\\", replies.ToString());
        terminal.Write("\u001b]7501;state=blocked:kind=permission:id=deploy/eu:app=terraform:title=RVU=:msg=QXBwbHk/\a"u8);
        terminal.Write("\u001b]7501;state=blocked:msg=AA==\a"u8); // Controls discard the whole report.
        terminal.Write("\u001b]133;A\a\u001b[!p\u001bc"u8);
        Assert.Equal(["Apply?", "prompt", "clear", "reset"], events);
        Assert.Equal(2, reports.Count);
        Assert.Equal((nuint)sizeof(GhosttyVtNative.GhosttyTerminalProgramStatus), reports[0].Report.Size);
        Assert.Equal(GhosttyVtNative.GhosttyProgramStatusState.Blocked, reports[0].Report.State);
        Assert.Equal(GhosttyVtNative.GhosttyProgramStatusKind.Permission, reports[0].Report.Kind);
        Assert.Equal(-1, reports[0].Report.Progress);
        Assert.Equal("deploy/eu", reports[0].Id);
        Assert.Equal("terraform", reports[0].App);
        Assert.Equal("EU", reports[0].Title);
        terminal.SetProgramStatusCallback(0);
        replies.Clear();
        terminal.Write("\u001b]7501;?\a"u8);
        Assert.Empty(replies.ToString());
        GC.KeepAlive(write);
        GC.KeepAlive(status);
        GC.KeepAlive(prompt);
        GC.KeepAlive(reset);
    }

    [GhosttyNativeFact]
    public unsafe void UnknownOscCaptureIsBoundedAndCancelledSequencesAreDiscarded()
    {
        Assert.Equal(GhosttyVtNative.GhosttyResult.Success, GhosttyVtNative.OscNew(0, out nint parser));
        try
        {
            nuint limit = 8;
            Assert.Equal(GhosttyVtNative.GhosttyResult.Success,
                GhosttyVtNative.OscSet(parser, GhosttyVtNative.GhosttyOscOption.UnknownMaxBytes, &limit));
            foreach (byte value in "7400;abcdef"u8) GhosttyVtNative.OscNext(parser, value);
            nint command = GhosttyVtNative.OscEnd(parser, 7);
            Assert.Equal(GhosttyVtNative.GhosttyOscCommandType.Unknown, GhosttyVtNative.OscCommandType(command));
            GhosttyVtNative.GhosttyString content = default;
            bool truncated = false;
            GhosttyVtNative.GhosttyOscTerminator terminator = default;
            Assert.True(GhosttyVtNative.OscCommandData(command, GhosttyVtNative.GhosttyOscCommandData.UnknownContent, &content));
            Assert.True(GhosttyVtNative.OscCommandData(command, GhosttyVtNative.GhosttyOscCommandData.UnknownTruncated, &truncated));
            Assert.True(GhosttyVtNative.OscCommandData(command, GhosttyVtNative.GhosttyOscCommandData.UnknownTerminator, &terminator));
            Assert.Equal("7400;abc", content.ToUtf8String());
            Assert.True(truncated);
            Assert.Equal(GhosttyVtNative.GhosttyOscTerminator.Bel, terminator);
            foreach (byte cancel in new byte[] { 0x18, 0x1a })
            {
                GhosttyVtNative.OscReset(parser);
                foreach (byte value in "7400;ok"u8) GhosttyVtNative.OscNext(parser, value);
                Assert.Equal(0, GhosttyVtNative.OscEnd(parser, cancel));
            }
            Assert.False(GhosttyVtNative.OscCommandData(0, GhosttyVtNative.GhosttyOscCommandData.UnknownContent, &content));
        }
        finally { GhosttyVtNative.OscFree(parser); }
    }

    [GhosttyNativeFact]
    public void MemoryQueriesAndCompressedSnapshotRestorePreserveContent()
    {
        using GhosttyTerminal terminal = new(80, 5);
        terminal.SetScrollbackMaxBytes(16 * 1024 * 1024);
        for (int i = 0; i < 2000; i++) terminal.Write("repeated history for compression\r\n"u8);
        GhosttyVtNative.GhosttyTerminalMemoryUsage memory = terminal.GetMemoryUsage();
        Assert.True(memory.PrimaryPages > 1);
        Assert.True(memory.PrimaryVirtualBytes >= memory.PrimaryResidentBytes);
        Assert.Equal(0ul, memory.AlternatePages);
        using GhosttySnapshotDecoder decoder = new(GhosttySnapshot.Encode(terminal));
        Assert.False(decoder.GetCompressHistory());
        decoder.SetCompressHistory(true);
        Assert.True(decoder.GetCompressHistory());
        using GhosttyTerminal restored = decoder.Decode();
        Assert.Equal(terminal.GetTotalRows(), restored.GetTotalRows());
        GhosttyVtNative.GhosttyTerminalMemoryUsage compressed = restored.GetMemoryUsage();
        if (compressed.CompressionSupported)
        {
            Assert.True(compressed.PrimaryCompressedPages > 0);
            Assert.True(compressed.PrimaryResidentBytes < compressed.PrimaryVirtualBytes);
        }
        terminal.Write("\u001b]22;pointer\a"u8);
        Assert.Equal(GhosttyVtNative.GhosttyMouseShape.Pointer, terminal.GetMouseShape());
        terminal.Write("\u001b]22;\a"u8);
        Assert.Equal(GhosttyVtNative.GhosttyMouseShape.Text, terminal.GetMouseShape());
    }

    [GhosttyNativeFact]
    public unsafe void ChecksumsAreOptInAndFlagsAreValidated()
    {
        using GhosttyTerminal terminal = new(10, 2);
        StringBuilder replies = new();
        GhosttyVtNative.GhosttyTerminalWritePtyCallback write = (_, _, data, length) =>
            replies.Append(Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)data, (int)length)));
        terminal.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(write));
        terminal.Write("A\u001b[1;1;1;1;1;1*y"u8);
        Assert.Empty(replies.ToString());
        terminal.SetXtChecksumReport(true);
        terminal.SetXtChecksumExtension(3); // Positive sum, without attributes.
        terminal.Write("\u001b[1;1;1;1;1;1*y"u8);
        Assert.Equal("\u001bP1!~0041\u001b\\", replies.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.SetXtChecksumExtension(32));
        GC.KeepAlive(write);
    }
}
