using AppKit;
using Foundation;
using Jalium.UI;
using Jalium.UI.MacOS;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("macos15.0")]

JaliumMacApplication.Initialize();

if (args.Length == 0 && WindowWritingToolsChecks.TryRunBundled(out int bundledResult))
    return bundledResult;

if (args.Length == 1 && args[0].StartsWith("--window-monitor-notification-case=", StringComparison.Ordinal))
    return WindowMonitorNotificationChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-standalone-appearance-case=", StringComparison.Ordinal))
    return WindowStandaloneAppearanceChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-popup-viewport-case=", StringComparison.Ordinal))
    return WindowPopupViewportChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-combo-mouse-case=", StringComparison.Ordinal))
    return WindowComboMouseForwardingChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-dock-ownership-case=", StringComparison.Ordinal))
    return WindowDockOwnershipChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-dock-motion-case=", StringComparison.Ordinal))
    return WindowDockMotionChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-writing-tools-zero-inline-case=", StringComparison.Ordinal))
    return WindowWritingToolsZeroInlineChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-motion-notification-case=", StringComparison.Ordinal))
    return WindowMotionNotificationChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-mouse-release-case=", StringComparison.Ordinal))
    return WindowMouseReleaseOwnershipChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-popup-owner-case=", StringComparison.Ordinal))
    return WindowPopupOwnershipChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-embedded-object-case=", StringComparison.Ordinal))
    return WindowEmbeddedObjectChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-popup-drag-case=", StringComparison.Ordinal))
    return WindowPopupDragChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-writing-tools-partial-case=", StringComparison.Ordinal))
    return WindowWritingToolsStructureChecks.RunPartial(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-writing-tools-inline-case=", StringComparison.Ordinal))
    return WindowWritingToolsInlineChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-writing-tools-structure-case=", StringComparison.Ordinal))
    return WindowWritingToolsStructureChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-selection-transaction-case=", StringComparison.Ordinal))
    return WindowSelectionTransactionChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-placement-boundary-case=", StringComparison.Ordinal))
    return WindowPlacementBoundaryChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-occurrence-case=", StringComparison.Ordinal))
    return WindowOccurrenceChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-gesture-interleave-case=", StringComparison.Ordinal))
    return WindowGestureInterleaveChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-appearance-default-case=", StringComparison.Ordinal))
    return WindowAppearanceDefaultsChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-explicit-list-case=", StringComparison.Ordinal))
    return WindowSelectionNotificationChecks.RunExplicitOwnershipCase(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-appearance-case=", StringComparison.Ordinal))
    return WindowAppearanceLifetimeChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-keyboard-packet-case=", StringComparison.Ordinal))
    return WindowKeyboardPacketChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-popup-resource-case=", StringComparison.Ordinal))
    return WindowPopupResourceChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-deactivation-wheel-case=", StringComparison.Ordinal))
    return WindowDeactivationWheelChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-input-lifetime-case=", StringComparison.Ordinal))
    return WindowInputLifetimeChecks.Run(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-hosted-failure-case=", StringComparison.Ordinal))
    return WindowHostedFailureChecks.Run(args[0]);

if (args.Length == 1 && (args[0] == "--window-themes" || args[0].StartsWith("--window-theme-case=", StringComparison.Ordinal)))
    return WindowThemeModeChecks.Run(args[0]);

if (args.Length == 1 && args[0] == "--window-template-names")
    return WindowTemplateScopeChecks.Run();

if (args.Length == 1 && args[0].StartsWith("--window-item-lifetime-case=", StringComparison.Ordinal))
    return WindowSelectionNotificationChecks.RunItemLifetimeCase(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-combo-selection-case=", StringComparison.Ordinal))
    return WindowSelectionNotificationChecks.RunComboSelectionCase(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-item-content-case=", StringComparison.Ordinal))
    return WindowSelectionNotificationChecks.RunItemContentCase(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-virtualized-selection-case=", StringComparison.Ordinal))
    return WindowSelectionNotificationChecks.RunVirtualizedCase(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-selection-notifications-case=", StringComparison.Ordinal))
    return WindowSelectionNotificationChecks.RunCase(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-ax-notifications-case=", StringComparison.Ordinal))
    return WindowAXNotificationAcceptanceChecks.RunCase(args[0]);

if (args.Length == 1 && args[0].StartsWith("--window-file-panel-acceptance-case=", StringComparison.Ordinal))
    return WindowFilePanelAcceptanceChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-taskbar-software")
    return WindowTaskbarChecks.Run(software: true);
if (args.Length == 1 && args[0] == "--window-taskbar")
    return WindowTaskbarChecks.Run();

if (args.Length == 1 && args[0] == "--window-tablets-software")
    return WindowTabletChecks.Run(software: true);
if (args.Length == 1 && args[0] == "--window-tablets")
    return WindowTabletChecks.Run();

if (args.Length == 1 && args[0] == "--window-trackpad-gestures-software")
    return WindowTrackpadGestureChecks.Run(software: true);

if (args.Length == 1 && args[0] == "--window-trackpad-gestures")
    return WindowTrackpadGestureChecks.Run();

if (args.Length == 1 && args[0] == "--window-writing-tools-glyphs-software")
    return WindowWritingToolsGlyphChecks.Run(true);
if (args.Length == 1 && args[0] == "--window-writing-tools-software")
    return WindowWritingToolsChecks.Run(true);

if (args.Length == 1 && args[0] == "--window-writing-tools-glyphs")
    return WindowWritingToolsGlyphChecks.Run();

if (args.Length == 1 && args[0] == "--window-writing-tools")
    return WindowWritingToolsChecks.Run();

if (args.Length == 1 && args[0] == "--css-collapsed-borders")
{
    return CssCollapsedBorderChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-table-widths")
{
    return CssDocumentTableWidthChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-heights")
{
    return CssDocumentHeightChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-empty-table-grid")
{
    return CssEmptyTableGridChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-intrinsic-sizing")
{
    return CssDocumentIntrinsicSizingChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-hanging-whitespace")
{
    return CssHangingWhitespaceChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-line-height")
{
    return CssLineHeightChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-shared-justification")
{
    return CssSharedJustificationChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-tab-justification")
{
    return CssTabJustificationChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-paragraph-wrap")
{
    return CssParagraphWrapChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-inline-boxes")
{
    return CssInlineBoxChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-richtext-boxes")
{
    return CssRichTextBoxChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-placeholder")
{
    return CssWritingToolsChecks.RunPlaceholders();
}

if (args.Length == 1 && args[0] == "--css-placeholder-lifecycle")
{
    return CssWritingToolsChecks.RunPlaceholderLifecycle();
}

if (args.Length == 1 && args[0] == "--css-placeholder-gallery")
{
    return CssWritingToolsChecks.RunPlaceholderGallery();
}

if (args.Length == 1 && args[0] == "--css-background-text-clip")
{
    return CssWritingToolsChecks.RunBackgroundTextClip();
}

if (args.Length == 1 && args[0] == "--css-background-text-clip-scenes")
{
    return CssWritingToolsChecks.RunBackgroundTextClipScenes();
}

if (args.Length == 1 && args[0] == "--css-gradient-input-recording")
{
    return CssWritingToolsChecks.RunGradientRecordingInputs();
}

if (args.Length == 1 && args[0] == "--css-gradient-recording")
{
    return CssWritingToolsChecks.RunGradientRecording();
}

if (args.Length == 1 && args[0] == "--css-software-gradient-text")
{
    return CssWritingToolsChecks.RunSoftwareGradientText();
}

if (args.Length == 1 && args[0] == "--css-input-spacing")
{
    return CssWritingToolsChecks.RunInputSpacing();
}

if (args.Length == 1 && args[0] == "--css-input-spacing-selection")
{
    return CssWritingToolsChecks.RunInputSpacingSelection();
}

if (args.Length == 1 && args[0] == "--css-input-spacing-lifecycle")
{
    return CssWritingToolsChecks.RunInputSpacingLifecycle();
}

if (args.Length == 1 && args[0] == "--css-input-spacing-gallery")
{
    return CssWritingToolsChecks.RunInputSpacingGallery();
}

if (args.Length == 1 && args[0] == "--css-writing-tools")
{
    return CssWritingToolsChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-writing-tools-editing")
{
    return CssWritingToolsChecks.RunEditing();
}

if (args.Length == 1 && args[0] == "--css-document-list-markers")
{
    return CssWritingToolsChecks.RunDocumentListMarkers();
}

if (args.Length == 1 && args[0] == "--css-document-paint-refresh")
{
    return CssWritingToolsChecks.RunDocumentPaintRefresh();
}

if (args.Length == 1 && args[0] == "--css-font-numeric")
{
    return CssWritingToolsChecks.RunFontNumeric();
}

if (args.Length == 1 && args[0] == "--css-font-east-asian")
{
    return CssWritingToolsChecks.RunFontEastAsian();
}

if (args.Length == 1 && args[0] == "--css-font-caps")
{
    return CssWritingToolsChecks.RunFontCaps();
}

if (args.Length == 1 && args[0] == "--css-font-caps-boundaries")
{
    return CssWritingToolsChecks.RunFontCapsBoundaries();
}

if (args.Length == 1 && args[0] == "--css-shared-caps-context")
{
    return CssWritingToolsChecks.RunSharedCapsContext();
}

if (args.Length == 1 && args[0] == "--css-caps-combining-context")
{
    return CssWritingToolsChecks.RunCapsCombiningContext();
}

if (args.Length == 1 && args[0] == "--css-compositing")
{
    return CssWritingToolsChecks.RunCompositing();
}

if (args.Length == 1 && args[0] == "--css-geometry-transforms")
{
    return CssWritingToolsChecks.RunGeometryTransforms();
}

if (args.Length == 1 && args[0] == "--css-vector-effects")
{
    return CssWritingToolsChecks.RunVectorEffects();
}

if (args.Length == 1 && args[0] == "--css-svg-viewports")
{
    return CssWritingToolsChecks.RunSvgViewports();
}

if (args.Length == 1 && args[0] == "--css-shape-image-sizing")
{
    return CssWritingToolsChecks.RunShapeImageSizing();
}

if (args.Length == 1 && args[0] == "--css-first-letter")
{
    return CssWritingToolsChecks.RunFirstLetters();
}
if (args.Length == 1 && args[0] == "--css-first-line")
{
    return CssWritingToolsChecks.RunFirstLines();
}
if (args.Length == 1 && args[0] == "--css-initial-letter")
{
    return CssWritingToolsChecks.RunInitialLetters();
}
if (args.Length == 1 && args[0] == "--css-initial-letter-gallery")
{
    return CssWritingToolsChecks.RunInitialLetterGallery();
}
if (args.Length == 1 && args[0] == "--css-initial-letter-flow-gallery")
{
    return CssWritingToolsChecks.RunInitialLetterFlowGallery();
}
if (args.Length == 1 && args[0] == "--css-initial-letter-fragments")
{
    return CssWritingToolsChecks.RunInitialLetterFragments();
}
if (args.Length == 1 && args[0] == "--css-initial-letter-lifecycle")
{
    return CssWritingToolsChecks.RunInitialLetterLifecycle();
}
if (args.Length == 1 && args[0] == "--css-initial-letter-generated")
{
    return CssWritingToolsChecks.RunGeneratedInitialLetters();
}
if (args.Length == 1 && args[0] == "--css-initial-letter-block-flow")
{
    return CssWritingToolsChecks.RunInitialLetterBlockFlow();
}
if (args.Length == 1 && args[0] == "--css-selection-gallery")
{
    return CssWritingToolsChecks.RunSelectionGallery();
}
if (args.Length == 1 && args[0] == "--css-selection")
{
    return CssWritingToolsChecks.RunSelection();
}
if (args.Length == 1 && args[0] == "--css-selection-context")
{
    return CssWritingToolsChecks.RunSelectionContext();
}
if (args.Length == 1 && args[0] == "--css-editor-emphasis-gallery")
{
    return CssWritingToolsChecks.RunEditorEmphasisGallery();
}

if (args.Length == 1 && args[0] == "--css-input-foreground-gallery")
{
    return CssWritingToolsChecks.RunInputForegroundGallery();
}

if (args.Length == 1 && args[0] == "--css-theme-currentcolor")
{
    return CssWritingToolsChecks.RunThemeCurrentColor();
}

if (args.Length == 1 && args[0] == "--css-theme-currentcolor-gallery")
{
    return CssWritingToolsChecks.RunThemeCurrentColorGallery();
}

if (args.Length == 1 && args[0] == "--css-input-foreground")
{
    return CssWritingToolsChecks.RunInputForeground();
}

if (args.Length == 1 && args[0] == "--css-editor-emphasis")
{
    return CssWritingToolsChecks.RunEditorEmphasis();
}

if (args.Length == 1 && args[0] == "--css-text-emphasis")
{
    return CssWritingToolsChecks.RunTextEmphasis();
}

if (args.Length == 1 && args[0] == "--css-text-emphasis-gallery")
{
    return CssWritingToolsChecks.RunTextEmphasisGallery();
}

if (args.Length == 1 && args[0] == "--css-selection-fallback")
{
    return CssWritingToolsChecks.RunSelectionFallback();
}
if (args.Length == 1 && args[0] == "--css-selection-context-gallery")
{
    return CssWritingToolsChecks.RunSelectionContextGallery();
}
if (args.Length == 1 && args[0] == "--css-initial-letter-floats")
{
    return CssWritingToolsChecks.RunInitialLetterFloats();
}

if (args.Length == 1 && args[0] == "--css-image-intrinsic")
{
    return CssWritingToolsChecks.RunImageIntrinsic();
}

if (args.Length == 1 && args[0] == "--css-radial-cones")
{
    return CssWritingToolsChecks.RunRadialCones();
}

if (args.Length == 1 && args[0] == "--css-gradient-transforms")
{
    return CssWritingToolsChecks.RunGradientTransforms();
}

if (args.Length == 1 && args[0] == "--css-gradient-interpolation")
{
    return CssWritingToolsChecks.RunGradientInterpolation();
}

if (args.Length == 1 && args[0] == "--css-svg-markers")
{
    return CssWritingToolsChecks.RunSvgMarkers();
}

if (args.Length == 1 && args[0] == "--css-svg-text-images")
{
    return CssWritingToolsChecks.RunSvgTextImages();
}

if (args.Length == 1 && args[0] == "--css-svg-text-paint")
{
    return CssWritingToolsChecks.RunSvgTextPaint();
}

if (args.Length == 1 && args[0] == "--css-svg-strokes")
{
    return CssWritingToolsChecks.RunSvgStrokes();
}

if (args.Length == 1 && args[0] == "--css-svg-styles")
{
    return CssWritingToolsChecks.RunSvgStyles();
}

if (args.Length == 1 && args[0] == "--css-svg-masks")
{
    return CssWritingToolsChecks.RunSvgMasks();
}

if (args.Length == 1 && args[0] == "--css-mask-borders")
{
    return CssWritingToolsChecks.RunMaskBorders();
}

if (args.Length == 1 && args[0] == "--css-masks")
{
    return CssWritingToolsChecks.RunMasks();
}

if (args.Length == 1 && args[0] == "--css-font-face-metrics")
{
    return CssWritingToolsChecks.RunFontFaceMetrics();
}

if (args.Length == 1 && args[0] == "--css-font-size-adjust")
{
    return CssWritingToolsChecks.RunFontSizeAdjust();
}

if (args.Length == 1 && args[0] == "--css-font-optical")
{
    return CssWritingToolsChecks.RunFontOpticalSizing();
}

if (args.Length == 1 && args[0] == "--css-font-variations")
{
    return CssWritingToolsChecks.RunFontVariations();
}

if (args.Length == 1 && args[0] == "--css-font-feature-priority")
{
    return CssWritingToolsChecks.RunFontFeaturePriority();
}

if (args.Length == 1 && args[0] == "--css-font-emoji")
{
    return CssWritingToolsChecks.RunFontEmoji();
}

if (args.Length == 1 && args[0] == "--css-font-position")
{
    return CssWritingToolsChecks.RunFontPosition();
}

if (args.Length == 1 && args[0] == "--css-font-caps-synthesis")
{
    return CssWritingToolsChecks.RunFontCapsSynthesis();
}

if (args.Length == 1 && args[0] == "--css-font-caps-fallback")
{
    return CssWritingToolsChecks.RunFontCapsFallback();
}

if (args.Length == 1 && args[0] == "--css-font-alternates")
{
    return CssWritingToolsChecks.RunFontAlternates();
}

if (args.Length == 1 && args[0] == "--css-font-width-animation")
{
    return CssWritingToolsChecks.RunFontWidthAnimation();
}

if (args.Length == 1 && args[0] == "--css-system-fonts")
{
    return CssWritingToolsChecks.RunSystemFonts();
}

if (args.Length == 1 && args[0] == "--css-shaped-spacing")
{
    return CssWritingToolsChecks.RunShapedSpacing();
}

if (args.Length == 1 && args[0] == "--css-shared-shaping")
{
    return CssWritingToolsChecks.RunSharedShaping();
}

if (args.Length == 1 && args[0] == "--css-word-shaping")
{
    return CssWritingToolsChecks.RunWordShaping();
}

if (args.Length == 1 && args[0] == "--css-contextual-shaping")
{
    return CssWritingToolsChecks.RunContextualShaping();
}

if (args.Length == 1 && args[0] == "--css-justified-shaping")
{
    return CssWritingToolsChecks.RunJustifiedShaping();
}

if (args.Length == 1 && args[0] == "--css-percentage-spacing")
{
    return CssWritingToolsChecks.RunPercentageSpacing();
}

if (args.Length == 1 && args[0] == "--css-font-ligatures")
{
    return CssWritingToolsChecks.RunFontLigatures();
}

if (args.Length == 1 && args[0] == "--css-font-feature-settings")
{
    return CssWritingToolsChecks.RunFontFeatureSettings();
}

if (args.Length == 1 && args[0] == "--css-document-box-formatters")
    return CssDocumentBoxFormatterChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-document-table-contents")
    return CssDocumentTableContentsChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-document-contents")
    return CssDocumentContentsChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-document-display")
    return CssDocumentDisplayChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-document-line-clamp")
    return CssDocumentLineClampChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-line-clamp")
{
    return CssLineClampChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-calc-size")
{
    return CssCalcSizeChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-mixed-axis-sizing")
{
    return CssMixedAxisSizingChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-cyclic-percentage-sizing")
{
    return CssCyclicPercentageSizingChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-intrinsic-size-values")
{
    return CssIntrinsicSizeValueChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-flex-intrinsic-sizing")
{
    return CssFlexIntrinsicSizingChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-flex-item-sizing")
{
    return CssFlexItemSizingChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-auto-height")
{
    return CssTableAutoHeightChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-grid-normalization")
{
    return CssTableGridNormalizationChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-row-group-order")
{
    return CssTableRowGroupOrderChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-page-text-geometry")
{
    return CssPageTextGeometryChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-nested-table-intrinsics")
{
    return CssNestedTableIntrinsicChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-colspan-widths")
{
    return CssTableColspanWidthChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-percent-columns")
{
    return CssTablePercentColumnChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-row-group-heights")
{
    return CssTableRowGroupHeightChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-row-heights")
{
    return CssTableRowHeightChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-cell-heights")
{
    return CssDocumentCellHeightChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-root-boxes")
{
    return CssDocumentRootBoxChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-column-heights")
{
    return CssDocumentColumnHeightChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-boxes")
{
    return CssDocumentBoxChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-fragment-capacities")
{
    return CssDocumentFragmentCapacityChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-nested-document-columns")
{
    return CssNestedDocumentColumnChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-columns")
{
    return CssDocumentColumnChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-multicolumn")
{
    return CssMultiColumnChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-visibility")
{
    return CssTableVisibilityChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-captions")
{
    return CssTableCaptionChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-table-formatting")
{
    return CssTableFormattingChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-document-tables")
{
    return CssDocumentTableChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-pagination")
{
    return CssPaginationChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-flow-viewers")
{
    return CssFlowDocumentViewerChecks.RunAll();
}

if (args.Length == 1 && args[0] == "--css-rich-document-images")
    return CssRichDocumentImageChecks.RunAll();
if (args.Length == 1 && args[0] == "--css-border-image-inline")
    return CssBorderImageInlineChecks.RunAll();
if (args.Length == 1 && args[0] == "--css-border-image")
    return CssBorderImageChecks.RunAll();
if (args.Length == 1 && args[0] == "--css-image-annotations")
    return CssImageAnnotationChecks.RunAll();
if (args.Length == 1 && args[0] == "--css-cross-fade")
    return CssCrossFadeChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-contents")
    return CssContentsChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-image-content")
    return CssImageContentChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-image-set")
    return CssImageSetChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-image-shapes")
    return CssImageShapeChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-float-shapes")
    return CssFloatShapeChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-inline-block-descendants")
    return CssInlineBlockDescendantChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-nested-inline")
    return CssNestedInlineChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-inline-floats")
    return CssInlineFloatChecks.RunAll();
if (args.Length == 1 && args[0] == "--css-shared-inline")
    return CssSharedInlineChecks.RunAll();
if (args.Length == 1 && args[0] == "--css-inline-fragments")
    return CssInlineFragmentChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-text-content")
    return CssTextContentChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-content-hosts")
    return CssContentHostChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-list-markers")
    return CssListMarkerChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-counter-styles")
    return CssCounterStyleChecks.RunAll();

if (args.Length == 1 && args[0] == "--css-counters")
    return CssCounterChecks.RunAll();

if (args.Length == 1 && args[0] == "--window-notifications-observe")
    return WindowNotificationChecks.Observe(false);
if (args.Length == 1 && args[0] == "--window-notifications-observe-custom")
    return WindowNotificationChecks.Observe(true);

if (args.Length == 1 && args[0] == "--window-virtualized-content")
    return WindowVirtualizationChecks.Run();
if (args.Length == 1 && args[0] == "--window-virtualized-content-observe")
    return WindowVirtualizationChecks.Observe(false);
if (args.Length == 1 && args[0] == "--window-virtualized-content-observe-custom")
    return WindowVirtualizationChecks.Observe(true);

if (args.Length == 1 && args[0] == "--window-attached-binding")
    return WindowAttachedBindingChecks.Run();
if (args.Length == 1 && args[0] == "--window-attached-binding-observe")
    return WindowAttachedBindingChecks.Observe(false);
if (args.Length == 1 && args[0] == "--window-attached-binding-observe-custom")
    return WindowAttachedBindingChecks.Observe(true);

if (args.Length == 1 && args[0] == "--window-bitmap-dpi")
    return WindowBitmapDpiChecks.Run();
if (args.Length == 1 && args[0] == "--window-bitmap-dpi-observe")
    return WindowBitmapDpiChecks.Observe(false);
if (args.Length == 1 && args[0] == "--window-bitmap-dpi-observe-custom")
    return WindowBitmapDpiChecks.Observe(true);

if (args.Length == 1 && args[0] == "--window-content-scroll")
    return WindowPopupWheelChecks.Run(directContent: true);
if (args.Length == 1 && args[0] == "--window-content-scroll-observe")
    return WindowPopupWheelChecks.Observe(false, directContent: true);
if (args.Length == 1 && args[0] == "--window-content-scroll-observe-custom")
    return WindowPopupWheelChecks.Observe(true, directContent: true);

if (args.Length == 1 && args[0] == "--window-popup-wheel")
    return WindowPopupWheelChecks.Run();
if (args.Length == 1 && args[0] == "--window-popup-wheel-observe")
    return WindowPopupWheelChecks.Observe(false);
if (args.Length == 1 && args[0] == "--window-popup-wheel-observe-custom")
    return WindowPopupWheelChecks.Observe(true);

if (args.Length == 1 && args[0] == "--window-clipboard-files")
    return WindowClipboardFileChecks.Run();
if (args.Length == 1 && args[0] == "--window-clipboard-files-observe")
    return WindowClipboardFileChecks.Observe(false);
if (args.Length == 1 && args[0] == "--window-clipboard-files-observe-custom")
    return WindowClipboardFileChecks.Observe(true);

if (args.Length == 1 && args[0] == "--window-message-box")
    return WindowMessageBoxChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-message-box-case=", StringComparison.Ordinal))
    return WindowMessageBoxChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-message-box-observe")
    return WindowMessageBoxChecks.Observe();

if (args.Length == 1 && args[0] == "--window-file-dialog-options")
    return WindowFileDialogOptionsChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-file-dialog-options-case=", StringComparison.Ordinal))
    return WindowFileDialogOptionsChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-file-dialog-validation")
    return WindowFileDialogValidationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-file-dialog-validation-case=", StringComparison.Ordinal))
    return WindowFileDialogValidationChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-file-dialogs")
    return WindowFileDialogChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-file-dialog-case=", StringComparison.Ordinal))
    return WindowFileDialogChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--text-font-transforms")
    return TextAccessibilityStyleChecks.RunFontTransforms();
if (args.Length == 1 && args[0].StartsWith("--text-font-transform-case=", StringComparison.Ordinal))
    return TextAccessibilityStyleChecks.RunFontTransformCase(args[0]);

if (args.Length == 1 && args[0] == "--text-font-matching")
    return TextAccessibilityStyleChecks.RunFontMatching();
if (args.Length == 1 && args[0].StartsWith("--text-font-matching-case=", StringComparison.Ordinal))
    return TextAccessibilityStyleChecks.RunFontMatchingCase(args[0]);

if (args.Length == 1 && args[0] == "--text-accessibility-styles")
    return TextAccessibilityStyleChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-grapheme-keys")
    return WindowGraphemeKeyChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-grapheme-key-case=", StringComparison.Ordinal))
    return WindowGraphemeKeyChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--text-accessibility-style-case=", StringComparison.Ordinal))
    return TextAccessibilityStyleChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--accessibility-clipping")
    return AccessibilityClippingChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--accessibility-clipping-case=", StringComparison.Ordinal))
    return AccessibilityClippingChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-drag-representations")
    return WindowDragRepresentationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-drag-representation-case=", StringComparison.Ordinal))
    return WindowDragRepresentationChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--text-accessibility-appkit-probe")
    return TextAccessibilityNavigationChecks.ProbeAppKit();
if (args.Length == 1 && args[0] == "--text-accessibility-navigation")
    return TextAccessibilityNavigationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--text-accessibility-navigation-case=", StringComparison.Ordinal))
    return TextAccessibilityNavigationChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--path-rendering")
    return PathRenderingChecks.RunAll();
if (args.Length == 1 && args[0] == "--subpixel-motion")
    return SubpixelMotionChecks.RunAll();

if (args.Length == 1 && args[0] == "--window-visibility")
    return WindowVisibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-visibility-case=", StringComparison.Ordinal))
    return WindowVisibilityChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-opacity")
    return WindowOpacityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-opacity-case=", StringComparison.Ordinal))
    return WindowOpacityChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-background-alpha")
    return WindowBackgroundAlphaChecks.RunAll();

if (args.Length == 1 && args[0] == "--window-font")
    return WindowFontChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-font-case=", StringComparison.Ordinal))
    return WindowFontChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-editing-actions")
    return WindowEditingActionChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-editing-actions-case=", StringComparison.Ordinal))
    return WindowEditingActionChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-text-context-menu")
    return WindowTextContextMenuChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-text-context-menu-case=", StringComparison.Ordinal))
    return WindowTextContextMenuChecks.RunCase(args[0]);

if (args.Length == 1 && args[0] == "--window-editing-menu")
    return WindowEditingMenuChecks.RunAll();
if (args.Length == 1 && args[0] == "--editor-accessibility")
    return EditControlAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--editor-accessibility-case=", StringComparison.Ordinal))
    return EditControlAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-editing-menu-case=", StringComparison.Ordinal))
    return WindowEditingMenuChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-text-navigation")
    return WindowTextNavigationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-text-navigation-case=", StringComparison.Ordinal))
    return WindowTextNavigationChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-tab-focus")
    return WindowTabFocusChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-input-boundaries")
    return WindowInputBoundaryChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-input-boundary-case=", StringComparison.Ordinal))
    return WindowInputBoundaryChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-tab-focus-case=", StringComparison.Ordinal))
    return WindowTabFocusChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-word-navigation")
    return WindowWordNavigationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-word-navigation-case=", StringComparison.Ordinal))
    return WindowWordNavigationChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-lifecycle")
    return WindowLifecycleChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-minimize-transition")
    return WindowMinimizeTransitionChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-minimize-transition-case=", StringComparison.Ordinal))
    return WindowMinimizeTransitionChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-fullscreen-failure")
    return WindowFullScreenFailureChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-fullscreen-failure-case=", StringComparison.Ordinal))
    return WindowFullScreenFailureChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-startup-location")
    return WindowStartupLocationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-startup-location-case=", StringComparison.Ordinal))
    return WindowStartupLocationChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-system-menu")
    return WindowSystemMenuChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-system-menu-case=", StringComparison.Ordinal))
    return WindowSystemMenuChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-application-menu")
    return WindowApplicationMenuChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-application-menu-case=", StringComparison.Ordinal))
    return WindowApplicationMenuChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-application-activation")
    return WindowApplicationActivationChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-application-activation-case=", StringComparison.Ordinal))
    return WindowApplicationActivationChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-reopen")
    return WindowReopenChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-reopen-foreground")
    return WindowReopenForegroundChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-reopen-foreground-case=", StringComparison.Ordinal))
    return WindowReopenForegroundChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-quit")
    return WindowQuitChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-session-ending")
    return WindowSessionEndingChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-session-ending-case=", StringComparison.Ordinal))
    return WindowSessionEndingChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-native-quit")
    return WindowNativeQuitChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-native-quit-case=", StringComparison.Ordinal))
    return WindowNativeQuitChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-quit-case=", StringComparison.Ordinal))
    return WindowQuitChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-reopen-case=", StringComparison.Ordinal))
    return WindowReopenChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--accessibility")
    return AccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--accessibility-case=", StringComparison.Ordinal))
    return AccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--accessibility-semantics")
    return AccessibilitySemanticsChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--accessibility-semantics-case=", StringComparison.Ordinal))
    return AccessibilitySemanticsChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-accessibility")
    return WindowAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-accessibility-case=", StringComparison.Ordinal))
    return WindowAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-buttons-accessibility")
    return WindowButtonAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-buttons-accessibility-case=", StringComparison.Ordinal))
    return WindowButtonAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-caption-accessibility")
    return WindowCaptionAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0].StartsWith("--window-caption-accessibility-case=", StringComparison.Ordinal))
    return WindowCaptionAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0] == "--window-property-accessibility")
    return WindowPropertyAccessibilityChecks.RunAll();
if (args.Length == 1 && args[0] == "--window-property-activation-accessibility")
    return WindowPropertyAccessibilityChecks.RunActivationAll();
if (args.Length == 1 && args[0].StartsWith("--window-property-accessibility-case=", StringComparison.Ordinal))
    return WindowPropertyAccessibilityChecks.RunCase(args[0]);
if (args.Length == 1 && args[0].StartsWith("--window-case=", StringComparison.Ordinal))
    return WindowLifecycleChecks.RunCase(args[0]);

// AppKit menu contracts run on the process main thread without displaying or
// activating any window. The native Window suite covers desktop transitions.
const int checkCount = 9;
if (args.Length == 0)
{
    // AppKit retains its registered Windows menu for the application's lifetime.
    // Each fixture needs a fresh NSApplication, so run cases in separate processes.
    int failedCases = 0;
    for (int index = 0; index < checkCount; index++)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add($"--menu-case={index}");
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(30_000))
        {
            process.Kill();
            failedCases++;
            Console.Error.WriteLine($"FAIL: menu case {index} timed out");
        }
        else if (process.ExitCode != 0) failedCases++;
    }
    Console.WriteLine($"macOS host menu checks: {checkCount - failedCases}/{checkCount} passed");
    return failedCases == 0 ? 0 : 1;
}
if (args.Length != 1 || !args[0].StartsWith("--menu-case=", StringComparison.Ordinal)
    || !int.TryParse(args[0][12..], out int caseIndex) || caseIndex < 0 || caseIndex >= checkCount)
{
    Console.Error.WriteLine("Expected --menu-case=0 through --menu-case=8.");
    return 2;
}
JaliumMacApplication.Initialize();

var application = NSApplication.SharedApplication;
application.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
using var host = new MenuTestDelegate();
int failures = 0;
var checks = new (string Name, Action Run)[]
{
    ("null main menu gets Quit and Window actions", () =>
    {
        Reset(null);
        host.Configure(application);
        RequireQuit();
        RequireWindowActions();
    }),
    ("empty main menu gets an application menu", () =>
    {
        var menu = new NSMenu();
        Reset(menu);
        host.Configure(application);
        Require(application.MainMenu!.Handle == menu.Handle, "main menu was replaced");
        RequireQuit();
        RequireWindowActions();
    }),
    ("empty application submenu is filled and other menus survive", () =>
    {
        var appMenu = new NSMenu("Application");
        var fileMenu = new NSMenu("File");
        fileMenu.AddItem(new NSMenuItem("Custom File Action", new Selector("customFileAction:"), "n"));
        Reset(MainMenu(appMenu, fileMenu));
        host.Configure(application);
        Require(application.MainMenu!.Items[0].Submenu!.Handle == appMenu.Handle, "empty application menu was replaced");
        RequireQuit();
        Require(fileMenu.Items.Length == 1 && fileMenu.Items[0].Title == "Custom File Action", "custom menu changed");
    }),
    ("populated application submenu is preserved", () =>
    {
        var appMenu = new NSMenu("Custom Application");
        var custom = new NSMenuItem("Custom Quit", new Selector("customQuit:"), "q");
        appMenu.AddItem(custom);
        Reset(MainMenu(appMenu));
        host.Configure(application);
        Require(appMenu.Items.Length == 1 && appMenu.Items[0].Handle == custom.Handle, "custom application menu changed");
        RequireWindowActions();
    }),
    ("registered Window menu does not suppress default Quit", () =>
    {
        var appMenu = new NSMenu("Application");
        var windows = new NSMenu("Custom Windows");
        windows.AddItem(new NSMenuItem("Custom Window Action", new Selector("customWindowAction:"), string.Empty));
        Reset(MainMenu(appMenu, windows), windows);
        host.Configure(application);
        RequireQuit();
        Require(application.WindowsMenu!.Handle == windows.Handle && windows.Items.Length == 1, "registered Window menu changed");
    }),
    ("existing Window menu contents are preserved", () =>
    {
        var appMenu = new NSMenu("Application");
        var windows = new NSMenu("Window");
        var custom = new NSMenuItem("Custom Window Action", new Selector("customWindowAction:"), string.Empty);
        windows.AddItem(custom);
        Reset(MainMenu(appMenu, windows));
        host.Configure(application);
        RequireQuit();
        Require(application.WindowsMenu!.Handle == windows.Handle && windows.Items.Length == 1 && windows.Items[0].Handle == custom.Handle,
            "existing Window menu changed");
    }),
    ("registered Window-only menu gets a separate application menu", () =>
    {
        var windows = new NSMenu("Custom Windows");
        Reset(MainMenu(windows), windows);
        host.Configure(application);
        RequireQuit();
        Require(windows.Items.Length == 0 && application.WindowsMenu!.Handle == windows.Handle, "registered Window menu changed");
        Require(application.MainMenu!.Items[0].Submenu!.Handle != windows.Handle, "Quit was inserted into Window menu");
    }),
    ("named Window-only menu gets a separate application menu", () =>
    {
        var windows = new NSMenu("Window");
        Reset(MainMenu(windows));
        host.Configure(application);
        RequireQuit();
        Require(windows.Items.Length == 0 && application.WindowsMenu!.Handle == windows.Handle, "named Window menu changed");
        Require(application.MainMenu!.Items[0].Submenu!.Handle != windows.Handle, "Quit was inserted into Window menu");
    }),
    ("repeated configuration does not duplicate menus or actions", () =>
    {
        Reset(new NSMenu());
        host.Configure(application);
        var menu = application.MainMenu!;
        var windows = application.WindowsMenu!;
        int menuCount = menu.Items.Length;
        int windowItemCount = windows.Items.Length;
        int appItemCount = menu.Items[0].Submenu!.Items.Length;
        host.Configure(application);
        RequireQuit();
        Require(menu.Items.Length == menuCount && windows.Items.Length == windowItemCount &&
            menu.Items[0].Submenu!.Items.Length == appItemCount, "configuration duplicated items");
        Require(application.WindowsMenu!.Handle == windows.Handle, "configuration replaced Window menu");
    })
};

Require(checks.Length == checkCount, "menu smoke runner and fixture counts differ");
foreach (var (name, run) in checks.Skip(caseIndex).Take(1))
{
    try
    {
        run();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL: {name}: {exception.Message}");
        Console.Error.WriteLine($"Before configuration: {host.BeforeConfiguration}");
        Console.Error.WriteLine("Main menu: " + string.Join(" | ", application.MainMenu?.Items.Select(item => $"{item.Title} -> {item.Submenu?.Title}") ?? []));
        Console.Error.WriteLine($"Window menu: {application.WindowsMenu?.Title}; items: " +
            string.Join(" | ", application.WindowsMenu?.Items.Select(item => $"{item.Title} [{item.Action?.Name}]") ?? []));
    }
}
return failures == 0 ? 0 : 1;

void Reset(NSMenu? menu, NSMenu? windows = null)
{
    // AppKit permits nil for MainMenu. The .NET binding rejects its null setter.
    NativeMenu.Set(application.Handle, new Selector("setMainMenu:").Handle, menu?.Handle ?? 0);
    if (windows != null) application.WindowsMenu = windows;
}

static NSMenu MainMenu(params NSMenu[] submenus)
{
    var menu = new NSMenu();
    // A menu item's title is also applied to its submenu by AppKit.
    foreach (var submenu in submenus) menu.AddItem(new NSMenuItem { Title = submenu.Title, Submenu = submenu });
    return menu;
}

void RequireQuit()
{
    var appMenu = application.MainMenu?.Items.FirstOrDefault()?.Submenu;
    var quit = appMenu?.Items.Where(item => item.Action?.Name == "terminate:").ToArray() ?? [];
    Require(quit.Length == 1, "application menu has no unique terminate: action");
    Require(quit[0].KeyEquivalent == "q" && quit[0].KeyEquivalentModifierMask == NSEventModifierMask.CommandKeyMask,
        "default Quit does not use Command-Q");
    foreach (var (action, key, modifiers) in new[]
    {
        ("hide:", "h", NSEventModifierMask.CommandKeyMask),
        ("hideOtherApplications:", "h", NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask),
        ("unhideAllApplications:", string.Empty, NSEventModifierMask.CommandKeyMask)
    })
    {
        var items = appMenu!.Items.Where(item => item.Action?.Name == action).ToArray();
        Require(items.Length == 1, $"application menu has no unique {action} action");
        Require(items[0].KeyEquivalent == key && items[0].KeyEquivalentModifierMask == modifiers,
            $"incorrect shortcut for {action}");
        Require(items[0].Target?.Handle == application.Handle, $"{action} does not target NSApplication");
    }
}

void RequireWindowActions()
{
    var windows = application.WindowsMenu;
    Require(windows != null, "Window menu is not registered");
    foreach (string action in new[] { "performMiniaturize:", "zoom:", "toggleFullScreen:", "performClose:", "arrangeInFront:" })
        Require(windows!.Items.Count(item => item.Action?.Name == action) == 1, $"missing or duplicate Window action {action}");
    var fullscreen = windows!.Items.Single(item => item.Action?.Name == "toggleFullScreen:");
    Require(fullscreen.KeyEquivalent == "f" && fullscreen.KeyEquivalentModifierMask == (NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask),
        "full screen does not use Control-Command-F");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

[Register("JaliumMacHostMenuTestDelegate")]
internal sealed class MenuTestDelegate : JaliumMacApplicationDelegate
{
    internal string BeforeConfiguration { get; private set; } = string.Empty;
    internal void Configure(NSApplication application)
    {
        BeforeConfiguration = $"windows={application.WindowsMenu?.Title}; " +
            string.Join(" | ", application.MainMenu?.Items.Select(item => $"{item.Title} -> {item.Submenu?.Title}") ?? []);
        ConfigureNativeWindowMenu(application);
    }
    protected override JaliumApp CreateHostedApp() => throw new NotSupportedException("The menu smoke test does not start a hosted application.");
}

internal static class NativeMenu
{
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    internal static extern void Set(nint receiver, nint selector, nint menu);
}
