import AppKit
import GhosttyKit
import SwiftUI

final class GhosttyMetalTerminalView: NSView {
    var session: GhosttyTerminalSession {
        didSet {
            session.attach(to: self)
        }
    }

    private var trackingAreaHandle: NSTrackingArea?

    override var acceptsFirstResponder: Bool {
        true
    }

    init(session: GhosttyTerminalSession) {
        self.session = session
        super.init(frame: NSRect(x: 0, y: 0, width: 960, height: 640))
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not implemented")
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        window?.acceptsMouseMovedEvents = true
        session.attach(to: self)
        session.updateSurfaceMetrics(for: self)
    }

    override func layout() {
        super.layout()
        session.updateSurfaceMetrics(for: self)
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        session.updateSurfaceMetrics(for: self)
    }

    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties()
        session.updateSurfaceMetrics(for: self)
    }

    override func becomeFirstResponder() -> Bool {
        let result = super.becomeFirstResponder()
        if result {
            session.setFocused(true)
        }
        return result
    }

    override func resignFirstResponder() -> Bool {
        let result = super.resignFirstResponder()
        if result {
            session.setFocused(false)
        }
        return result
    }

    override func updateTrackingAreas() {
        if let trackingAreaHandle {
            removeTrackingArea(trackingAreaHandle)
        }

        let trackingAreaHandle = NSTrackingArea(
            rect: .zero,
            options: [.activeAlways, .inVisibleRect, .mouseMoved, .mouseEnteredAndExited],
            owner: self,
            userInfo: nil)
        addTrackingArea(trackingAreaHandle)
        self.trackingAreaHandle = trackingAreaHandle

        super.updateTrackingAreas()
    }

    override func keyDown(with event: NSEvent) {
        let action: ghostty_input_action_e = event.isARepeat ? GHOSTTY_ACTION_REPEAT : GHOSTTY_ACTION_PRESS
        session.sendKeyEvent(event, action: action)
    }

    override func keyUp(with event: NSEvent) {
        session.sendKeyEvent(event, action: GHOSTTY_ACTION_RELEASE)
    }

    override func mouseDown(with event: NSEvent) {
        session.sendMouseButton(GHOSTTY_MOUSE_PRESS, event: event, in: self, button: GHOSTTY_MOUSE_LEFT)
    }

    override func mouseUp(with event: NSEvent) {
        session.sendMouseButton(GHOSTTY_MOUSE_RELEASE, event: event, in: self, button: GHOSTTY_MOUSE_LEFT)
    }

    override func rightMouseDown(with event: NSEvent) {
        session.sendMouseButton(GHOSTTY_MOUSE_PRESS, event: event, in: self, button: GHOSTTY_MOUSE_RIGHT)
    }

    override func rightMouseUp(with event: NSEvent) {
        session.sendMouseButton(GHOSTTY_MOUSE_RELEASE, event: event, in: self, button: GHOSTTY_MOUSE_RIGHT)
    }

    override func otherMouseDown(with event: NSEvent) {
        session.sendMouseButton(
            GHOSTTY_MOUSE_PRESS,
            event: event,
            in: self,
            button: GhosttyInputConverter.mouseButton(from: event))
    }

    override func otherMouseUp(with event: NSEvent) {
        session.sendMouseButton(
            GHOSTTY_MOUSE_RELEASE,
            event: event,
            in: self,
            button: GhosttyInputConverter.mouseButton(from: event))
    }

    override func mouseMoved(with event: NSEvent) {
        session.sendMouseMove(event, in: self)
    }

    override func mouseDragged(with event: NSEvent) {
        session.sendMouseMove(event, in: self)
    }

    override func rightMouseDragged(with event: NSEvent) {
        session.sendMouseMove(event, in: self)
    }

    override func otherMouseDragged(with event: NSEvent) {
        session.sendMouseMove(event, in: self)
    }

    override func scrollWheel(with event: NSEvent) {
        session.sendMouseScroll(event, in: self)
    }
}

struct GhosttyTerminalHost: NSViewRepresentable {
    @ObservedObject var session: GhosttyTerminalSession

    func makeNSView(context: Context) -> GhosttyMetalTerminalView {
        GhosttyMetalTerminalView(session: session)
    }

    func updateNSView(_ nsView: GhosttyMetalTerminalView, context: Context) {
        if nsView.session !== session {
            nsView.session = session
        }

        session.attach(to: nsView)
    }
}
