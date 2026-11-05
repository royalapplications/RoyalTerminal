// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Multiplexing.Cli;

/// <summary>Environment-provided action context, used only when no explicit session is supplied.</summary>
internal sealed record CliCurrentContext(string? Session = null, string? Block = null, string? Window = null);
