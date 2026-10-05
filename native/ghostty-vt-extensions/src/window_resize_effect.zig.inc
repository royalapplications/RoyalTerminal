    fn royalWindowResizeTrampoline(handler: *Handler, rows: u16, columns: u16) void {
        const wrapper = TerminalWrapper.fromHandler(handler);
        const callback = wrapper.effects.royal_window_resize orelse return;
        callback(@ptrCast(wrapper), wrapper.effects.royal_window_resize_userdata, rows, columns);
    }

