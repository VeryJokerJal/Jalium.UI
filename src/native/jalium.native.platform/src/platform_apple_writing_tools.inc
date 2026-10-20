// Included after the accessibility text-style decoder. Every bridge response is
// copied during the synchronous callback; neither side retains borrowed pointers.
#if defined(__MAC_OS_X_VERSION_MAX_ALLOWED) && __MAC_OS_X_VERSION_MAX_ALLOWED >= 150200
static constexpr size_t AppleTextServiceMaximumBytes = 64u * 1024u * 1024u;
static NSDictionary* AppleTextServiceQuery(JaliumAppleView* view, int32_t kind,
    NSRange range, int32_t flags = 0, NSDictionary* payload = nil)
{
    AppleTextInputContext lifetime(view);
    const bool cleanup = kind == 8 && [payload[@"stopped"] boolValue];
    if (!lifetime.IsSameContext() || (!cleanup && (!view.textServicesEnabled || !lifetime.owner->enabled ||
        !lifetime.owner->visible || lifetime.owner->destroying)) ||
        range.location > INT32_MAX || range.length > INT32_MAX) return nil;
    NSData* json = payload ? [NSJSONSerialization dataWithJSONObject:payload options:0 error:nil] : nil;
    if (payload && !json || json.length > AppleTextServiceMaximumBytes) return nil;
    std::string input(json ? static_cast<const char*>(json.bytes) : "", json.length), output;
    JaliumTextServicesRequest request{kind, static_cast<int32_t>(range.location),
        static_cast<int32_t>(range.length), flags, input.c_str(), &output,
        [](void* context, const char* bytes, size_t length) {
            if (bytes && length <= AppleTextServiceMaximumBytes)
                static_cast<std::string*>(context)->assign(bytes, length);
        }};
    JaliumPlatformEvent event{}; event.type = JALIUM_EVENT_TEXT_SERVICES_REQUEST;
    event.textServicesRequest.request = &request;
    DispatchWindowEvent(lifetime.owner, event);
    if (!lifetime.IsSameContext() || (!cleanup && (!view.textServicesEnabled || !lifetime.owner->enabled ||
        !lifetime.owner->visible || lifetime.owner->destroying)) || output.empty()) return nil;
    NSData* response = [NSData dataWithBytes:output.data() length:output.size()];
    id object = [NSJSONSerialization JSONObjectWithData:response options:0 error:nil];
    return [object isKindOfClass:NSDictionary.class] ? object : nil;
}
static NSString* AppleTextServiceString(NSDictionary* response)
{
    id base64 = response[@"text16"];
    if (![base64 isKindOfClass:NSString.class]) return nil;
    NSData* bytes = [[NSData alloc] initWithBase64EncodedString:base64 options:0];
    if (!bytes || bytes.length % 2 || bytes.length > 32u * 1024u * 1024u) return nil;
    // Preserve UTF-16 code units, including isolated surrogates.
    std::vector<unichar> text(bytes.length / 2);
    const auto* raw = static_cast<const uint8_t*>(bytes.bytes);
    for (size_t i = 0; i < text.size(); ++i) text[i] = raw[i * 2] | raw[i * 2 + 1] << 8;
    return [[NSString alloc] initWithCharacters:text.data() length:text.size()];
}
static NSRange AppleTextServiceRange(id object, NSUInteger length)
{
    if (![object isKindOfClass:NSArray.class] || [object count] != 2) return NSMakeRange(NSNotFound, 0);
    id a = object[0], b = object[1];
    if (![a isKindOfClass:NSNumber.class] || ![b isKindOfClass:NSNumber.class] ||
        [a doubleValue] != [a unsignedIntegerValue] || [b doubleValue] != [b unsignedIntegerValue])
        return NSMakeRange(NSNotFound, 0);
    NSRange range = NSMakeRange([a unsignedIntegerValue], [b unsignedIntegerValue]);
    return ValidTextRange(range, length) ? range : NSMakeRange(NSNotFound, 0);
}
static bool AppleTextServiceRect(id object, CGFloat scale, NSRect& rectangle)
{
    if (![object isKindOfClass:NSArray.class] || [object count] != 4 ||
        !std::isfinite(scale) || scale <= 0) return false;
    double values[4];
    for (int i = 0; i < 4; ++i) {
        if (![object[i] isKindOfClass:NSNumber.class]) return false;
        values[i] = [object[i] doubleValue] / scale;
        if (!std::isfinite(values[i])) return false;
    }
    rectangle = NSMakeRect(values[0], values[1], values[2], values[3]);
    return rectangle.size.width >= 0 && rectangle.size.height > 0;
}
// AppKit keeps lists and tables in separate paragraph-style arrays. Retain
// their original interleaving and item ownership as a private attributed value.
static NSAttributedStringKey const AppleWritingToolsAncestorsAttribute = @"JaliumWritingToolsAncestors";
static NSAttributedStringKey const AppleWritingToolsStorageSeparatorAttribute = @"JaliumWritingToolsStorageSeparator";
static NSAttributedStringKey const AppleWritingToolsEmptyCellAttribute = @"JaliumWritingToolsEmptyCell";
static NSAttributedStringKey const AppleWritingToolsInlineBreakAttribute = @"JaliumWritingToolsInlineBreak";
static NSString* const AppleWritingToolsInlineBreakValue = @"inline";
static bool AppleWritingToolsIsInlineBreak(NSAttributedString* text, NSUInteger index)
{
    unichar character = [text.string characterAtIndex:index];
    if (character != '\n' && character != '\r' && character != 0x2029) return false;
    id value = [text attribute:AppleWritingToolsInlineBreakAttribute atIndex:index effectiveRange:nil];
    // NSNumber booleans compare equal to 0/1, so attributed-string run
    // coalescing can replace their concrete type. Keep provenance distinct.
    return [value isKindOfClass:NSString.class] && [value isEqualToString:AppleWritingToolsInlineBreakValue];
}
static NSArray* AppleWritingToolsValidateAncestors(id value, NSUInteger lists, NSUInteger tables)
{
    if (![value isKindOfClass:NSArray.class] || [value count] != lists + tables || [value count] > 64) return nil;
    NSUInteger list = 0, table = 0;
    for (id entry in value) {
        if (![entry isKindOfClass:NSDictionary.class] || ![entry[@"kind"] isKindOfClass:NSString.class] ||
            !JaliumAccessibilityStyleNumber(entry,@"index",0,31) ||
            CFGetTypeID((__bridge CFTypeRef)entry[@"index"]) == CFBooleanGetTypeID()) return nil;
        bool isTable = [entry[@"kind"] isEqualToString:@"table"];
        if (!isTable && ![entry[@"kind"] isEqualToString:@"list"]) return nil;
        NSUInteger index = [entry[@"index"] unsignedIntegerValue];
        if (isTable ? index != table++ || index >= tables : index != list++ || index >= lists) return nil;
        id item = entry[@"item"];
        if (item && (isTable || ![item isKindOfClass:NSString.class] || ![item length] || [item length] > 1024)) return nil;
    }
    return value;
}
static NSArray* AppleWritingToolsRetainedAncestors(id value, NSParagraphStyle* style)
{
    if (![value isKindOfClass:NSDictionary.class]) return nil;
    NSArray* lists = value[@"lists"], *blocks = value[@"tables"];
    NSMutableArray<NSTextTableBlock*>* current = [NSMutableArray new];
    for (NSTextBlock* block in style.textBlocks)
        if ([block isKindOfClass:NSTextTableBlock.class]) [current addObject:(NSTextTableBlock*)block];
    if (![lists isKindOfClass:NSArray.class] || ![blocks isKindOfClass:NSArray.class] ||
        lists.count != style.textLists.count || blocks.count != current.count) return nil;
    for (NSUInteger i = 0; i < lists.count; ++i) if (lists[i] != style.textLists[i]) return nil;
    for (NSUInteger i = 0; i < blocks.count; ++i) {
        id saved = blocks[i]; NSTextTableBlock* now = current[i];
        if (![saved isKindOfClass:NSTextTableBlock.class] || [saved table] != now.table ||
            [saved startingRow] != now.startingRow || [saved startingColumn] != now.startingColumn ||
            [saved rowSpan] != now.rowSpan || [saved columnSpan] != now.columnSpan) return nil;
    }
    return AppleWritingToolsValidateAncestors(value[@"ancestors"], lists.count, blocks.count);
}
static bool AppleWritingToolsApplyStructure(NSMutableAttributedString* text, id records)
{
    if (!records) return true;
    if (![records isKindOfClass:NSArray.class] || [records count] > 100000) return false;
    NSMutableDictionary<NSString*,NSTextList*>* lists = [NSMutableDictionary new];
    NSMutableDictionary<NSString*,NSTextTable*>* tables = [NSMutableDictionary new];
    if (@available(macOS 15.2,*)) {
        // FlowDocument's tabs/row separators are structural storage, not
        // editable paragraph text. Preserve their exact UTF-16 positions.
        [text addAttribute:NSWritingToolsExclusionAttributeName value:@YES range:NSMakeRange(0,text.length)];
        [text addAttribute:AppleWritingToolsStorageSeparatorAttribute value:@YES range:NSMakeRange(0,text.length)];
        [text removeAttribute:AppleWritingToolsEmptyCellAttribute range:NSMakeRange(0,text.length)];
        [text removeAttribute:AppleWritingToolsInlineBreakAttribute range:NSMakeRange(0,text.length)];
        NSUInteger previousEnd = 0;
        for (id record in records) {
            if (![record isKindOfClass:NSDictionary.class]) return false;
            if (!JaliumAccessibilityStyleNumber(record,@"start",previousEnd,text.length) ||
                !JaliumAccessibilityStyleNumber(record,@"length",1,text.length)) return false;
            NSRange range = NSMakeRange([record[@"start"] unsignedIntegerValue],[record[@"length"] unsignedIntegerValue]);
            if (!ValidTextRange(range,text.length)) return false;
            previousEnd = NSMaxRange(range);
            id inlineBreaks = record[@"inlineBreaks"];
            if (inlineBreaks) {
                if (![inlineBreaks isKindOfClass:NSArray.class] || [inlineBreaks count] > range.length) return false;
                NSUInteger previous = range.location;
                bool first = true;
                for (id value in inlineBreaks) {
                    if (![value isKindOfClass:NSNumber.class] ||
                        CFGetTypeID((__bridge CFTypeRef)value) == CFBooleanGetTypeID()) return false;
                    double number = [value doubleValue];
                    if (!std::isfinite(number) || number != std::floor(number) ||
                        number < range.location || number >= NSMaxRange(range)) return false;
                    NSUInteger index = (NSUInteger)number;
                    if (!first && index <= previous) return false;
                    unichar character = [text.string characterAtIndex:index];
                    if (character != '\n' && character != '\r' && character != 0x2029) return false;
                    [text addAttribute:AppleWritingToolsInlineBreakAttribute value:AppleWritingToolsInlineBreakValue range:NSMakeRange(index,1)];
                    previous = index; first = false;
                }
            }
            id emptyValue = record[@"emptyCell"];
            if (emptyValue && (![emptyValue isKindOfClass:NSNumber.class] ||
                CFGetTypeID((__bridge CFTypeRef)emptyValue) != CFBooleanGetTypeID())) return false;
            const bool emptyCell = emptyValue && [emptyValue boolValue];
            id listRecords = record[@"lists"], tableRecords = record[@"tables"];
            if (![listRecords isKindOfClass:NSArray.class] || [listRecords count] > 32 ||
                ![tableRecords isKindOfClass:NSArray.class] || [tableRecords count] > 32) return false;
            id ancestry = record[@"ancestors"];
            if (ancestry && !AppleWritingToolsValidateAncestors(ancestry, [listRecords count], [tableRecords count])) return false;
            NSMutableArray* listObjects = [NSMutableArray new]; NSMutableArray* tableObjects = [NSMutableArray new];
            for (id metadata in listRecords) {
                if (![metadata isKindOfClass:NSDictionary.class]) return false;
                NSString* identifier = metadata[@"id"], *format = metadata[@"format"];
                if (![identifier isKindOfClass:NSString.class] || identifier.length > 1024 ||
                    ![format isKindOfClass:NSString.class] || format.length > 1024 ||
                    !JaliumAccessibilityStyleNumber(metadata,@"start",0,INT32_MAX)) return false;
                NSTextList* list = lists[identifier];
                if (!list) {
                    list = [[NSTextList alloc] initWithMarkerFormat:format options:0];
                    list.startingItemNumber = [metadata[@"start"] integerValue]; lists[identifier] = list;
                }
                [listObjects addObject:list];
            }
            for (id metadata in tableRecords) {
                if (![metadata isKindOfClass:NSDictionary.class]) return false;
                NSString* identifier = metadata[@"id"];
                if (![identifier isKindOfClass:NSString.class] || identifier.length > 1024 ||
                    !JaliumAccessibilityStyleNumber(metadata,@"row",0,4095) ||
                    !JaliumAccessibilityStyleNumber(metadata,@"column",0,255) ||
                    !JaliumAccessibilityStyleNumber(metadata,@"rows",1,4096) ||
                    !JaliumAccessibilityStyleNumber(metadata,@"columns",1,256) ||
                    !JaliumAccessibilityStyleNumber(metadata,@"tableColumns",1,256)) return false;
                NSInteger columns = [metadata[@"tableColumns"] integerValue];
                NSInteger column = [metadata[@"column"] integerValue], span = [metadata[@"columns"] integerValue];
                if (column + span > columns) return false;
                NSTextTable* table = tables[identifier];
                if (!table) { table = [NSTextTable new]; table.numberOfColumns = columns; tables[identifier] = table; }
                if (table.numberOfColumns != columns) return false;
                [tableObjects addObject:[[NSTextTableBlock alloc] initWithTable:table
                    startingRow:[metadata[@"row"] integerValue] rowSpan:[metadata[@"rows"] integerValue]
                    startingColumn:column columnSpan:span]];
            }
            if (emptyCell && (!tableObjects.count || range.length != 1 ||
                [text.string characterAtIndex:range.location] != '\t')) return false;
            NSParagraphStyle* original = [text attribute:NSParagraphStyleAttributeName atIndex:range.location effectiveRange:nil];
            NSMutableParagraphStyle* style = original ? [original mutableCopy] : [NSMutableParagraphStyle new];
            style.textLists = listObjects; style.textBlocks = tableObjects;
            [text addAttribute:NSParagraphStyleAttributeName value:style range:range];
            if (ancestry) [text addAttribute:AppleWritingToolsAncestorsAttribute
                value:@{@"ancestors":ancestry,@"lists":listObjects,@"tables":tableObjects} range:range];
            if (emptyCell) [text addAttribute:AppleWritingToolsEmptyCellAttribute value:@YES range:range];
            else [text removeAttribute:NSWritingToolsExclusionAttributeName range:range];
            [text removeAttribute:AppleWritingToolsStorageSeparatorAttribute range:range];
        }
    }
    return true;
}


