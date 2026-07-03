// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Media;

namespace RoyalTerminal.Avalonia.App.Controls;

internal readonly record struct CompositionRingSpinnerVisualState(
    bool IsActive,
    Size Size,
    Color ForegroundColor,
    Color TrackColor,
    double StrokeThickness);
