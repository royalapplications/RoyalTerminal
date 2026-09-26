// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

internal interface INativeNotificationTransport : IDisposable
{
    void Send(ReadOnlySpan<byte> command);
    byte[]? Poll();
}

// No managed callback pointers or platform callback blocks cross the boundary. Native
// completion blocks own their cleanup even when a managed session has ended.
internal sealed partial class NativeNotificationTransport : INativeNotificationTransport
{
    private readonly object _sync = new();
    private readonly NativeNotificationHandle _handle;
    private const string Library = "royalterminal-notifications";

    internal NativeNotificationTransport()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        _handle = new(Create());
        if (_handle.IsInvalid) { _handle.Dispose(); throw new InvalidOperationException("The native notification host is unavailable."); }
    }

    public unsafe void Send(ReadOnlySpan<byte> command)
    {
        if (command.Length > 8 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(command));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            fixed (byte* bytes = command)
                if (Command(_handle, bytes, (nuint)command.Length) == 0) throw new InvalidOperationException("Notification command rejected.");
        }
    }

    public unsafe byte[]? Poll()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            nint bytes = PollNative(_handle, out nuint count);
            if (bytes == 0) return null;
            try
            {
                if (count > 256 * 1024) throw new InvalidOperationException("Oversized notification state.");
                return new ReadOnlySpan<byte>((void*)bytes, (int)count).ToArray();
            }
            finally { Free(bytes); }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _handle.Dispose();
        }
    }

    [LibraryImport(Library, EntryPoint = "rt_notifications_create")] private static partial nint Create();
    [LibraryImport(Library, EntryPoint = "rt_notifications_command")] private static unsafe partial int Command(NativeNotificationHandle handle, byte* bytes, nuint count);
    [LibraryImport(Library, EntryPoint = "rt_notifications_poll")] private static partial nint PollNative(NativeNotificationHandle handle, out nuint count);
    [LibraryImport(Library, EntryPoint = "rt_notifications_free")] private static partial void Free(nint bytes);
    [LibraryImport(Library, EntryPoint = "rt_notifications_destroy")] private static partial void Destroy(nint handle);

    private sealed class NativeNotificationHandle : SafeHandle
    {
        public NativeNotificationHandle() : base(0, ownsHandle: true) { }
        internal NativeNotificationHandle(nint value) : this() => SetHandle(value);
        public override bool IsInvalid => handle == 0;
        protected override bool ReleaseHandle() { Destroy(handle); return true; }
    }
}

internal sealed class NativeNotificationCommand
{
    public string Op { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? Replaces { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Application { get; set; }
    public string[] Icons { get; set; } = [];
    public byte[]? Image { get; set; }
    public string[] Buttons { get; set; } = [];
    public string Sound { get; set; } = "system";
    public int Urgency { get; set; } = 1;
}

internal sealed class NativeNotificationState
{
    public bool Ready { get; set; }
    public int Capabilities { get; set; }
    public NativeNotificationEvent[] Events { get; set; } = [];
}

internal sealed class NativeNotificationEvent
{
    public string Kind { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public bool Success { get; set; }
    public int Button { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NativeNotificationCommand))]
[JsonSerializable(typeof(NativeNotificationState))]
internal partial class NativeNotificationJsonContext : JsonSerializerContext { }
