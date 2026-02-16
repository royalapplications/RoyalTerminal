// swift-tools-version: 5.10

import PackageDescription

let package = Package(
    name: "RoyalTerminalMacNativeTabbed",
    platforms: [
        .macOS(.v14),
    ],
    products: [
        .executable(
            name: "RoyalTerminalMacNativeTabbed",
            targets: ["RoyalTerminalMacNativeTabbed"]),
    ],
    targets: [
        .binaryTarget(
            name: "GhosttyKit",
            path: "../../external/ghostty/macos/GhosttyKit.xcframework"),
        .executableTarget(
            name: "RoyalTerminalMacNativeTabbed",
            dependencies: ["GhosttyKit"],
            path: "Sources/RoyalTerminalMacNativeTabbed",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("Carbon"),
                .linkedFramework("CoreText"),
                .linkedFramework("Metal"),
                .linkedFramework("QuartzCore"),
                .linkedLibrary("c++"),
            ]),
    ])
