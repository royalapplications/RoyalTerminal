// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Shared runtime handle extraction helpers.

using System.Reflection;
using Avalonia.Skia;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Interop;

internal static class AvaloniaInteropHandleExtraction
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void DumpObjectMembers(object? obj, string label)
    {
        if (obj is null)
        {
            Console.WriteLine($"[Dumper] {label} is null");
            return;
        }
        Type type = obj.GetType();
        Console.WriteLine($"[Dumper] {label} Type: {type.FullName}");
        foreach (PropertyInfo prop in type.GetProperties(MemberFlags))
        {
            try
            {
                object? val = prop.GetValue(obj);
                Console.WriteLine($"[Dumper]   Property: {prop.Name} = {val}");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.ToString() ?? ex.ToString();
                Console.WriteLine($"[Dumper]   Property: {prop.Name} (Error: {inner})");
            }
        }
        foreach (FieldInfo field in type.GetFields(MemberFlags))
        {
            try
            {
                object? val = field.GetValue(obj);
                Console.WriteLine($"[Dumper]   Field: {field.Name} = {val}");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.ToString() ?? ex.ToString();
                Console.WriteLine($"[Dumper]   Field: {field.Name} (Error: {inner})");
            }
        }
    }

    public static bool TryGetCurrentSkiaSession(ISkiaSharpApiLease lease, out object? session)
    {
        session = null;
        ArgumentNullException.ThrowIfNull(lease);

        ListSkiaApiMethods();
        DumpObjectMembers(lease, "Lease");

        if ((!TryGetMemberValue(lease, "_context", out object? drawingContext) || drawingContext is null) &&
            (!TryGetMemberValue(lease, "Context", out drawingContext) || drawingContext is null))
        {
            return false;
        }

        DumpObjectMembers(drawingContext, "DrawingContext");

        if (TryGetMemberValue(drawingContext, "Surface", out object? surfaceObj) && surfaceObj is not null)
        {
            DumpObjectMembers(surfaceObj, "SurfaceObj");
        }

        if (TryGetMemberValue(drawingContext, "_gpu", out object? gpuObj) && gpuObj is not null)
        {
            DumpObjectMembers(gpuObj, "GpuObj");
        }
        if (TryGetMemberValue(drawingContext, "_disposables", out object? disposablesVal) && disposablesVal is Array dispArray)
        {
            Console.WriteLine($"[Dumper]   _disposables Array (Length: {dispArray.Length}):");
            for (int i = 0; i < dispArray.Length; i++)
            {
                object? item = dispArray.GetValue(i);
                if (item is not null)
                {
                    Console.WriteLine($"[Dumper]     Item {i}: Type = {item.GetType().FullName}, ToString = {item}");
                    DumpObjectMembers(item, $"_disposables[{i}]");
                }
            }
        }
        if ((!TryGetMemberValue(drawingContext, "_session", out object? currentSession) || currentSession is null) &&
            (!TryGetMemberValue(drawingContext, "Session", out currentSession) || currentSession is null))
        {
            return false;
        }

        DumpObjectMembers(currentSession, "Session");

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

                if (value is not null &&
                    TryGetMemberValue(value, "NativePointer", out object? nativePointerValue) &&
                    TryConvertToHandle(nativePointerValue, out handle))
                {
                    return true;
                }

                handle = nint.Zero;
                return false;
        }
    }

    public static void ListSkiaApiMethods()
    {
        try
        {
            var skiaApiType = typeof(SKSurface).Assembly.GetType("SkiaSharp.SkiaApi");
            if (skiaApiType is null)
            {
                Console.WriteLine("[Dumper] SkiaApi type not found");
                return;
            }
            Console.WriteLine("[Dumper] Scanning SkiaApi methods:");
            foreach (var method in skiaApiType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.Name.Contains("surface", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Dumper]   Method: {method.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dumper] Error scanning SkiaApi methods: {ex.Message}");
        }
    }
}
