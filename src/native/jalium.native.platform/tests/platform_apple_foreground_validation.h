#pragma once

#import <AppKit/AppKit.h>
#include "jalium_platform.h"
#include <cstdio>
#include <cstdlib>
#include <unistd.h>

// An opt-in, bundled host obtains real desktop selection before the fixture
// starts. CLI/CTest retain their existing background policy and assertions.
static bool UsesForegroundValidationHost()
{
    bool enabled = [[NSBundle.mainBundle objectForInfoDictionaryKey:@"JaliumForegroundValidation"] boolValue];
    if (enabled) {
        NSString* path = [NSBundle.mainBundle objectForInfoDictionaryKey:@"JaliumForegroundValidationLog"];
        if (path.length && (!std::freopen(path.fileSystemRepresentation, "w", stdout)
            || dup2(fileno(stdout), fileno(stderr)) < 0)) std::exit(4);
        std::setvbuf(stdout, nullptr, _IOLBF, 0);
    }
    return enabled;
}

@interface JaliumForegroundValidationGate : NSObject
@property(nonatomic) BOOL requested;
@property(nonatomic, weak) NSWindow* window;
@property(nonatomic, strong) NSTextField* status;
- (void)start:(NSButton*)sender;
@end

@implementation JaliumForegroundValidationGate
- (void)start:(NSButton*)sender
{
    if (!NSApp.active || NSApp.keyWindow != self.window || NSApp.mainWindow != self.window) {
        std::printf("FOREGROUND_REJECT pid=%d active=%d key=%d main=%d\n", getpid(),
            NSApp.active, NSApp.keyWindow == self.window, NSApp.mainWindow == self.window);
        self.status.stringValue = @"请先点击此窗口取得前台焦点，再开始回归。";
        return;
    }
    self.requested = YES;
    sender.enabled = NO;
}
@end

static bool AwaitForegroundValidation(NSString* title)
{
    NSWindow* window = [[NSWindow alloc] initWithContentRect:NSMakeRect(0, 0, 480, 248)
        styleMask:NSWindowStyleMaskTitled | NSWindowStyleMaskClosable
        backing:NSBackingStoreBuffered defer:NO];
    window.releasedWhenClosed = NO;
    window.title = title;
    [window center];
    auto* gate = [JaliumForegroundValidationGate new];
    gate.window = window;
    auto* heading = [NSTextField labelWithString:@"Window 原生回归"];
    heading.font = [NSFont systemFontOfSize:26 weight:NSFontWeightSemibold];
    auto* description = [NSTextField wrappingLabelWithString:
        @"确认窗口处于前台后开始。执行期间请保持当前桌面状态。"];
    description.font = [NSFont systemFontOfSize:14];
    gate.status = [NSTextField wrappingLabelWithString:@"等待开始 · 尚未执行测试"];
    gate.status.textColor = NSColor.secondaryLabelColor;
    auto* button = [NSButton buttonWithTitle:@"开始回归" target:gate action:@selector(start:)];
    button.bezelStyle = NSBezelStyleRounded;
    button.keyEquivalent = @"\r";
    window.initialFirstResponder = button;
    [button.heightAnchor constraintGreaterThanOrEqualToConstant:44].active = YES;
    auto* stack = [NSStackView stackViewWithViews:@[heading, description, gate.status, button]];
    stack.orientation = NSUserInterfaceLayoutOrientationVertical;
    stack.alignment = NSLayoutAttributeLeading;
    stack.spacing = 16;
    stack.translatesAutoresizingMaskIntoConstraints = NO;
    [window.contentView addSubview:stack];
    [NSLayoutConstraint activateConstraints:@[
        [stack.leadingAnchor constraintEqualToAnchor:window.contentView.leadingAnchor constant:24],
        [stack.trailingAnchor constraintEqualToAnchor:window.contentView.trailingAnchor constant:-24],
        [stack.topAnchor constraintEqualToAnchor:window.contentView.topAnchor constant:24],
        [stack.bottomAnchor constraintLessThanOrEqualToAnchor:window.contentView.bottomAnchor constant:-24]
    ]];
    [NSApp activate];
    [window makeKeyAndOrderFront:nil];
    std::printf("FOREGROUND_READY pid=%d bundle=%s suite=%s\n", getpid(),
        NSBundle.mainBundle.bundleIdentifier.UTF8String, title.UTF8String);
    std::fflush(stdout);
    NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:180];
    while (!gate.requested && window.visible && deadline.timeIntervalSinceNow > 0) {
        jalium_platform_poll_events();
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
    }
    bool selected = gate.requested && NSApp.active && NSApp.keyWindow == window && NSApp.mainWindow == window;
    std::printf("FOREGROUND_START pid=%d accepted=%d active=%d key=%d main=%d\n",
        getpid(), selected, NSApp.active, NSApp.keyWindow == window, NSApp.mainWindow == window);
    std::fflush(stdout);
    [window orderOut:nil];
    [window close];
    if (!selected) std::fprintf(stderr, "Foreground validation was not started; no fixture assertions ran.\n");
    return selected;
}
