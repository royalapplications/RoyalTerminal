    const old_node = self.cursor.page_pin.node;
    const old_serial = old_node.serial;
    // A partial grow may prune before it fails. Repair the cursor before the
    // integrity check on both exits, including reuse of the same page address.
    defer {
        if (self.cursor.page_pin.node != old_node or
            self.cursor.page_pin.node.serial != old_serial)
        {
            self.cursor.page_pin.garbage = false;
            self.cursorPageReplaced();
        } else {
            self.cursorReload();
        }
    }
    try self.pages.scrollClear();
