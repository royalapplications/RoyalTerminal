    // Restore pins from this chunk's successful copies before their temporary
    // pages are retired. The source chunk remains alive until complete success.
    errdefer {
        const pin_keys = self.tracked_pins.keys();
        var source_end = copied;
        var current = chunk.node.prev;
        while (current) |node| {
            if (node == prev) break;
            const source_start = source_end - node.page().size.rows;
            for (pin_keys) |p| {
                if (p.node != node) continue;
                p.node = chunk.node;
                p.y += source_start;
            }
            source_end = source_start;
            current = node.prev;
        }
        if (prev_copied > 0) {
            const prev_node = prev.?;
            const start = prev_node.page().size.rows - prev_copied;
            for (pin_keys) |p| {
                if (p.node != prev_node or p.y < start) continue;
                p.node = chunk.node;
                p.y -= start;
            }
        }
    }

    // We need to loop because our col growth may force us
