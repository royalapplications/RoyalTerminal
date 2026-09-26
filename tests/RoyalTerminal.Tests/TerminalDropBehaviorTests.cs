// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalDropBehaviorTests
{
    [AvaloniaFact]
    public void RegisteredDropNegotiatesCopyAndServesCapturedBytesWithoutPrintingThem()
    {
        TerminalControl control = CreateControl(out Endpoint endpoint);
        control.Padding = new Thickness(7, 9, 0, 0);
        control.WriteOutput("\x1b]72;t=a:i=8\x1b\\"u8);
        BasicVtProcessor processor = Assert.IsType<BasicVtProcessor>(control.ActiveVtProcessor);
        using IDataTransfer transfer = TextTransfer("hello\x1b[2J\n");
        Point position = new(7 + control.Renderer!.CellWidth * 2 + 0.25, 9 + control.Renderer.CellHeight + 0.25);
        DragEventArgs move = Raise(control, DragDrop.DragOverEvent, transfer, position);
        Assert.True(move.Handled);
        Assert.Equal(DragDropEffects.Copy, move.DragEffects);
        Assert.Contains("t=m:x=2:y=1:", endpoint.Output.ToString());
        Assert.Contains(":o=1:i=8:m=0;text/plain ", endpoint.Output.ToString());
        control.WriteOutput("\x1b]72;t=m:o=1;text/plain\x1b\\"u8);
        endpoint.Output.Clear();
        DragEventArgs drop = Raise(control, DragDrop.DropEvent, transfer, position);
        Assert.True(drop.Handled);
        Assert.Equal(DragDropEffects.Copy, drop.DragEffects);
        Assert.Contains("t=M:x=2:y=1:", endpoint.Output.ToString());
        Assert.DoesNotContain("hello", endpoint.Output.ToString());
        Assert.Equal(0, control.Screen!.GetViewportRow(0)[0].Codepoint);
        StringBuilder reply = new();
        processor.ResponseCallback = bytes => reply.Append(Encoding.ASCII.GetString(bytes));
        processor.Process("\x1b]72;t=r:x=1\x1b\\"u8);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("hello\x1b[2J\n")), reply.ToString());
        control.StopPty();
        reply.Clear();
        processor.ResponseCallback = bytes => reply.Append(Encoding.ASCII.GetString(bytes));
        processor.Process("\x1b]72;t=r:x=1\x1b\\"u8);
        Assert.Contains("ENOENT", reply.ToString());
        processor.Dispose();
    }

    [AvaloniaFact]
    public void UnregisteredDropsRemainAvailableToEmbeddingApplication()
    {
        TerminalControl control = CreateControl(out Endpoint endpoint);
        using IDataTransfer transfer = TextTransfer("not shell input");
        Assert.False(Raise(control, DragDrop.DragOverEvent, transfer, default).Handled);
        Assert.False(Raise(control, DragDrop.DropEvent, transfer, default).Handled);
        Assert.Empty(endpoint.Output.ToString());
        control.ActiveVtProcessor!.Dispose();
    }

    [AvaloniaFact]
    public void RejectedAndMoveOnlyDropsNeverTransferOrDeleteSourceData()
    {
        TerminalControl control = CreateControl(out Endpoint endpoint);
        control.WriteOutput("\x1b]72;t=a\x1b\\"u8);
        using IDataTransfer transfer = TextTransfer("unaccepted");
        Raise(control, DragDrop.DragOverEvent, transfer, default);
        control.WriteOutput("\x1b]72;t=m:o=0\x1b\\"u8);
        endpoint.Output.Clear();
        Assert.Equal(DragDropEffects.None, Raise(control, DragDrop.DropEvent, transfer, default).DragEffects);
        Assert.Empty(endpoint.Output.ToString());
        Assert.Equal(DragDropEffects.None, Raise(control, DragDrop.DragOverEvent, transfer, default, DragDropEffects.Move).DragEffects);
        Assert.Empty(endpoint.Output.ToString());
        control.ActiveVtProcessor!.Dispose();
    }

    [AvaloniaFact]
    public void FailedOrStaleDataRetrievalDoesNotPublishADrop()
    {
        TerminalControl control = CreateControl(out Endpoint endpoint);
        control.WriteOutput("\x1b]72;t=a\x1b\\"u8);
        DataTransferItem item = new();
        item.Set(DataFormat.Text, () => throw new IOException("source unavailable"));
        DataTransfer data = new(); data.Add(item);
        Assert.Equal(DragDropEffects.None, Raise(control, DragDrop.DropEvent, data, default).DragEffects);
        Assert.Empty(endpoint.Output.ToString());
        item.Set(DataFormat.Text, () =>
        {
            control.StopPty(); // Simulate toolkit reentrancy while fetching drop data.
            return "stale";
        });
        Assert.Equal(DragDropEffects.None, Raise(control, DragDrop.DropEvent, data, default).DragEffects);
        Assert.Empty(endpoint.Output.ToString());
        control.ActiveVtProcessor!.Dispose();
    }

    [AvaloniaFact]
    public void MimeDiscoveryIsBoundedDoesNotFetchPayloadsAndRejectsUnsafeTokens()
    {
        DataTransfer data = new();
        DataTransferItem item = new();
        int reads = 0;
        item.Set(DataFormat.Text, () => { reads++; return "text"; });
        item.Set(DataFormat.CreateBytesPlatformFormat("application/octet-stream"), () => { reads++; return new byte[] { 0, 255, 27 }; });
        item.Set(DataFormat.CreateBytesPlatformFormat("text/unsafe name"), new byte[] { 1 });
        item.Set(DataFormat.CreateBytesApplicationFormat("private-data"), new byte[] { 2 });
        data.Add(item);
        var formats = TerminalDropDataReader.GetFormats(data);
        Assert.Equal(0, reads);
        Assert.Equal(new[] { "text/plain", "application/octet-stream" }, formats.Select(f => f.Mime));
        TerminalDropItem[] items = TerminalDropDataReader.Read(data, formats);
        Assert.Equal(2, reads);
        Assert.Equal("text"u8.ToArray(), items[0].Data.ToArray());
        Assert.Equal(new byte[] { 0, 255, 27 }, items[1].Data.ToArray());
        for (int i = 0; i < 30; i++) item.Set(DataFormat.CreateBytesPlatformFormat("application/type" + i), Array.Empty<byte>());
        // DataTransfer caches the aggregate format list; use a new view after source mutation.
        DataTransfer expanded = new(); expanded.Add(item);
        Assert.Equal(16, TerminalDropDataReader.GetFormats(expanded).Length);
    }

    private static TerminalControl CreateControl(out Endpoint endpoint)
    {
        TerminalControl control = new() { VtProcessorPreference = VtProcessorPreference.Managed };
        control.WriteOutput(""u8);
        endpoint = new();
        control.TerminalSessionService.AttachEndpoint(endpoint);
        return control;
    }

    [AvaloniaFact]
    public async Task FileDropsExposeEscapedUrisWithoutOpeningOrMovingFiles()
    {
        // Avalonia 12 forbids client implementations of IStorageItem. Use its
        // headless BCL provider and real, exclusively held files instead.
        DirectoryInfo directory = Directory.CreateTempSubdirectory("royalterminal-drop-");
        Window window = new();
        try
        {
            string firstPath = Path.Combine(directory.FullName, "a b.txt");
            string secondPath = Path.Combine(directory.FullName, OperatingSystem.IsWindows() ? "line%0Abreak.txt" : "line\nbreak.txt");
            await File.WriteAllTextAsync(firstPath, "first");
            await File.WriteAllTextAsync(secondPath, "second");
            using IStorageFile first = (await window.StorageProvider.TryGetFileFromPathAsync(firstPath))!;
            using IStorageFile second = (await window.StorageProvider.TryGetFileFromPathAsync(secondPath))!;
            Assert.NotNull(first); Assert.NotNull(second);
            DataTransfer data = new();
            DataTransferItem a = new(); a.Set(DataFormat.File, first); data.Add(a);
            DataTransferItem b = new(); b.Set(DataFormat.File, second); data.Add(b);
            using (FileStream firstLock = File.Open(firstPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (FileStream secondLock = File.Open(secondPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var formats = TerminalDropDataReader.GetFormats(data);
                Assert.Equal("text/uri-list", Assert.Single(formats).Mime);
                TerminalDropItem item = Assert.Single(TerminalDropDataReader.Read(data, formats));
                string uris = Encoding.UTF8.GetString(item.Data.Span);
                Assert.Equal(first.Path.AbsoluteUri + "\r\n" + second.Path.AbsoluteUri + "\r\n", uris);
                Assert.Contains("a%20b.txt", uris);
                Assert.Contains(OperatingSystem.IsWindows() ? "line%250Abreak.txt" : "line%0Abreak.txt", uris);
                Assert.Equal(2, uris.Count(c => c == '\n'));
            }
            Assert.Equal("first", await File.ReadAllTextAsync(firstPath));
            Assert.Equal("second", await File.ReadAllTextAsync(secondPath));
        }
        finally { window.Close(); directory.Delete(recursive: true); }
    }

    private static IDataTransfer TextTransfer(string text)
    {
        DataTransfer data = new();
        DataTransferItem item = new(); item.Set(DataFormat.Text, text); data.Add(item);
        return data;
    }

    private static DragEventArgs Raise(TerminalControl control, RoutedEvent<DragEventArgs> routedEvent,
        IDataTransfer data, Point position, DragDropEffects effects = DragDropEffects.Copy | DragDropEffects.Move)
    {
        DragEventArgs args = new(routedEvent, data, control, position, KeyModifiers.None) { DragEffects = effects };
        control.RaiseEvent(args);
        return args;
    }

    private sealed class Endpoint : ITerminalEndpoint
    {
        internal StringBuilder Output { get; } = new();
        public void SendText(ReadOnlySpan<byte> utf8) => Output.Append(Encoding.UTF8.GetString(utf8));
        public void SetFocus(bool focused) { }
        public void SetSize(int widthPx, int heightPx) { }
    }
}
