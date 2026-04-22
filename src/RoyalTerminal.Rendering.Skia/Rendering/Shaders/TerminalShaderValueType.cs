// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Full terminal shader package model.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Identifies a normalized shader resource or constant value type.
/// </summary>
public enum TerminalShaderValueType
{
    /// <summary>Unknown value type.</summary>
    Unknown,

    /// <summary>Scalar 32-bit floating-point value.</summary>
    Float,

    /// <summary>Two-component 32-bit floating-point value.</summary>
    Float2,

    /// <summary>Three-component 32-bit floating-point value.</summary>
    Float3,

    /// <summary>Four-component 32-bit floating-point value.</summary>
    Float4,

    /// <summary>Four-by-four 32-bit floating-point matrix.</summary>
    Matrix4x4,

    /// <summary>Signed 32-bit integer value.</summary>
    Int,

    /// <summary>Unsigned 32-bit integer value.</summary>
    UInt,

    /// <summary>Boolean value.</summary>
    Bool,

    /// <summary>Two-dimensional texture value.</summary>
    Texture2D,

    /// <summary>Sampler state value.</summary>
    Sampler,

    /// <summary>Structured buffer value.</summary>
    StructuredBuffer,

    /// <summary>Byte-address buffer value.</summary>
    ByteAddressBuffer,
}
