// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Shared runtime handle extraction helpers.

using System.Reflection;
using Avalonia.Skia;

namespace RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;

internal static class AvaloniaInteropHandleExtraction
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool TryGetCurrentSkiaSession(ISkiaSharpApiLease lease, out object? session)
    {
        session = null;
        ArgumentNullException.ThrowIfNull(lease);

        if ((!TryGetMemberValue(lease, "_context", out object? drawingContext) || drawingContext is null) &&
            (!TryGetMemberValue(lease, "Context", out drawingContext) || drawingContext is null))
        {
            return false;
        }

        if ((!TryGetMemberValue(drawingContext, "_session", out object? currentSession) || currentSession is null) &&
            (!TryGetMemberValue(drawingContext, "Session", out currentSession) || currentSession is null))
        {
            return false;
        }

        session = currentSession;
        return true;
    }

    public static bool TryGetMemberValue(
        object source,
        string memberName,
        out object? value)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        Type sourceType = source.GetType();

        PropertyInfo? property = sourceType.GetProperty(memberName, MemberFlags);
        if (property is not null)
        {
            try
            {
                value = property.GetValue(source);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        FieldInfo? field = sourceType.GetField(memberName, MemberFlags);
        if (field is not null)
        {
            try
            {
                value = field.GetValue(source);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    public static bool TryGetNestedMemberValue(
        object source,
        out object? value,
        params string[] memberPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(memberPath);

        object? current = source;
        for (int i = 0; i < memberPath.Length; i++)
        {
            string member = memberPath[i];
            if (current is null || !TryGetMemberValue(current, member, out current))
            {
                value = null;
                return false;
            }
        }

        value = current;
        return true;
    }

    public static bool TryGetHandle(object source, out nint handle, params string[] candidateMembers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidateMembers);

        for (int i = 0; i < candidateMembers.Length; i++)
        {
            if (TryGetMemberValue(source, candidateMembers[i], out object? rawValue) &&
                TryConvertToHandle(rawValue, out handle))
            {
                return true;
            }
        }

        handle = nint.Zero;
        return false;
    }

    public static bool TryGetNestedHandle(object source, out nint handle, params string[] memberPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(memberPath);

        if (!TryGetNestedMemberValue(source, out object? value, memberPath))
        {
            handle = nint.Zero;
            return false;
        }

        return TryConvertToHandle(value, out handle);
    }

    public static bool TryConvertToHandle(object? value, out nint handle)
    {
        switch (value)
        {
            case IntPtr intPtr:
                handle = intPtr;
                return handle != nint.Zero;

            case nuint nativeUInt when nativeUInt <= (nuint)nint.MaxValue:
                handle = (nint)nativeUInt;
                return handle != nint.Zero;

            case uint uintValue:
                handle = (nint)uintValue;
                return handle != nint.Zero;

            case int intValue when intValue > 0:
                handle = (nint)intValue;
                return true;

            case ulong ulongValue when ulongValue <= (ulong)nint.MaxValue:
                handle = (nint)ulongValue;
                return handle != nint.Zero;

            case long longValue when longValue > 0 && longValue <= nint.MaxValue:
                handle = (nint)longValue;
                return true;

            default:
                if (value is not null &&
                    TryGetMemberValue(value, "Handle", out object? handleValue) &&
                    TryConvertToHandle(handleValue, out handle))
                {
                    return true;
                }

                if (value is not null &&
                    TryGetMemberValue(value, "NativeHandle", out object? nativeHandleValue) &&
                    TryConvertToHandle(nativeHandleValue, out handle))
                {
                    return true;
                }

                handle = nint.Zero;
                return false;
        }
    }
}