static NSAttributedString* AppleTextServiceAttributedString(NSDictionary* response)
{
    NSString* text = AppleTextServiceString(response);
    if (!text) return nil;
    id styles = response[@"styles"];
    if ([styles isKindOfClass:NSString.class]) {
        NSAttributedString* value = JaliumAccessibilityDecodeStyles(styles, text.length, NO);
        if (![value.string isEqualToString:text]) return nil;
        NSMutableAttributedString* structured = [value mutableCopy];
        return AppleWritingToolsApplyStructure(structured,response[@"structure"]) ? structured : nil;
    }
    NSMutableAttributedString* value = [[NSMutableAttributedString alloc] initWithString:text];
    return AppleWritingToolsApplyStructure(value,response[@"structure"]) ? value : nil;
}

static NSNumber* AppleWritingToolsColor(id value)
{
    if (![value isKindOfClass:NSColor.class]) return nil;
    NSColor* color = [value colorUsingColorSpace:NSColorSpace.sRGBColorSpace];
    if (!color) return nil;
    auto component = [](CGFloat n) { return static_cast<uint32_t>(std::lround(std::clamp<double>(n, 0, 1)*255)); };
    return @((component(color.alphaComponent)<<24)|(component(color.redComponent)<<16)|
        (component(color.greenComponent)<<8)|component(color.blueComponent));
}
static NSInteger AppleWritingToolsWeight(NSFont* font)
{
    NSDictionary* traits = CFBridgingRelease(CTFontCopyTraits((__bridge CTFontRef)font));
    double actual = [traits[(__bridge NSString*)kCTFontWeightTrait] doubleValue];
    const double weights[] = {-.8,-.6,-.4,0,.23,.3,.4,.56,.62};
    if (actual <= weights[0]) return 100;
    for (int i = 1; i < 9; ++i) if (actual <= weights[i])
        return std::lround(i*100 + (actual-weights[i-1])/(weights[i]-weights[i-1])*100);
    return 900;
}
static NSDictionary* AppleWritingToolsProposal(NSAttributedString* text)
{
    NSMutableArray* runs = [NSMutableArray new]; NSMutableArray* paragraphs = [NSMutableArray new];
    NSString* plain = text.string;
    std::vector<unichar> characters(plain.length);
    [plain getCharacters:characters.data() range:NSMakeRange(0,plain.length)];
    NSString* encoded = [[NSData dataWithBytes:characters.data() length:characters.size()*2] base64EncodedStringWithOptions:0];
    for (NSUInteger index = 0; index < text.length;) {
        NSRange range; NSDictionary* attributes = [text attributesAtIndex:index effectiveRange:&range];
        NSFont* font = attributes[NSFontAttributeName];
        if (![font isKindOfClass:NSFont.class]) font = [NSFont systemFontOfSize:NSFont.systemFontSize];
        NSFontTraitMask traits = [NSFontManager.sharedFontManager traitsOfFont:font];
        NSMutableDictionary* run = [@{@"start":@(range.location),@"length":@(range.length),
            @"family":font.fontName,@"size":@(font.pointSize),@"weight":@(AppleWritingToolsWeight(font)),
            @"italic":@((traits & NSItalicFontMask) != 0 || [attributes[NSObliquenessAttributeName] doubleValue] != 0),
            @"underline":attributes[NSUnderlineStyleAttributeName] ?: @0,
            @"strike":attributes[NSStrikethroughStyleAttributeName] ?: @0} mutableCopy];
        for (NSArray* item in @[@[@"foreground",NSForegroundColorAttributeName],@[@"background",NSBackgroundColorAttributeName],
            @[@"underlineColor",NSUnderlineColorAttributeName],@[@"strikeColor",NSStrikethroughColorAttributeName]]) {
            NSNumber* color = AppleWritingToolsColor(attributes[item[1]]); if (color) run[item[0]] = color;
        }
        [runs addObject:run]; index = NSMaxRange(range);
    }
    NSMutableDictionary<NSString*,NSNumber*>* ordinals = [NSMutableDictionary new];
    for (NSUInteger index = 0; index < text.length;) {
        NSRange range = [plain paragraphRangeForRange:NSMakeRange(index,0)];
        // A framework paragraph can contain authored LF/CR characters. The
        // source metadata distinguishes them from its final storage newline;
        // retain that distinction when AppKit splits its own paragraph ranges.
        while (NSMaxRange(range) < text.length &&
            AppleWritingToolsIsInlineBreak(text, NSMaxRange(range)-1)) {
            NSRange next = [plain paragraphRangeForRange:NSMakeRange(NSMaxRange(range),0)];
            if (NSMaxRange(next) <= NSMaxRange(range)) break;
            range.length = NSMaxRange(next)-range.location;
        }
        NSRange searchRange = NSMakeRange(index, NSMaxRange(range)-index), styleRange;
        // An ordinary effective range may end at an unrelated font/color run.
        // Only a real paragraph/cell or provenance change can split this record.
        // Bound longest-range searches to the current paragraph/cell.
        NSParagraphStyle* style = [text attribute:NSParagraphStyleAttributeName atIndex:index
            longestEffectiveRange:&styleRange inRange:searchRange];
        if (![style isKindOfClass:NSParagraphStyle.class]) style = NSParagraphStyle.defaultParagraphStyle;
        NSUInteger end = NSMaxRange(range);
        if (style.textBlocks.count) end = std::min(end, NSMaxRange(styleRange));
        searchRange.length = end-index;
        NSRange separatorRange, emptyRange;
        id separatorValue = [text attribute:AppleWritingToolsStorageSeparatorAttribute atIndex:index
            longestEffectiveRange:&separatorRange inRange:searchRange];
        BOOL storageSeparator = [separatorValue isKindOfClass:NSNumber.class] &&
            CFGetTypeID((__bridge CFTypeRef)separatorValue) == CFBooleanGetTypeID() && [separatorValue boolValue];
        id emptyValue = [text attribute:AppleWritingToolsEmptyCellAttribute atIndex:index
            longestEffectiveRange:&emptyRange inRange:searchRange];
        BOOL emptyCell = [emptyValue isKindOfClass:NSNumber.class] &&
            CFGetTypeID((__bridge CFTypeRef)emptyValue) == CFBooleanGetTypeID() && [emptyValue boolValue];
        end = std::min(end, NSMaxRange(separatorRange));
        end = std::min(end, NSMaxRange(emptyRange));
        range = NSMakeRange(index, end-index);
        // Only our virtual storage characters may be omitted by the importer.
        // A copied/stale private value must never discard newly authored text.
        for (NSUInteger i = index; storageSeparator && i < end; ++i) {
            unichar value = [plain characterAtIndex:i];
            if (value != '\t' && value != '\n' && value != '\r' && value != 0x2029) storageSeparator = NO;
        }
        NSArray* ancestry = AppleWritingToolsRetainedAncestors(
            [text attribute:AppleWritingToolsAncestorsAttribute atIndex:index effectiveRange:nil], style);
        NSUInteger skipPrefix = 0;
        // Source paragraphs already contain authored content, not generated
        // AppKit list markers, even when that content happens to look like one.
        if (style.textLists.count && !ancestry) {
            NSTextList* list = style.textLists.lastObject;
            NSString* key = [NSString stringWithFormat:@"%p",list];
            NSInteger ordinal = ordinals[key] ? ordinals[key].integerValue : list.startingItemNumber;
            ordinals[key] = @(ordinal+1);
            NSString* marker = [list markerForItemNumber:ordinal];
            NSString* segment = [plain substringWithRange:range];
            for (NSString* prefix in @[[NSString stringWithFormat:@"\t%@\t",marker],
                [NSString stringWithFormat:@"%@\t",marker]])
                if ([segment hasPrefix:prefix]) { skipPrefix = prefix.length; break; }
        }
        NSMutableArray* lists = [NSMutableArray new]; NSMutableArray* tables = [NSMutableArray new];
        for (NSTextList* list in style.textLists) {
            [lists addObject:@{@"id":[NSString stringWithFormat:@"%p",list],@"format":list.markerFormat,
                @"start":@(list.startingItemNumber)}];
        }
        for (NSTextBlock* block in style.textBlocks) if ([block isKindOfClass:NSTextTableBlock.class]) {
            NSTextTableBlock* cell = (NSTextTableBlock*)block;
            [tables addObject:@{@"id":[NSString stringWithFormat:@"%p",cell.table],
                @"row":@(cell.startingRow),@"column":@(cell.startingColumn),@"rows":@(cell.rowSpan),
                @"columns":@(cell.columnSpan),@"tableColumns":@(cell.table.numberOfColumns)}];
        }
        NSMutableDictionary* paragraph = [@{@"start":@(range.location),@"length":@(range.length),
            @"alignment":@(JaliumBridgeTextAlignment(style.alignment)),@"direction":@(style.baseWritingDirection),
            @"lists":lists,@"tables":tables,@"skipPrefix":@(skipPrefix)} mutableCopy];
        NSMutableArray* inlineBreaks = [NSMutableArray new];
        for (NSUInteger i = range.location; i < NSMaxRange(range); ++i)
            if (AppleWritingToolsIsInlineBreak(text,i)) [inlineBreaks addObject:@(i)];
        if (inlineBreaks.count) paragraph[@"inlineBreaks"] = inlineBreaks;
        if (ancestry) paragraph[@"ancestors"] = ancestry;
        // Private provenance is useful only while the real table/list style
        // still matches, and it can never suppress newly authored characters.
        if (emptyCell && ancestry && style.textBlocks.count && range.length == 1 &&
            [plain characterAtIndex:index] == '\t') paragraph[@"emptyCell"] = @YES;
        if (storageSeparator) paragraph[@"storageSeparator"] = @YES;
        [paragraphs addObject:paragraph];
        index = NSMaxRange(range);
    }
    return @{@"version":@1,@"text16":encoded ?: @"",@"runs":runs,@"paragraphs":paragraphs};
}


