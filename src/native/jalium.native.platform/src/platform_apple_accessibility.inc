// Included after JaliumAppleView's declaration. These objects represent the
// managed controls painted into one content view, without replacing NSWindow's
// own title-bar and menu accessibility implementation. Managed modality and
// enabled state supplement the real NSWindow's accessibility properties.
@class JaliumAppleAccessibilityElement;
@interface JaliumAppleAccessibilityBridge : NSObject
@property(nonatomic, weak) JaliumAppleView* view;
@property(nonatomic, assign) JaliumAccessibilityCallback callback;
@property(nonatomic, assign) void* userData;
@property(nonatomic, strong) NSMutableDictionary<NSNumber*, JaliumAppleAccessibilityElement*>* elements;
- (BOOL)query:(JaliumAccessibilityRequest*)request;
- (JaliumAppleAccessibilityElement*)element:(uint64_t)nodeId;
- (NSString*)string:(uint64_t)nodeId property:(int32_t)property;
- (NSAttributedString*)styledText:(uint64_t)nodeId range:(NSRange)range accessibility:(BOOL)accessibility;
- (NSRect)screenFrame:(NSRect)frame;
- (id)hitTest:(NSPoint)point;
- (BOOL)hasAccessibleContent;
- (BOOL)hasContentFocus;
- (id)focusedElement;
- (void)detach;
@end

// The AX attributed-string vocabulary differs from AppKit's RTF vocabulary.
// Build both from the same validated managed style snapshot.
static NSFont* JaliumAccessibilityFont(NSDictionary* style)
{
    CTFontRef font = jalium::CreateAppleTextFont((__bridge CFStringRef)style[@"family"], [style[@"size"] floatValue],
        [style[@"weight"] intValue], [style[@"italic"] intValue],
        style[@"width"] ? [style[@"width"] floatValue] : 100, style[@"width"] != nil);
    return font ? CFBridgingRelease(font) : nil;
}

static NSColor* JaliumAccessibilityColor(NSNumber* number)
{
    if (!number) return nil;
    uint32_t color = number.unsignedIntValue;
    return [NSColor colorWithSRGBRed:((color >> 16) & 255) / 255.0 green:((color >> 8) & 255) / 255.0
        blue:(color & 255) / 255.0 alpha:(color >> 24) / 255.0];
}

static double JaliumAccessibilityFontShear(NSFont* font)
{
    const CGAffineTransform matrix = CTFontGetMatrix((__bridge CTFontRef)font);
    // The shared font resolver synthesizes slanted width/private faces with
    // this horizontal shear. RTF drops the font matrix, so encode the actual
    // shape as AppKit obliqueness without applying the shear twice.
    return matrix.a == 1 && matrix.b == 0 && matrix.d == 1 && matrix.tx == 0 && matrix.ty == 0
        && std::isfinite(matrix.c) ? matrix.c : 0;
}

static NSDictionary* JaliumAccessibilityTextAttributes(NSDictionary* style, BOOL accessibility)
{
    NSFont* font = JaliumAccessibilityFont(style);
    if (!font) return nil;
    const double shear = JaliumAccessibilityFontShear(font);
    NSColor* foreground = JaliumAccessibilityColor(style[@"foreground"]);
    NSColor* underlineColor = JaliumAccessibilityColor(style[@"underlineColor"]);
    NSColor* strikeColor = JaliumAccessibilityColor(style[@"strikeColor"]);
    int underline = [style[@"underline"] intValue], strike = [style[@"strike"] intValue];
    NSMutableDictionary* attributes = [NSMutableDictionary new];
    if (accessibility) {
        NSMutableDictionary* fontAttributes = [@{NSAccessibilityFontNameKey:font.fontName,
            NSAccessibilityFontFamilyKey:font.familyName, NSAccessibilityVisibleNameKey:font.displayName,
            NSAccessibilityFontSizeKey:@(font.pointSize)} mutableCopy];
        attributes[NSAccessibilityFontTextAttribute] = fontAttributes;
        if (@available(macOS 26.0, *)) {
            NSFontTraitMask traits = [NSFontManager.sharedFontManager traitsOfFont:font];
            attributes[NSAccessibilityFontBoldAttribute] = @((traits & NSBoldFontMask) != 0);
            attributes[NSAccessibilityFontItalicAttribute] = @((traits & NSItalicFontMask) != 0 || shear != 0);
        }
        if (foreground) attributes[NSAccessibilityForegroundColorTextAttribute] = (__bridge id)foreground.CGColor;
        if (underline) attributes[NSAccessibilityUnderlineTextAttribute] = @(underline);
        if (underlineColor) attributes[NSAccessibilityUnderlineColorTextAttribute] = (__bridge id)underlineColor.CGColor;
        if (strike) attributes[NSAccessibilityStrikethroughTextAttribute] = @YES;
        if (strikeColor) attributes[NSAccessibilityStrikethroughColorTextAttribute] = (__bridge id)strikeColor.CGColor;
        attributes[NSAccessibilityTextAlignmentAttribute] = style[@"alignment"];
        id language = style[@"language"];
        if ([language isKindOfClass:NSString.class] && [language length]) attributes[NSAccessibilityLanguageTextAttribute] = language;
    } else {
        if (shear != 0) {
            CGAffineTransform identity = CGAffineTransformIdentity;
            CTFontRef upright = CTFontCreateCopyWithAttributes((__bridge CTFontRef)font,font.pointSize,&identity,nullptr);
            if (!upright) return nil;
            font = CFBridgingRelease(upright);
            attributes[NSObliquenessAttributeName] = @(shear);
        }
        attributes[NSFontAttributeName] = font;
        if (foreground) attributes[NSForegroundColorAttributeName] = foreground;
        if (underline) attributes[NSUnderlineStyleAttributeName] = @(underline);
        if (underlineColor) attributes[NSUnderlineColorAttributeName] = underlineColor;
        if (strike) attributes[NSStrikethroughStyleAttributeName] = @(strike);
        if (strikeColor) attributes[NSStrikethroughColorAttributeName] = strikeColor;
        NSMutableParagraphStyle* paragraph = [NSMutableParagraphStyle new];
        paragraph.alignment = (NSTextAlignment)[style[@"alignment"] intValue];
        paragraph.baseWritingDirection = (NSWritingDirection)[style[@"direction"] intValue];
        attributes[NSParagraphStyleAttributeName] = paragraph;
    }
    return attributes;
}

