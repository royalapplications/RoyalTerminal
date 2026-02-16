import AppKit
import GhosttyKit

enum GhosttyInputConverter {
    static func mods(from flags: NSEvent.ModifierFlags) -> ghostty_input_mods_e {
        var rawValue: UInt32 = GHOSTTY_MODS_NONE.rawValue

        if flags.contains(.shift) {
            rawValue |= GHOSTTY_MODS_SHIFT.rawValue
        }

        if flags.contains(.control) {
            rawValue |= GHOSTTY_MODS_CTRL.rawValue
        }

        if flags.contains(.option) {
            rawValue |= GHOSTTY_MODS_ALT.rawValue
        }

        if flags.contains(.command) {
            rawValue |= GHOSTTY_MODS_SUPER.rawValue
        }

        if flags.contains(.capsLock) {
            rawValue |= GHOSTTY_MODS_CAPS.rawValue
        }

        let deviceFlags = flags.rawValue

        if deviceFlags & UInt(NX_DEVICERSHIFTKEYMASK) != 0 {
            rawValue |= GHOSTTY_MODS_SHIFT_RIGHT.rawValue
        }

        if deviceFlags & UInt(NX_DEVICERCTLKEYMASK) != 0 {
            rawValue |= GHOSTTY_MODS_CTRL_RIGHT.rawValue
        }

        if deviceFlags & UInt(NX_DEVICERALTKEYMASK) != 0 {
            rawValue |= GHOSTTY_MODS_ALT_RIGHT.rawValue
        }

        if deviceFlags & UInt(NX_DEVICERCMDKEYMASK) != 0 {
            rawValue |= GHOSTTY_MODS_SUPER_RIGHT.rawValue
        }

        return ghostty_input_mods_e(rawValue)
    }

    static func mouseButton(from event: NSEvent) -> ghostty_input_mouse_button_e {
        switch event.buttonNumber {
        case 0:
            return GHOSTTY_MOUSE_LEFT
        case 1:
            return GHOSTTY_MOUSE_RIGHT
        case 2:
            return GHOSTTY_MOUSE_MIDDLE
        case 3:
            return GHOSTTY_MOUSE_FOUR
        case 4:
            return GHOSTTY_MOUSE_FIVE
        default:
            return GHOSTTY_MOUSE_UNKNOWN
        }
    }
}
