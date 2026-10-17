#import <Foundation/Foundation.h>
#import <ApplicationServices/ApplicationServices.h>
#include <unistd.h>
#include <stdio.h>

static id Copy(AXUIElementRef element, CFStringRef attribute, AXError* result) {
    CFTypeRef value = NULL;
    AXError error = AXUIElementCopyAttributeValue(element, attribute, &value);
    if (result) *result = error;
    return value ? CFBridgingRelease(value) : nil;
}
static NSString* Text(id value) { return [value isKindOfClass:NSString.class] ? value : @""; }
static bool Element(id value) {
    return value && CFGetTypeID((__bridge CFTypeRef)value) == AXUIElementGetTypeID();
}
static BOOL SameElements(NSArray* left, NSArray* right) {
    if (left.count != right.count) return NO;
    for (id element in left) if (![right containsObject:element]) return NO;
    return YES;
}

int main(int argc, const char** argv) { @autoreleasepool {
    if (argc < 3) return 2;
    pid_t target = atoi(argv[1]);
    if (target <= 0 || target == getpid()) return 2;
    NSString* mode = [NSString stringWithUTF8String:argv[2]];
    if (![@[@"confirm",@"cancel",@"dump"] containsObject:mode]) return 2;
    NSString* newName = nil; NSArray* names = @[];
    if (argc > 3) {
        NSString* argument = [NSString stringWithUTF8String:argv[3]];
        if ([argument hasPrefix:@"["]) {
            id decoded = [NSJSONSerialization JSONObjectWithData:[argument dataUsingEncoding:NSUTF8StringEncoding]
                options:0 error:nil];
            if (![decoded isKindOfClass:NSArray.class] || ![decoded count]) return 2;
            for (id name in decoded) if (![name isKindOfClass:NSString.class]) return 2;
            names = decoded;
        } else newName = argument;
    }
    AXUIElementRef app = AXUIElementCreateApplication(target);
    AXUIElementSetMessagingTimeout(app, 0.5);
    AXError windowsError = 0;
    NSArray* windows = Copy(app, kAXWindowsAttribute, &windowsError);
    NSMutableArray* queue = [NSMutableArray new];
    for (id window in windows) [queue addObject:@{@"element":window}];
    NSMutableSet* seen = [NSMutableSet new]; NSMutableArray* nodes = [NSMutableArray new];
    NSMutableDictionary* namedRows = [NSMutableDictionary new];
    id defaultButton = nil, button = nil, cancelButton = nil, field = nil;
    NSArray* attrs = @[(__bridge NSString*)kAXRoleAttribute, (__bridge NSString*)kAXTitleAttribute,
        (__bridge NSString*)kAXDescriptionAttribute, (__bridge NSString*)kAXIdentifierAttribute,
        (__bridge NSString*)kAXValueAttribute, (__bridge NSString*)kAXEnabledAttribute,
        (__bridge NSString*)kAXChildrenAttribute, (__bridge NSString*)kAXDefaultButtonAttribute];
    for (NSUInteger at = 0; at < queue.count && at < 500; ++at) {
        NSDictionary* entry = queue[at]; id value = entry[@"element"];
        if (!Element(value) || [seen containsObject:value]) continue;
        [seen addObject:value];
        AXUIElementRef node = (__bridge AXUIElementRef)value;
        AXUIElementSetMessagingTimeout(node, 0.5);
        CFArrayRef raw = NULL;
        AXError error = AXUIElementCopyMultipleAttributeValues(node, (__bridge CFArrayRef)attrs, 0, &raw);
        NSArray* data = raw ? CFBridgingRelease(raw) : nil;
        if (error || data.count != attrs.count) { [nodes addObject:@{@"readError":@(error)}]; continue; }
        NSString* role = Text(data[0]); NSString* title = Text(data[1]);
        NSString* description = Text(data[2]); NSString* identifier = Text(data[3]); NSString* text = Text(data[4]);
        NSNumber* enabled = [data[5] isKindOfClass:NSNumber.class] ? data[5] : @NO;
        id table = entry[@"table"], row = entry[@"row"];
        if ([role isEqualToString:@"AXOutline"] || [role isEqualToString:@"AXTable"]) { table = value; row = nil; }
        if ([role isEqualToString:@"AXRow"]) row = value;
        if (row && table && [names containsObject:text]) namedRows[text] = @{@"row":row,@"table":table};
        if ([role isEqualToString:@"AXSheet"] || [role isEqualToString:@"AXWindow"])
            if (Element(data[7])) defaultButton = data[7];
        if ([role isEqualToString:@"AXTextField"] && [identifier isEqualToString:@"saveAsNameTextField"]) field = value;
        if ([role isEqualToString:@"AXButton"]) {
            if ([identifier isEqualToString:@"OKButton"]) button = value;
            if ([identifier isEqualToString:@"CancelButton"]) cancelButton = value;
        }
        BOOL ownText = [names containsObject:text] || [identifier isEqualToString:@"saveAsNameTextField"];
        [nodes addObject:@{@"role":role,@"title":title,@"description":description,@"id":identifier,
            @"value":ownText ? text : @"",@"enabled":enabled}];
        if ([data[6] isKindOfClass:NSArray.class]) for (id child in data[6]) {
            NSMutableDictionary* next = [@{@"element":child} mutableCopy];
            if (row) next[@"row"] = row;
            if (table) next[@"table"] = table;
            [queue addObject:next];
        }
    }
    id selected = [mode isEqualToString:@"cancel"] ? cancelButton : (button ?: defaultButton);
    AXError writeError = kAXErrorNoValue, selectionError = kAXErrorNoValue, pressError = kAXErrorNoValue;
    NSString* readBack = @""; BOOL selectionMatches = NO;
    NSArray* actionNames = nil; AXError actionsError = kAXErrorNoValue;
    if (selected) {
        CFArrayRef actions = NULL;
        actionsError = AXUIElementCopyActionNames((__bridge AXUIElementRef)selected, &actions);
        if (actions) actionNames = CFBridgingRelease(actions);
    }
    if (![mode isEqualToString:@"dump"]) {
        BOOL ready = YES;
        if (newName) {
            if (field) {
                writeError = AXUIElementSetAttributeValue((__bridge AXUIElementRef)field, kAXValueAttribute,
                    (__bridge CFStringRef)newName);
                readBack = Text(Copy((__bridge AXUIElementRef)field, kAXValueAttribute, NULL));
            }
            ready = writeError == kAXErrorSuccess && [readBack isEqualToString:newName];
        }
        if (names.count) {
            NSMutableArray* rows = [NSMutableArray new]; id table = nil;
            for (NSString* name in names) {
                NSDictionary* found = namedRows[name];
                if (!found || (table && ![table isEqual:found[@"table"]])) { ready = NO; break; }
                table = found[@"table"]; [rows addObject:found[@"row"]];
            }
            if (ready && table && rows.count == names.count) {
                selectionError = AXUIElementSetAttributeValue((__bridge AXUIElementRef)table,
                    kAXSelectedRowsAttribute, (__bridge CFArrayRef)rows);
                NSArray* actual = Copy((__bridge AXUIElementRef)table, kAXSelectedRowsAttribute, NULL);
                selectionMatches = [actual isKindOfClass:NSArray.class] && SameElements(rows, actual);
            }
            ready = ready && selectionError == kAXErrorSuccess && selectionMatches;
        }
        // Read-only polling does not replay an action after an ambiguous response.
        double end = NSProcessInfo.processInfo.systemUptime + 0.5;
        while (ready && selected && ![Copy((__bridge AXUIElementRef)selected,kAXEnabledAttribute,NULL) boolValue]
            && NSProcessInfo.processInfo.systemUptime < end)
            CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.01, true);
        if (ready && selected && [Copy((__bridge AXUIElementRef)selected,kAXEnabledAttribute,NULL) boolValue])
            pressError = AXUIElementPerformAction((__bridge AXUIElementRef)selected, kAXPressAction);
    }
    NSDictionary* receipt = @{@"clientPID":@(getpid()),@"targetPID":@(target),@"trusted":@(AXIsProcessTrusted()),
        @"windowsError":@(windowsError),@"windowCount":@(windows.count),@"mode":mode,@"nodes":nodes,
        @"defaultButtonFound":@(defaultButton!=nil),@"confirmButtonFound":@(button!=nil),@"nameFieldFound":@(field!=nil),
        @"cancelButtonFound":@(cancelButton!=nil),@"actionsError":@(actionsError),@"actions":actionNames?:@[],
        @"writeError":@(writeError),@"readBack":readBack,@"pressError":@(pressError),@"requestedName":newName?:@"",
        @"requestedNames":names,@"selectionError":@(selectionError),@"selectionMatches":@(selectionMatches)};
    NSData* json = [NSJSONSerialization dataWithJSONObject:receipt options:0 error:nil];
    fwrite(json.bytes, 1, json.length, stdout); fputc('\n', stdout); CFRelease(app);
    return windowsError==kAXErrorSuccess && ([mode isEqualToString:@"dump"] || pressError==kAXErrorSuccess) ? 0 : 1;
} }
