import Foundation
import GhosttyKit

@MainActor
final class TerminalWorkspace: ObservableObject {
    @Published var sessions: [GhosttyTerminalSession] = []
    @Published var selectedSessionID: UUID?
    @Published var startupError: String?
    @Published var isStarting = false

    private var runtime: GhosttyRuntime?
    private var hasStarted = false

    init() {}

    var selectedSession: GhosttyTerminalSession? {
        guard let selectedSessionID else {
            return sessions.first
        }

        return sessions.first(where: { $0.id == selectedSessionID })
    }

    func addTab() {
        guard let runtime else {
            return
        }

        let context: ghostty_surface_context_e = sessions.isEmpty
            ? GHOSTTY_SURFACE_CONTEXT_WINDOW
            : GHOSTTY_SURFACE_CONTEXT_TAB

        let session = GhosttyTerminalSession(runtime: runtime, context: context)
        session.onCloseRequested = { [weak self] closedSession in
            self?.closeTab(id: closedSession.id)
        }

        sessions.append(session)
        selectedSessionID = session.id
    }

    func selectTab(id: UUID) {
        selectedSessionID = id
    }

    func closeCurrentTab() {
        guard let selectedSessionID else {
            return
        }

        closeTab(id: selectedSessionID)
    }

    func closeTab(id: UUID) {
        guard let index = sessions.firstIndex(where: { $0.id == id }) else {
            return
        }

        sessions[index].dispose()
        sessions.remove(at: index)

        if sessions.isEmpty {
            addTab()
            return
        }

        if selectedSessionID == id {
            let fallbackIndex = min(index, sessions.count - 1)
            selectedSessionID = sessions[fallbackIndex].id
        }
    }

    func setAppFocused(_ focused: Bool) {
        runtime?.setFocused(focused)
    }

    func startup() {
        guard !hasStarted else {
            return
        }

        hasStarted = true
        isStarting = true

        defer {
            isStarting = false
        }

        do {
            runtime = try GhosttyRuntime()
            startupError = nil

            if sessions.isEmpty {
                addTab()
            }
        } catch {
            runtime = nil
            startupError = error.localizedDescription
        }
    }
}
