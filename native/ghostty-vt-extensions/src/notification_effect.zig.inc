    fn royalNotificationTrampoline(handler: *Handler, command: ?osc.Command.KittyDesktopNotification) void {
        const wrapper = TerminalWrapper.fromHandler(handler);
        const callback = wrapper.effects.royal_notification orelse return;
        if (command) |notification| {
            callback(@ptrCast(wrapper), wrapper.effects.royal_notification_userdata,
                notification.metadata.ptr, notification.metadata.len,
                notification.payload.ptr, notification.payload.len,
                @intFromBool(notification.terminator == .bel));
        } else {
            callback(@ptrCast(wrapper), wrapper.effects.royal_notification_userdata, null, 0, null, 0, 2);
        }
    }
