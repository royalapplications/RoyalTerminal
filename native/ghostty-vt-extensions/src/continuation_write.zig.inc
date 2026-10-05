        // APC -> C1 DCS/CSI/OSC can commit handler work without a new ESC.
        // Retaining that APC prefix would repeat its effects on restore. Find
        // the last such boundary before writing to a non-rewindable writer.
        // This only changes export: input tracking retains its fast feed path.
        var start: usize = 0;
        var introducer: ?u8 = null;
        var scanner: BoundaryScanner = .init();
        if (std.mem.indexOfAny(u8, self.bytes.items, "\x90\x9b\x9d") != null) {
            for (self.bytes.items, 0..) |c, i| {
                if (scanner.next(c) != .committed) continue;
                const replacement: ?u8 = switch (scanner.parser.state) {
                    .dcs_entry => 'P',
                    .csi_entry => '[',
                    .osc_string => ']',
                    else => null,
                };
                if (replacement) |value| {
                    start = i + 1;
                    introducer = value;
                }
            }
            scanner = .init();
        }
        if (introducer) |value| {
            _ = scanner.next(0x1b);
            _ = scanner.next(value);
            try writer.writeAll(&.{ 0x1b, value });
        }
        for (self.bytes.items[start..]) |c| {
            if (scanner.next(c) == .omittable) continue;
            try writer.writeByte(c);
        }
