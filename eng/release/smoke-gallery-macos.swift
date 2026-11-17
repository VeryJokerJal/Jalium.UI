import AppKit
import CoreGraphics
import Foundation
import Darwin

guard CommandLine.arguments.count == 2 else {
    fatalError("Usage: smoke-gallery-macos.swift <Gallery.app>")
}
let bundle = URL(fileURLWithPath: CommandLine.arguments[1]).standardizedFileURL
let process = Process()
process.executableURL = bundle.appendingPathComponent("Contents/MacOS/Jalium.UI.Gallery.MacOS")
process.standardOutput = FileHandle.standardOutput
process.standardError = FileHandle.standardError
try process.run()
defer {
    if process.isRunning { kill(process.processIdentifier, SIGKILL) }
}
var hasWindow = false
let deadline = Date().addingTimeInterval(30)
while process.isRunning && Date() < deadline {
    let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
    hasWindow = windows.contains { window in
        (window[kCGWindowOwnerPID as String] as? Int32) == process.processIdentifier
            && (window[kCGWindowLayer as String] as? Int) == 0
    }
    if hasWindow { break }
    RunLoop.current.run(until: Date().addingTimeInterval(0.25))
}
guard hasWindow, process.isRunning else {
    fatalError("Gallery did not create an on-screen window")
}
print("[gallery-smoke] on-screen window ready: \(process.processIdentifier)")
RunLoop.current.run(until: Date().addingTimeInterval(3))
guard process.isRunning,
      let application = NSRunningApplication(processIdentifier: process.processIdentifier),
      application.terminate() else {
    fatalError("Gallery exited unexpectedly or rejected normal termination")
}
let closeDeadline = Date().addingTimeInterval(15)
while process.isRunning && Date() < closeDeadline {
    RunLoop.current.run(until: Date().addingTimeInterval(0.25))
}
guard !process.isRunning, process.terminationStatus == 0 else {
    fatalError("Gallery did not exit cleanly")
}
print("[gallery-smoke] clean exit")