static bool JaliumAccessibilityStyleNumber(NSDictionary* style, NSString* key, double minimum, double maximum)
{
    id value = style[key];
    if (![value isKindOfClass:NSNumber.class]) return false;
    double number = [value doubleValue];
    return std::isfinite(number) && number >= minimum && number <= maximum
        && ([key isEqualToString:@"size"] || [key isEqualToString:@"width"] || number == std::floor(number));
}

static NSAttributedString* JaliumAccessibilityDecodeStyles(NSString* payload, NSUInteger requestedLength, BOOL accessibility)
{
    NSData* json = [payload dataUsingEncoding:NSUTF8StringEncoding];
    id object = json ? [NSJSONSerialization JSONObjectWithData:json options:0 error:nil] : nil;
    if (![object isKindOfClass:NSDictionary.class]) return nil;
    NSDictionary* document = object;
    if (![document[@"version"] isEqual:@1] || ![document[@"text16"] isKindOfClass:NSString.class]
        || ![document[@"runs"] isKindOfClass:NSArray.class]) return nil;
    NSData* text = [[NSData alloc] initWithBase64EncodedString:document[@"text16"] options:0];
    if (!text || text.length != requestedLength * 2) return nil;
    std::vector<uint16_t> characters(std::max((NSUInteger)1, requestedLength));
    const auto* bytes = static_cast<const uint8_t*>(text.bytes);
    for (NSUInteger i = 0; i < requestedLength; i++) characters[i] = bytes[i * 2] | (bytes[i * 2 + 1] << 8);
    NSString* string = [[NSString alloc] initWithCharacters:characters.data() length:requestedLength];
    NSMutableAttributedString* result = [[NSMutableAttributedString alloc] initWithString:string];
    NSUInteger through = 0;
    for (id item in document[@"runs"]) {
        if (![item isKindOfClass:NSDictionary.class]) return nil;
        NSDictionary* style = item;
        if (!JaliumAccessibilityStyleNumber(style, @"start", through, through)
            || !JaliumAccessibilityStyleNumber(style, @"length", 1, requestedLength - through)
            || !JaliumAccessibilityStyleNumber(style, @"size", .000001, 65536)
            || style[@"width"] && !JaliumAccessibilityStyleNumber(style, @"width", 0, std::numeric_limits<float>::max())
            || !JaliumAccessibilityStyleNumber(style, @"weight", 1, 1000)
            || !JaliumAccessibilityStyleNumber(style, @"italic", 0, 2)
            || !JaliumAccessibilityStyleNumber(style, @"underline", 0, 0xffff)
            || !JaliumAccessibilityStyleNumber(style, @"strike", 0, 0xffff)
            || !JaliumAccessibilityStyleNumber(style, @"alignment", 0, 3)
            || !JaliumAccessibilityStyleNumber(style, @"direction", -1, 1)
            || ![style[@"family"] isKindOfClass:NSString.class] || ![style[@"family"] length] || [style[@"family"] length] > 4096
            || style[@"language"] != NSNull.null && (![style[@"language"] isKindOfClass:NSString.class] || [style[@"language"] length] > 1024)) return nil;
        for (NSString* key in @[@"foreground", @"underlineColor", @"strikeColor"])
            if (style[key] && !JaliumAccessibilityStyleNumber(style, key, 0, UINT32_MAX)) return nil;
        NSUInteger length = [style[@"length"] unsignedIntegerValue];
        NSDictionary* attributes = JaliumAccessibilityTextAttributes(style, accessibility);
        if (!attributes) return nil;
        [result setAttributes:attributes range:NSMakeRange(through, length)];
        through += length;
    }
    return through == requestedLength ? result : nil;
}

static bool QueryWindowAccessibilityInfo(JaliumPlatformWindow* window, JaliumAccessibilityRequest* request)
{
    if (!NSThread.isMainThread || !window || !IsLiveWindow(window) || window->destroying) return false;
    request->nodeId = 1;
    request->operation = JALIUM_AX_INFO;
    return [window->view.accessibilityBridge query:request] != NO;
}

static id QueryWindowAccessibilityButton(JaliumPlatformWindow* window, int32_t kind)
{
    if (!NSThread.isMainThread || !window || !IsLiveWindow(window) || window->destroying) return nil;
    // Retain only the bridge across a managed callback, which may close the
    // Window and free the platform owner. Never read window after that call.
    JaliumAppleAccessibilityBridge* bridge = window->view.accessibilityBridge;
    JaliumAccessibilityRequest request{};
    request.nodeId = 1; request.operation = JALIUM_AX_WINDOW_BUTTON; request.index = kind;
    if (![bridge query:&request]) return nil;
    id button = [bridge element:request.resultId];
    return [button isAccessibilityElement] ? button : nil;
}

@interface JaliumAppleAccessibilityElement : NSAccessibilityElement
@property(nonatomic, weak) JaliumAppleAccessibilityBridge* bridge;
@property(nonatomic) uint64_t nodeId;
@property(nonatomic) BOOL retired;
@property(nonatomic) BOOL postingDestruction;
- (BOOL)info:(JaliumAccessibilityRequest*)request;
- (BOOL)action:(int32_t)action;
- (void)retire;
@end

