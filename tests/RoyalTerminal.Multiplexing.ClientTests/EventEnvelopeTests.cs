// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using RoyalTerminal.Multiplexing.Cli;
using Xunit;
namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class EventEnvelopeTests
{
    [Fact]
    public void WrapsScopedExitAndKeepsTheSessionResumeCursorDistinctFromTheBlockCursor()
    {
        var session=Guid.NewGuid();var id=Guid.NewGuid();
        using var record=JsonDocument.Parse($$"""{"sequence":120,"blockSequence":2,"blockId":"{{id}}","name":"terminal.child_exited","exitCode":37}""");
        using var envelope=JsonDocument.Parse(TerminalEventEnvelope.Write(session,null,record.RootElement));
        var e=envelope.RootElement;Assert.Equal("session_event",e.GetProperty("type").GetString());Assert.Equal(120,e.GetProperty("sequence").GetInt64());
        var wrapper=e.GetProperty("payload");Assert.Equal(session,wrapper.GetProperty("session_id").GetGuid());var block=wrapper.GetProperty("event");Assert.Equal("block_event",block.GetProperty("type").GetString());
        var body=block.GetProperty("payload");Assert.Equal(id,body.GetProperty("block_id").GetGuid());Assert.Equal("child_exited",body.GetProperty("name").GetString());Assert.Equal(37,body.GetProperty("payload").GetProperty("exit_code").GetInt32());
    }
    [Fact]
    public void RemovalPreservesAnEmptyRootIdAndDoesNotPretendToBeAnotherStatusUpdate()
    {
        using var record=JsonDocument.Parse("""{"sequence":3,"name":"terminal.program_status_changed","recordId":"","reason":"removed"}""");
        using var doc=JsonDocument.Parse(TerminalEventEnvelope.Write(Guid.NewGuid(),Guid.NewGuid(),record.RootElement));
        var b=doc.RootElement.GetProperty("payload").GetProperty("event").GetProperty("payload");Assert.Equal("program_status_removed",b.GetProperty("name").GetString());
        var fields=b.GetProperty("payload");Assert.Equal("",fields.GetProperty("ids")[0].GetString());Assert.False(fields.TryGetProperty("record",out _));
    }
}
