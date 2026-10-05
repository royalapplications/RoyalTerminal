// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoyalTerminal.Terminal;

/// <summary>Platform scheduling for session-owned terminal IO threads.</summary>
public static partial class UnixThreadScheduling
{
    /// <summary>
    /// Creates an unstarted, dedicated background terminal worker. On macOS it
    /// uses pthread creation attributes to retain user-initiated QoS when entering
    /// managed code; other platforms use a standard managed thread. The owner must
    /// arrange cancellation and Join before releasing resources used by the callback.
    /// </summary>
    /// <param name="action">Serial work to execute. Callback failures are rethrown by Join.</param>
    /// <param name="name">The worker's diagnostic name.</param>
    public static ITerminalThread CreateUserInitiatedThread(Action action, string name)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(name);
        return OperatingSystem.IsMacOS()
            ? new MacOsTerminalThread(action, name)
            : new ManagedTerminalThread(action, name);
    }

    /// <summary>
    /// Requests macOS user-initiated QoS for the current dedicated terminal thread.
    /// Returns false if the OS rejects the request, leaving its existing policy in effect.
    /// </summary>
    /// <remarks>
    /// Requests Ghostty's gather/parse policy, never on shared pool threads. Runtimes that
    /// call pthread_setschedparam during thread startup opt those threads out of macOS QoS;
    /// on those runtimes this is a best-effort request and returns false (EPERM).
    /// </remarks>
    [SupportedOSPlatform("macos")]
    public static bool TrySetCurrentThreadUserInitiated()
        => TrySetCurrentThreadUserInitiated(out _);

    [SupportedOSPlatform("macos")]
    internal static bool TrySetCurrentThreadUserInitiated(out int error)
    {
        if (Thread.CurrentThread.IsThreadPoolThread)
        {
            error = 1; // EPERM: never change a shared runtime thread's policy.
            return false;
        }

        error = SetQosClass(0x19, 0);
        return error == 0;
    }

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_set_qos_class_self_np")]
    private static partial int SetQosClass(uint qosClass, int relativePriority);
}