static NSAccessibilityRole JaliumAccessibilityRole(int32_t role, uint32_t flags)
{
    switch (role) {
        case 0: return NSAccessibilityButtonRole;
        case 2: return NSAccessibilityCheckBoxRole;
        case 3: return NSAccessibilityComboBoxRole;
        case 4: return (flags & JALIUM_AX_MULTILINE) ? NSAccessibilityTextAreaRole : NSAccessibilityTextFieldRole;
        case 5: return NSAccessibilityLinkRole;
        case 6: return NSAccessibilityImageRole;
        case 7: return NSAccessibilityCellRole;
        case 8: return NSAccessibilityListRole;
        case 9: return NSAccessibilityMenuRole;
        case 10: return NSAccessibilityMenuBarRole;
        case 11: return NSAccessibilityMenuItemRole;
        case 12: return NSAccessibilityProgressIndicatorRole;
        case 13: return NSAccessibilityRadioButtonRole;
        case 14: return NSAccessibilityScrollBarRole;
        case 15: return NSAccessibilitySliderRole;
        case 16: return NSAccessibilityIncrementorRole;
        case 18: return NSAccessibilityTabGroupRole;
        case 19: return NSAccessibilityRadioButtonRole;
        case 20: return NSAccessibilityStaticTextRole;
        case 21: return NSAccessibilityToolbarRole;
        case 22: return NSAccessibilityHelpTagRole;
        case 23: return NSAccessibilityOutlineRole;
        case 24: case 29: return NSAccessibilityRowRole;
        case 27: case 38: return NSAccessibilitySplitterRole;
        case 28: case 36: return NSAccessibilityTableRole;
        case 30: return NSAccessibilityTextAreaRole;
        case 31: return NSAccessibilityPopUpButtonRole;
        case 35: return NSAccessibilityColumnRole;
        // The real NSWindow is already exposed by AppKit; its managed peer is
        // the content group. Containers without a closer role are groups too.
        default: return NSAccessibilityGroupRole;
    }
}

@implementation JaliumAppleAccessibilityBridge
- (instancetype)init {
    self = [super init];
    if (self) _elements = [NSMutableDictionary new];
    return self;
}
- (BOOL)query:(JaliumAccessibilityRequest*)request {
    // AppKit's accessibility callbacks are served on its UI thread. A foreign
    // thread cannot synchronously marshal through the managed dispatcher.
    JaliumAppleAccessibilityBridge* bridge = self;
    if (![bridge hasAccessibleContent]) return NO;
    JaliumAppleView* view = bridge.view;
    NSWindow* window = view.window;
    // Mutations report whether the provider accepted the action, including a
    // close button. Reads must not publish a snapshot of a retired tree.
    const bool mutation = request->operation == JALIUM_AX_ACTION || request->operation == JALIUM_AX_SET_VALUE ||
        request->operation == JALIUM_AX_SET_TEXT_SELECTION || (request->operation == JALIUM_AX_TEXT_NAVIGATION &&
        (request->index == JALIUM_AX_SET_INSERTION_LINE || request->index == JALIUM_AX_REPLACE_SELECTION));
    const BOOL accepted = bridge.callback(request, bridge.userData) != 0;
    return accepted && (mutation || (bridge.view == view && view.window == window && [bridge hasAccessibleContent]));
}
- (JaliumAppleAccessibilityElement*)element:(uint64_t)nodeId {
    if (!nodeId || ![self hasAccessibleContent]) return nil;
    NSNumber* key = @(nodeId);
    JaliumAppleAccessibilityElement* element = _elements[key];
    if (!element) {
        element = [JaliumAppleAccessibilityElement new];
        element.bridge = self;
        element.nodeId = nodeId;
        _elements[key] = element;
    }
    return element;
}
- (NSString*)string:(uint64_t)nodeId property:(int32_t)property {
    JaliumAppleAccessibilityBridge* bridge = self;
    JaliumAccessibilityRequest request{};
    request.nodeId = nodeId; request.operation = JALIUM_AX_STRING; request.index = property;
    if (![bridge query:&request] || request.textCount < 0 || request.textCount > 16 * 1024 * 1024) return nil;
    // A provider may change between the sizing and copying calls. Retry once
    // with its new size; never read beyond the caller-owned buffer.
    for (int attempt = 0; attempt < 2; attempt++) {
        std::vector<uint16_t> buffer(std::max(1, request.textCount));
        request.text = buffer.data(); request.textCapacity = (int32_t)buffer.size();
        if ([bridge query:&request] && request.textCount >= 0 && request.textCount <= request.textCapacity)
            return [[NSString alloc] initWithCharacters:buffer.data() length:(NSUInteger)request.textCount];
        if (![bridge hasAccessibleContent]) break;
        if (request.textCount < 0 || request.textCount > 16 * 1024 * 1024) break;
    }
    return nil;
}
- (NSAttributedString*)styledText:(uint64_t)nodeId range:(NSRange)range accessibility:(BOOL)accessibility {
    JaliumAppleAccessibilityBridge* bridge = self;
    if (range.location > INT32_MAX || range.length > 16 * 1024 * 1024) return nil;
    JaliumAccessibilityRequest request{};
    request.nodeId = nodeId; request.operation = JALIUM_AX_TEXT_STYLES;
    request.textStart = (int32_t)range.location; request.textLength = (int32_t)range.length;
    if (![bridge query:&request] || request.textCount < 0 || request.textCount > 64 * 1024 * 1024) return nil;
    for (int attempt = 0; attempt < 2; attempt++) {
        std::vector<uint16_t> buffer(std::max(1, request.textCount));
        request.text = buffer.data(); request.textCapacity = (int32_t)buffer.size();
        if ([bridge query:&request] && request.textCount >= 0 && request.textCount <= request.textCapacity) {
            NSString* payload = [[NSString alloc] initWithCharacters:buffer.data() length:(NSUInteger)request.textCount];
            return JaliumAccessibilityDecodeStyles(payload, range.length, accessibility);
        }
        if (![bridge hasAccessibleContent]) break;
        if (request.textCount < 0 || request.textCount > 64 * 1024 * 1024) break;
    }
    return nil;
}
- (NSRect)screenFrame:(NSRect)frame {
    if (![self hasAccessibleContent]) return NSZeroRect;
    JaliumAppleView* view = _view;
    if (!view.window) return NSZeroRect;
    return [view.window convertRectToScreen:[view convertRect:frame toView:nil]];
}
- (id)hitTest:(NSPoint)point {
    JaliumAppleAccessibilityBridge* bridge = self;
    if (![bridge hasAccessibleContent]) return nil;
    JaliumAppleView* view = bridge.view;
    NSPoint local = [view convertPoint:[view.window convertPointFromScreen:point] fromView:nil];
    JaliumAccessibilityRequest request{};
    request.nodeId = 1; request.operation = JALIUM_AX_HIT_TEST;
    request.x = local.x; request.y = local.y;
    return [bridge query:&request] ? [bridge element:request.resultId] : nil;
}
- (BOOL)hasAccessibleContent {
    if (!NSThread.isMainThread || !_callback) return NO;
    JaliumAppleView* view = _view;
    JaliumPlatformWindow* owner = view.jaliumOwner;
    NSWindow* window = view.window;
    // Disabled content remains readable. The owning view and bridge must
    // still belong to this live, visible native Window.
    return owner && IsLiveWindow(owner, window) && !owner->destroying && owner->visible &&
        owner->view == view && view.accessibilityBridge == self && window.visible;
}
- (BOOL)hasContentFocus {
    if (![self hasAccessibleContent]) return NO;
    JaliumAppleView* view = _view;
    JaliumPlatformWindow* owner = view.jaliumOwner;
    NSWindow* window = view.window;
    return owner->enabled && window.keyWindow && window.firstResponder == view;
}
- (id)focusedElement {
    // The provider may close the window and detach/release its bridge. Retain
    // this bridge across that callback and recheck the real responder after it.
    JaliumAppleAccessibilityBridge* bridge = self;
    if (![bridge hasContentFocus]) return nil;
    JaliumAccessibilityRequest request{};
    request.nodeId = 1; request.operation = JALIUM_AX_FOCUS;
    return [bridge query:&request] && [bridge hasContentFocus] ? [bridge element:request.resultId] : nil;
}
- (void)detach {
    _callback = nullptr; _userData = nullptr;
    NSArray* elements = _elements.allValues;
    [_elements removeAllObjects];
    for (JaliumAppleAccessibilityElement* element in elements) [element retire];
    _view = nil;
}
@end

