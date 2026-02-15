// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Contracts - Validation result model.

namespace RoyalTerminal.Rendering.Contracts;

/// <summary>
/// Represents the result of validating a rendering descriptor or request.
/// </summary>
public readonly record struct RenderValidationResult
{
    /// <summary>
    /// Initializes a new validation result.
    /// </summary>
    /// <param name="isValid">Whether validation succeeded.</param>
    /// <param name="errorMessage">Error message when validation fails.</param>
    public RenderValidationResult(bool isValid, string? errorMessage)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets whether validation succeeded.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the validation error message when <see cref="IsValid"/> is <see langword="false"/>.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static RenderValidationResult Valid() => new(true, null);

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static RenderValidationResult Invalid(string errorMessage) =>
        new(false, string.IsNullOrWhiteSpace(errorMessage) ? "Invalid descriptor." : errorMessage);
}
