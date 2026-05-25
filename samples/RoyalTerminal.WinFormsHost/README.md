# RoyalTerminal WinForms Host Sample

This sample embeds `TerminalControl` in a Windows Forms application using
`Avalonia.Win32.Interoperability.WinFormsAvaloniaControlHost`.

The bridge demonstrates the three WinForms-specific details hosts should keep:

- set `TerminalControl.Padding` for visual breathing room when the host control is docked edge-to-edge;
- forward `WM_SETFOCUS` to Avalonia by posting `Terminal.Focus()` at `DispatcherPriority.Input`;
- enable PerMonitorV2 DPI and replay `DeviceDpi / 96.0` through `Terminal.SetContentScale(...)`.

Run on Windows:

```powershell
dotnet run --project samples/RoyalTerminal.WinFormsHost/RoyalTerminal.WinFormsHost.csproj
```
