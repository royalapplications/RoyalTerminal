// Copyright (c) Royal Apps. Licensed under the MIT license.
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;
using Xunit;
namespace RoyalTerminal.Multiplexing.InspectionTests;

public sealed class DisplayCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADisplayMutationNeverConsumesOrCompletesPendingChildBytes(bool utf8)
    {
        var wire = new Wire(); var dimensions = new TerminalSessionDimensions(24,4,0,0);
        using var terminal = new InspectingTerminalTransport(wire,dimensions);
        var options = new PipeTransportOptions(new("unused",[]),null,null,true,dimensions);
        await terminal.StartAsync(options);
        var output = new List<byte>(); terminal.DataReceived += (bytes,n) => output.AddRange(bytes.AsSpan(0,n).ToArray());
        var prefix = utf8 ? new byte[]{0xe2,0x82} : "\u001b[2;"u8.ToArray();
        wire.Emit(prefix);
        Assert.Equal(MultiplexerErrorCode.Conflict, Assert.Throws<MultiplexerException>(()=>terminal.ResetDisplay()).Code);
        Assert.Equal(MultiplexerErrorCode.Conflict, Assert.Throws<MultiplexerException>(()=>terminal.SetDisplayColors("#010203",null,null)).Code);
        Assert.Equal(prefix,output.ToArray());
        var suffix = utf8 ? new byte[]{0xac} : "3HX"u8.ToArray();wire.Emit(suffix);
        Assert.Contains(utf8 ? "€" : "X",terminal.Capture(TerminalSnapshotExportFormat.PlainText));
        var count=output.Count;
        Assert.Throws<ArgumentException>(()=>terminal.SetDisplayColors("#010203",null,"red\u001b]52;"));
        Assert.Equal(count,output.Count);
        terminal.SetDisplayColors("#010203",null,null);terminal.ResetDisplay();
        Assert.EndsWith("\u001bc",System.Text.Encoding.UTF8.GetString(output.ToArray()));
        Assert.True(wire.IsRunning);Assert.Empty(wire.Input);
    }
    private sealed class Wire : ITerminalTransport
    {
        public event Action<byte[],int>? DataReceived;
        public event Action<int>? ProcessExited { add {} remove {} }
        public List<byte> Input {get;}=[];
        public bool IsRunning {get;private set;}
        public ValueTask StartAsync(ITerminalTransportOptions options,CancellationToken cancellationToken=default){IsRunning=true;return ValueTask.CompletedTask;}
        internal void Emit(byte[] bytes)=>DataReceived?.Invoke(bytes,bytes.Length);
        public void SendInput(ReadOnlySpan<byte> bytes)=>Input.AddRange(bytes.ToArray());
        public ValueTask StopAsync(){IsRunning=false;return ValueTask.CompletedTask;}
        public void Dispose()=>IsRunning=false;
        public void Resize(TerminalSessionDimensions dimensions){}
    }
}
