import AppKit
import Foundation
import GhosttyKit

@MainActor
final class GhosttyTerminalSession: ObservableObject, Identifiable {
    let id = UUID()
    let context: ghostty_surface_context_e

    @Published var title: String
    @Published var processExited = false
    @Published var exitCode: Int?

    var onCloseRequested: ((GhosttyTerminalSession) -> Void)?

    weak var runtime: GhosttyRuntime?
    weak var hostView: GhosttyMetalTerminalView?

    private var surface: ghostty_surface_t?
    private var userdata: UnsafeMutableRawPointer?

    var surfaceHandle: ghostty_surface_t? {
        surface
    }

    init(runtime: GhosttyRuntime, context: ghostty_surface_context_e, title: String = "Terminal") {
        self.runtime = runtime
        self.context = context
        self.title = title
    }

    func attach(to view: GhosttyMetalTerminalView) {
        hostView = view

        if surface == nil {
            createSurface(in: view)
        }

        updateSurfaceMetrics(for: view)
    }

    func setFocused(_ focused: Bool) {
        guard let surface else {
            return
        }

        ghostty_surface_set_focus(surface, focused)
    }

    func render() {
        guard let surface else {
            return
        }

        ghostty_surface_draw(surface)
    }

    func updateSurfaceMetrics(for view: NSView) {
        guard let surface else {
            return
        }

        let scale = max(view.window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 1.0, 1.0)
        let width = max(Int((view.bounds.width * scale).rounded(.toNearestOrEven)), 1)
        let height = max(Int((view.bounds.height * scale).rounded(.toNearestOrEven)), 1)

        ghostty_surface_set_content_scale(surface, scale, scale)
        ghostty_surface_set_size(surface, UInt32(width), UInt32(height))
    }

    func sendKeyEvent(_ event: NSEvent, action: ghostty_input_action_e) {
        guard let surface else {
            return
        }

        var keyEvent = ghostty_input_key_s()
        keyEvent.action = action
        keyEvent.mods = GhosttyInputConverter.mods(from: event.modifierFlags)
        keyEvent.consumed_mods = ghostty_input_mods_e(0)
        keyEvent.keycode = UInt32(event.keyCode)
        keyEvent.unshifted_codepoint = 0
        keyEvent.composing = false

        let text = event.characters ?? ""
        if !text.isEmpty {
            text.withCString { pointer in
                keyEvent.text = pointer
                _ = ghostty_surface_key(surface, keyEvent)
            }
        } else {
            keyEvent.text = nil
            _ = ghostty_surface_key(surface, keyEvent)
        }
    }

    func sendMouseMove(_ event: NSEvent, in view: NSView) {
        guard let surface else {
            return
        }

        let location = view.convert(event.locationInWindow, from: nil)
        let normalizedY = view.bounds.height - location.y
        let mods = GhosttyInputConverter.mods(from: event.modifierFlags)

        ghostty_surface_mouse_pos(surface, location.x, normalizedY, mods)
    }

    func sendMouseButton(
        _ state: ghostty_input_mouse_state_e,
        event: NSEvent,
        in view: NSView,
        button: ghostty_input_mouse_button_e)
    {
        guard let surface else {
            return
        }

        let mods = GhosttyInputConverter.mods(from: event.modifierFlags)
        _ = ghostty_surface_mouse_button(surface, state, button, mods)
        sendMouseMove(event, in: view)
    }

    func sendMouseScroll(_ event: NSEvent, in view: NSView) {
        guard let surface else {
            return
        }

        sendMouseMove(event, in: view)
        ghostty_surface_mouse_scroll(surface, event.scrollingDeltaX, event.scrollingDeltaY, 0)
    }

    func requestCloseFromRuntime(processAlive: Bool) {
        _ = processAlive
        onCloseRequested?(self)
    }

    func dispose() {
        destroySurface()
    }

    func handle(action: ghostty_action_s) {
        switch action.tag {
        case GHOSTTY_ACTION_RENDER:
            render()

        case GHOSTTY_ACTION_SET_TITLE:
            if let pointer = action.action.set_title.title {
                let value = String(cString: pointer)
                if !value.isEmpty {
                    title = value
                }
            }

        case GHOSTTY_ACTION_SHOW_CHILD_EXITED:
            processExited = true
            exitCode = Int(action.action.child_exited.exit_code)
            if title.isEmpty {
                title = "Exited"
            }

        case GHOSTTY_ACTION_CLOSE_TAB, GHOSTTY_ACTION_CLOSE_WINDOW, GHOSTTY_ACTION_QUIT:
            requestCloseFromRuntime(processAlive: false)

        default:
            break
        }
    }

    private func createSurface(in view: NSView) {
        guard let runtime, let app = runtime.appHandle else {
            return
        }

        if userdata == nil {
            userdata = Unmanaged.passUnretained(self).toOpaque()
        }

        var config = ghostty_surface_config_new()
        config.platform_tag = GHOSTTY_PLATFORM_MACOS
        config.platform.macos.nsview = Unmanaged.passUnretained(view).toOpaque()
        config.userdata = userdata
        config.scale_factor = max(view.window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 1.0, 1.0)
        config.font_size = 14
        config.context = context

        let workingDirectory = FileManager.default.homeDirectoryForCurrentUser.path
        workingDirectory.withCString { pointer in
            config.working_directory = pointer
            surface = ghostty_surface_new(app, &config)
        }

        guard let surface else {
            return
        }

        runtime.register(session: self, surface: surface, userdata: userdata)
    }

    private func destroySurface() {
        guard let surface else {
            return
        }

        runtime?.unregister(surface: surface, userdata: userdata)
        ghostty_surface_free(surface)
        self.surface = nil
    }
}