API_AVAILABLE(macos(15.2))
@interface JaliumWritingToolsContext : NSObject
@property(nonatomic) NSUInteger documentStart;
@property(nonatomic) NSUInteger length;
@property(nonatomic, strong) NSWritingToolsCoordinatorContext* context;
@end
@implementation JaliumWritingToolsContext
@end

API_AVAILABLE(macos(15.2))
@interface JaliumAppleWritingTools : NSObject<NSWritingToolsCoordinatorDelegate>
@property(nonatomic, weak) JaliumAppleView* view;
@property(nonatomic, strong) NSWritingToolsCoordinator* coordinator;
@property(nonatomic, copy) NSString* editor;
@property(nonatomic, copy) NSString* session;
@property(nonatomic, strong) NSAttributedString* document;
@property(nonatomic, strong) NSMutableDictionary<NSUUID*, JaliumWritingToolsContext*>* contexts;
@property(nonatomic) uint64_t inputGeneration;
@property(nonatomic) uint64_t selectionGeneration;
@property(nonatomic) BOOL stopped;
@property(nonatomic) NSUInteger applying;
@property(nonatomic) BOOL refreshing;
@property(nonatomic) NSUInteger documentRevision;
@property(nonatomic) NSWritingToolsCoordinatorState presentationState;
@property(nonatomic, strong) NSUUID* latestContext;
- (instancetype)initWithView:(JaliumAppleView*)view capabilities:(NSDictionary*)capabilities;
- (void)stop;
- (void)refresh;
@end

