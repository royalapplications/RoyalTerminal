# Kitty PNG decoding boundary

The framework-independent terminal core exposes `IKittyGraphicsPngDecoder` and an
owned straight-alpha RGBA result. The managed VT parser accepts the decoder through
`BasicVtProcessorOptions`. Both default Avalonia control construction and application
composition inject `SkiaKittyGraphicsPngDecoder`; callers constructing a standalone
`BasicVtProcessor` or `DefaultVtProcessorFactory` must inject a provider for PNG.
With no provider, raw RGB/RGBA works but PNG is explicitly unsupported. This keeps
the standalone managed parser free of Skia, Avalonia, reflection, and native Ghostty.

The native Ghostty system hook and the managed Skia adapter compile the same
internal codec helper as linked source. This shares validation/decoding without
introducing a dependency from the managed renderer to GhosttySharp (or from native
bindings to the renderer). Native output still decodes directly into the allocator
provided by Ghostty and transfers ownership only on success; managed output allocates
one exact RGBA array after validating the header.

The helper checks the PNG signature and initial IHDR, encoded byte limit, positive
dimensions, a 10,000-pixel per-axis limit, and the decoded budget before copying the
encoded payload or creating a codec. It then verifies the codec's format and actual
dimensions against the validated header. Complete decode success is required;
truncated and malformed data is rejected. The hard encoded/decoded limit is 400 MiB;
managed callers may set tighter decoded limits per call and encoded limits on the
provider. Straight alpha is retained for later rendering/compositing.

Reference decisions: Ghostty's `src/terminal/kitty/graphics_image.zig` sets the same
10,000 / 400 MiB limits and delegates PNG decoding to its system hook. xterm.js's
`addons/addon-image/src/kitty/KittyGraphicsHandler.ts` delegates PNG to the browser's
`createImageBitmap`; we use the already-shipped Skia codec instead of a custom PNG
implementation. Windows Terminal has no corresponding Kitty PNG decoder. No shell
or PowerShell behavior changes at this boundary.
