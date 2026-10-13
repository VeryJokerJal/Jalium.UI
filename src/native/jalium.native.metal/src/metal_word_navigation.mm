#include "metal_word_navigation.h"
#import <TargetConditionals.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#include <algorithm>

// Linguistic segmentation comes from TextKit. Only geometry is supplied here:
// it is the same immutable CoreText frame used to draw and hit-test the editor.
@interface JaliumParagraphNavigationSource : NSObject <NSTextSelectionDataSource> {
    NSTextContentStorage* _content;
    NSTextLayoutManager* _linguistic;
    std::vector<jalium::MetalWordNavigationRow> _rows;
}
- (instancetype)initWithText:(NSString*)text rows:(std::vector<jalium::MetalWordNavigationRow>)rows;
- (NSInteger)index:(id<NSTextLocation>)location;
- (id<NSTextLocation>)location:(NSUInteger)index;
@end

@implementation JaliumParagraphNavigationSource
- (instancetype)initWithText:(NSString*)text rows:(std::vector<jalium::MetalWordNavigationRow>)rows {
    if ((self = [super init])) {
        _rows = std::move(rows);
        _content = [NSTextContentStorage new];
        _linguistic = [NSTextLayoutManager new];
        [_content addTextLayoutManager:_linguistic];
        _linguistic.textContainer = [[NSTextContainer alloc] initWithSize:NSMakeSize(1000000, 1000000)];
        _content.attributedString = [[NSAttributedString alloc] initWithString:text];
    }
    return self;
}
- (NSTextRange*)documentRange { return _content.documentRange; }
- (NSInteger)index:(id<NSTextLocation>)location {
    return [_content offsetFromLocation:_content.documentRange.location toLocation:location];
}
- (id<NSTextLocation>)location:(NSUInteger)index {
    return [_content locationFromLocation:_content.documentRange.location withOffset:index];
}
- (NSTextRange*)range:(NSUInteger)start end:(NSUInteger)end {
    return [[NSTextRange alloc] initWithLocation:[self location:start] endLocation:[self location:end]];
}
- (const jalium::MetalWordNavigationRow*)rowAt:(NSInteger)index {
    for (auto row = _rows.rbegin(); row != _rows.rend(); ++row)
        if (index >= row->start) return &*row;
    return _rows.empty() ? nullptr : &_rows.front();
}
- (void)enumerateSubstringsFromLocation:(id<NSTextLocation>)location options:(NSStringEnumerationOptions)options
    usingBlock:(void (NS_NOESCAPE ^)(NSString*, NSTextRange*, NSTextRange*, BOOL*))block {
    if ((options & 0xff) != NSStringEnumerationByLines) {
        [_linguistic enumerateSubstringsFromLocation:location options:options usingBlock:block];
        return;
    }
    NSInteger index = [self index:location];
    BOOL stop = NO;
    auto emit = [&](size_t i) {
        const auto& row = _rows[i];
        NSUInteger end = i + 1 < _rows.size() ? _rows[i + 1].start : _content.attributedString.length;
        NSString* substring = (options & NSStringEnumerationSubstringNotRequired) ? nil :
            [_content.attributedString.string substringWithRange:NSMakeRange(row.start, row.end - row.start)];
        block(substring, [self range:row.start end:row.end], [self range:row.start end:end], &stop);
    };
    if (options & NSStringEnumerationReverse) {
        for (size_t i = _rows.size(); i > 0 && !stop; --i)
            if (_rows[i - 1].start < index) emit(i - 1);
    } else {
        for (size_t i = 0; i < _rows.size() && !stop; ++i) {
            NSUInteger end = i + 1 < _rows.size() ? _rows[i + 1].start : _content.attributedString.length;
            if (index < end || (index == end && i + 1 == _rows.size())) emit(i);
        }
    }
}
- (NSTextRange*)textRangeForSelectionGranularity:(NSTextSelectionGranularity)granularity
    enclosingLocation:(id<NSTextLocation>)location {
    if (granularity == NSTextSelectionGranularityLine) {
        auto row = [self rowAt:[self index:location]];
        return row ? [self range:row->start end:row->end] : nil;
    }
    return [_linguistic textRangeForSelectionGranularity:granularity enclosingLocation:location];
}
- (id<NSTextLocation>)locationFromLocation:(id<NSTextLocation>)location withOffset:(NSInteger)offset {
    return [_content locationFromLocation:location withOffset:offset];
}
- (NSInteger)offsetFromLocation:(id<NSTextLocation>)from toLocation:(id<NSTextLocation>)to {
    return [_content offsetFromLocation:from toLocation:to];
}
- (NSTextSelectionNavigationWritingDirection)baseWritingDirectionAtLocation:(id<NSTextLocation>)location {
    auto row = [self rowAt:[self index:location]];
    return row && row->direction >= 0 ? static_cast<NSTextSelectionNavigationWritingDirection>(row->direction) :
        [_linguistic baseWritingDirectionAtLocation:location];
}
- (void)enumerateCaretOffsetsInLineFragmentAtLocation:(id<NSTextLocation>)location
    usingBlock:(void (NS_NOESCAPE ^)(CGFloat, id<NSTextLocation>, BOOL, BOOL*))block {
    auto row = [self rowAt:[self index:location]];
    if (!row) return;
    BOOL stop = NO;
    for (const auto& caret : row->carets) {
        // NSTextSelectionDataSource identifies the character at both of its
        // edges. Our rendering carets instead store the insertion position
        // after a cluster at its trailing edge.
        NSUInteger character = caret.textPosition;
        if (caret.backwardAffinity && character > 0)
            character = [_content.attributedString.string rangeOfComposedCharacterSequenceAtIndex:character - 1].location;
        block(caret.x, [self location:character], !caret.backwardAffinity, &stop);
        if (stop) break;
    }
}
- (NSTextRange*)lineFragmentRangeForPoint:(CGPoint)point inContainerAtLocation:(id<NSTextLocation>)location {
    for (const auto& row : _rows)
        if (point.y >= row.y && point.y < row.y + row.height)
            return [self range:row.start end:row.end];
    return nil;
}
@end
#endif

