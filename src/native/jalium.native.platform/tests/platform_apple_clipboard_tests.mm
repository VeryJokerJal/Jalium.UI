#include "../src/platform_apple_pasteboard.h"
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
using namespace jalium::platform::apple;
static unsigned checks = 0;
static void Check(bool condition, const char* message)
{
    ++checks;
    if (!condition) { std::fprintf(stderr, "FAIL: %s\n", message); std::exit(1); }
}
static NSPasteboardType Type(const char* mime)
{
    if (!mime || !*mime) return nil;
    if (!std::strcmp(mime, "text/uri-list")) return NSPasteboardTypeURL;
    if (!std::strcmp(mime, "text/plain")) return NSPasteboardTypeString;
    if (!std::strcmp(mime, "text/html")) return NSPasteboardTypeHTML;
    return @"org.jalium.fixture.binary";
}
static JaliumClipboardDataItem Item(const char* type, const std::string& bytes)
{ return {type, reinterpret_cast<const uint8_t*>(bytes.data()), static_cast<uint32_t>(bytes.size())}; }
int main()
{
    @autoreleasepool {
        NSPasteboard* board = [NSPasteboard pasteboardWithUniqueName];
        std::string uris = "file:///tmp/one%20file.txt\r\nfile:///tmp/中文%F0%9F%99%82.txt\r\n"
            "file:///tmp/folder/\r\nfile:///tmp/one%20file.txt\r\n";
        std::string text = "附带文本🙂", html = "<b>保留🙂</b>", binary("\0\x01\xff", 3);
        std::string canonical = "file:///tmp/one%20file.txt\r\nfile:///tmp/%E4%B8%AD%E6%96%87%F0%9F%99%82.txt\r\n"
            "file:///tmp/folder/\r\nfile:///tmp/one%20file.txt\r\n";
        JaliumClipboardDataItem items[] = {Item("text/uri-list", uris), Item("text/plain", text),
            Item("text/html", html), Item("org.jalium.fixture.binary", binary)};
        Check(WritePasteboardData(board, items, 4, Type) == JALIUM_OK, "write four file URL items");
        Check(board.pasteboardItems.count == 4, "one item per file, including duplicates");
        NSArray<NSURL*>* files = [board readObjectsForClasses:@[NSURL.class]
            options:@{NSPasteboardURLReadingFileURLsOnlyKey:@YES}];
        Check(files.count == 4, "AppKit reads the complete native file batch");
        Check([files[0].path isEqualToString:@"/tmp/one file.txt"], "space survives native URL reading");
        Check([files[1].path isEqualToString:@"/tmp/中文🙂.txt"], "Unicode survives native URL reading");
        Check([files[2].absoluteString hasSuffix:@"/folder/"], "directory URL retained");
        Check([files[3] isEqual:files[0]], "duplicate and order retained");
        Check([[board stringForType:NSPasteboardTypeString] isEqualToString:@"附带文本🙂"], "plain text not repeated");
        Check([[board dataForType:NSPasteboardTypeHTML] isEqual:[NSData dataWithBytes:html.data() length:html.size()]], "HTML retained");
        Check([[board dataForType:@"org.jalium.fixture.binary"] isEqual:[NSData dataWithBytes:binary.data() length:binary.size()]], "binary retained");
        for (NSUInteger index = 1; index < 4; ++index)
            Check(![board.pasteboardItems[index].types containsObject:NSPasteboardTypeString], "secondary text belongs to first item");
        NSData* roundtrip = ReadPasteboardUriList(board);
        Check([roundtrip isEqual:[NSData dataWithBytes:canonical.data() length:canonical.size()]], "read complete canonical URI list");
        Check(!ReadPasteboardUriList(board, canonical.size() - 1), "aggregate byte bound");
        Check([ReadPasteboardUriList(board, canonical.size()) isEqual:roundtrip], "exact aggregate byte bound accepted");
        Check(!ReadPasteboardUriList(board, MaxPasteboardPayloadBytes, 3), "aggregate item bound");
        Check(ReadPasteboardUriList(board, MaxPasteboardPayloadBytes, 4) != nil, "exact item bound accepted");

        NSArray<NSPasteboardItem*>* built = nil;
        Check(BuildPasteboardItems(items, 4, Type, &built, MaxPasteboardPayloadBytes, 3) == JALIUM_ERROR_INVALID_ARGUMENT && !built,
            "representation count bound rejects before materialization");
        Check(BuildPasteboardItems(items, 1, Type, &built, MaxPasteboardPayloadBytes, 3) == JALIUM_ERROR_INVALID_ARGUMENT && !built,
            "URL count bound");
        Check(BuildPasteboardItems(items, 1, Type, &built, uris.size() - 1) == JALIUM_ERROR_INVALID_ARGUMENT && !built,
            "representation bytes bound");

        std::string mixed = "\xEF\xBB\xBF# comment\r\n\r\n relative/path \n"
            "https://example.test/a%20b\nfile:///tmp/a%23%25.txt\nfile:///tmp/a%23%25.txt\n";
        auto mixedItem = Item("text/uri-list", mixed);
        Check(WritePasteboardData(board, &mixedItem, 1, Type) == JALIUM_OK, "BOM, comments and malformed relative entry");
        Check(board.pasteboardItems.count == 3, "only absolute URLs become native items");
        Check(![board.pasteboardItems[0].types containsObject:NSPasteboardTypeFileURL], "web URL is not a file");
        files = [board readObjectsForClasses:@[NSURL.class] options:@{NSPasteboardURLReadingFileURLsOnlyKey:@YES}];
        Check(files.count == 2 && [files[0].path isEqualToString:@"/tmp/a#%.txt"], "AppKit filters web URLs and decodes escaped filename");
        Check([files[0] isEqual:files[1]], "mixed duplicates retained");
        std::string sparse(100'000, '\n'); sparse += "file:///tmp/last.txt\n";
        auto sparseItem = Item("text/uri-list", sparse);
        Check(BuildPasteboardItems(&sparseItem, 1, Type, &built) == JALIUM_OK && built.count == 1,
            "many empty lines still retain the last URL without a split array");
        auto unchanged = ReadPasteboardUriList(board); auto generation = board.changeCount;
        auto reject = [&](const JaliumClipboardDataItem* value, uint32_t count, const char* message) {
            Check(WritePasteboardData(board, value, count, Type) == JALIUM_ERROR_INVALID_ARGUMENT, message);
            Check(board.changeCount == generation && [ReadPasteboardUriList(board) isEqual:unchanged],
                "invalid write leaves prior board untouched");
        };
        reject(nullptr, 1, "null input array");
        JaliumClipboardDataItem bad = {nullptr, nullptr, 0}; reject(&bad, 1, "null MIME");
        bad = {"", nullptr, 0}; reject(&bad, 1, "empty MIME");
        bad = {"text/plain", nullptr, 1}; reject(&bad, 1, "missing bytes");
        uint8_t invalid = 0xff;
        bad = {"text/uri-list", &invalid, 1}; reject(&bad, 1, "invalid URL UTF8");
        bad = {"text/plain", &invalid, 1}; reject(&bad, 1, "invalid text UTF8");
        bad = {"text/plain", &invalid, static_cast<uint32_t>(MaxPasteboardPayloadBytes + 1)};
        reject(&bad, 1, "oversize rejected before reading pointer");
        std::string empty = "# only comments\r\n relative";
        bad = Item("text/uri-list", empty); reject(&bad, 1, "URL-only write with no absolute URLs");
        Check(jalium_clipboard_set_data(nullptr, 1) == JALIUM_ERROR_INVALID_ARGUMENT,
            "public invalid write rejects without taking the general board");

        std::string zero;
        auto emptyText = Item("text/plain", zero);
        Check(WritePasteboardData(board, &emptyText, 1, Type) == JALIUM_OK &&
            [[board stringForType:NSPasteboardTypeString] isEqualToString:@""], "empty text is a valid representation");
        Check(!ReadPasteboardUriList(board), "text-only board has no URI list");
        Check(WritePasteboardData<JaliumClipboardDataItem>(board, nullptr, 0, Type) == JALIUM_OK &&
            !board.pasteboardItems.count, "zero items clears a private board");
        [board releaseGlobally];
        std::printf("macOS private pasteboard file batch tests: %u checks passed\n", checks);
    }
}
