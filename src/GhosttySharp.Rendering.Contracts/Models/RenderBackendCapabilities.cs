// Licensed under the MIT License.
// GhosttySharp.Rendering.Contracts - Backend capability model.

namespace GhosttySharp.Rendering.Contracts;

/// <summary>
/// Describes capabilities advertised by a rendering backend.
/// </summary>
public readonly record struct RenderBackendCapabilities
{
    /// <summary>
    /// Initializes backend capabilities.
    /// </summary>
    public RenderBackendCapabilities(
        RenderBackendKind backendKind,
        RenderFeatureFlags featureFlags,
        uint minSampleCount,
        uint maxSampleCount,
        IReadOnlyList<RenderPixelFormat>? supportedPixelFormats = null)
    {
        if (backendKind == RenderBackendKind.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(backendKind), "Backend kind must be specified.");
        }

        if (minSampleCount == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSampleCount), "Minimum sample count must be >= 1.");
        }

        if (maxSampleCount < minSampleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSampleCount), "Maximum sample count must be >= minimum sample count.");
        }

        BackendKind = backendKind;
        FeatureFlags = featureFlags;
        MinSampleCount = minSampleCount;
        MaxSampleCount = maxSampleCount;
        SupportedPixelFormats = CopySupportedFormats(supportedPixelFormats);
    }

    /// <summary>
    /// Gets the backend kind represented by this capability set.
    /// </summary>
    public RenderBackendKind BackendKind { get; }

    /// <summary>
    /// Gets advertised optional features.
    /// </summary>
    public RenderFeatureFlags FeatureFlags { get; }

    /// <summary>
    /// Gets the minimum supported sample count.
    /// </summary>
    public uint MinSampleCount { get; }

    /// <summary>
    /// Gets the maximum supported sample count.
    /// </summary>
    public uint MaxSampleCount { get; }

    /// <summary>
    /// Gets supported render target pixel formats.
    /// </summary>
    public IReadOnlyList<RenderPixelFormat> SupportedPixelFormats { get; }

    /// <summary>
    /// Returns true when all required features are available.
    /// </summary>
    public bool SupportsFeatures(RenderFeatureFlags requiredFeatures) =>
        (FeatureFlags & requiredFeatures) == requiredFeatures;

    /// <summary>
    /// Returns true when the provided format is supported.
    /// </summary>
    public bool SupportsPixelFormat(RenderPixelFormat format)
    {
        if (format == RenderPixelFormat.Unknown)
        {
            return false;
        }

        for (int i = 0; i < SupportedPixelFormats.Count; i++)
        {
            if (SupportedPixelFormats[i] == format)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<RenderPixelFormat> CopySupportedFormats(
        IReadOnlyList<RenderPixelFormat>? supportedPixelFormats)
    {
        if (supportedPixelFormats is null || supportedPixelFormats.Count == 0)
        {
            return Array.Empty<RenderPixelFormat>();
        }

        RenderPixelFormat[] buffer = new RenderPixelFormat[supportedPixelFormats.Count];
        int uniqueCount = 0;

        for (int i = 0; i < supportedPixelFormats.Count; i++)
        {
            RenderPixelFormat format = supportedPixelFormats[i];
            if (format == RenderPixelFormat.Unknown)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(supportedPixelFormats),
                    "Supported pixel formats cannot contain RenderPixelFormat.Unknown.");
            }

            bool alreadyPresent = false;
            for (int j = 0; j < uniqueCount; j++)
            {
                if (buffer[j] == format)
                {
                    alreadyPresent = true;
                    break;
                }
            }

            if (!alreadyPresent)
            {
                buffer[uniqueCount++] = format;
            }
        }

        if (uniqueCount == 0)
        {
            return Array.Empty<RenderPixelFormat>();
        }

        if (uniqueCount == buffer.Length)
        {
            return buffer;
        }

        RenderPixelFormat[] trimmed = new RenderPixelFormat[uniqueCount];
        Array.Copy(buffer, trimmed, uniqueCount);
        return trimmed;
    }
}
