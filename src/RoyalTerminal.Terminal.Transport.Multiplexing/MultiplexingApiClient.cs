// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>
/// Reflection-free HTTP client for the experimental multiplexer management API.
/// The supplied HttpClient remains owned by its caller. Credentials are scoped to each request.
/// </summary>
public sealed class MultiplexingApiClient
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _accessToken;
    private readonly int _maximumResponseBytes;

    /// <summary>Creates a client over an existing HTTP lifetime with a bounded response budget.</summary>
    public MultiplexingApiClient(HttpClient http, Uri endpoint, string accessToken, int maximumResponseBytes = 8 * 1024 * 1024)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _endpoint = MultiplexingEndpoint.Normalize(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (maximumResponseBytes is < 1024 or > 64 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        }

        _accessToken = accessToken;
        _maximumResponseBytes = maximumResponseBytes;
    }

    /// <summary>Reads the discoverable server method catalog.</summary>
    public ValueTask<JsonElement> GetMethodsAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, "api/methods", cancellationToken: cancellationToken);

    /// <summary>Calls a named server method using explicitly supplied JSON arguments.</summary>
    public ValueTask<JsonElement> CallAsync(string method, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        JsonElement payload = JsonSerializer.SerializeToElement(new MultiplexingApiCall(method, arguments),
            MultiplexingClientJsonContext.Default.MultiplexingApiCall);
        return SendAsync(HttpMethod.Post, "api/call", payload, cancellationToken);
    }

    /// <summary>
    /// Sends a request relative to the configured base URI. Only API-relative paths are accepted;
    /// absolute URLs and path traversal cannot redirect bearer credentials to a different endpoint.
    /// </summary>
    public async ValueTask<JsonElement> SendAsync(HttpMethod method, string relativePath, JsonElement? payload = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (!relativePath.StartsWith("api/", StringComparison.Ordinal)
            || relativePath.Contains('\\') || relativePath.Contains('#')
            || relativePath.Contains("..", StringComparison.Ordinal)
            || relativePath.Contains('%'))
        {
            throw new ArgumentException("Only unescaped API-relative paths without traversal are accepted.", nameof(relativePath));
        }

        Uri uri = new(_endpoint, relativePath);
        using HttpRequestMessage request = new(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is { } body)
        {
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, MultiplexingClientJsonContext.Default.JsonElement));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > _maximumResponseBytes)
        {
            throw new IOException("The multiplexer API response exceeds the configured size budget.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using MemoryStream bytes = new();
        byte[] buffer = new byte[Math.Min(8192, _maximumResponseBytes)];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (bytes.Length + read > _maximumResponseBytes)
            {
                throw new IOException("The multiplexer API response exceeds the configured size budget.");
            }

            bytes.Write(buffer, 0, read);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MultiplexingApiException(response.StatusCode,
                $"Multiplexer API returned HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                TryReadError(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
        }

        if (bytes.Length == 0)
        {
            using JsonDocument empty = JsonDocument.Parse("null");
            return empty.RootElement.Clone();
        }

        using JsonDocument document = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)));
        return document.RootElement.Clone();
    }

    private static string? TryReadError(ReadOnlySpan<byte> bytes)
    {
        try
        {
            MultiplexingServerMessage? error = JsonSerializer.Deserialize(bytes,
                MultiplexingClientJsonContext.Default.MultiplexingServerMessage);
            return error?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A failed HTTP management operation, without exposing response bodies or credentials.</summary>
public sealed class MultiplexingApiException : HttpRequestException
{
    /// <summary>Creates an API failure with its HTTP status and optional host classification.</summary>
    public MultiplexingApiException(HttpStatusCode statusCode, string message, string? code = null)
        : base(message, null, statusCode) => Code = code;

    /// <summary>Gets the stable host failure classification, if supplied.</summary>
    public string? Code { get; }
}
