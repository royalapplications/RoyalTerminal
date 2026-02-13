// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Runtime.InteropServices;

namespace GhosttySharp.Native;

/// <summary>Callback invoked to wake up the application event loop.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void GhosttyWakeupCallback(nint userdata);

/// <summary>Callback invoked when an action needs to be handled by the runtime.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
[return: MarshalAs(UnmanagedType.U1)]
public delegate bool GhosttyActionCallback(nint app, GhosttyTarget target, GhosttyAction action);

/// <summary>Callback invoked to read clipboard contents.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void GhosttyReadClipboardCallback(nint userdata, GhosttyClipboard clipboard, nint state);

/// <summary>Callback invoked to confirm a clipboard read operation.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void GhosttyConfirmReadClipboardCallback(nint userdata, nint str, nint state, GhosttyClipboardRequest request);

/// <summary>Callback invoked to write clipboard contents.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void GhosttyWriteClipboardCallback(nint userdata, GhosttyClipboard clipboard, nint content, nuint len, [MarshalAs(UnmanagedType.U1)] bool confirm);

/// <summary>Callback invoked when a surface should be closed.</summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void GhosttyCloseSurfaceCallback(nint userdata, [MarshalAs(UnmanagedType.U1)] bool processAlive);
