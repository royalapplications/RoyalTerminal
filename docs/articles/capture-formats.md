---
title: Capture Formats
---

# Capture Formats

RoyalTerminal capture files store a terminal session as a timed event stream, not as rendered pixels. The same `TerminalCaptureSession` document can drive replay inside `TerminalControl`, be saved for support/debugging, or be converted to a different recording format.

The built-in persistence layer supports the native RoyalTerminal JSON capture format and [asciicast v3](https://docs.asciinema.org/manual/asciicast/v3/) files. Hosts can add more formats through the same registry API used by the reusable app shell.

## Capture model

`TerminalCaptureRuntime` is the Avalonia-facing coordinator. It subscribes to `TerminalControl.DataReceived`, `TerminalControl.TerminalResized`, `TerminalControl.ProcessExited`, and `TerminalSessionService.InputSent`, then forwards those events to `TerminalCaptureRecorder`.

| Capture event | Source | Replay behavior |
| --- | --- | --- |
| `Output` | Bytes written to the terminal output pipeline. | Written back to the control. |
| `Input` | Bytes sent through session input routing. | Preserved in the timeline but not replayed into a live transport. |
| `Resize` | Terminal column/row changes. | Applied to the control before later output events. |
| `Marker` | Navigation labels loaded from formats that support them. | Preserved for timeline-aware hosts. |
| `Exit` | Process exit status. | Preserved for diagnostics. |

Replay uses the event offsets to support play, pause, stop, and seek. A progress bar is host UI state derived from `TerminalCaptureSession.DurationMilliseconds` and the current replay position; it is not a separate field in the file format.

## Format API

The capture format API lives in `RoyalTerminal.Terminal` and has no Avalonia dependency.

| Type | Purpose |
| --- | --- |
| `ITerminalCaptureSessionFormat` | Reads and writes one concrete file format. |
| `TerminalCaptureFileFormatDescriptor` | Stable id, display name, default extension, supported extensions, and MIME types. |
| `TerminalCaptureSessionFormatRegistry` | Finds formats by id or file name and probes registered formats on load. |
| `TerminalCaptureSessionFormats` | Built-in format instances and the default registry. |
| `TerminalCaptureSessionSerializer` | Compatibility helper for the native JSON format plus explicit-format overloads. |

Use `TerminalCaptureSessionFormats.DefaultRegistry` when a host should load files selected by users. The registry first tries the format inferred from the file name, then probes the remaining registered formats.

```csharp
using RoyalTerminal.Terminal;

TerminalCaptureSession captured = captureRuntime.StopCapture();

await TerminalCaptureSessionSerializer.SaveToFileAsync(
    captured,
    "session.rtcap.json");

await TerminalCaptureSessionSerializer.SaveToFileAsync(
    captured,
    "session.cast",
    TerminalCaptureSessionFormats.AsciicastV3);

await using FileStream input = File.OpenRead("session.cast");
TerminalCaptureSession loaded = await TerminalCaptureSessionFormats.DefaultRegistry
    .LoadAsync(input, "session.cast");
```

To add a host-specific format, implement `ITerminalCaptureSessionFormat`, expose a `TerminalCaptureFileFormatDescriptor`, then build a registry that includes the built-ins and the custom format.

```csharp
using RoyalTerminal.Terminal;

List<ITerminalCaptureSessionFormat> formats =
[
    ..TerminalCaptureSessionFormats.BuiltIn,
    new MyCaptureSessionFormat(),
];

TerminalCaptureSessionFormatRegistry registry = new(formats);
```

## Built-in formats

| Format id | Display name | Extension | Best use |
| --- | --- | --- | --- |
| `royalterminal-json` | RoyalTerminal JSON | `.rtcap.json` | Lossless RoyalTerminal capture/replay documents, including binary output and input payloads. |
| `asciicast-v3` | Asciicast v3 | `.cast` | Interoperability with asciinema-compatible tooling and web players. |

The native JSON format is the default used by existing serializer overloads. It stores the `TerminalCaptureSession` model directly and remains the safest choice when a recording can contain arbitrary terminal bytes.

The asciicast v3 format writes a JSON header followed by newline-delimited event arrays. RoyalTerminal maps capture events as follows:

| RoyalTerminal event | Asciicast code | Data |
| --- | --- | --- |
| `Output` | `o` | UTF-8 output text. |
| `Input` | `i` | UTF-8 input text. |
| `Resize` | `r` | `columnsxrows`, for example `120x36`. |
| `Marker` | `m` | Marker label. |
| `Exit` | `x` | Process exit code. |

Asciicast intervals are stored as seconds relative to the previous written event. RoyalTerminal converts them to absolute millisecond offsets when loading so replay can seek deterministically.

Because asciicast payloads are JSON strings, output and input chunks must be valid UTF-8. The writer handles UTF-8 sequences split across adjacent chunks, but it rejects invalid or incomplete sequences. Use RoyalTerminal JSON when byte-for-byte preservation matters more than asciicast interoperability.

## Shared shell behavior

`RoyalApps.RoyalTerminal.Avalonia.App` exposes the built-in formats in the Session menu's `Capture Format` submenu. `Save Capture` uses the selected format's descriptor to choose the default extension and file type. `Load Replay` accepts RoyalTerminal JSON and asciicast v3 files, then uses the default registry to infer or probe the recording format before loading the replay timeline.
