// Licensed under the MIT License.
// GhosttySharp.Rendering.Interop - Exception type for native renderer interop failures.

namespace GhosttySharp.Rendering.Interop;

/// <summary>
/// Represents an error returned from the native renderer C API.
/// </summary>
public sealed class GhosttyRenderInteropException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new exception for a renderer interop failure.
    /// </summary>
    /// <param name="operation">Operation name that failed.</param>
    /// <param name="result">Mapped native result code.</param>
    /// <param name="message">Optional renderer-provided message.</param>
    public GhosttyRenderInteropException(string operation, GhosttyRenderInteropResult result, string? message)
        : base(BuildMessage(operation, result, message))
    {
        Operation = string.IsNullOrWhiteSpace(operation) ? "unknown operation" : operation;
        Result = result;
    }

    /// <summary>
    /// Gets the operation name that failed.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the mapped native result code.
    /// </summary>
    public GhosttyRenderInteropResult Result { get; }

    private static string BuildMessage(string operation, GhosttyRenderInteropResult result, string? message)
    {
        string operationText = string.IsNullOrWhiteSpace(operation) ? "unknown operation" : operation;
        string details = string.IsNullOrWhiteSpace(message) ? "No native error message was provided." : message;
        return $"Renderer interop call '{operationText}' failed with result '{result}': {details}";
    }
}
