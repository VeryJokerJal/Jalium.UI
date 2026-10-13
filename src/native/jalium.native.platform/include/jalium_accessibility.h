#pragma once

#include <stdint.h>

// Synchronous, main-thread requests. Strings use caller-owned UTF-16 buffers;
// a null buffer first requests the length. Geometry is in content-view points
// with a top-left origin, never physical Retina pixels or global screen space.
enum JaliumAccessibilityOperation {
    JALIUM_AX_INFO, JALIUM_AX_CHILD, JALIUM_AX_STRING, JALIUM_AX_FOCUS,
    JALIUM_AX_HIT_TEST, JALIUM_AX_ACTION, JALIUM_AX_SET_VALUE,
    JALIUM_AX_TEXT_SELECTION, JALIUM_AX_SET_TEXT_SELECTION, JALIUM_AX_TEXT_BOUNDS,
    JALIUM_AX_ATTACHED, JALIUM_AX_WINDOW_BUTTON, JALIUM_AX_TEXT_NAVIGATION,
    JALIUM_AX_TEXT_STYLES, JALIUM_AX_TEXT_STYLE_RANGE,
    // Request-scoped ID snapshot: resultId is its token, childCount its size.
    // READ copies uint64_t IDs into text; textCapacity counts IDs, not UTF-16.
    JALIUM_AX_BEGIN_CHILDREN, JALIUM_AX_READ_CHILDREN, JALIUM_AX_RELEASE_CHILDREN
};
enum JaliumAccessibilityTextNavigation {
    JALIUM_AX_LINE_FOR_INDEX, JALIUM_AX_RANGE_FOR_INDEX, JALIUM_AX_RANGE_FOR_LINE,
    JALIUM_AX_RANGE_FOR_POSITION, JALIUM_AX_VISIBLE_TEXT_RANGE,
    JALIUM_AX_INSERTION_LINE, JALIUM_AX_SET_INSERTION_LINE, JALIUM_AX_REPLACE_SELECTION
};
enum JaliumAccessibilityWindowButton {
    JALIUM_AX_DEFAULT_BUTTON, JALIUM_AX_CANCEL_BUTTON,
    JALIUM_AX_CLOSE_BUTTON, JALIUM_AX_MINIMIZE_BUTTON, JALIUM_AX_ZOOM_BUTTON
};
enum JaliumAccessibilityString {
    JALIUM_AX_NAME, JALIUM_AX_HELP, JALIUM_AX_IDENTIFIER, JALIUM_AX_VALUE,
    JALIUM_AX_PLACEHOLDER
};
enum JaliumAccessibilityAction {
    JALIUM_AX_PRESS, JALIUM_AX_SET_FOCUS, JALIUM_AX_INCREMENT,
    JALIUM_AX_DECREMENT, JALIUM_AX_EXPAND, JALIUM_AX_COLLAPSE, JALIUM_AX_DESELECT
};
enum JaliumAccessibilityFlags {
    JALIUM_AX_ENABLED = 1 << 0, JALIUM_AX_FOCUSABLE = 1 << 1,
    JALIUM_AX_FOCUSED = 1 << 2, JALIUM_AX_PASSWORD = 1 << 3,
    JALIUM_AX_PRESSABLE = 1 << 4, JALIUM_AX_WRITABLE = 1 << 5,
    JALIUM_AX_RANGE = 1 << 6, JALIUM_AX_TOGGLE = 1 << 7,
    JALIUM_AX_SELECTED = 1 << 8, JALIUM_AX_EXPANDABLE = 1 << 9,
    JALIUM_AX_EXPANDED = 1 << 10, JALIUM_AX_TEXT = 1 << 11,
    JALIUM_AX_MULTILINE = 1 << 12, JALIUM_AX_DIALOG = 1 << 13,
    JALIUM_AX_MODAL = 1 << 14, JALIUM_AX_SELECTABLE = 1 << 15,
    JALIUM_AX_SELECTION = 1 << 16, JALIUM_AX_HAS_VALUE = 1 << 17,
    JALIUM_AX_CLOSE = 1 << 18, JALIUM_AX_MINIMIZE = 1 << 19, JALIUM_AX_ZOOM = 1 << 20,
    JALIUM_AX_NAVIGABLE_TEXT = 1 << 21, JALIUM_AX_EDITABLE_TEXT = 1 << 22,
    JALIUM_AX_OFFSCREEN = 1 << 23, JALIUM_AX_STYLED_TEXT = 1 << 24
};
// Values deliberately match AutomationControlType. Keep the native role map
// and managed enum contract covered by end-to-end host checks.
enum JaliumAccessibilityNotification {
    JALIUM_AX_NOTIFY_FOCUS, JALIUM_AX_NOTIFY_VALUE, JALIUM_AX_NOTIFY_SELECTION,
    JALIUM_AX_NOTIFY_LAYOUT, JALIUM_AX_NOTIFY_TITLE
};
typedef struct JaliumAccessibilityRequest {
    uint64_t nodeId;
    int32_t operation;
    int32_t index;
    uint64_t resultId;
    uint64_t parentId;
    int32_t role;
    uint32_t flags;
    int32_t childCount;
    int32_t textStart;
    int32_t textLength;
    double x, y, width, height;
    double value, minimum, maximum, step;
    uint16_t* text;
    int32_t textCapacity;
    int32_t textCount;
} JaliumAccessibilityRequest;

typedef int32_t (*JaliumAccessibilityCallback)(JaliumAccessibilityRequest* request, void* userData);