namespace jalium {
JaliumResult NavigateMetalWords(const uint16_t* text, uint32_t length,
    const std::vector<uint64_t>& identities, std::vector<MetalWordNavigationRow> rows,
    uint32_t position, uint32_t start, uint32_t selectionLength,
    int32_t direction, bool backwardAffinity, JaliumParagraphCaret* result)
{
#if TARGET_OS_OSX
    @autoreleasepool { @try {
        struct Context {
            std::vector<uint64_t> identities;
            NSString* text;
            JaliumParagraphNavigationSource* source;
            NSTextSelectionNavigation* navigation;
        };
        // Thread-owned, bounded to one current immutable document. New native
        // paragraph identities invalidate this after content/style/width edits.
        static thread_local Context cached;
        Context transient;
        Context& context = length <= 65536 ? cached : transient;
        NSString* string = [[NSString alloc] initWithCharacters:text length:length];
        if (context.identities != identities || ![context.text isEqualToString:string]) {
            context.identities = identities; context.text = string;
            context.source = [[JaliumParagraphNavigationSource alloc] initWithText:string rows:std::move(rows)];
            context.navigation = [[NSTextSelectionNavigation alloc] initWithDataSource:context.source];
            context.navigation.allowsNonContiguousRanges = NO;
        }
        auto snap = [&](NSUInteger index, bool forward) {
            if (index >= length) return static_cast<NSUInteger>(length);
            NSRange cluster = [string rangeOfComposedCharacterSequenceAtIndex:index];
            return index == cluster.location || !forward ? cluster.location : NSMaxRange(cluster);
        };
        position = snap(position, false);
        if (!position && !selectionLength) backwardAffinity = false;
        NSUInteger end = snap(start + selectionLength, true);
        start = snap(start, false);
        NSTextRange* range = selectionLength ? [[NSTextRange alloc] initWithLocation:[context.source location:start]
            endLocation:[context.source location:end]] : [[NSTextRange alloc] initWithLocation:[context.source location:position]];
        NSTextSelection* selection = [[NSTextSelection alloc] initWithRange:range
            affinity:backwardAffinity ? NSTextSelectionAffinityUpstream : NSTextSelectionAffinityDownstream
            granularity:NSTextSelectionGranularityCharacter];
        NSTextSelection* destination = [context.navigation destinationSelectionForTextSelection:selection
            direction:static_cast<NSTextSelectionNavigationDirection>(direction)
            destination:NSTextSelectionNavigationDestinationWord extending:NO confined:NO];
        NSInteger index = destination ? [context.source index:destination.textRanges.firstObject.location] : position;
        if (index < 0 || index > length) return JALIUM_ERROR_UNKNOWN;
        result->textPosition = snap(index, index >= position);
        result->backwardAffinity = result->textPosition == 0 ? 0 :
            destination ? destination.affinity == NSTextSelectionAffinityUpstream : backwardAffinity;
        result->x = 0;
        return JALIUM_OK;
    } @catch (NSException*) { return JALIUM_ERROR_UNKNOWN; } }
#else
    (void)text; (void)length; (void)identities; (void)rows; (void)position; (void)start;
    (void)selectionLength; (void)direction; (void)backwardAffinity; (void)result;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
}
