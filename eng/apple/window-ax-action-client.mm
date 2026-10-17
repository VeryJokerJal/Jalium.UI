// Separate-process AX action/readback client for the Window notification host.
// JSON lines on stdin/stdout keep old AX references alive across lifecycle checks.
#import <Foundation/Foundation.h>
#import <ApplicationServices/ApplicationServices.h>
#include <iostream>
#include <string>
#include <unistd.h>
#include <cerrno>
#include <climits>
#include <cstdlib>

static id Copy(AXUIElementRef element, CFStringRef attribute, AXError* error = nullptr) {
    CFTypeRef value = nullptr;
    AXError result = AXUIElementCopyAttributeValue(element, attribute, &value);
    if (error) *error = result;
    return value ? CFBridgingRelease(value) : nil;
}
static bool IsElement(id value) {
    return value && CFGetTypeID((__bridge CFTypeRef)value) == AXUIElementGetTypeID();
}
static NSString* Text(id value) { return [value isKindOfClass:NSString.class] ? value : @""; }
static id Scalar(id value) {
    if (!value) return NSNull.null;
    if ([value isKindOfClass:NSString.class] || [value isKindOfClass:NSNumber.class]) return value;
    if (CFGetTypeID((__bridge CFTypeRef)value) == AXValueGetTypeID()) {
        CGPoint point{}; CGSize size{};
        if (AXValueGetType((__bridge AXValueRef)value) == kAXValueTypeCGPoint &&
            AXValueGetValue((__bridge AXValueRef)value,kAXValueTypeCGPoint,&point))
            return @{@"x":@(point.x),@"y":@(point.y)};
        if (AXValueGetType((__bridge AXValueRef)value) == kAXValueTypeCGSize &&
            AXValueGetValue((__bridge AXValueRef)value,kAXValueTypeCGSize,&size))
            return @{@"width":@(size.width),@"height":@(size.height)};
        CFRange range{};
        if (AXValueGetType((__bridge AXValueRef)value) == kAXValueTypeCFRange &&
            AXValueGetValue((__bridge AXValueRef)value, kAXValueTypeCFRange, &range))
            return @{@"location":@(range.location),@"length":@(range.length)};
    }
    return NSNull.null;
}

struct Client {
    AXUIElementRef app;
    NSMutableArray* held = [NSMutableArray new];
    NSMutableDictionary* latest = [NSMutableDictionary new];
    explicit Client(pid_t pid) : app(AXUIElementCreateApplication(pid)) {
        AXUIElementSetMessagingTimeout(app, 0.5);
    }
    ~Client() { CFRelease(app); }
    NSUInteger Token(id element) {
        for (NSUInteger i = 0; i < held.count; ++i)
            if (CFEqual((__bridge CFTypeRef)held[i], (__bridge CFTypeRef)element)) return i + 1;
        [held addObject:element]; return held.count;
    }
    NSDictionary* Read(id value) {
        AXUIElementRef element = (__bridge AXUIElementRef)value;
        AXUIElementSetMessagingTimeout(element, 0.5);
        NSMutableDictionary* result = [@{@"token":@(Token(value))} mutableCopy];
        NSMutableDictionary* errors = [NSMutableDictionary new];
        NSDictionary* attributes = @{@"id":@"AXIdentifier",@"role":@"AXRole",@"name":@"AXTitle",
            @"value":@"AXValue",@"enabled":@"AXEnabled",@"focused":@"AXFocused",
            @"selection":@"AXSelectedTextRange",@"selectedText":@"AXSelectedText",@"selected":@"AXSelected",@"position":@"AXPosition",@"size":@"AXSize"};
        for (NSString* key in attributes) {
            AXError error = kAXErrorSuccess;
            result[key] = Scalar(Copy(element, (__bridge CFStringRef)attributes[key], &error));
            errors[key] = @(error);
        }
        Boolean valueSettable = false, selectionSettable = false;
        AXError valueError = AXUIElementIsAttributeSettable(element,kAXValueAttribute,&valueSettable);
        AXError selectionError = AXUIElementIsAttributeSettable(element,kAXSelectedTextRangeAttribute,&selectionSettable);
        result[@"valueSettable"] = @(valueError == 0 && valueSettable);
        result[@"selectionSettable"] = @(selectionError == 0 && selectionSettable);
        result[@"errors"] = errors;
        for (NSString* attribute in @[@"AXSelected", @"AXSelectedRows", @"AXSelectedChildren"]) {
            Boolean settable = false;
            AXError error = AXUIElementIsAttributeSettable(element, (__bridge CFStringRef)attribute, &settable);
            result[[attribute stringByAppendingString:@"Settable"]] = @(error == 0 && settable);
        }
        for (NSString* attribute in @[@"AXSelectedChildren", @"AXSelectedRows"]) {
            AXError error = kAXErrorSuccess;
            id children = Copy(element, (__bridge CFStringRef)attribute, &error);
            if (![children isKindOfClass:NSArray.class]) continue;
            NSMutableArray* selected = [NSMutableArray new];
            for (id child in children) {
                if (!IsElement(child)) continue;
                AXUIElementRef node = (__bridge AXUIElementRef)child;
                [selected addObject:@{@"token":@(Token(child)),@"id":Scalar(Copy(node,kAXIdentifierAttribute)),
                    @"role":Scalar(Copy(node,kAXRoleAttribute)),@"index":Scalar(Copy(node,kAXIndexAttribute))}];
            }
            result[attribute] = selected;
        }
        id parent = Copy(element,kAXParentAttribute);
        if (IsElement(parent)) result[@"parent"] = @{@"token":@(Token(parent)),
            @"id":Scalar(Copy((__bridge AXUIElementRef)parent,kAXIdentifierAttribute)),
            @"role":Scalar(Copy((__bridge AXUIElementRef)parent,kAXRoleAttribute))};
        return result;
    }
    NSArray* Discover() {
        [latest removeAllObjects];
        id windows = Copy(app, kAXWindowsAttribute);
        NSMutableArray* queue = [windows isKindOfClass:NSArray.class] ? [windows mutableCopy] : [NSMutableArray new];
        NSMutableArray* seen = [NSMutableArray new]; NSMutableArray* nodes = [NSMutableArray new];
        for (NSUInteger at = 0; at < queue.count && seen.count < 256; ++at) {
            id value = queue[at]; if (!IsElement(value)) continue;
            bool duplicate = false;
            for (id old in seen) if (CFEqual((__bridge CFTypeRef)value,(__bridge CFTypeRef)old)) duplicate = true;
            if (duplicate) continue;
            [seen addObject:value];
            AXUIElementRef element = (__bridge AXUIElementRef)value;
            NSDictionary* node = Read(value); [nodes addObject:node];
            NSString* identifier = Text(node[@"id"]);
            if (identifier.length) latest[identifier] = value;
            id children = Copy(element, kAXChildrenAttribute);
            if ([children isKindOfClass:NSArray.class]) [queue addObjectsFromArray:children];
        }
        return nodes;
    }
    id Resolve(NSDictionary* command) {
        if (command[@"token"]) {
            NSInteger index = [command[@"token"] integerValue];
            return index > 0 && index <= (NSInteger)held.count ? held[index-1] : nil;
        }
        return latest[Text(command[@"id"])];
    }
};

