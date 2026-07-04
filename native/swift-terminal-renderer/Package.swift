// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "SwiftTerminalRenderer",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .library(
            name: "SwiftTerminalRenderer",
            type: .dynamic,
            targets: ["SwiftTerminalRenderer"]
        )
    ],
    dependencies: [],
    targets: [
        .target(
            name: "SwiftTerminalRenderer",
            dependencies: [],
            path: "Sources/SwiftTerminalRenderer"
        )
    ]
)
