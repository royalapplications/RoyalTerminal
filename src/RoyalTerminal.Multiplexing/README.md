# Experimental terminal multiplexer

This package owns RoyalTerminal transport processes independently of client
connections. A session contains windows; each window has a binary split tree of
terminal blocks. Each block has an ordered, bounded output journal and independent
client subscriptions. A separately hosted broker lets terminals survive GUI and
network disconnection. It does **not** preserve running processes through broker
termination, machine restart, or process migration.

## Using existing transport providers

```csharp
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;

CompositeTerminalTransportFactory transports = new([
    new PipeTerminalTransportProvider(),
]);
await using MultiplexerBroker broker = new(transports);
MultiplexerSessionDescriptor session = broker.CreateSession("work");
MultiplexerBlockDescriptor block = await broker.AddBlockAsync(
    session.Id,
    "shell",
    new PipeTransportOptions(
        new TerminalCommandSpec("/bin/sh", []),
        WorkingDirectory: null,
        Environment: null,
        MergeStdErrIntoStdOut: true,
        Dimensions: new(100, 30, 1000, 600)));

using MultiplexerAttachment controller = broker.Attach(
    session.Id, block.Id, MultiplexerRole.Controller);
controller.SendInput("printf 'hello\\n'\n"u8);

await foreach (MultiplexerEvent item in controller.Events.ReadAllAsync())
{
    // Apply Resize, Output, and Exit in Sequence order.
    // Preserve UTF-8 decoder and VT parser state across output frame boundaries.
    if (item.Data is { } bytes)
        await Console.OpenStandardOutput().WriteAsync(bytes);
}
```

Interactive full-screen applications should use the existing PTY provider instead
of redirected process pipes. Neither this package nor its attachment model changes
PTY, ConPTY, SSH, or terminal parsing behavior.

## Lifetimes and concurrency

| Operation | Effect |
| --- | --- |
| Attachment disposal | Drops that subscription and releases its control lease; process continues. |
| Process exit | Publishes one exit event, completes subscribers after retained events, keeps the block inspectable. |
| Close block | Removes its layout leaf, collapses the containing split, and stops/disposes its transport. |
| Close session | Removes every window/block and stops all its processes. |
| Broker disposal | Waits for active and already-retiring blocks, including concurrent explicit closes. |

An attachment's role is assigned by the host after authentication. The broker does
not authenticate remote principals. At most one attachment controls input and
shared geometry. An observer never acquires authority implicitly. Once the
controller detaches, another explicit controller attachment can acquire it.

Transport callbacks are serialized through a per-block event gate. Input and
resize have a separate operation gate, so a transport may report output from a
different thread while handling those operations. Lifecycle methods are never
invoked while holding the event gate. Startup cancellation, failed startup, exit,
close, and broker shutdown each retain one cleanup path.

## Replay contract

Every block begins with an initial `Resize` event at sequence 1. Output, later
geometry changes, exit, and explicit close share the same sequence. Attach takes
its replay and subscribes to future events atomically. Output buffers are owned by
the journal; each subscriber gets its own copy, so transport buffer reuse or a
consumer mutation cannot alter another consumer's data.

`lastSequence = 0` means a new terminal that needs the complete stream. A positive
cursor means the client has already applied every event through that cursor and
retained the corresponding terminal state. The API rejects an ahead-of-stream
cursor. Once required events have been evicted, it throws `HistoryGap` rather than
feeding an arbitrary VT suffix into a new parser. The package does not currently
produce authoritative full-state snapshots; a host may add this capability using
RoyalTerminal's VT snapshot interfaces and a consistent event barrier.

Input is sent once and is never recorded or retransmitted by this package. A
network adapter must not retry input automatically after losing an acknowledgement:
it cannot prove whether the process already consumed those bytes.

## Resource bounds

`MultiplexerOptions` bounds sessions, windows, blocks, attachments, journal events
and bytes, output frame size, per-reader queue events and bytes, input size,
dimensions, and label length. An attachment whose replay exceeds its queue budget
is rejected. A live reader that exceeds its budget is disconnected with
`CapacityExceeded`; it cannot block output to other readers. Journal eviction
continues while every client is detached. Exited blocks count against block quotas
until explicitly closed. These limits do not constrain the memory or CPU of child
processes; OS process isolation belongs to the host.

## Layout revisions

Session, window, and block label changes and layout mutations increment the
session's structural revision. Mutations accepting `expectedRevision` reject stale
edits with `Conflict`. A replacement window layout must contain every block assigned
to that window exactly once, with finite split ratios strictly between 0 and 1.
Transport output and attachment activity do not invalidate structural revisions.

## Reference decisions

The implementation reuses RoyalTerminal's transport contracts and leaves terminal
emulation unchanged. The following public source was inspected for the boundaries:

- [Windows Terminal ConptyConnection](https://github.com/microsoft/terminal/blob/main/src/cascadia/TerminalConnection/ConptyConnection.cpp):
  closing the connection destroys the pseudoconsole and ends attached processes;
  therefore a viewer detach cannot call the owned transport's stop method.
- [Ghostty Terminal resize](https://github.com/ghostty-org/ghostty/blob/main/src/terminal/Terminal.zig):
  dimensions must be valid before mutation; pixel-only changes still matter, and
  primary/alternate screen resize semantics differ. Preserve ordered geometry
  events rather than reconstructing state from output bytes alone.
- [xterm.js AttachAddon](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-attach/src/AttachAddon.ts):
  bidirectional and observation-only attachment are distinct, and socket teardown
  releases subscription listeners.
- [xterm.js SerializeAddon](https://github.com/xtermjs/xterm.js/blob/master/addons/addon-serialize/src/SerializeAddon.ts):
  terminal reconstruction includes primary/alternate buffers, modes, and scroll
  regions. An evicted byte stream cannot be silently treated as a complete snapshot.

Run the package's behavioral and real-process tests with:

```sh
dotnet test tests/RoyalTerminal.Multiplexing.Tests -c Release
```
