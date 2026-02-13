// Licensed under the MIT License.
// GhosttySharp - .NET bindings for the Ghostty terminal emulator.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GhosttySharp.Native;

namespace GhosttySharp;

/// <summary>
/// Represents the Ghostty application lifecycle.
/// Manages runtime callbacks, app creation, event loop ticking, and global key handling.
/// </summary>
public sealed class GhosttyApp : IDisposable
{
    private nint _handle;
    private bool _disposed;
    private GCHandle _gcHandle;
    private GhosttyRuntimeConfig _runtimeConfig;

    // Store delegates to prevent GC collection of callback pointers
#pragma warning disable CS0169 // Used to prevent GC collection of delegates pinned as unmanaged function pointers
    private readonly GhosttyWakeupCallback? _wakeupDelegate;
    private readonly GhosttyActionCallback? _actionDelegate;
    private readonly GhosttyReadClipboardCallback? _readClipboardDelegate;
    private readonly GhosttyConfirmReadClipboardCallback? _confirmReadClipboardDelegate;
    private readonly GhosttyWriteClipboardCallback? _writeClipboardDelegate;
    private readonly GhosttyCloseSurfaceCallback? _closeSurfaceDelegate;
#pragma warning restore CS0169

    /// <summary>Gets the native app handle.</summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Returns true if the app handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Fired when the event loop wakeup is requested.</summary>
    public event Action? WakeupRequested;

    /// <summary>Fired when an action needs to be handled.</summary>
    public event Func<GhosttyTarget, GhosttyAction, bool>? ActionRequested;

    /// <summary>Fired when clipboard read is requested.</summary>
    public event Action<GhosttyClipboard, nint>? ClipboardReadRequested;

    /// <summary>Fired when clipboard read confirmation is requested.</summary>
    public event Action<string?, nint, GhosttyClipboardRequest>? ClipboardConfirmReadRequested;

    /// <summary>Fired when clipboard write is requested.</summary>
    public event Action<GhosttyClipboard, nint, nuint, bool>? ClipboardWriteRequested;

    /// <summary>Fired when a surface close is requested.</summary>
    public event Action<bool>? SurfaceCloseRequested;

    /// <summary>
    /// Creates a new Ghostty application with the specified configuration.
    /// </summary>
    /// <param name="config">The configuration to use. Will be consumed by the app.</param>
    /// <param name="supportsSelectionClipboard">Whether the platform supports selection clipboard.</param>
    public unsafe GhosttyApp(GhosttyConfig config, bool supportsSelectionClipboard = false)
    {
        NativeLibraryLoader.Initialize();

        _gcHandle = GCHandle.Alloc(this);

        _runtimeConfig = new GhosttyRuntimeConfig
        {
            Userdata = GCHandle.ToIntPtr(_gcHandle),
            SupportsSelectionClipboard = supportsSelectionClipboard,
            WakeupCb = &OnWakeupNative,
            ActionCb = &OnActionNative,
            ReadClipboardCb = &OnReadClipboardNative,
            ConfirmReadClipboardCb = &OnConfirmReadClipboardNative,
            WriteClipboardCb = &OnWriteClipboardNative,
            CloseSurfaceCb = &OnCloseSurfaceNative,
        };

        fixed (GhosttyRuntimeConfig* runtimePtr = &_runtimeConfig)
        {
            _handle = GhosttyNative.AppNew(runtimePtr, config.Handle);
        }

        if (_handle == nint.Zero)
            throw new InvalidOperationException("Failed to create Ghostty application.");
    }

    /// <summary>Ticks the event loop. Should be called when the wakeup callback fires.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.AppTick(_handle);
    }

    /// <summary>Updates the app focus state.</summary>
    public void SetFocus(bool focused)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.AppSetFocus(_handle, (byte)(focused ? 1 : 0));
    }

    /// <summary>Sends a key event to the application.</summary>
    public bool SendKey(GhosttyInputKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyNative.AppKey(_handle, key) != 0;
    }

    /// <summary>Checks if a key event would trigger a binding.</summary>
    public bool IsKeyBinding(GhosttyInputKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyNative.AppKeyIsBinding(_handle, key) != 0;
    }

    /// <summary>Notifies the app that the keyboard layout has changed.</summary>
    public void NotifyKeyboardChanged()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.AppKeyboardChanged(_handle);
    }

    /// <summary>Opens the configuration file.</summary>
    public void OpenConfig()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.AppOpenConfig(_handle);
    }

    /// <summary>Updates the app configuration.</summary>
    public void UpdateConfig(GhosttyConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.AppUpdateConfig(_handle, config.Handle);
    }

    /// <summary>Checks if the app needs quit confirmation from the user.</summary>
    public bool NeedsConfirmQuit
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyNative.AppNeedsConfirmQuit(_handle) != 0;
        }
    }

    /// <summary>Checks if the app has any global key bindings.</summary>
    public bool HasGlobalKeybinds
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyNative.AppHasGlobalKeybinds(_handle) != 0;
        }
    }

    /// <summary>Sets the color scheme for the entire application.</summary>
    public void SetColorScheme(GhosttyColorScheme scheme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyNative.AppSetColorScheme(_handle, scheme);
    }

    // Native callback trampolines using UnmanagedCallersOnly

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWakeupNative(nint userdata)
    {
        if (userdata == nint.Zero) return;
        if (GCHandle.FromIntPtr(userdata).Target is GhosttyApp app)
            app.WakeupRequested?.Invoke();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte OnActionNative(nint appHandle, GhosttyTarget target, GhosttyAction action)
    {
        // We need to find our app from the userdata, which is stored in the runtime config
        var userdata = GhosttyNative.AppUserdata(appHandle);
        if (userdata == nint.Zero) return 0;

        if (GCHandle.FromIntPtr(userdata).Target is GhosttyApp app)
        {
            var result = app.ActionRequested?.Invoke(target, action);
            return (byte)(result == true ? 1 : 0);
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnReadClipboardNative(nint userdata, GhosttyClipboard clipboard, nint state)
    {
        if (userdata == nint.Zero) return;
        if (GCHandle.FromIntPtr(userdata).Target is GhosttyApp app)
            app.ClipboardReadRequested?.Invoke(clipboard, state);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnConfirmReadClipboardNative(nint userdata, nint str, nint state, GhosttyClipboardRequest request)
    {
        if (userdata == nint.Zero) return;
        if (GCHandle.FromIntPtr(userdata).Target is GhosttyApp app)
        {
            var text = str != nint.Zero ? Marshal.PtrToStringUTF8(str) : null;
            app.ClipboardConfirmReadRequested?.Invoke(text, state, request);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWriteClipboardNative(nint userdata, GhosttyClipboard clipboard, nint content, nuint len, byte confirm)
    {
        if (userdata == nint.Zero) return;
        if (GCHandle.FromIntPtr(userdata).Target is GhosttyApp app)
            app.ClipboardWriteRequested?.Invoke(clipboard, content, len, confirm != 0);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnCloseSurfaceNative(nint userdata, byte processAlive)
    {
        if (userdata == nint.Zero) return;
        if (GCHandle.FromIntPtr(userdata).Target is GhosttyApp app)
            app.SurfaceCloseRequested?.Invoke(processAlive != 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero)
        {
            GhosttyNative.AppFree(_handle);
            _handle = nint.Zero;
        }

        if (_gcHandle.IsAllocated)
            _gcHandle.Free();
    }
}
