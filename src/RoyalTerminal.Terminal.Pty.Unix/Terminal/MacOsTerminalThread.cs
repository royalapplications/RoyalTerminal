// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Creates the pthread before entering the CLR: .NET-created threads call
/// pthread_setschedparam and permanently opt out of Apple's QoS system.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacOsTerminalThread(Action action, string name) : ITerminalThread
{
    private Action? _action = action;
    private readonly string _name = name;
    private readonly object _sync = new();
    private nint _pthread;
    private bool _started;
    private int _managedId;
    private ExceptionDispatchInfo? _failure;
    private ExecutionContext? _executionContext;

    public bool IsCurrent => Volatile.Read(ref _managedId) == Environment.CurrentManagedThreadId;

    public void Start()
    {
        lock (_sync)
        {
            if (_started) throw new ThreadStateException("The terminal worker has already started.");
            Attributes attributes = default;
            Check(InitializeAttributes(ref attributes));
            try
            {
                // Ghostty gives both read-pipeline stages user-initiated QoS.
                // Keep that policy for the complete owned worker lifetime.
                Check(SetQos(ref attributes, 0x19, 0));
                // Darwin's 512-KiB pthread default is too small for general CLR work.
                Check(SetStackSize(ref attributes, 4 * 1024 * 1024));
                _executionContext = ExecutionContext.Capture();
                GCHandle context = GCHandle.Alloc(this);
                int error;
                try { error = Create(out _pthread, ref attributes, &Run, GCHandle.ToIntPtr(context)); }
                catch { context.Free(); throw; }
                if (error != 0)
                {
                    context.Free();
                    _pthread = 0;
                    Check(error);
                }
                _started = true;
            }
            finally { _ = DestroyAttributes(ref attributes); }
        }
    }

    public void Join()
    {
        // Never take the join lock from the worker: another caller may hold it
        // while waiting for this very callback (including self-disposal) to exit.
        if (IsCurrent) throw new InvalidOperationException("A terminal worker cannot join itself.");
        lock (_sync)
        {
            if (!_started) throw new ThreadStateException("The terminal worker has not started.");
            if (_pthread != 0)
            {
                Check(JoinNative(_pthread, out _));
                _pthread = 0;
                GC.SuppressFinalize(this);
            }
            _failure?.Throw();
        }
        GC.KeepAlive(this);
    }

    ~MacOsTerminalThread()
    {
        // A callback roots this owner until it finishes. If an embedder abandons
        // the completed worker without joining, release its joinable resources.
        if (_pthread != 0) _ = Detach(_pthread);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint Run(nint state)
    {
        GCHandle context = GCHandle.FromIntPtr(state);
        MacOsTerminalThread owner = (MacOsTerminalThread)context.Target!;
        try
        {
            Volatile.Write(ref owner._managedId, Environment.CurrentManagedThreadId);
            Thread.CurrentThread.IsBackground = true;
            Thread.CurrentThread.Name = owner._name;
            if (owner._executionContext is { } executionContext)
                ExecutionContext.Run(executionContext, static state => ((MacOsTerminalThread)state!)._action!(), owner);
            else owner._action!();
        }
        catch (Exception exception)
        {
            // Managed exceptions must never unwind through pthread's C entry point.
            owner._failure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            Volatile.Write(ref owner._managedId, 0);
            owner._executionContext = null;
            owner._action = null;
            context.Free();
        }
        return 0;
    }

    private static void Check(int error)
    {
        if (error != 0) throw new Win32Exception(error, "Unable to create or join a terminal pthread.");
    }

    // Public Darwin LP64 ABI: sys/_pthread/_pthread_types.h, both arm64 and x64.
    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        public long Signature;
        public fixed byte Opaque[56];
    }

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_attr_init")]
    private static partial int InitializeAttributes(ref Attributes attributes);
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_attr_destroy")]
    private static partial int DestroyAttributes(ref Attributes attributes);
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_attr_set_qos_class_np")]
    private static partial int SetQos(ref Attributes attributes, uint qosClass, int priority);
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_attr_setstacksize")]
    private static partial int SetStackSize(ref Attributes attributes, nuint stackSize);
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_create")]
    private static partial int Create(out nint thread, ref Attributes attributes,
        delegate* unmanaged[Cdecl]<nint, nint> callback, nint state);
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_join")]
    private static partial int JoinNative(nint thread, out nint result);
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_detach")]
    private static partial int Detach(nint thread);
}