@implementation JaliumAppleWritingTools
- (instancetype)initWithView:(JaliumAppleView*)view capabilities:(NSDictionary*)capabilities
{
    if ((self = [super init])) {
        _view = view; _editor = [capabilities[@"editor"] copy]; _session = NSUUID.UUID.UUIDString;
        _contexts = [NSMutableDictionary new];
        _inputGeneration = view.textInputGeneration;
        _selectionGeneration = view.jaliumOwner->selectionGeneration;
        _coordinator = [[NSWritingToolsCoordinator alloc] initWithDelegate:self];
        _coordinator.preferredBehavior = NSWritingToolsBehaviorComplete;
        NSUInteger options = [capabilities[@"resultOptions"] unsignedIntegerValue] & 15u;
        _coordinator.preferredResultOptions = static_cast<NSWritingToolsResultOptions>(options ?: NSWritingToolsResultPlainText);
    }
    return self;
}
- (BOOL)isCurrent
{
    JaliumAppleView* view = self.view;
    if (self.stopped || !view) return NO;
    AppleTextInputContext lifetime(view);
    return lifetime.IsSameContext() && view.textServicesEnabled && view.writingToolsAdapter == self &&
        lifetime.owner->enabled && lifetime.owner->visible &&
        !lifetime.owner->destroying && view.textInputGeneration == self.inputGeneration &&
        lifetime.owner->selectionGeneration == self.selectionGeneration;
}
- (NSDictionary*)request:(int32_t)kind range:(NSRange)range flags:(int32_t)flags payload:(NSDictionary*)payload
{
    if (!self.isCurrent) return nil;
    NSMutableDictionary* arguments = payload ? [payload mutableCopy] : [NSMutableDictionary new];
    arguments[@"editor"] = self.editor;
    arguments[@"session"] = self.session;
    NSDictionary* response = AppleTextServiceQuery(self.view, kind, range, flags, arguments);
    return self.isCurrent && [response[@"editor"] isEqual:self.editor] &&
        [response[@"session"] isEqual:self.session] ? response : nil;
}
- (JaliumWritingToolsContext*)resolve:(NSWritingToolsCoordinatorContext*)context range:(NSRange)range
{
    if (!self.isCurrent) return nil;
    JaliumWritingToolsContext* stored = self.contexts[context.identifier];
    return stored.context == context && ValidTextRange(range, stored.length) ? stored : nil;
}
- (NSRange)absolute:(NSRange)range context:(JaliumWritingToolsContext*)context
{ return NSMakeRange(context.documentStart + range.location, range.length); }
- (void)stop
{
    if (self.stopped) return;
    // Clear presentation before retiring the bridge. Managed focus/lifecycle
    // cleanup also clears the old editor when the input generation already changed.
    self.stopped = YES; ++self.documentRevision;
    JaliumAppleView* view = self.view;
    if (view && view.textInputGeneration == self.inputGeneration)
        (void)AppleTextServiceQuery(view, 8, NSMakeRange(0, 0), 0,
            @{@"editor":self.editor, @"session":self.session, @"stopped":@YES});
    [self.contexts removeAllObjects];
    [self.coordinator stopWritingTools];
}
- (void)refresh
{
    if (!self.isCurrent) { [self stop]; return; }
    if (!self.contexts.count || self.applying || self.refreshing) return;
    self.refreshing = YES;
    NSDictionary* response = [self request:1 range:NSMakeRange(0, 0) flags:0 payload:nil];
    NSAttributedString* updated = AppleTextServiceAttributedString(response);
    if (!updated) { self.refreshing = NO; [self stop]; return; }
    [self updateDocument:updated notify:YES reason:[response[@"undoRedo"] boolValue]
        ? NSWritingToolsCoordinatorTextUpdateReasonUndoRedo : NSWritingToolsCoordinatorTextUpdateReasonTyping];
    self.refreshing = NO;
}
- (void)updateDocument:(NSAttributedString*)updated notify:(BOOL)notify reason:(NSWritingToolsCoordinatorTextUpdateReason)reason
{
    NSUInteger revision = ++self.documentRevision;
    if (!self.document) { self.document = updated; return; }
    NSAttributedString* previous = self.document;
    NSString* before = previous.string; NSString* after = updated.string;
    BOOL formattingOnly = [before isEqualToString:after] && ![previous isEqualToAttributedString:updated];
    NSUInteger prefix = 0, suffix = 0;
    while (prefix < before.length && prefix < after.length &&
        [before characterAtIndex:prefix] == [after characterAtIndex:prefix]) ++prefix;
    if (prefix < before.length) prefix = std::min(prefix, [before rangeOfComposedCharacterSequenceAtIndex:prefix].location);
    if (prefix < after.length) prefix = std::min(prefix, [after rangeOfComposedCharacterSequenceAtIndex:prefix].location);
    while (suffix < before.length - prefix && suffix < after.length - prefix &&
        [before characterAtIndex:before.length - suffix - 1] == [after characterAtIndex:after.length - suffix - 1]) ++suffix;
    for (NSString* text in @[before, after]) {
        NSUInteger start = text.length - suffix;
        if (start && start < text.length) {
            NSRange sequence = [text rangeOfComposedCharacterSequenceAtIndex:start];
            if (sequence.location < start) suffix = std::min(suffix, text.length - NSMaxRange(sequence));
        }
    }
    NSRange oldRange = NSMakeRange(prefix, before.length - prefix - suffix);
    NSRange newRange = NSMakeRange(prefix, after.length - prefix - suffix);
    self.document = updated;
    NSInteger delta = static_cast<NSInteger>(newRange.length) - static_cast<NSInteger>(oldRange.length);
    for (JaliumWritingToolsContext* stored in self.contexts.allValues) {
        NSUInteger start = stored.documentStart, end = start + stored.length;
        if (formattingOnly) {
            if (notify) [self.coordinator updateRange:NSMakeRange(0,stored.length)
                withText:[updated attributedSubstringFromRange:NSMakeRange(start,stored.length)]
                reason:reason forContextWithIdentifier:stored.context.identifier];
        } else if (oldRange.location >= start && NSMaxRange(oldRange) <= end) {
            NSRange relative = NSMakeRange(oldRange.location - start, oldRange.length);
            stored.length = static_cast<NSUInteger>(static_cast<NSInteger>(stored.length) + delta);
            if (notify && (oldRange.length || newRange.length))
                [self.coordinator updateRange:relative withText:[updated attributedSubstringFromRange:newRange]
                    reason:reason forContextWithIdentifier:stored.context.identifier];
        } else if (NSMaxRange(oldRange) <= start) {
            stored.documentStart = static_cast<NSUInteger>(static_cast<NSInteger>(start) + delta);
        } else if (oldRange.location < end) {
            // A wholesale change crossing the context boundary needs a fresh
            // system operation instead of inventing a new context identity.
            [self stop]; return;
        }
        if (!self.isCurrent || revision != self.documentRevision) return;
        if (notify) [self.coordinator updateForReflowedTextInContextWithIdentifier:stored.context.identifier];
        if (!self.isCurrent || revision != self.documentRevision) return;
    }
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator
    requestsContextsForScope:(NSWritingToolsCoordinatorContextScope)scope
    completion:(void(^)(NSArray<NSWritingToolsCoordinatorContext*>*))completion
{
    NSMutableArray* result = [NSMutableArray new];
    NSDictionary* response = [self request:1 range:NSMakeRange(0, 0) flags:static_cast<int32_t>(scope) payload:nil];
    NSAttributedString* document = AppleTextServiceAttributedString(response);
    NSRange selected = AppleTextServiceRange(response[@"selection"], document.length);
    NSRange visible = AppleTextServiceRange(response[@"visible"], document.length);
    if (document && selected.location != NSNotFound && self.isCurrent) {
        [self updateDocument:document notify:YES reason:NSWritingToolsCoordinatorTextUpdateReasonTyping];
        if (!self.isCurrent || self.document != document) { completion(@[]); return; }
        NSRange evaluated = scope == NSWritingToolsCoordinatorContextScopeFullDocument
            ? NSMakeRange(0, document.length) : scope == NSWritingToolsCoordinatorContextScopeVisibleArea ? visible : selected;
        if (evaluated.location != NSNotFound && self.isCurrent) {
            NSRange surrounding = [document.string paragraphRangeForRange:evaluated];
            if (scope == NSWritingToolsCoordinatorContextScopeFullDocument) surrounding = evaluated;
            if (!evaluated.length && scope == NSWritingToolsCoordinatorContextScopeUserSelection) evaluated = surrounding;
            NSAttributedString* excerpt = [document attributedSubstringFromRange:surrounding];
            NSRange relative = NSMakeRange(evaluated.location - surrounding.location, evaluated.length);
            auto* context = [[NSWritingToolsCoordinatorContext alloc] initWithAttributedString:excerpt range:relative];
            auto* stored = [JaliumWritingToolsContext new]; stored.context = context;
            stored.documentStart = surrounding.location; stored.length = surrounding.length;
            self.contexts[context.identifier] = stored; self.latestContext = context.identifier; [result addObject:context];
            if (self.presentationState == NSWritingToolsCoordinatorStateNoninteractive) [self notifyPresentationState];
        }
    }
    if (!self.isCurrent) [result removeAllObjects];
    completion(result);
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator replaceRange:(NSRange)range
    inContext:(NSWritingToolsCoordinatorContext*)context proposedText:(NSAttributedString*)text
    reason:(NSWritingToolsCoordinatorTextReplacementReason)reason
    animationParameters:(NSWritingToolsCoordinatorAnimationParameters*)parameters
    completion:(void(^)(NSAttributedString*))completion
{
    NSAttributedString* accepted = nil;
    JaliumWritingToolsContext* stored = [self resolve:context range:range];
    if (stored) {
        NSDictionary* proposal = AppleWritingToolsProposal(text);
        NSDictionary* payload = @{@"text16":proposal[@"text16"], @"proposal":proposal,
            @"context":context.identifier.UUIDString};
        ++self.applying;
        NSDictionary* response = [self request:2 range:[self absolute:range context:stored]
            flags:static_cast<int32_t>(reason) payload:payload];
        --self.applying;
        if ([response[@"applied"] boolValue] && self.isCurrent) {
            accepted = AppleTextServiceAttributedString(response);
            NSAttributedString* current = AppleTextServiceAttributedString(
                [self request:1 range:NSMakeRange(0, 0) flags:0 payload:nil]);
            if (!current) accepted = nil;
            else [self updateDocument:current notify:NO reason:NSWritingToolsCoordinatorTextUpdateReasonTyping];
        }
        else if (!self.applying) {
            // A preview callback can make its own edit and reject this proposal.
            // That edit still needs to be reported after leaving the request.
            [self refresh];
        }
    }
    completion(accepted);
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator selectRanges:(NSArray<NSValue*>*)ranges
    inContext:(NSWritingToolsCoordinatorContext*)context completion:(void(^)(void))completion
{
    NSMutableArray* absolute = [NSMutableArray new];
    for (NSValue* value in ranges) {
        if (![value isKindOfClass:NSValue.class] || strcmp(value.objCType, @encode(NSRange))) { [absolute removeAllObjects]; break; }
        NSRange range = value.rangeValue; auto* stored = [self resolve:context range:range];
        if (!stored) { [absolute removeAllObjects]; break; }
        NSRange mapped = [self absolute:range context:stored]; [absolute addObject:@[@(mapped.location),@(mapped.length)]];
    }
    if (absolute.count) (void)[self request:3 range:NSMakeRange(0, 0) flags:0
        payload:@{@"ranges":absolute,@"context":context.identifier.UUIDString}];
    completion();
}
- (NSArray<NSBezierPath*>*)paths:(NSRange)range context:(NSWritingToolsCoordinatorContext*)context underline:(BOOL)underline
{
    auto* stored = [self resolve:context range:range]; if (!stored) return @[];
    NSDictionary* response = [self request:4 range:[self absolute:range context:stored] flags:0 payload:nil];
    id rectangles = response[@"rects"]; if (![rectangles isKindOfClass:NSArray.class]) return @[];
    NSMutableArray* paths = [NSMutableArray new];
    for (id item in rectangles) {
        NSRect rectangle; if (!AppleTextServiceRect(item, self.view.jaliumOwner->scale, rectangle)) return @[];
        NSBezierPath* path = [NSBezierPath bezierPath];
        if (underline) { [path moveToPoint:NSMakePoint(NSMinX(rectangle), NSMaxY(rectangle))];
            [path lineToPoint:NSMakePoint(NSMaxX(rectangle), NSMaxY(rectangle))]; }
        else [path appendBezierPathWithRect:rectangle];
        [paths addObject:path];
    }
    return paths;
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator requestsBoundingBezierPathsForRange:(NSRange)range
    inContext:(NSWritingToolsCoordinatorContext*)context completion:(void(^)(NSArray<NSBezierPath*>*))completion
{ completion([self paths:range context:context underline:NO]); }
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator requestsUnderlinePathsForRange:(NSRange)range
    inContext:(NSWritingToolsCoordinatorContext*)context completion:(void(^)(NSArray<NSBezierPath*>*))completion
{ completion([self paths:range context:context underline:YES]); }
- (NSTextPreview*)preview:(NSDictionary*)response
{
    if (!self.isCurrent || ![response[@"png"] isKindOfClass:NSString.class]) return nil;
    CGFloat scale = self.view.jaliumOwner->scale; NSRect frame;
    if (!AppleTextServiceRect(response[@"frame"], scale, frame) || frame.size.width <= 0) return nil;
    NSData* data = [[NSData alloc] initWithBase64EncodedString:response[@"png"] options:0];
    NSBitmapImageRep* image = data ? [NSBitmapImageRep imageRepWithData:data] : nil;
    if (!image.CGImage) return nil;
    NSMutableArray* candidates = [NSMutableArray new];
    if (![response[@"rects"] isKindOfClass:NSArray.class]) return nil;
    for (id item in response[@"rects"]) {
        NSRect rectangle; if (!AppleTextServiceRect(item, scale, rectangle)) return nil;
        [candidates addObject:[NSValue valueWithRect:rectangle]];
    }
    return [[NSTextPreview alloc] initWithSnapshotImage:image.CGImage presentationFrame:frame candidateRects:candidates];
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator prepareForTextAnimation:(NSWritingToolsCoordinatorTextAnimation)animation
    forRange:(NSRange)range inContext:(NSWritingToolsCoordinatorContext*)context completion:(void(^)(void))completion
{
    auto* stored = [self resolve:context range:range];
    if (stored) (void)[self request:6 range:[self absolute:range context:stored] flags:static_cast<int32_t>(animation)
        payload:@{@"context":context.identifier.UUIDString,
            @"animationKey":[NSString stringWithFormat:@"%ld:%lu:%lu", (long)animation, (unsigned long)range.location, (unsigned long)range.length]}];
    completion();
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator requestsPreviewForTextAnimation:(NSWritingToolsCoordinatorTextAnimation)animation
    ofRange:(NSRange)range inContext:(NSWritingToolsCoordinatorContext*)context completion:(void(^)(NSArray<NSTextPreview*>*))completion
{
    auto* stored = [self resolve:context range:range]; NSTextPreview* preview = nil;
    if (stored) preview = [self preview:[self request:5 range:[self absolute:range context:stored]
        flags:static_cast<int32_t>(animation) payload:@{@"context":context.identifier.UUIDString}]];
    completion(preview ? @[preview] : nil);
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator requestsPreviewForRect:(NSRect)rect
    inContext:(NSWritingToolsCoordinatorContext*)context completion:(void(^)(NSTextPreview*))completion
{
    auto* stored = [self resolve:context range:NSMakeRange(0, 0)]; NSTextPreview* preview = nil;
    if (stored && self.isCurrent) {
        CGFloat scale = self.view.jaliumOwner->scale;
        preview = [self preview:[self request:5 range:NSMakeRange(stored.documentStart, stored.length) flags:-1
            payload:@{@"rect":@[@(rect.origin.x*scale),@(rect.origin.y*scale),
                @(rect.size.width*scale),@(rect.size.height*scale)], @"context":context.identifier.UUIDString}]];
    }
    completion(preview);
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator finishTextAnimation:(NSWritingToolsCoordinatorTextAnimation)animation
    forRange:(NSRange)range inContext:(NSWritingToolsCoordinatorContext*)context completion:(void(^)(void))completion
{
    // Finishing an old animation must work after a replacement shortened the
    // context. Its presentation token is independent of today's text offsets.
    auto* stored = [self resolve:context range:NSMakeRange(0, 0)];
    if (stored) (void)[self request:7 range:NSMakeRange(0, 0) flags:static_cast<int32_t>(animation)
        payload:@{@"context":context.identifier.UUIDString,
            @"animationKey":[NSString stringWithFormat:@"%ld:%lu:%lu", (long)animation, (unsigned long)range.location, (unsigned long)range.length]}];
    completion();
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator willChangeToState:(NSWritingToolsCoordinatorState)state
    completion:(void(^)(void))completion
{
    self.presentationState = state;
    [self notifyPresentationState];
    if (state == NSWritingToolsCoordinatorStateInactive) { [self.contexts removeAllObjects]; self.document = nil; ++self.documentRevision; }
    completion();
}
- (void)notifyPresentationState
{
    NSMutableArray* ranges = [NSMutableArray new];
    for (JaliumWritingToolsContext* stored in self.contexts.allValues) {
        NSRange range = stored.context.resolvedRange;
        if (range.location == NSNotFound) range = stored.context.range;
        if (ValidTextRange(range, stored.length)) {
            NSRange absolute = [self absolute:range context:stored];
            [ranges addObject:@[@(absolute.location),@(absolute.length)]];
        }
    }
    (void)[self request:8 range:NSMakeRange(0, 0) flags:static_cast<int32_t>(self.presentationState) payload:@{@"ranges":ranges}];
}
// Required only by the original macOS 15.2/15.3 coordinator. Newer AppKit
// resolves points itself; keep the original client-local -> document mapping.
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator requestsRangeInContextWithIdentifierForPoint:(NSPoint)point
    completion:(void(^)(NSRange, NSUUID*))completion
{
    JaliumWritingToolsContext* context = self.latestContext ? self.contexts[self.latestContext] : nil;
    NSRange range = NSMakeRange(NSNotFound,0); NSUUID* identifier = nil;
    if (self.isCurrent && context) {
        CGFloat scale = self.view.jaliumOwner->scale;
        NSDictionary* response = [self request:4 range:NSMakeRange(0,0) flags:1 payload:@{@"point":@[@(point.x*scale),@(point.y*scale)]}];
        id value = response[@"index"];
        if ([value isKindOfClass:NSNumber.class] && [value doubleValue] == [value unsignedIntegerValue]) {
            NSUInteger index = [value unsignedIntegerValue];
            if (index >= context.documentStart && index-context.documentStart <= context.length) {
                range = NSMakeRange(index-context.documentStart,0); identifier = context.context.identifier;
            }
        }
    }
    completion(range,identifier);
}
- (void)writingToolsCoordinator:(NSWritingToolsCoordinator*)coordinator requestsSingleContainerSubrangesOfRange:(NSRange)range
    inContext:(NSWritingToolsCoordinatorContext*)context completion:(void(^)(NSArray<NSValue*>*))completion
{ completion([self resolve:context range:range] ? @[[NSValue valueWithRange:range]] : @[]); }
@end
static void InvalidateAppleWritingTools(JaliumAppleView* view)
{
    if (@available(macOS 15.2, *)) {
        ++view.writingToolsGeneration;
        JaliumAppleWritingTools* adapter = view.writingToolsAdapter;
        view.writingToolsAdapter = nil;
        if (view.writingToolsCoordinator == adapter.coordinator) view.writingToolsCoordinator = nil;
        [adapter stop];
    }
}
static void RefreshAppleWritingTools(JaliumAppleView* view)
{
    if (@available(macOS 15.2, *)) {
        AppleTextInputContext lifetime(view);
        if (!lifetime.IsSameContext() || !view.textServicesEnabled || view.markedText || !lifetime.owner->visible ||
            !lifetime.owner->enabled || lifetime.owner->destroying) { InvalidateAppleWritingTools(view); return; }
        uint64_t generation = view.writingToolsGeneration;
        NSDictionary* capabilities = AppleTextServiceQuery(view, 0, NSMakeRange(0, 0));
        if (!lifetime.IsSameContext() || generation != view.writingToolsGeneration || !view.textServicesEnabled) return;
        if (![capabilities[@"canRead"] boolValue] || ![capabilities[@"editor"] isKindOfClass:NSString.class]) {
            InvalidateAppleWritingTools(view); return;
        }
        JaliumAppleWritingTools* adapter = view.writingToolsAdapter;
        if (adapter && (!adapter.isCurrent || ![adapter.editor isEqual:capabilities[@"editor"]])) {
            InvalidateAppleWritingTools(view); adapter = nil;
        }
        if (!lifetime.IsSameContext() || !view.textServicesEnabled || !lifetime.owner->visible ||
            !lifetime.owner->enabled || lifetime.owner->destroying) return;
        // Stopping an adapter invokes the client. That callback can already
        // install its successor; the outer refresh must retain that successor.
        if (!adapter && view.writingToolsAdapter) {
            [view.writingToolsAdapter refresh]; return;
        }
        if (!adapter) {
            ++view.writingToolsGeneration;
            adapter = [[JaliumAppleWritingTools alloc] initWithView:view capabilities:capabilities];
            view.writingToolsAdapter = adapter; view.writingToolsCoordinator = adapter.coordinator;
        }
        [adapter refresh];
    }
}
#else
static void InvalidateAppleWritingTools(JaliumAppleView*) {}
static void RefreshAppleWritingTools(JaliumAppleView*) {}
#endif

