// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo - About dialog view model.

using ReactiveUI;

namespace RoyalTerminal.Demo.ViewModels;

/// <summary>
/// Provides product metadata for the RoyalTerminal about dialog.
/// </summary>
public sealed class AboutRoyalTerminalViewModel : ReactiveObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutRoyalTerminalViewModel"/> class.
    /// </summary>
    /// <param name="productName">The product name shown in the dialog.</param>
    /// <param name="versionText">The version or release channel text shown below the product name.</param>
    /// <param name="description">The short product description.</param>
    /// <param name="runtimeText">The runtime and framework attribution.</param>
    /// <param name="copyrightText">The product copyright line.</param>
    public AboutRoyalTerminalViewModel(
        string productName,
        string versionText,
        string description,
        string runtimeText,
        string copyrightText)
    {
        ProductName = productName;
        WindowTitle = $"About {productName}";
        VersionText = versionText;
        Description = description;
        RuntimeText = runtimeText;
        CopyrightText = copyrightText;
    }

    /// <summary>
    /// Gets the default about dialog metadata for the sample application.
    /// </summary>
    /// <returns>A new about dialog view model.</returns>
    public static AboutRoyalTerminalViewModel CreateDefault()
    {
        return new AboutRoyalTerminalViewModel(
            "RoyalTerminal",
            "Demo Preview",
            "A modern terminal sample with PTY sessions, command history, profiles, search, replay capture, and VT rendering.",
            "Built with Avalonia and ReactiveUI.",
            "(c) 2026 Royal Apps");
    }

    /// <summary>
    /// Gets the dialog window title.
    /// </summary>
    public string WindowTitle { get; }

    /// <summary>
    /// Gets the product name.
    /// </summary>
    public string ProductName { get; }

    /// <summary>
    /// Gets the version or release channel text.
    /// </summary>
    public string VersionText { get; }

    /// <summary>
    /// Gets the short product description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the runtime and framework attribution.
    /// </summary>
    public string RuntimeText { get; }

    /// <summary>
    /// Gets the product copyright text.
    /// </summary>
    public string CopyrightText { get; }
}
