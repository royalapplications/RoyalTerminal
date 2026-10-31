// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using RoyalTerminal.Multiplexing.Inspection;

namespace RoyalTerminal.Multiplexing.Server;

internal static class WorkspaceCheckpointEndpoint
{
    internal static void Map(WebApplication app, WorkspaceOperations workspaces, AccessTokens tokens)
    {
        // Bound retained transfer buffers as well as encoder concurrency. Do not queue work behind slow readers.
        SemaphoreSlim permits = new(2, 2);
        app.MapGet("/api/sessions/{sessionId}/blocks/{blockId}/checkpoint", async (HttpContext context) =>
        {
            Guid s = ServerHttp.RouteId(context, "sessionId"), b = ServerHttp.RouteId(context, "blockId");
            AccessGrant grant = ServerHttp.Authorize(context, tokens, s);
            if (!permits.Wait(0)) throw new ApiException(429,"checkpoint_busy","All checkpoint slots are occupied.");
            try
            {
                var inspection = workspaces.Inspection(s,b);
                MultiplexerCheckpoint result;
                try { result = inspection.CaptureCheckpoint(() => workspaces.GetBlock(s,b), context.RequestAborted); }
                catch (InvalidDataException) { throw new ApiException(422,"checkpoint_unavailable","The current terminal state cannot be completely represented within the checkpoint limits."); }
                catch (ObjectDisposedException) { throw new ApiException(410,"block_closed","The block closed before a checkpoint was available."); }
                catch (InvalidOperationException ex) when (ex is not MultiplexerException) { throw new ApiException(409,"checkpoint_unavailable","The current parser operation cannot be checkpointed."); }
                if (!tokens.IsActive(grant)) throw new ApiException(403,"expired","Capability expired during checkpoint capture.");
                var response = context.Response;
                response.ContentType = "application/vnd.royalterminal.ghostty-snapshot-v1";
                response.ContentLength = result.Binary.Length; response.Headers.CacheControl = "no-store";
                response.Headers["X-Workspace-Session"] = s.ToString("D"); response.Headers["X-Workspace-Block"] = b.ToString("D");
                response.Headers["X-Workspace-Sequence"] = result.Block.LastSequence.ToString(CultureInfo.InvariantCulture);
                if (result.Block.ExitCode is int exit) response.Headers["X-Workspace-Exit"] = exit.ToString(CultureInfo.InvariantCulture);
                using var write = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                write.CancelAfter(TimeSpan.FromSeconds(20));
                await response.Body.WriteAsync(result.Binary, write.Token);
            }
            finally { permits.Release(); }
        });
    }
}
