#pragma once

#include "jalium_platform.h"
#import <AppKit/AppKit.h>

namespace jalium::platform::apple {
inline constexpr NSUInteger MaxPasteboardPayloadBytes = 256 * 1024 * 1024;
inline constexpr NSUInteger MaxPasteboardItems = 65'536;

// Clipboard and drag sources must expose one native item per URL. Secondary
// representations belong to the first item, so they are not repeated on paste.
template<class Item, class Mapper>
inline JaliumResult BuildPasteboardItems(const Item* items, uint32_t count,
    Mapper typeFromMime, NSArray<NSPasteboardItem*>** output,
    NSUInteger maxBytes = MaxPasteboardPayloadBytes, NSUInteger maxItems = MaxPasteboardItems)
{
    *output = nil;
    if ((!items && count) || count > maxItems) return JALIUM_ERROR_INVALID_ARGUMENT;
    if (!count) { *output = @[]; return JALIUM_OK; }
    NSPasteboardItem* first = [NSPasteboardItem new];
    NSMutableArray<NSPasteboardItem*>* boards = [NSMutableArray arrayWithObject:first];
    NSString* uriList = nil;
    for (uint32_t index = 0; index < count; ++index) {
        const auto& item = items[index];
        if (!item.mimeType || (!item.data && item.dataSize) || item.dataSize > maxBytes)
            return JALIUM_ERROR_INVALID_ARGUMENT;
        NSPasteboardType type = typeFromMime(item.mimeType);
        if (!type) return JALIUM_ERROR_INVALID_ARGUMENT;
        NSData* data = [NSData dataWithBytes:item.data length:item.dataSize];
        if ([type isEqualToString:NSPasteboardTypeURL] || [type isEqualToString:NSPasteboardTypeString]) {
            NSString* value = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
            if (!value) return JALIUM_ERROR_INVALID_ARGUMENT;
            if ([type isEqualToString:NSPasteboardTypeURL]) uriList = value;
            else if (![first setString:value forType:type]) return JALIUM_ERROR_INVALID_STATE;
        } else if (![first setData:data forType:type]) return JALIUM_ERROR_INVALID_STATE;
    }
    if ([uriList hasPrefix:@"\uFEFF"]) uriList = [uriList substringFromIndex:1];
    __block NSUInteger urlCount = 0;
    __block JaliumResult result = JALIUM_OK;
    // Enumerate lazily: a bounded byte payload can still contain millions of
    // empty/comment lines, which must not allocate a separate array of strings.
    [uriList enumerateLinesUsingBlock:^(NSString* raw, BOOL* stop) {
        NSString* value = [raw stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceCharacterSet];
        if (!value.length || [value hasPrefix:@"#"]) return;
        NSURL* url = [NSURL URLWithString:value];
        if (!url.scheme.length) return;
        if (++urlCount > maxItems) { result = JALIUM_ERROR_INVALID_ARGUMENT; *stop = YES; return; }
        NSPasteboardItem* board = urlCount == 1 ? first : [NSPasteboardItem new];
        if (urlCount != 1) [boards addObject:board];
        if (![board setString:url.absoluteString forType:NSPasteboardTypeURL] ||
            (url.fileURL && ![board setString:url.absoluteString forType:NSPasteboardTypeFileURL]))
            { result = JALIUM_ERROR_INVALID_STATE; *stop = YES; }
    }];
    if (result != JALIUM_OK) return result;
    if (!first.types.count) return JALIUM_ERROR_INVALID_ARGUMENT;
    *output = boards;
    return JALIUM_OK;
}

template<class Item, class Mapper>
inline JaliumResult WritePasteboardData(NSPasteboard* pasteboard, const Item* items,
    uint32_t count, Mapper typeFromMime)
{
    NSArray<NSPasteboardItem*>* boards = nil;
    JaliumResult result = BuildPasteboardItems(items, count, typeFromMime, &boards);
    if (result != JALIUM_OK) return result;
    // Validate and materialize everything before taking ownership of the board.
    [pasteboard clearContents];
    return !boards.count || [pasteboard writeObjects:boards] ? JALIUM_OK : JALIUM_ERROR_INVALID_STATE;
}

inline NSData* ReadPasteboardUriList(NSPasteboard* pasteboard,
    NSUInteger maxBytes = MaxPasteboardPayloadBytes, NSUInteger maxItems = MaxPasteboardItems)
{
    NSMutableData* result = [NSMutableData data];
    NSUInteger count = 0;
    auto append = [&](NSString* value) -> bool {
        if (!value) return true;
        NSUInteger bytes = [value lengthOfBytesUsingEncoding:NSUTF8StringEncoding];
        if (++count > maxItems || bytes > maxBytes || maxBytes - bytes < 2 ||
            result.length > maxBytes - bytes - 2) return false;
        [result appendData:[value dataUsingEncoding:NSUTF8StringEncoding]];
        [result appendBytes:"\r\n" length:2];
        return true;
    };
    // Finder supplies separate items. Retain order, duplicates and directory URLs.
    for (NSPasteboardItem* item in pasteboard.pasteboardItems) {
        if (!append([item stringForType:NSPasteboardTypeFileURL] ?: [item stringForType:NSPasteboardTypeURL]))
            return nil;
    }
    if (!count && !append([pasteboard stringForType:NSPasteboardTypeFileURL] ?:
        [pasteboard stringForType:NSPasteboardTypeURL])) return nil;
    return count ? result : nil;
}
} // namespace jalium::platform::apple