int main(int argc, const char** argv) { @autoreleasepool {
    if (argc != 2) return 2;
    char* end = nullptr; errno = 0;
    long parsed = strtol(argv[1], &end, 10);
    if (errno || !end || *end || parsed <= 0 || parsed > INT_MAX || parsed == getpid()) return 2;
    pid_t target = static_cast<pid_t>(parsed); Client client(target); std::string line;
    while (std::getline(std::cin,line)) { @autoreleasepool {
        NSData* data = [NSData dataWithBytes:line.data() length:line.size()];
        id decoded = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
        if (![decoded isKindOfClass:NSDictionary.class]) return 2;
        NSDictionary* command = decoded; NSString* action = Text(command[@"action"]);
        NSMutableDictionary* result = [@{@"action":action,@"clientPID":@(getpid()),
            @"targetPID":@(target),@"trusted":@(AXIsProcessTrusted())} mutableCopy];
        if (!AXIsProcessTrusted()) result[@"error"] = @"Existing AX permission is unavailable";
        else if ([action isEqualToString:@"discover"]) result[@"nodes"] = client.Discover();
        else if (![action isEqualToString:@"quit"]) {
            id value = client.Resolve(command); AXError error = kAXErrorNoValue;
            if (value) {
                AXUIElementRef element = (__bridge AXUIElementRef)value;
                if ([action isEqualToString:@"read"]) error = kAXErrorSuccess;
                else if ([action isEqualToString:@"press"]) error = AXUIElementPerformAction(element,kAXPressAction);
                else if ([action isEqualToString:@"focus"]) error = AXUIElementSetAttributeValue(element,kAXFocusedAttribute,kCFBooleanTrue);
                else if ([action isEqualToString:@"set_selected"]) error = AXUIElementSetAttributeValue(element,kAXSelectedAttribute,
                    [command[@"selected"] boolValue] ? kCFBooleanTrue : kCFBooleanFalse);
                else if ([action isEqualToString:@"set_selected_items"]) {
                    NSString* attribute = Text(command[@"attribute"]);
                    id tokens = command[@"tokens"];
                    bool valid = ([attribute isEqualToString:@"AXSelectedRows"] || [attribute isEqualToString:@"AXSelectedChildren"])
                        && [tokens isKindOfClass:NSArray.class] && [tokens count] <= 65536;
                    NSMutableArray* selected = [NSMutableArray new];
                    if (valid) for (id token in tokens) {
                        if (![token isKindOfClass:NSNumber.class]) { valid = false; break; }
                        id item = client.Resolve(@{@"token":token});
                        if (!item) { valid = false; break; }
                        [selected addObject:item];
                    }
                    if (valid) error = AXUIElementSetAttributeValue(element, (__bridge CFStringRef)attribute, (__bridge CFArrayRef)selected);
                }
                else if ([action isEqualToString:@"set_value"]) error = AXUIElementSetAttributeValue(element,kAXValueAttribute,
                    (__bridge CFStringRef)Text(command[@"value"]));
                else if ([action isEqualToString:@"set_selection"]) {
                    CFRange range{[command[@"start"] longLongValue],[command[@"length"] longLongValue]};
                    AXValueRef object = AXValueCreate(kAXValueTypeCFRange,&range);
                    error = AXUIElementSetAttributeValue(element,kAXSelectedTextRangeAttribute,object); CFRelease(object);
                }
                result[@"node"] = client.Read(value);
            }
            result[@"actionError"] = @(error);
        }
        AXError focusError = kAXErrorSuccess; id focused = Copy(client.app,kAXFocusedUIElementAttribute,&focusError);
        result[@"focusedId"] = IsElement(focused) ? Text(Copy((__bridge AXUIElementRef)focused,kAXIdentifierAttribute)) : @"";
        result[@"focusReadError"] = @(focusError);
        NSData* output = [NSJSONSerialization dataWithJSONObject:result options:0 error:nil];
        fwrite(output.bytes,1,output.length,stdout);fputc('\n',stdout);fflush(stdout);
        if ([action isEqualToString:@"quit"]) break;
    } }
    return 0;
} }
