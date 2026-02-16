import AppKit
import Foundation
import GhosttyKit

private final class WeakRuntimeBox {
    weak var value: GhosttyRuntime?

    init(_ value: GhosttyRuntime) {
        self.value = value
    }
}

private final class WeakSessionBox {
    weak var value: GhosttyTerminalSession?

    init(_ value: GhosttyTerminalSession) {
        self.value = value
    }
}

private enum GhosttyCallbackRegistry {
    private static let lock = NSLock()
    private static var runtimes: [UInt: WeakRuntimeBox] = [:]
    private static var sessions: [UInt: WeakSessionBox] = [:]

    static func register(runtime: GhosttyRuntime, key: UnsafeMutableRawPointer) {
        lock.lock()
        runtimes[UInt(bitPattern: key)] = WeakRuntimeBox(runtime)
        lock.unlock()
    }

    static func unregisterRuntime(key: UnsafeMutableRawPointer?) {
        guard let key else {
            return
        }

        lock.lock()
        runtimes.removeValue(forKey: UInt(bitPattern: key))
        lock.unlock()
    }

    static func runtime(for key: UnsafeMutableRawPointer?) -> GhosttyRuntime? {
        guard let key else {
            return nil
        }

        lock.lock()
        defer { lock.unlock() }

        let dictionaryKey = UInt(bitPattern: key)
        guard let box = runtimes[dictionaryKey] else {
            return nil
        }

        guard let runtime = box.value else {
            runtimes.removeValue(forKey: dictionaryKey)
            return nil
        }

        return runtime
    }

    static func register(session: GhosttyTerminalSession, key: UnsafeMutableRawPointer) {
        lock.lock()
        sessions[UInt(bitPattern: key)] = WeakSessionBox(session)
        lock.unlock()
    }

    static func unregisterSession(key: UnsafeMutableRawPointer?) {
        guard let key else {
            return
        }

        lock.lock()
        sessions.removeValue(forKey: UInt(bitPattern: key))
        lock.unlock()
    }

    static func session(for key: UnsafeMutableRawPointer?) -> GhosttyTerminalSession? {
        guard let key else {
            return nil
        }

        lock.lock()
        defer { lock.unlock() }

        let dictionaryKey = UInt(bitPattern: key)
        guard let box = sessions[dictionaryKey] else {
            return nil
        }

        guard let session = box.value else {
            sessions.removeValue(forKey: dictionaryKey)
            return nil
        }

        return session
    }
}

enum GhosttyRuntimeError: Error, LocalizedError {
    case initializationFailed
    case configCreateFailed
    case appCreateFailed

    var errorDescription: String? {
        switch self {
        case .initializationFailed:
            return "ghostty_init failed"
        case .configCreateFailed:
            return "ghostty_config_new failed"
        case .appCreateFailed:
            return "ghostty_app_new failed"
        }
    }
}

@MainActor
final class GhosttyRuntime {
    private var config: ghostty_config_t?
    private var app: ghostty_app_t?
    private var runtimeConfig = ghostty_runtime_config_s(
        userdata: nil,
        supports_selection_clipboard: true,
        wakeup_cb: nil,
        action_cb: nil,
        read_clipboard_cb: nil,
        confirm_read_clipboard_cb: nil,
        write_clipboard_cb: nil,
        close_surface_cb: nil)
    private var runtimeUserdata: UnsafeMutableRawPointer?
    private var sessionsBySurface: [UInt: WeakSessionBox] = [:]

    var appHandle: ghostty_app_t? {
        app
    }

    init() throws {
        if ghostty_init(UInt(CommandLine.argc), CommandLine.unsafeArgv) != GHOSTTY_SUCCESS {
            throw GhosttyRuntimeError.initializationFailed
        }

        guard let createdConfig = ghostty_config_new() else {
            throw GhosttyRuntimeError.configCreateFailed
        }

        config = createdConfig

        ghostty_config_load_default_files(createdConfig)
        ghostty_config_load_recursive_files(createdConfig)
        ghostty_config_finalize(createdConfig)

        runtimeConfig = ghostty_runtime_config_s(
            userdata: nil,
            supports_selection_clipboard: true,
            wakeup_cb: { userdata in
                GhosttyRuntime.onWakeup(userdata)
            },
            action_cb: { app, target, action in
                GhosttyRuntime.onAction(app, target, action)
            },
            read_clipboard_cb: { userdata, clipboard, state in
                GhosttyRuntime.onReadClipboard(userdata, clipboard, state)
            },
            confirm_read_clipboard_cb: { userdata, text, state, request in
                GhosttyRuntime.onConfirmReadClipboard(userdata, text, state, request)
            },
            write_clipboard_cb: { userdata, clipboard, content, len, confirm in
                GhosttyRuntime.onWriteClipboard(userdata, clipboard, content, len, confirm)
            },
            close_surface_cb: { userdata, processAlive in
                GhosttyRuntime.onCloseSurface(userdata, processAlive)
            })

        runtimeUserdata = Unmanaged.passUnretained(self).toOpaque()
        runtimeConfig.userdata = runtimeUserdata

        guard let app = ghostty_app_new(&runtimeConfig, createdConfig) else {
            throw GhosttyRuntimeError.appCreateFailed
        }

        self.app = app
        ghostty_app_set_focus(app, true)

        GhosttyCallbackRegistry.register(runtime: self, key: runtimeUserdata!)
    }

