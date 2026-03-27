// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.GhosttySharp — extended libghostty-vt bindings.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    private static readonly nint s_nativeLibraryHandle = LoadNativeLibraryHandle();

    /// <summary>
    /// Checks whether <c>libghostty-vt</c> is available at runtime.
    /// </summary>
    public static bool IsAvailable()
    {
        if (ShouldSkipNativeAvailabilityProbe())
        {
            return false;
        }

        return s_nativeLibraryHandle != nint.Zero;
    }

    private static bool ShouldSkipNativeAvailabilityProbe()
    {
        string? disableProbe = Environment.GetEnvironmentVariable("ROYALTERMINAL_DISABLE_GHOSTTY_PROBE");
        if (string.Equals(disableProbe, "1", StringComparison.Ordinal) ||
            string.Equals(disableProbe, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool runningInCi =
            string.Equals(Environment.GetEnvironmentVariable("CI"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "1", StringComparison.Ordinal) ||
            string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

        return runningInCi && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());
    }

    private static nint LoadNativeLibraryHandle()
    {
        NativeLibraryLoader.Initialize();
        return NativeLibrary.TryLoad(LibName, typeof(GhosttyVtNative).Assembly, null, out nint handle)
            ? handle
            : nint.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GhosttyString
    {
        public GhosttyString(nint ptr, nuint len)
        {
            Ptr = ptr;
            Len = len;
        }

        public readonly nint Ptr;
        public readonly nuint Len;

        public bool IsEmpty => Ptr == nint.Zero || Len == 0;

        public string ToUtf8String()
        {
            if (IsEmpty)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUTF8(Ptr, checked((int)Len)) ?? string.Empty;
        }

        public byte[] ToArray()
        {
            if (IsEmpty)
            {
                return [];
            }

            byte[] data = new byte[checked((int)Len)];
            Marshal.Copy(Ptr, data, 0, data.Length);
            return data;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct GhosttyMode : IEquatable<GhosttyMode>
    {
        public GhosttyMode(ushort value)
        {
            Value = value;
        }

        public ushort Value { get; }

        public bool Equals(GhosttyMode other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is GhosttyMode other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static implicit operator ushort(GhosttyMode mode) => mode.Value;
        public static implicit operator GhosttyMode(ushort value) => new(value);
        public override string ToString() => Value.ToString();
    }

    public static GhosttyMode CreateMode(ushort value, bool ansi)
    {
        return (ushort)((value & 0x7FFF) | (ansi ? 0x8000 : 0));
    }

    public static GhosttyMode ModeDecckm => CreateMode(1, ansi: false);
    public static GhosttyMode ModeKeypadKeys => CreateMode(66, ansi: false);
    public static GhosttyMode ModeAltScreen => CreateMode(1047, ansi: false);
    public static GhosttyMode ModeAltScreenSave => CreateMode(1049, ansi: false);
    public static GhosttyMode ModeBracketedPaste => CreateMode(2004, ansi: false);
    public static GhosttyMode ModeFocusEvent => CreateMode(1004, ansi: false);
    public static GhosttyMode ModeColorSchemeReport => CreateMode(2031, ansi: false);
    public static GhosttyMode ModeInBandResize => CreateMode(2048, ansi: false);

    public enum GhosttyModeReportState : int
    {
        NotRecognized = 0,
        Set = 1,
        Reset = 2,
        PermanentlySet = 3,
        PermanentlyReset = 4,
    }

    public enum GhosttyOptimizeMode : int
    {
        Debug = 0,
        ReleaseSafe = 1,
        ReleaseSmall = 2,
        ReleaseFast = 3,
    }

    public enum GhosttyBuildInfoData : int
    {
        Invalid = 0,
        Simd = 1,
        KittyGraphics = 2,
        TmuxControlMode = 3,
        Optimize = 4,
    }

    public enum GhosttyFocusEvent : int
    {
        Gained = 0,
        Lost = 1,
    }

    public enum GhosttySizeReportStyle : int
    {
        Mode2048 = 0,
        Csi14T = 1,
        Csi16T = 2,
        Csi18T = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttySizeReportSize
    {
        public ushort Rows;
        public ushort Columns;
        public uint CellWidth;
        public uint CellHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyDeviceAttributesPrimary
    {
        public ushort ConformanceLevel;
        public fixed ushort Features[64];
        public nuint NumFeatures;

        public static GhosttyDeviceAttributesPrimary Create()
        {
            return new GhosttyDeviceAttributesPrimary();
        }

        public void SetFeature(int index, ushort value)
        {
            if ((uint)index >= 64)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            fixed (ushort* features = Features)
            {
                features[index] = value;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyDeviceAttributesSecondary
    {
        public ushort DeviceType;
        public ushort FirmwareVersion;
        public ushort RomCartridge;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyDeviceAttributesTertiary
    {
        public uint UnitId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyDeviceAttributes
    {
        public GhosttyDeviceAttributesPrimary Primary;
        public GhosttyDeviceAttributesSecondary Secondary;
        public GhosttyDeviceAttributesTertiary Tertiary;

        public static GhosttyDeviceAttributes Create()
        {
            return new GhosttyDeviceAttributes
            {
                Primary = GhosttyDeviceAttributesPrimary.Create(),
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyPointCoordinate
    {
        public ushort X;
        public uint Y;
    }

    public enum GhosttyPointTag : int
    {
        Active = 0,
        Viewport = 1,
        Screen = 2,
        History = 3,
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct GhosttyPointValue
    {
        [FieldOffset(0)]
        public GhosttyPointCoordinate Coordinate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GhosttyPoint
    {
        public GhosttyPointTag Tag;
        public GhosttyPointValue Value;

        public static GhosttyPoint Active(ushort x, uint y) => Create(GhosttyPointTag.Active, x, y);
        public static GhosttyPoint Viewport(ushort x, uint y) => Create(GhosttyPointTag.Viewport, x, y);
        public static GhosttyPoint Screen(ushort x, uint y) => Create(GhosttyPointTag.Screen, x, y);
        public static GhosttyPoint History(ushort x, uint y) => Create(GhosttyPointTag.History, x, y);

        private static GhosttyPoint Create(GhosttyPointTag tag, ushort x, uint y)
        {
            return new GhosttyPoint
            {
                Tag = tag,
                Value = new GhosttyPointValue
                {
                    Coordinate = new GhosttyPointCoordinate
                    {
                        X = x,
                        Y = y,
                    },
                },
            };
        }
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_build_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult BuildInfo(GhosttyBuildInfoData data, void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_focus_encode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult FocusEncode(
        GhosttyFocusEvent @event,
        byte* buffer,
        nuint bufferLength,
        out nuint written);

    [LibraryImport(LibName, EntryPoint = "ghostty_mode_report_encode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult ModeReportEncode(
        GhosttyMode mode,
        GhosttyModeReportState state,
        byte* buffer,
        nuint bufferLength,
        out nuint written);

    [LibraryImport(LibName, EntryPoint = "ghostty_size_report_encode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SizeReportEncode(
        GhosttySizeReportStyle style,
        GhosttySizeReportSize size,
        byte* buffer,
        nuint bufferLength,
        out nuint written);
}
