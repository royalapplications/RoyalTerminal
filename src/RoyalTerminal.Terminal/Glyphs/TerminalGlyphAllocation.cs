// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Glyphs;

// Per-owner failure seams at fallible protocol/storage preparation boundaries.
// No global allocator override or production callback is installed.
internal enum TerminalGlyphAllocation
{
    CommandBuffer, DecodeBuffer, Outline, Registration, Glossary, RegistryCapacity, OrderCapacity,
}
