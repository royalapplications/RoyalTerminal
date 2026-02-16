import AppKit
import SwiftUI

final class RoyalTerminalMacNativeAppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)

        // Ensure at least one window is brought forward when launched via `swift run`.
        DispatchQueue.main.async {
            if let window = NSApp.windows.first {
                window.makeKeyAndOrderFront(nil)
            }

            NSApp.activate(ignoringOtherApps: true)
        }
    }
}

@main
struct RoyalTerminalMacNativeTabbedApp: App {
    @NSApplicationDelegateAdaptor(RoyalTerminalMacNativeAppDelegate.self)
    private var appDelegate

    @StateObject private var workspace = TerminalWorkspace()

    var body: some Scene {
        WindowGroup {
            TabbedTerminalView()
                .environmentObject(workspace)
                .task {
                    workspace.startup()
                }
                .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
                    workspace.setAppFocused(true)
                }
                .onReceive(NotificationCenter.default.publisher(for: NSApplication.didResignActiveNotification)) { _ in
                    workspace.setAppFocused(false)
                }
        }
        .commands {
            CommandMenu("Tabs") {
                Button("New Tab") {
                    workspace.addTab()
                }
                .keyboardShortcut("t", modifiers: [.command])

                Button("Close Tab") {
                    workspace.closeCurrentTab()
                }
                .keyboardShortcut("w", modifiers: [.command])
                .disabled(workspace.sessions.isEmpty)
            }
        }
    }
}
