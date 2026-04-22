// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Rendering.Skia - Terminal shader diagnostics.

using System.Globalization;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Describes one shader package validation, include, compilation, or runtime diagnostic.
/// </summary>
public sealed class TerminalShaderDiagnostic
{
    /// <summary>
    /// Initializes a new shader diagnostic.
    /// </summary>
    /// <param name="severity">The diagnostic severity.</param>
    /// <param name="code">Stable diagnostic code.</param>
    /// <param name="message">Human-readable diagnostic message.</param>
    /// <param name="filePath">Optional virtual source path.</param>
    /// <param name="line">Optional one-based source line.</param>
    /// <param name="column">Optional one-based source column.</param>
    public TerminalShaderDiagnostic(
        TerminalShaderDiagnosticSeverity severity,
        string code,
        string message,
        string? filePath = null,
        int? line = null,
        int? column = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Diagnostic code must be non-empty.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Diagnostic message must be non-empty.", nameof(message));
        }

        Severity = severity;
        Code = code.Trim();
        Message = message.Trim();
        FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath.Trim();
        Line = line is > 0 ? line : null;
        Column = column is > 0 ? column : null;
    }

    /// <summary>
    /// Gets the diagnostic severity.
    /// </summary>
    public TerminalShaderDiagnosticSeverity Severity { get; }

    /// <summary>
    /// Gets the stable diagnostic code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the human-readable diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the optional virtual source path.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Gets the optional one-based source line.
    /// </summary>
    public int? Line { get; }

    /// <summary>
    /// Gets the optional one-based source column.
    /// </summary>
    public int? Column { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        string location = FilePath is null ? string.Empty : $"{FilePath}:";
        if (Line is not null)
        {
            location += Line.Value.ToString(CultureInfo.InvariantCulture);
            if (Column is not null)
            {
                location += ":" + Column.Value.ToString(CultureInfo.InvariantCulture);
            }

            location += ": ";
        }
        else if (location.Length > 0)
        {
            location += " ";
        }

        return $"{location}{Severity} {Code}: {Message}";
    }
}
