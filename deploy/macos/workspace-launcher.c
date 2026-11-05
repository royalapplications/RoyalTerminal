/* Personal workspace application entry; never compose a shell command from a path or client argument. */
#include <mach-o/dyld.h>
#include <limits.h>
#include <unistd.h>
#include <stdlib.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
int main(int argc, char **argv) {
    char location[PATH_MAX], real[PATH_MAX], script[PATH_MAX]; uint32_t size = sizeof location;
    if (_NSGetExecutablePath(location, &size) != 0 || !realpath(location, real)) return 78;
    char *end = strrchr(real, '/'); if (!end) return 78; *end = '\0';
    if (snprintf(script, sizeof script, "%s/../Resources/personal.sh", real) >= (int)sizeof script) return 78;
    char **next = calloc((size_t)argc + 3, sizeof *next); if (!next) return 71;
    next[0] = "/bin/bash"; next[1] = script;
    for (int i=1; i<argc; ++i) next[i+1] = argv[i];
    execv(next[0], next); perror("workspace launcher"); return 71;
}