    deinit {
        if let app {
            ghostty_app_free(app)
        }

        if let config {
            ghostty_config_free(config)
        }

        GhosttyCallbackRegistry.unregisterRuntime(key: runtimeUserdata)
    }

    func register(session: GhosttyTerminalSession, surface: ghostty_surface_t, userdata: UnsafeMutableRawPointer?) {
        let key = UInt(bitPattern: surface)
        sessionsBySurface[key] = WeakSessionBox(session)

        if let userdata {
            GhosttyCallbackRegistry.register(session: session, key: userdata)
        }
    }

    func unregister(surface: ghostty_surface_t?, userdata: UnsafeMutableRawPointer?) {
        if let surface {
            sessionsBySurface.removeValue(forKey: UInt(bitPattern: surface))
        }

        GhosttyCallbackRegistry.unregisterSession(key: userdata)
    }

    func setFocused(_ focused: Bool) {
        guard let app else {
            return
        }

        ghostty_app_set_focus(app, focused)
    }

    private func session(for surface: ghostty_surface_t?) -> GhosttyTerminalSession? {
        guard let surface else {
            return nil
        }

        let key = UInt(bitPattern: surface)
        guard let box = sessionsBySurface[key] else {
            return nil
        }

        guard let session = box.value else {
            sessionsBySurface.removeValue(forKey: key)
            return nil
        }

        return session
    }

    private func tick() {
        guard let app else {
            return
        }

        ghostty_app_tick(app)
    }

    private func processAction(target: ghostty_target_s, action: ghostty_action_s) {
        if target.tag == GHOSTTY_TARGET_SURFACE {
            guard let session = session(for: target.target.surface) else {
                return
            }

            session.handle(action: action)
            return
        }

        if action.tag == GHOSTTY_ACTION_QUIT {
            NSApp.terminate(nil)
        }
    }

    private static func onWakeup(_ userdata: UnsafeMutableRawPointer?) {
        guard let runtime = GhosttyCallbackRegistry.runtime(for: userdata) else {
            return
        }

        Task { @MainActor in
            runtime.tick()
        }
    }

    private static func onAction(_ app: ghostty_app_t?, _ target: ghostty_target_s, _ action: ghostty_action_s) -> Bool {
        guard let app else {
            return false
        }

        guard let userdata = ghostty_app_userdata(app) else {
            return false
        }

        guard let runtime = GhosttyCallbackRegistry.runtime(for: userdata) else {
            return false
        }

        Task { @MainActor in
            runtime.processAction(target: target, action: action)
        }

        return false
    }

    private static func onReadClipboard(
        _ userdata: UnsafeMutableRawPointer?,
        _ clipboard: ghostty_clipboard_e,
        _ state: UnsafeMutableRawPointer?)
    {
        guard let session = GhosttyCallbackRegistry.session(for: userdata) else {
            return
        }

        Task { @MainActor in
            guard let surface = session.surfaceHandle else {
                return
            }

            let pasteboard: NSPasteboard
            switch clipboard {
            case GHOSTTY_CLIPBOARD_SELECTION:
                pasteboard = .general
            default:
                pasteboard = .general
            }

            let text = pasteboard.string(forType: .string) ?? ""
            text.withCString { pointer in
                ghostty_surface_complete_clipboard_request(surface, pointer, state, true)
            }
        }
    }

    private static func onConfirmReadClipboard(
        _ userdata: UnsafeMutableRawPointer?,
        _ text: UnsafePointer<CChar>?,
        _ state: UnsafeMutableRawPointer?,
        _ request: ghostty_clipboard_request_e)
    {
        guard let session = GhosttyCallbackRegistry.session(for: userdata) else {
            return
        }

        let resolvedText = text.map { String(cString: $0) } ?? ""

        Task { @MainActor in
            guard let surface = session.surfaceHandle else {
                return
            }

            resolvedText.withCString { pointer in
                ghostty_surface_complete_clipboard_request(surface, pointer, state, true)
            }
        }
    }

    private static func onWriteClipboard(
        _ userdata: UnsafeMutableRawPointer?,
        _ clipboard: ghostty_clipboard_e,
        _ content: UnsafePointer<ghostty_clipboard_content_s>?,
        _ len: Int,
        _ confirm: Bool)
    {
        guard GhosttyCallbackRegistry.session(for: userdata) != nil else {
            return
        }

        guard let content, len > 0 else {
            return
        }

        var resolvedText: String?

        for index in 0..<len {
            let entry = content[index]
            guard let mimePointer = entry.mime, let dataPointer = entry.data else {
                continue
            }

            let mime = String(cString: mimePointer)
            let value = String(cString: dataPointer)

            if mime == "text/plain" {
                resolvedText = value
                break
            }

            if resolvedText == nil {
                resolvedText = value
            }
        }

        guard let resolvedText else {
            return
        }

        Task { @MainActor in
            let pasteboard: NSPasteboard
            switch clipboard {
            case GHOSTTY_CLIPBOARD_SELECTION:
                pasteboard = .general
            default:
                pasteboard = .general
            }

            pasteboard.clearContents()
            pasteboard.setString(resolvedText, forType: .string)

            if confirm {
                NSSound.beep()
            }
        }
    }

    private static func onCloseSurface(_ userdata: UnsafeMutableRawPointer?, _ processAlive: Bool) {
        guard let session = GhosttyCallbackRegistry.session(for: userdata) else {
            return
        }

        Task { @MainActor in
            session.requestCloseFromRuntime(processAlive: processAlive)
        }
    }
}
