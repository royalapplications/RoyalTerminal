// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Sixel - Sixel decode status values.

namespace RoyalTerminal.Sixel;

/// <summary>
/// Describes the outcome of a sixel decode operation.
/// </summary>
public enum SixelDecodeStatus
{
    /// <summary>The payload decoded successfully.</summary>
    Success = 0,

    /// <summary>The payload did not contain data.</summary>
    EmptyInput = 1,

    /// <summary>The DCS payload was not a sixel payload.</summary>
    MissingIntroducer = 2,

    /// <summary>The payload was malformed.</summary>
    InvalidData = 3,

    /// <summary>The encoded payload exceeded the configured input limit.</summary>
    InputTooLarge = 4,

    /// <summary>The decoded image dimensions or pixel count exceeded configured limits.</summary>
    ImageTooLarge = 5,

    /// <summary>The payload referenced more color registers than allowed.</summary>
    ColorRegisterLimitExceeded = 6,
}
