static void RunSelectionCollectionCases()
{
    int completed = 0;
    for (int titlebar = 0; titlebar < 2; ++titlebar)
        for (int role : {8, 23, 28, 36})
            for (int mutation = 0; mutation < 4; ++mutation) {
                @autoreleasepool {
                    JaliumWindowParams params{};
                    params.title = reinterpret_cast<const uint16_t*>(u"AX selection collection contract");
                    params.x = params.y = JALIUM_DEFAULT_POS; params.width = 320; params.height = 220;
                    params.style = JALIUM_WINDOW_STYLE_CLOSABLE | JALIUM_WINDOW_STYLE_RESIZABLE |
                        (titlebar ? JALIUM_WINDOW_STYLE_TITLEBAR : JALIUM_WINDOW_STYLE_BORDERLESS);
                    auto* window = jalium_window_create(&params);
                    Check(window != nullptr, "create selection collection Window");
                    TableSelectionFixture fixture{role, false}; fixture.writable = true;
                    jalium_apple_window_set_accessibility(window, TableSelectionQuery<JaliumAccessibilityOperation>, &fixture);
                    jalium_apple_window_show(window, 0);
                    NSView* view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window));
                    NSAccessibilityElement* root = view.accessibilityChildren.firstObject;
                    NSAccessibilityElement* container = root.accessibilityChildren.firstObject;
                    NSAccessibilityElement* row = container.accessibilityChildren.firstObject;
                    const bool rows = role != 8;
                    auto set = [&](NSArray* selection) {
                        if (rows) container.accessibilitySelectedRows = selection;
                        else container.accessibilitySelectedChildren = selection;
                    };
                    SEL setter = rows ? @selector(setAccessibilitySelectedRows:) : @selector(setAccessibilitySelectedChildren:);
                    SEL other = rows ? @selector(setAccessibilitySelectedChildren:) : @selector(setAccessibilitySelectedRows:);
                    Check([container isAccessibilitySelectorAllowed:setter], "qualified container advertises its native batch setter");
                    Check(![container isAccessibilitySelectorAllowed:other], "container does not advertise the wrong selection relationship");
                    set(@[row]); Check(fixture.selected && fixture.writes == 1, "native array setter commits one selected row");
                    set(@[row, row]); Check(fixture.selected && fixture.writes == 2, "duplicate row references retain exact identity");
                    set(@[row, root]); Check(fixture.selected && fixture.writes == 2, "foreign member cannot partly change selection");
                    fixture.rowEnabled = false; set(@[row]);
                    Check(fixture.writes == 2, "disabled row cannot enter selection writes");
                    fixture.rowEnabled = true;
                    set(@[]); Check(!fixture.selected && fixture.writes == 3, "empty native array clears selection");
                    bool fired = false;
                    if (mutation == 0) {
                        fixture.writable = false;
                        Check(![container isAccessibilitySelectorAllowed:setter], "read-only container does not advertise writes");
                    } else {
                        fixture.duringQuery = [&](JaliumAccessibilityRequest* request) {
                            if (fired || request->operation != JALIUM_AX_INFO || request->nodeId != 3) return;
                            fired = true;
                            if (mutation == 1) jalium_window_hide(window);
                            else if (mutation == 2) jalium_apple_window_set_accessibility(window, nullptr, nullptr);
                            else { jalium_window_destroy(window); window = nullptr; }
                        };
                    }
                    set(@[row]); Check(!fixture.selected && fixture.writes == 3,
                        "read-only or reentrantly retired row cannot commit an obsolete batch");
                    fixture.duringQuery = nullptr;
                    if (window) jalium_window_destroy(window);
                    completed++;
                }
            }
    std::printf("macOS AX selection collections: %d/32 passed\n", completed);
}
