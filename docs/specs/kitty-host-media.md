# Managed Kitty host-media boundary

The managed parser uses `IKittyGraphicsMediumReader`; it performs no filesystem or
shared-memory calls. `LocalKittyGraphicsMediumReader` lives in Terminal.Services and
accepts an explicit `KittyGraphicsMediumPolicy`. A standalone parser with no provider,
or a provider with its default policy, rejects host-backed media. The default Avalonia
and application composition enable file, temporary-file, and POSIX shared-memory
capabilities to match the existing native backend. An embedding host can instead
supply restricted options/factories. Shared memory is unsupported on Windows, matching
Ghostty; file and temporary-file paths have Windows-specific checks.
Enabling these capabilities permits terminal output, including output from remote
connections, to request reads and protocol-scoped removal of local host resources
with the application's permissions. Native-equivalent defaults are not a sandbox;
hosts handling untrusted content should inject a restricted or direct-only policy.

The reference is Ghostty's `src/terminal/kitty/graphics_image.zig` and `windows.zig`.
xterm.js's image add-on currently rejects non-direct media; Windows Terminal has no
Kitty image-media implementation. These capabilities do not change shell startup,
command invocation, or PowerShell formatting behavior.

## Validation and ownership

- File paths are opened once and the canonical path is derived from that handle:
  Linux `/proc/self/fd`, Darwin `F_GETPATH`, or Windows `GetFinalPathNameByHandle`.
  The validated object is the object read, even if a symlink or directory entry changes.
- POSIX `/proc`, `/sys`, and `/dev` (except `/dev/shm`) are rejected. Only regular files
  are accepted. Nonblocking open prevents a FIFO from hanging before its type is checked.
- Windows rejects UNC, device/NT namespaces, and reserved DOS-device components before
  opening; the final path must also be drive-absolute and pass those checks.
- `S` is an exact byte count, not a maximum. Offset/range arithmetic uses wide integers
  and is checked against object size and the configured limit before allocating. The
  hard cap is 400 MiB. A file that grows during a whole-file read is rejected instead of
  silently truncating the payload; a short read also fails.
- Temporary-file ownership begins only after canonical path, regular-file type,
  temporary-directory boundary, and `tty-graphics-protocol` name checks pass. Cleanup
  happens after an accepted read attempt, including a range/read failure, as in Ghostty.
  Ordinary files are never removed. Windows marks the opened handle for deletion;
  Unix additionally verifies device/inode identity immediately before unlink, so an
  already replaced directory entry is not removed. Unix has no portable atomic
  unlink-by-open-handle primitive: a same-user actor can still race between that final
  identity check and unlink, as with upstream's path-based cleanup.
- Shared-memory names must start with one slash, have no other slash/NUL, and fit 255
  encoded bytes. Ownership starts only after successful `shm_open`; the name is unlinked
  even if subsequent range validation fails. `S=0` uses the validated raw-image length
  (ignoring page padding), or the remaining bytes for PNG/compressed input. Linux uses
  bounded positioned reads; Darwin requires a read-only, page-aligned mapping of only
  the checked range. Darwin rejects both repeated `ftruncate` and `O_TRUNC` after
  initial allocation, so a producer cannot shrink the validated nonempty object during
  the copy; see [XNU's shared-memory implementation](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/kern/posix_shm.c#L549-L553).
  A focused regression asserts that truncating an initialized object fails and its
  original range remains readable. No Windows shared-memory emulation is introduced.

Paths must be valid UTF-8, consistent with the managed filesystem boundary; byte paths
that are not UTF-8 are rejected instead of being decoded lossily. Darwin's variadic
`fcntl` ABI receives its pointer on the stack on arm64 and in the normal argument slot
on x64. Shared-memory length uses `fstat`, because .NET's `RandomAccess.GetLength`
rejects non-seekable Darwin shared-memory handles.

## Validation

The 41 focused path/media tests passed on macOS arm64, including real regular files,
FIFO rejection, symlink escapes, replaced temporary entries, and POSIX shared-memory
mapping/unlink. Pure Windows path-policy cases run on every host. Windows handle
deletion and Linux-specific IO paths still require their platform CI execution.
