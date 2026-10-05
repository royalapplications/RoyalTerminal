// Copyright (c) Royal Apps. Licensed under the MIT license.
// Darwin applies SETSID after spawn file actions. Acquire the controlling tty
// here, after exec, then replace this process without ever running managed code.
#include <errno.h>
#include <fcntl.h>
#include <sys/ioctl.h>
#include <unistd.h>

int main(int argc, char **argv)
{
    const int status_fd = 3;
    int error;
    if (argc < 3) { error = EINVAL; goto fail; }
    if (fcntl(status_fd, F_SETFD, FD_CLOEXEC) == -1 ||
        ioctl(STDIN_FILENO, TIOCSCTTY, 0) == -1) {
        error = errno;
        goto fail;
    }
    // libc supplies PATH search and ENOEXEC /bin/sh fallback in this process,
    // without closing/reacquiring a controlling terminal between candidates.
    execvp(argv[1], &argv[2]);
    error = errno;
fail:
    // The parent owns the read end until this short status write completes.
    while (write(status_fd, &error, sizeof(error)) == -1 && errno == EINTR) {}
    _exit(127);
}
