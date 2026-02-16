import SwiftUI

struct TabbedTerminalView: View {
    @EnvironmentObject private var workspace: TerminalWorkspace

    var body: some View {
        VStack(spacing: 0) {
            tabStrip
            Divider()
            content
        }
        .frame(minWidth: 980, minHeight: 640)
        .background(
            LinearGradient(
                colors: [Color(red: 0.08, green: 0.09, blue: 0.11), Color(red: 0.05, green: 0.06, blue: 0.08)],
                startPoint: .top,
                endPoint: .bottom))
    }

    private var tabStrip: some View {
        HStack(spacing: 10) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(workspace.sessions) { session in
                        terminalTabButton(for: session)
                    }
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
            }

            Button(action: workspace.addTab) {
                Image(systemName: "plus")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundStyle(Color.white.opacity(0.9))
                    .padding(8)
                    .background(Color.white.opacity(0.08), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            }
            .buttonStyle(.plain)
            .padding(.trailing, 12)
        }
        .frame(height: 52)
        .background(
            LinearGradient(
                colors: [Color.black.opacity(0.35), Color.black.opacity(0.15)],
                startPoint: .top,
                endPoint: .bottom))
    }

    private var content: some View {
        Group {
            if let startupError = workspace.startupError {
                VStack(spacing: 10) {
                    Text("Failed to initialize Ghostty")
                        .font(.title3.weight(.semibold))
                        .foregroundStyle(.white)
                    Text(startupError)
                        .font(.body)
                        .foregroundStyle(.white.opacity(0.75))
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 32)
                }
            } else if workspace.isStarting && workspace.sessions.isEmpty {
                VStack(spacing: 10) {
                    ProgressView()
                        .progressViewStyle(.circular)
                        .tint(.white)
                    Text("Starting native Ghostty runtime...")
                        .font(.body.weight(.semibold))
                        .foregroundStyle(.white.opacity(0.8))
                }
            } else {
                ZStack {
                    ForEach(workspace.sessions) { session in
                        GhosttyTerminalHost(session: session)
                            .id(session.id)
                            .opacity(workspace.selectedSessionID == session.id ? 1 : 0)
                            .allowsHitTesting(workspace.selectedSessionID == session.id)
                    }
                }
            }
        }
    }

    private func terminalTabButton(for session: GhosttyTerminalSession) -> some View {
        let isSelected = workspace.selectedSessionID == session.id

        return HStack(spacing: 8) {
            Circle()
                .fill(session.processExited ? Color.orange : Color.green)
                .frame(width: 6, height: 6)

            Text(session.title)
                .font(.system(size: 12, weight: .semibold, design: .rounded))
                .lineLimit(1)
                .foregroundStyle(isSelected ? Color.white : Color.white.opacity(0.82))
                .frame(maxWidth: 170, alignment: .leading)

            Button {
                workspace.closeTab(id: session.id)
            } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 9, weight: .bold))
                    .foregroundStyle(Color.white.opacity(0.72))
            }
            .buttonStyle(.plain)
            .help("Close Tab")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 7)
        .background(
            RoundedRectangle(cornerRadius: 11, style: .continuous)
                .fill(isSelected ? Color.white.opacity(0.15) : Color.white.opacity(0.06)))
        .overlay(
            RoundedRectangle(cornerRadius: 11, style: .continuous)
                .strokeBorder(isSelected ? Color.white.opacity(0.22) : Color.clear, lineWidth: 1))
        .contentShape(RoundedRectangle(cornerRadius: 11, style: .continuous))
        .onTapGesture {
            workspace.selectTab(id: session.id)
        }
        .help(session.title)
    }
}
