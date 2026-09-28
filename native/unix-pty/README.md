# macOS PTY launcher

This small executable belongs to the Unix PTY package, independently of Ghostty.
Build its universal arm64/x86_64 binary with `bash scripts/build-unix-pty.sh` on
macOS with Xcode command-line tools. macOS project builds do this automatically;
CI/release transfer the artifact to the Linux NuGet packaging job. Packing fails
if the helper is absent. Generated binaries are not checked in.

Managed code must not resume in a fork child: runtime locks and inherited signal
handlers make that unsafe. Linux uses `posix_spawn` with session creation and a
slave-open file action. Darwin applies `SETSID` **after** file actions, so it
spawns this helper, which explicitly acquires the controlling terminal before
`execvp`. The helper is already in a fresh native process and may safely use libc
PATH search and script fallback. The original argv and environment are preserved;
no shell command string or wrapper shell is introduced.

File descriptor 3 reports a native errno to the parent on failure and closes on
successful exec. The parent reaps failed launches and synchronously reports the
failure. stdin/stdout/stderr refer to the slave; the master and unrelated inherited
descriptors are closed by spawn actions and Darwin `CLOEXEC_DEFAULT`.

References: Ghostty `src/pty.zig` (new session and controlling tty),
[node-pty native launcher](https://github.com/microsoft/node-pty/blob/main/src/unix/spawn-helper.cc),
and [Darwin spawn ordering](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/kern/kern_exec.c).
Windows Terminal uses ConPTY rather than POSIX job control; xterm.js delegates
native terminal process setup to its embedding application/node-pty. This change
does not modify Windows/PowerShell launch behavior.
