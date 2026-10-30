// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Features;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Multiplexing.Automation;

namespace RoyalTerminal.Multiplexing.Server;

internal static class ServerHttp
{
    public static AccessGrant Authorize(HttpContext context, AccessTokens tokens, Guid? sessionId = null, bool administrator = false)
    {
        string header = context.Request.Headers.Authorization.ToString();
        AccessGrant? grant = header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? tokens.Resolve(header[7..]) : null;
        if (grant is null) throw new ApiException(401, "unauthorized", "A valid bearer token is required.");
        if (administrator && !grant.IsAdministrator || sessionId is { } id && !grant.CanRead(id))
            throw new ApiException(403, "forbidden", "This capability does not authorize the requested operation.");
        return grant;
    }

    public static void RequireAdministrator(AccessGrant grant)
    {
        if (!grant.IsAdministrator) throw new ApiException(403, "forbidden", "This operation requires an administrator capability.");
    }

    public static void RequireSession(AccessGrant grant, Guid id)
    {
        if (!grant.CanRead(id)) throw new ApiException(403, "forbidden", "This capability is scoped to a different session.");
    }

    public static Guid RouteId(HttpContext context, string name)
        => Guid.TryParse(context.Request.RouteValues[name]?.ToString(), out Guid id) && id != Guid.Empty
            ? id : throw new ArgumentException($"A valid {name} is required.");

    public static async ValueTask<T> ReadAsync<T>(HttpContext context, JsonTypeInfo<T> type)
    {
        byte[] bytes = await ReadBytesAsync(context);
        return JsonSerializer.Deserialize(bytes, type)
            ?? throw new ArgumentException("A JSON request object is required.");
    }

    public static async ValueTask<byte[]> ReadBytesAsync(HttpContext context, int maximumBytes = 256 * 1024)
    {
        if (context.Request.ContentLength > maximumBytes) throw new ApiException(413, "too_large", "The request exceeds its byte budget.");
        if (!context.Request.HasJsonContentType()) throw new ApiException(415, "content_type", "Use application/json.");
        IHttpMaxRequestBodySizeFeature? limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = maximumBytes;
        using MemoryStream body = new();
        byte[] buffer = new byte[16 * 1024];
        int count;
        while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) != 0)
        {
            if (body.Length + count > maximumBytes) throw new ApiException(413, "too_large", "The request exceeds its byte budget.");
            body.Write(buffer, 0, count);
        }
        return body.ToArray();
    }

    public static Task WriteAsync<T>(HttpContext context, T value, JsonTypeInfo<T> type, int status = 200)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(value, type, cancellationToken: context.RequestAborted);
    }

    public static (int Status, ApiError Error) Classify(Exception exception)
    {
        if (exception is ApiException api) return (api.Status, new(api.Code, api.Message));
        if (exception is BadHttpRequestException request) return (request.StatusCode, new("bad_request", request.Message));
        if (exception is MultiplexerActionExecutionException execution)
            return (execution.Code == MultiplexerAutomationError.Timeout ? 408 : 400,
                new(execution.Code.ToString(), execution.Message,
                    new(execution.ActionId, execution.StepIndex, execution.Method, execution.CompletedSteps)));
        if (exception is MultiplexerAutomationException automation)
            return (automation.Code == MultiplexerAutomationError.Timeout ? 408 : 400, new(automation.Code.ToString(), automation.Message));
        if (exception is MultiplexerException mux)
            return (mux.Code switch
            {
                MultiplexerErrorCode.NotFound => 404,
                MultiplexerErrorCode.Conflict or MultiplexerErrorCode.HistoryGap => 409,
                MultiplexerErrorCode.Forbidden => 403,
                MultiplexerErrorCode.CapacityExceeded => 429,
                _ => 410,
            }, new(mux.Code.ToString(), mux.Message));
        if (exception is JsonException or ArgumentException or FormatException)
            return (400, new("bad_request", exception.Message));
        if (exception is OperationCanceledException) return (408, new("cancelled", "The operation was cancelled."));
        return (500, new("internal_error", "The operation failed; inspect the server log for details."));
    }

    public static bool IsSameOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var origin)) return true;
        return Uri.TryCreate(origin.ToString(), UriKind.Absolute, out Uri? uri)
            && uri.Scheme == request.Scheme && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
