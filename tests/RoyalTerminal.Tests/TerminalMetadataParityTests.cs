// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalMetadataParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostMetadataCopiesArbitraryBytesWithoutCallbacksAndShortDestinationsStayUntouched(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalMetadata metadata = Assert.IsAssignableFrom<ITerminalMetadata>(processor);
        int callbacks = 0;
        processor.TitleCallback = _ => callbacks++;
        ((ITerminalEffectSource)processor).WorkingDirectoryCallback = _ => callbacks++;
        byte[] title = new byte[3000], directory = new byte[5000];
        title.AsSpan().Fill(0xFF); directory.AsSpan().Fill(0xFE);
        title[1] = directory[1] = 0;
        metadata.SetTitle(title); metadata.SetWorkingDirectory(directory);
        Assert.Equal(title, CopyTitle(metadata)); Assert.Equal(directory, CopyDirectory(metadata));
        byte[] shortBuffer = [0xA5, 0xA5];
        Assert.False(metadata.TryCopyTitle(shortBuffer, out int titleLength)); Assert.Equal(3000, titleLength);
        Assert.False(metadata.TryCopyWorkingDirectory(shortBuffer, out int directoryLength)); Assert.Equal(5000, directoryLength);
        Assert.Equal(new byte[] { 0xA5, 0xA5 }, shortBuffer);
        title[0] = directory[0] = 1;
        Assert.Equal(0xFF, CopyTitle(metadata)[0]); Assert.Equal(0xFE, CopyDirectory(metadata)[0]);
        metadata.SetTitle([]); metadata.SetWorkingDirectory([]);
        Assert.True(metadata.TryCopyTitle([], out titleLength)); Assert.Equal(0, titleLength);
        Assert.True(metadata.TryCopyWorkingDirectory([], out directoryLength)); Assert.Equal(0, directoryLength);
        Assert.Equal(0, callbacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportingIsOptInRawAndPolicySurvivesAllSessionResets(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalMetadata metadata = (ITerminalMetadata)processor;
        byte[]? reply = null;
        processor.ResponseCallback = value => reply = value;
        metadata.SetTitle([0xFF, 0, (byte)'X']);
        processor.Process("\u001b[21t"u8); Assert.Null(reply); Assert.False(metadata.TitleReportEnabled);
        metadata.TitleReportEnabled = true;
        processor.Process("\u001b[21t"u8);
        Assert.Equal(new byte[] { 27, (byte)']', (byte)'l', 0xFF, 0, (byte)'X', 27, (byte)'\\' }, reply);
        metadata.TitleReportEnabled = false; reply = null;
        processor.Process("\u001b[21t"u8); Assert.Null(reply);
        metadata.TitleReportEnabled = true;
        for (int reset = 0; reset < 4; reset++)
        {
            metadata.SetTitle("title"u8); metadata.SetWorkingDirectory("directory"u8);
            processor.Process("\u001b[1$}"u8);
            switch (reset)
            {
                case 0: processor.Process("\u001bc"u8); break;
                case 1: processor.Reset(); break;
                case 2: ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserveScrollback: false); break;
                case 3: ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserveScrollback: true); break;
            }
            Assert.Empty(CopyTitle(metadata)); Assert.Empty(CopyDirectory(metadata));
            Assert.True(metadata.TitleReportEnabled);
            processor.Process("\u001b[21t"u8); Assert.Equal("\u001b]l\u001b\\"u8.ToArray(), reply);
            processor.Process("\u001b[H!"u8); Assert.Equal(1, processor.CursorCol);
        }
    }

    public static IEnumerable<object[]> MetadataCommands()
    {
        foreach (string command in new[] { "0;hello", "2;hello", "1;icon-only", "0;", "2;", "7;", "7;file:///a%20b", "9;9;/raw/path",
            "9;9;", "1337;CurrentDir=/raw/path", "1337;cUrReNtDiR=/mixed", "1337;CurrentDir=", "1337;CurrentDir",
            "00;alias", "02;alias", "01;alias", "07;file:///alias", "09;9;alias", "01337;CurrentDir=alias" })
            yield return [Encoding.UTF8.GetBytes(command)];
        yield return [new byte[] { (byte)'2', (byte)';', 0xFF }];
        yield return [new byte[] { (byte)'2', (byte)';', 0xC3 }];
        foreach (string prefix in new[] { "7;", "9;9;", "1337;CurrentDir=" })
            yield return [Encoding.ASCII.GetBytes(prefix).Concat(new byte[] { 0xFF, 0xC3, 0x28 }).ToArray()];
        yield return [Encoding.UTF8.GetBytes("2;日本語")];
    }

    [Theory]
    [MemberData(nameof(MetadataCommands))]
    public void OscMetadataMatchesNativeCallbacksAndRawStateAtEverySplit(byte[] command)
    {
        if (!Available()) return;
        byte[] bytes = [27, (byte)']', ..command, 27, (byte)'\\'];
        for (int split = 0; split <= bytes.Length; split++)
        {
            using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
            using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
            native.SetTitle("previous"u8); managed.SetTitle("previous"u8);
            native.SetWorkingDirectory("previous"u8); managed.SetWorkingDirectory("previous"u8);
            List<string> expected = [], actual = [];
            native.TitleCallback = value => expected.Add("title:" + value);
            managed.TitleCallback = value => actual.Add("title:" + value);
            native.WorkingDirectoryCallback = value => expected.Add("pwd:" + value);
            managed.WorkingDirectoryCallback = value => actual.Add("pwd:" + value);
            native.Process(bytes.AsSpan(0, split)); native.Process(bytes.AsSpan(split));
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal(CopyTitle(native), CopyTitle(managed)); Assert.Equal(CopyDirectory(native), CopyDirectory(managed));
            Assert.Equal(expected, actual);
        }
    }

    public static IEnumerable<object[]> CaptureLimits()
    {
        foreach (string prefix in new[] { "0;", "2;", "7;", "9;9;", "1337;CurrentDir=" })
        foreach (int length in new[] { 1023, 1024, 1025, 2047, 2048, 4096 })
        foreach (bool scalar in new[] { false, true }) yield return [prefix, length, scalar];
    }

    [Theory]
    [MemberData(nameof(CaptureLimits))]
    public void FixedCapturesAndHandlerTruncationMatchNative(string prefix, int length, bool scalar)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        native.SetTitle("old"u8); managed.SetTitle("old"u8);
        native.SetWorkingDirectory("old"u8); managed.SetWorkingDirectory("old"u8);
        // Length is the whole capture after the selector, including subcommands.
        int subcommandLength = prefix.Length - prefix.IndexOf(';') - 1;
        byte[] bytes = Encoding.ASCII.GetBytes("\u001b]" + prefix + new string('x', length - subcommandLength) + "\a");
        if (scalar)
            for (int i = 0; i < bytes.Length; i++) { native.Process(bytes.AsSpan(i, 1)); managed.Process(bytes.AsSpan(i, 1)); }
        else { native.Process(bytes); managed.Process(bytes); }
        Assert.Equal(CopyTitle(native), CopyTitle(managed)); Assert.Equal(CopyDirectory(native), CopyDirectory(managed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TitleValidatesEntirePayloadBeforeTruncatingEvenThroughAScalar(bool invalidTail)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        byte[] value = Encoding.UTF8.GetBytes(new string('x', 1023) + "€");
        byte[] bytes = invalidTail ? [27, (byte)']', (byte)'2', (byte)';', ..value, 0xFF, 7] : [27, (byte)']', (byte)'2', (byte)';', ..value, 7];
        native.Process(bytes); managed.Process(bytes);
        byte[] title = CopyTitle(managed);
        Assert.Equal(CopyTitle(native), title);
        if (invalidTail) Assert.Empty(title);
        else { Assert.Equal(1024, title.Length); Assert.Equal(0xE2, title[^1]); }
    }

    [Theory]
    [InlineData("\u001b[1$}ignored\u001b[0$}A")]
    [InlineData("X\u001b[1$}ignored\u001b[2b\r\n\u001b[0$}\u001b[2b")]
    [InlineData("\u001b[1$}\u001b[2;3Hignored\u001b[0$}B")]
    [InlineData("\u001b[1$}\u001b[2$}ignored\u001b[$}ignored\u001b[0;0$}ignored\u001b[0$}C")]
    [InlineData("\u001b[?1$}A\u001b[1:0$}B\u001b[1;0$}C")]
    [InlineData("\u001b*0\u001bN\u001b[1$}q\u001b[0$}q")]
    [InlineData("\u001b[1$}日本語\u001b[0$}a")]
    [InlineData("\u001b[1$}\u001b[?1049hignored\u001b[?1049lignored\u001b[0$}A")]
    public void StatusDisplaySuppressesPrintingButNotControlsOrFutureRep(string commands)
    {
        if (!Available()) return;
        byte[] bytes = Encoding.UTF8.GetBytes(commands);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen expected = new(16, 4), actual = new(16, 4);
            using GhosttyVtProcessor native = new(expected);
            using BasicVtProcessor managed = new(actual);
            native.Process(bytes.AsSpan(0, split)); native.Process(bytes.AsSpan(split));
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 16; column++) Assert.Equal(expected.GetRow(row)[column].Codepoint, actual.GetRow(row)[column].Codepoint);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotInstallationRetainsRawUnboundedMetadataAndStatusWithoutEffects(bool status)
    {
        if (!Available()) return;
        using GhosttyTerminal source = new(8, 3);
        byte[] title = new byte[3000], pwd = new byte[5000];
        title.AsSpan().Fill(0xFF); pwd.AsSpan().Fill(0xFE); title[1] = pwd[1] = 0;
        source.SetTitleBytes(title); source.SetWorkingDirectoryBytes(pwd);
        if (status) source.Write("\u001b[1$}"u8);
        using GhosttyTerminal restored = GhosttySnapshot.Decode(GhosttySnapshot.Encode(source));
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(restored), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        int effects = 0;
        managed.TitleCallback = _ => effects++; managed.WorkingDirectoryCallback = _ => effects++;
        managed.ResponseCallback = _ => effects++;
        managed.InstallSnapshotMetadata(ready.Terminal);
        Assert.Equal(0, effects); Assert.False(managed.TitleReportEnabled);
        Assert.Equal(title, CopyTitle(managed)); Assert.Equal(pwd, CopyDirectory(managed));
        Assert.Equal(status ? 1 : 0, managed.SnapshotStatusDisplay);
        managed.Process("X"u8); Assert.Equal(status ? 0 : 1, managed.CursorCol);
        managed.Process("\u001b[0$}Y"u8); Assert.Equal(status ? 1 : 2, managed.CursorCol);
    }

    [Fact]
    public void ManagedWarmMetadataAccessAndUnobservedTitleCommandsAllocateNothing()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        byte[] destination = new byte[20];
        void Run()
        {
            for (int i = 0; i < 1000; i++)
            {
                managed.Process("\u001b]2;hello world\a"u8);
                managed.SetTitle("hello world"u8); managed.SetWorkingDirectory("file:///path"u8);
                managed.TryCopyTitle(destination, out _); managed.TryCopyWorkingDirectory(destination, out _);
            }
        }
        Run(); long before = GC.GetAllocatedBytesForCurrentThread(); Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void NativeRawBindingsRejectDisposedAccess()
    {
        if (!Available()) return;
        using GhosttyTerminal native = new(8, 3);
        native.Dispose();
        Assert.Throws<ObjectDisposedException>(() => native.SetTitleBytes([]));
        Assert.Throws<ObjectDisposedException>(() => native.SetWorkingDirectoryBytes([]));
        Assert.Throws<ObjectDisposedException>(() => native.TryCopyTitle([], out _));
        Assert.Throws<ObjectDisposedException>(() => native.TryCopyWorkingDirectory([], out _));
        using GhosttyVtProcessor processor = new(new TerminalScreen(8, 3)); processor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => processor.TitleReportEnabled = true);
    }

    private static byte[] CopyTitle(ITerminalMetadata metadata)
    {
        metadata.TryCopyTitle([], out int length);
        byte[] bytes = new byte[length]; Assert.True(metadata.TryCopyTitle(bytes, out int actual)); Assert.Equal(length, actual); return bytes;
    }

    private static byte[] CopyDirectory(ITerminalMetadata metadata)
    {
        metadata.TryCopyWorkingDirectory([], out int length);
        byte[] bytes = new byte[length]; Assert.True(metadata.TryCopyWorkingDirectory(bytes, out int actual)); Assert.Equal(length, actual); return bytes;
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable(); output.WriteLine($"Native metadata differential available: {available}"); return available;
    }
}
