    const old_cursor_node = self.cursor.page_pin.node;
    const old_cursor_serial = old_cursor_node.serial;
    // A partial grow can also prune before allocation fails. Repair on both
    // exits, before Terminal's fallback clear or the integrity check runs.
    defer {
        if (self.cursor.page_pin.node != old_cursor_node or
            self.cursor.page_pin.node.serial != old_cursor_serial)
        {
            // The old page was retired/recycled. Its IDs must never be
            // released through the remapped pin: they can alias unrelated
            // entries or point at missing storage on the surviving page.
            const old_style = self.cursor.style;
            const old_link = self.cursor.hyperlink;
            self.cursor.style = .{};
            self.cursor.style_id = style.default_id;
            self.cursor.hyperlink = null;
            self.cursor.hyperlink_id = 0;
            self.cursorReload();
            self.cursor.style = old_style;
            self.manualStyleUpdate() catch |err| {
                log.err("failed to restore scroll-clear cursor style err={}", .{err});
                self.cursor.style = .{};
                self.cursor.style_id = style.default_id;
            };
            if (old_link) |link| {
                defer {
                    link.deinit(self.alloc);
                    self.alloc.destroy(link);
                }
                self.startHyperlink(link.uri, switch (link.id) {
                    .explicit => |id| id,
                    .implicit => null,
                }) catch |err| {
                    log.err("failed to restore scroll-clear cursor hyperlink err={}", .{err});
                };
            }
        } else {
            self.cursorReload();
        }
    }
    try self.pages.scrollClear();
