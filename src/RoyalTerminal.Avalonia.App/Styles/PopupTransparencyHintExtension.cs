// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RoyalTerminal.Avalonia.App.Styles;

/// <summary>
/// Provides the transparency levels used by popup chrome styles.
/// </summary>
public sealed class PopupTransparencyHintExtension : MarkupExtension
{
    private static readonly IReadOnlyList<WindowTransparencyLevel> WindowsTransparencyHint =
    [
        WindowTransparencyLevel.AcrylicBlur,
        WindowTransparencyLevel.Transparent,
    ];

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) => WindowsTransparencyHint;
}