@implementation JaliumAppleAccessibilityElement
- (void)retire {
    if (_retired) return;
    _retired = YES;
    _bridge = nil;
    // AppKit resolves the unignored notification target before sending it.
    // Keep only this proxy identifiable during that call; all provider-backed
    // attributes/actions already reject the detached bridge.
    _postingDestruction = YES;
    @try {
        NSAccessibilityPostNotification(self, NSAccessibilityUIElementDestroyedNotification);
    } @finally {
        _postingDestruction = NO;
    }
}
- (BOOL)accessibilityNotifiesWhenDestroyed {
    // The bridge owns each proxy until its UI node is removed and explicitly
    // posts AXUIElementDestroyed. AppKit requires this opt-in for custom peers.
    return YES;
}
- (BOOL)respondsToSelector:(SEL)selector {
    if (![super respondsToSelector:selector]) return NO;
    NSString* name = NSStringFromSelector(selector);
    // AppKit discovers writable attributes from their setters before checking
    // whether a particular invocation is allowed. Reflect the current provider
    // capability here too, so read-only values do not advertise an editor.
    if ([name hasPrefix:@"setAccessibility"] || [name hasPrefix:@"accessibilityPerform"])
        return [self isAccessibilitySelectorAllowed:selector];
    if (selector == @selector(accessibilityLineForIndex:) || selector == @selector(accessibilityRangeForLine:)
        || selector == @selector(accessibilityRangeForIndex:) || selector == @selector(accessibilityRangeForPosition:)
        || selector == @selector(accessibilityVisibleCharacterRange) || selector == @selector(accessibilityInsertionPointLineNumber)
        || selector == @selector(accessibilitySelectedTextRanges)
        || selector == @selector(accessibilityRTFForRange:) || selector == @selector(accessibilityStyleRangeForIndex:))
        return [self isAccessibilitySelectorAllowed:selector];
    return YES;
}
- (BOOL)info:(JaliumAccessibilityRequest*)request {
    *request = {};
    request->nodeId = _nodeId; request->operation = JALIUM_AX_INFO;
    return [_bridge query:request];
}
- (BOOL)action:(int32_t)action {
    JaliumAccessibilityRequest request{};
    if (![self info:&request] || !(request.flags & JALIUM_AX_ENABLED)) return NO;
    request.operation = JALIUM_AX_ACTION; request.index = action;
    return [_bridge query:&request];
}
- (BOOL)isAccessibilityElement {
    if (_postingDestruction) return YES;
    JaliumAccessibilityRequest request{};
    return [self info:&request];
}
- (NSAccessibilityRole)accessibilityRole {
    JaliumAccessibilityRequest request{};
    return [self info:&request] ? JaliumAccessibilityRole(request.role, request.flags) : NSAccessibilityUnknownRole;
}
- (NSAccessibilitySubrole)accessibilitySubrole {
    JaliumAccessibilityRequest request{};
    if (![self info:&request]) return nil;
    if (request.flags & JALIUM_AX_PASSWORD) return NSAccessibilitySecureTextFieldSubrole;
    if (request.role == 0) {
        if (request.flags & JALIUM_AX_CLOSE) return NSAccessibilityCloseButtonSubrole;
        if (request.flags & JALIUM_AX_MINIMIZE) return NSAccessibilityMinimizeButtonSubrole;
        if (request.flags & JALIUM_AX_ZOOM) return NSAccessibilityZoomButtonSubrole;
    }
    return nil;
}
- (NSString*)accessibilityRoleDescription { return NSAccessibilityRoleDescription(self.accessibilityRole, self.accessibilitySubrole); }
- (NSString*)accessibilityLabel { return [_bridge string:_nodeId property:JALIUM_AX_NAME]; }
- (NSString*)accessibilityTitle { return self.accessibilityLabel; }
- (NSString*)accessibilityHelp { return [_bridge string:_nodeId property:JALIUM_AX_HELP]; }
- (NSString*)accessibilityIdentifier { return [_bridge string:_nodeId property:JALIUM_AX_IDENTIFIER]; }
- (NSString*)accessibilityPlaceholderValue { return [_bridge string:_nodeId property:JALIUM_AX_PLACEHOLDER]; }
- (id)accessibilityParent {
    JaliumAccessibilityRequest request{};
    if (![self info:&request]) return nil;
    return request.parentId ? [_bridge element:request.parentId] : NSAccessibilityUnignoredAncestor(_bridge.view);
}
- (id)accessibilityWindow { return _bridge.view.window; }
- (id)accessibilityTopLevelUIElement { return self.accessibilityWindow; }
- (NSRect)accessibilityFrame {
    JaliumAccessibilityRequest request{};
    return [self info:&request] ? [_bridge screenFrame:NSMakeRect(request.x, request.y, request.width, request.height)] : NSZeroRect;
}
- (NSArray*)accessibilityChildren {
    JaliumAppleAccessibilityBridge* bridge = _bridge;
    JaliumAccessibilityRequest request{};
    if (![self info:&request]) return @[];
    int count = request.childCount;
    NSMutableArray* children = [NSMutableArray new];
    for (int index = 0; index < count; index++) {
        request.operation = JALIUM_AX_CHILD; request.index = index;
        if ([bridge query:&request]) {
            id child = [bridge element:request.resultId];
            if (child) [children addObject:child];
        }
    }
    return [bridge hasAccessibleContent] ? children : @[];
}
- (NSArray*)accessibilityChildrenInNavigationOrder { return self.accessibilityChildren; }
- (NSArray*)accessibilityVisibleChildren {
    NSRect frame = [_bridge screenFrame:_bridge.view.bounds];
    NSMutableArray* children = [NSMutableArray new];
    for (id child in self.accessibilityChildren) {
        if ([child isKindOfClass:[JaliumAppleAccessibilityElement class]]) {
            JaliumAccessibilityRequest request{};
            if (![child info:&request] || (request.flags & JALIUM_AX_OFFSCREEN)) continue;
        }
        if (NSIntersectsRect(frame, [child accessibilityFrame])) [children addObject:child];
    }
    return children;
}
- (NSArray*)accessibilitySelectedChildren {
    NSMutableArray* children = [NSMutableArray new];
    for (id child in self.accessibilityChildren)
        if ([child isAccessibilitySelected]) [children addObject:child];
    return children;
}
- (NSArray*)accessibilityDisclosedRows {
    if (![self.accessibilityRole isEqual:NSAccessibilityRowRole]) return nil;
    NSMutableArray* rows = [NSMutableArray new];
    for (id child in self.accessibilityChildren)
        if ([[child accessibilityRole] isEqual:NSAccessibilityRowRole]) [rows addObject:child];
    return rows;
}
- (NSArray*)accessibilityRows {
    if (![self.accessibilityRole isEqual:NSAccessibilityOutlineRole]) return nil;
    NSMutableArray* rows = [NSMutableArray new];
    NSMutableArray* pending = [self.accessibilityChildren mutableCopy];
    while (pending.count) {
        id row = pending.firstObject;
        [pending removeObjectAtIndex:0];
        if (![[row accessibilityRole] isEqual:NSAccessibilityRowRole]) continue;
        [rows addObject:row];
        NSArray* children = [row accessibilityDisclosedRows];
        if (children.count) [pending insertObjects:children
            atIndexes:[NSIndexSet indexSetWithIndexesInRange:NSMakeRange(0, children.count)]];
    }
    return rows;
}
- (NSArray*)accessibilitySelectedRows {
    NSMutableArray* rows = [NSMutableArray new];
    for (id row in self.accessibilityRows)
        if ([row isAccessibilitySelected]) [rows addObject:row];
    return rows;
}
- (NSInteger)accessibilityDisclosureLevel {
    NSInteger level = 0;
    for (id parent = self.accessibilityParent; parent && level < 128
        && [[parent accessibilityRole] isEqual:NSAccessibilityRowRole]; parent = [parent accessibilityParent]) level++;
    return level;
}
- (BOOL)isAccessibilityDisclosed { return self.accessibilityExpanded; }
- (void)setAccessibilityDisclosed:(BOOL)disclosed { self.accessibilityExpanded = disclosed; }
- (BOOL)isAccessibilityEnabled {
    JaliumAccessibilityRequest request{};
    return [self info:&request] && (request.flags & JALIUM_AX_ENABLED);
}
- (BOOL)isAccessibilityFocused {
    JaliumAppleAccessibilityBridge* bridge = _bridge;
    JaliumAccessibilityRequest request{};
    return [bridge hasContentFocus] && [self info:&request] && [bridge hasContentFocus] &&
        (request.flags & JALIUM_AX_FOCUSED);
}
- (void)setAccessibilityFocused:(BOOL)focused { if (focused) [self action:JALIUM_AX_SET_FOCUS]; }
- (BOOL)isAccessibilitySelected {
    JaliumAccessibilityRequest request{};
    return [self info:&request] && (request.flags & JALIUM_AX_SELECTED);
}
- (void)setAccessibilitySelected:(BOOL)selected { [self action:selected ? JALIUM_AX_PRESS : JALIUM_AX_DESELECT]; }
- (BOOL)isAccessibilityExpanded {
    JaliumAccessibilityRequest request{};
    return [self info:&request] && (request.flags & JALIUM_AX_EXPANDED);
}
- (void)setAccessibilityExpanded:(BOOL)expanded { [self action:expanded ? JALIUM_AX_EXPAND : JALIUM_AX_COLLAPSE]; }
- (BOOL)isAccessibilityProtectedContent {
    JaliumAccessibilityRequest request{};
    return [self info:&request] && (request.flags & JALIUM_AX_PASSWORD);
}
- (id)accessibilityValue {
    JaliumAccessibilityRequest request{};
    if (![self info:&request]) return nil;
    if (request.flags & (JALIUM_AX_RANGE | JALIUM_AX_TOGGLE)) return @(request.value);
    if (request.role == 13 || request.role == 19) return @((request.flags & JALIUM_AX_SELECTED) != 0);
    return [_bridge string:_nodeId property:JALIUM_AX_VALUE];
}
- (void)setAccessibilityValue:(id)value {
    JaliumAccessibilityRequest request{};
    if (![self info:&request] || !(request.flags & JALIUM_AX_ENABLED) || !(request.flags & JALIUM_AX_WRITABLE)) return;
    request.operation = JALIUM_AX_SET_VALUE;
    if (request.flags & JALIUM_AX_RANGE) {
        if (![value isKindOfClass:NSNumber.class]) return;
        request.value = [value doubleValue];
        [_bridge query:&request];
    } else if ([value isKindOfClass:NSString.class]) {
        NSString* string = value;
        if (string.length > INT32_MAX) return;
        std::vector<uint16_t> buffer(std::max((NSUInteger)1, string.length));
        [string getCharacters:buffer.data() range:NSMakeRange(0, string.length)];
        request.text = buffer.data(); request.textCount = (int32_t)string.length;
        request.textCapacity = (int32_t)buffer.size();
        [_bridge query:&request];
    }
}
- (id)accessibilityMinValue {
    JaliumAccessibilityRequest request{};
    return [self info:&request] && (request.flags & JALIUM_AX_RANGE) ? @(request.minimum) : nil;
}
- (id)accessibilityMaxValue {
    JaliumAccessibilityRequest request{};
    return [self info:&request] && (request.flags & JALIUM_AX_RANGE) ? @(request.maximum) : nil;
}
- (BOOL)accessibilityPerformPress { return [self action:JALIUM_AX_PRESS]; }
- (BOOL)accessibilityPerformIncrement { return [self action:JALIUM_AX_INCREMENT]; }
- (BOOL)accessibilityPerformDecrement { return [self action:JALIUM_AX_DECREMENT]; }
- (id)accessibilityHitTest:(NSPoint)point { return [_bridge hitTest:point]; }
- (id)accessibilityFocusedUIElement { return [_bridge focusedElement]; }
- (NSInteger)accessibilityNumberOfCharacters {
    JaliumAccessibilityRequest request{};
    return [self info:&request] && (request.flags & JALIUM_AX_TEXT) ? request.textCount : 0;
}
- (NSRange)accessibilitySelectedTextRange {
    JaliumAccessibilityRequest request{};
    request.nodeId = _nodeId; request.operation = JALIUM_AX_TEXT_SELECTION;
    return [_bridge query:&request] ? NSMakeRange(request.textStart, request.textLength) : NSMakeRange(NSNotFound, 0);
}
- (void)setAccessibilitySelectedTextRange:(NSRange)range {
    if (range.location > INT32_MAX || range.length > INT32_MAX) return;
    JaliumAccessibilityRequest request{};
    request.nodeId = _nodeId; request.operation = JALIUM_AX_SET_TEXT_SELECTION;
    request.textStart = (int32_t)range.location; request.textLength = (int32_t)range.length;
    [_bridge query:&request];
}
- (NSString*)accessibilityStringForRange:(NSRange)range {
    JaliumAccessibilityRequest request{};
    if (![self info:&request] || !(request.flags & JALIUM_AX_TEXT)) return nil;
    NSString* text = [_bridge string:_nodeId property:JALIUM_AX_VALUE];
    return range.location <= text.length && range.length <= text.length - range.location ? [text substringWithRange:range] : nil;
}
- (NSAttributedString*)accessibilityAttributedStringForRange:(NSRange)range {
    JaliumAccessibilityRequest request{};
    if (![self info:&request] || !(request.flags & JALIUM_AX_TEXT) || (request.flags & JALIUM_AX_PASSWORD)) return nil;
    if (request.flags & JALIUM_AX_STYLED_TEXT) return [_bridge styledText:_nodeId range:range accessibility:YES];
    NSString* string = [self accessibilityStringForRange:range];
    return string ? [[NSAttributedString alloc] initWithString:string] : nil;
}
- (NSData*)accessibilityRTFForRange:(NSRange)range {
    JaliumAccessibilityRequest request{};
    if (!range.length || ![self info:&request] || !(request.flags & JALIUM_AX_TEXT)
        || !(request.flags & JALIUM_AX_STYLED_TEXT) || (request.flags & JALIUM_AX_PASSWORD)) return nil;
    NSAttributedString* text = [_bridge styledText:_nodeId range:range accessibility:NO];
    if (!text) return nil;
    @try { return [text RTFFromRange:NSMakeRange(0, text.length) documentAttributes:@{}]; }
    @catch (NSException*) { return nil; }
}
- (NSRange)accessibilityStyleRangeForIndex:(NSInteger)index {
    if (index < 0 || index > INT32_MAX) return NSMakeRange(NSNotFound, 0);
    JaliumAccessibilityRequest request{};
    if (![self info:&request] || !(request.flags & JALIUM_AX_TEXT)
        || !(request.flags & JALIUM_AX_STYLED_TEXT) || (request.flags & JALIUM_AX_PASSWORD)) return NSMakeRange(NSNotFound, 0);
    request = {};
    request.nodeId = _nodeId; request.operation = JALIUM_AX_TEXT_STYLE_RANGE; request.textStart = (int32_t)index;
    if (![_bridge query:&request] || request.textStart < 0 || request.textLength < 0) return NSMakeRange(NSNotFound, 0);
    return NSMakeRange(request.textStart, request.textLength);
}
- (NSString*)accessibilitySelectedText { return [self accessibilityStringForRange:self.accessibilitySelectedTextRange]; }
- (NSArray<NSValue*>*)accessibilitySelectedTextRanges {
    NSRange range = self.accessibilitySelectedTextRange;
    return range.location == NSNotFound ? @[] : @[[NSValue valueWithRange:range]];
}
- (void)setAccessibilitySelectedTextRanges:(NSArray<NSValue*>*)ranges {
    // The managed editors support one contiguous selection. Reject a
    // disjoint request instead of silently discarding additional ranges.
    if (![ranges isKindOfClass:NSArray.class] || ranges.count != 1) return;
    id value = ranges.firstObject;
    if (![value isKindOfClass:NSValue.class] || strcmp([value objCType], @encode(NSRange)) != 0) return;
    self.accessibilitySelectedTextRange = [value rangeValue];
}
- (BOOL)textNavigation:(int32_t)operation argument:(NSInteger)argument request:(JaliumAccessibilityRequest*)request {
    if (argument < 0 || argument > INT32_MAX) return NO;
    *request = {};
    request->nodeId = _nodeId; request->operation = JALIUM_AX_TEXT_NAVIGATION;
    request->index = operation; request->textStart = (int32_t)argument;
    return [_bridge query:request];
}
- (NSInteger)accessibilityLineForIndex:(NSInteger)index {
    JaliumAccessibilityRequest request{};
    return [self textNavigation:JALIUM_AX_LINE_FOR_INDEX argument:index request:&request] ? request.textStart : NSNotFound;
}
- (NSRange)accessibilityRangeForIndex:(NSInteger)index {
    JaliumAccessibilityRequest request{};
    return [self textNavigation:JALIUM_AX_RANGE_FOR_INDEX argument:index request:&request]
        ? NSMakeRange(request.textStart, request.textLength) : NSMakeRange(NSNotFound, 0);
}
- (NSRange)accessibilityRangeForLine:(NSInteger)line {
    JaliumAccessibilityRequest request{};
    return [self textNavigation:JALIUM_AX_RANGE_FOR_LINE argument:line request:&request]
        ? NSMakeRange(request.textStart, request.textLength) : NSMakeRange(NSNotFound, 0);
}
- (NSRange)accessibilityRangeForPosition:(NSPoint)point {
    if (!NSThread.isMainThread || !std::isfinite(point.x) || !std::isfinite(point.y)) return NSMakeRange(NSNotFound, 0);
    JaliumAppleView* view = _bridge.view;
    if (!view.window) return NSMakeRange(NSNotFound, 0);
    NSPoint local = [view convertPoint:[view.window convertPointFromScreen:point] fromView:nil];
    JaliumAccessibilityRequest request{};
    request.nodeId = _nodeId; request.operation = JALIUM_AX_TEXT_NAVIGATION; request.index = JALIUM_AX_RANGE_FOR_POSITION;
    request.x = local.x; request.y = local.y;
    return [_bridge query:&request] ? NSMakeRange(request.textStart, request.textLength) : NSMakeRange(NSNotFound, 0);
}
- (NSRange)accessibilityVisibleCharacterRange {
    JaliumAccessibilityRequest request{};
    return [self textNavigation:JALIUM_AX_VISIBLE_TEXT_RANGE argument:0 request:&request]
        ? NSMakeRange(request.textStart, request.textLength) : NSMakeRange(NSNotFound, 0);
}
- (NSInteger)accessibilityInsertionPointLineNumber {
    JaliumAccessibilityRequest request{};
    return [self textNavigation:JALIUM_AX_INSERTION_LINE argument:0 request:&request] ? request.textStart : NSNotFound;
}
- (void)setAccessibilityInsertionPointLineNumber:(NSInteger)line {
    JaliumAccessibilityRequest request{};
    [self textNavigation:JALIUM_AX_SET_INSERTION_LINE argument:line request:&request];
}
- (void)setAccessibilitySelectedText:(NSString*)text {
    if (![text isKindOfClass:NSString.class] || text.length > 16 * 1024 * 1024) return;
    JaliumAccessibilityRequest request{};
    request.nodeId = _nodeId; request.operation = JALIUM_AX_TEXT_NAVIGATION; request.index = JALIUM_AX_REPLACE_SELECTION;
    std::vector<uint16_t> buffer(std::max<NSUInteger>(1, text.length));
    [text getCharacters:buffer.data() range:NSMakeRange(0, text.length)];
    request.text = buffer.data(); request.textCount = request.textCapacity = (int32_t)text.length;
    [_bridge query:&request];
}
- (NSRect)accessibilityFrameForRange:(NSRange)range {
    if (range.location > INT32_MAX || range.length > INT32_MAX) return NSZeroRect;
    JaliumAccessibilityRequest request{};
    request.nodeId = _nodeId; request.operation = JALIUM_AX_TEXT_BOUNDS;
    request.textStart = (int32_t)range.location; request.textLength = (int32_t)range.length;
    return [_bridge query:&request] ? [_bridge screenFrame:NSMakeRect(request.x, request.y, request.width, request.height)] : NSZeroRect;
}
- (BOOL)isAccessibilitySelectorAllowed:(SEL)selector {
    // AppKit asks permission before resolving the destruction notification's
    // target. These lifetime queries must not depend on the retired provider.
    if (selector == @selector(accessibilityNotifiesWhenDestroyed)
        || (_postingDestruction && selector == @selector(isAccessibilityElement))) return YES;
    JaliumAccessibilityRequest request{};
    if (![self info:&request]) return NO;
    bool enabled = (request.flags & JALIUM_AX_ENABLED) != 0;
    bool selectable = (request.flags & JALIUM_AX_SELECTABLE) != 0;
    bool expandable = (request.flags & JALIUM_AX_EXPANDABLE) != 0;
    bool row = request.role == 24 || request.role == 29;
    if (selector == @selector(isAccessibilitySelected)) return selectable;
    if (selector == @selector(isAccessibilityExpanded)) return expandable;
    if (selector == @selector(isAccessibilityDisclosed) || selector == @selector(accessibilityDisclosedRows))
        return row && expandable;
    if (selector == @selector(accessibilityDisclosureLevel)) return row;
    if (selector == @selector(accessibilityRows)) return request.role == 23;
    if (selector == @selector(accessibilitySelectedRows)) return request.role == 23 && (request.flags & JALIUM_AX_SELECTION);
    if (selector == @selector(accessibilitySelectedChildren)) return (request.flags & JALIUM_AX_SELECTION) != 0;
    if (selector == @selector(accessibilityMinValue) || selector == @selector(accessibilityMaxValue))
        return (request.flags & JALIUM_AX_RANGE) != 0;
    if (selector == @selector(accessibilityValue))
        return (request.flags & (JALIUM_AX_HAS_VALUE | JALIUM_AX_TEXT | JALIUM_AX_RANGE | JALIUM_AX_TOGGLE))
            || request.role == 20 || ((request.role == 13 || request.role == 19) && selectable);
    if (selector == @selector(accessibilityPlaceholderValue)) return request.role == 4;
    if (selector == @selector(accessibilityPerformPress)) return enabled && (request.flags & JALIUM_AX_PRESSABLE);
    if (selector == @selector(setAccessibilityFocused:)) return enabled && (request.flags & JALIUM_AX_FOCUSABLE);
    if (selector == @selector(setAccessibilityValue:)) return enabled && (request.flags & JALIUM_AX_WRITABLE);
    if (selector == @selector(accessibilityPerformIncrement) || selector == @selector(accessibilityPerformDecrement))
        return enabled && (request.flags & JALIUM_AX_RANGE) && (request.flags & JALIUM_AX_WRITABLE) && request.step > 0;
    if (selector == @selector(setAccessibilityExpanded:)) return enabled && expandable;
    if (selector == @selector(setAccessibilityDisclosed:)) return enabled && row && expandable;
    if (selector == @selector(setAccessibilitySelectedRows:)) return NO;
    if (selector == @selector(setAccessibilitySelected:))
        return enabled && selectable;
    if (selector == @selector(setAccessibilitySelectedTextRange:) || selector == @selector(setAccessibilitySelectedTextRanges:))
        return enabled && (request.flags & JALIUM_AX_TEXT);
    if (selector == @selector(setAccessibilitySelectedText:)) return enabled && (request.flags & JALIUM_AX_EDITABLE_TEXT);
    if (selector == @selector(setAccessibilityInsertionPointLineNumber:)) return enabled && (request.flags & JALIUM_AX_NAVIGABLE_TEXT);
    if (selector == @selector(accessibilityLineForIndex:) || selector == @selector(accessibilityRangeForLine:)
        || selector == @selector(accessibilityRangeForIndex:) || selector == @selector(accessibilityRangeForPosition:)
        || selector == @selector(accessibilityVisibleCharacterRange) || selector == @selector(accessibilityInsertionPointLineNumber))
        return (request.flags & JALIUM_AX_NAVIGABLE_TEXT) != 0;
    if (selector == @selector(accessibilityRTFForRange:) || selector == @selector(accessibilityStyleRangeForIndex:))
        return (request.flags & (JALIUM_AX_TEXT | JALIUM_AX_STYLED_TEXT)) == (JALIUM_AX_TEXT | JALIUM_AX_STYLED_TEXT)
            && !(request.flags & JALIUM_AX_PASSWORD);
    if (selector == @selector(accessibilitySelectedText) || selector == @selector(accessibilitySelectedTextRange)
        || selector == @selector(accessibilitySelectedTextRanges)
        || selector == @selector(accessibilityStringForRange:) || selector == @selector(accessibilityAttributedStringForRange:)
        || selector == @selector(accessibilityFrameForRange:) || selector == @selector(accessibilityNumberOfCharacters))
        return (request.flags & JALIUM_AX_TEXT) != 0;
    NSString* name = NSStringFromSelector(selector);
    // NSAccessibilityElement supplies generic setters/actions. Only advertise
    // the operations above that actually reach the managed control provider.
    if ([name hasPrefix:@"accessibilityPerform"] || [name hasPrefix:@"setAccessibility"]) return NO;
    return [super isAccessibilitySelectorAllowed:selector];
}
@end

