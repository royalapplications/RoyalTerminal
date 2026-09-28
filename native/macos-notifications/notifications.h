// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
#pragma once
#include <stddef.h>

// Opaque, retained client; no callbacks into managed code. Commands/state are
// bounded UTF-8 JSON. All UI/UN state is serialized on the main dispatch queue.
// Create never requests authorization; the first show may request alert/sound.
// Poll returns an owned buffer, or NULL if unchanged. Free every returned buffer.
// Stop acknowledges quiescence; destroy also cancels native work and owns cleanup
// of any OS completion arriving after the handle has been released.
__attribute__((visibility("default"))) void *rt_notifications_create(void);
__attribute__((visibility("default"))) int rt_notifications_command(void *client, const void *bytes, size_t length);
__attribute__((visibility("default"))) void *rt_notifications_poll(void *client, size_t *length);
__attribute__((visibility("default"))) void rt_notifications_free(void *bytes);
__attribute__((visibility("default"))) void rt_notifications_destroy(void *client);