void jalium_apple_window_set_accessibility(JaliumPlatformWindow* window,
    JaliumAccessibilityCallback callback, void* userData)
{
    if (!NSThread.isMainThread || !window || !IsLiveWindow(window)) return;
    [window->view.accessibilityBridge detach];
    window->view.accessibilityBridge = nil;
    if (callback && !window->destroying) {
        auto* bridge = [JaliumAppleAccessibilityBridge new];
        bridge.view = window->view; bridge.callback = callback; bridge.userData = userData;
        window->view.accessibilityBridge = bridge;
    }
}
void jalium_apple_window_notify_accessibility(JaliumPlatformWindow* window, uint64_t nodeId, int32_t notification)
{
    if (!NSThread.isMainThread || !window || !IsLiveWindow(window) || window->destroying) return;
    JaliumAppleView* view = window->view;
    NSWindow* nativeWindow = window->window;
    JaliumAppleAccessibilityBridge* bridge = view.accessibilityBridge;
    const auto hasOriginalContent = [&] {
        return bridge.view == view && view.window == nativeWindow && [bridge hasAccessibleContent];
    };
    if (!hasOriginalContent()) return;
    if (notification == JALIUM_AX_NOTIFY_LAYOUT) {
        for (NSNumber* key in bridge.elements.allKeys) {
            JaliumAppleAccessibilityElement* peer = bridge.elements[key];
            if (!peer) continue;
            JaliumAccessibilityRequest request{};
            request.nodeId = key.unsignedLongLongValue; request.operation = JALIUM_AX_ATTACHED;
            const BOOL attached = [bridge query:&request];
            // A provider can hide, close or replace this Window in the query.
            // Losing access to its tree is not evidence that a node detached.
            if (!hasOriginalContent()) return;
            if (!attached && bridge.elements[key] == peer) {
                [bridge.elements removeObjectForKey:key];
                [peer retire];
                if (!hasOriginalContent()) return;
            }
        }
    }
    id element = [bridge element:nodeId];
    if (!element || !hasOriginalContent()) return;
    NSAccessibilityNotificationName name = nil;
    switch (notification) {
        case JALIUM_AX_NOTIFY_FOCUS:
            if (nativeWindow.keyWindow) name = NSAccessibilityFocusedUIElementChangedNotification;
            break;
        case JALIUM_AX_NOTIFY_VALUE: name = NSAccessibilityValueChangedNotification; break;
        case JALIUM_AX_NOTIFY_SELECTION: name = NSAccessibilitySelectedTextChangedNotification; break;
        case JALIUM_AX_NOTIFY_LAYOUT: name = NSAccessibilityLayoutChangedNotification; break;
        case JALIUM_AX_NOTIFY_TITLE: name = NSAccessibilityTitleChangedNotification; break;
    }
    if (name) NSAccessibilityPostNotification(element, name);
}
