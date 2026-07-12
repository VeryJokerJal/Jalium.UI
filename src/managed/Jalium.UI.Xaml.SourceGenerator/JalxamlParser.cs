using System.Xml;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Parses JALXAML files at compile time. The generator now does two things in a single
/// pass:
/// <list type="number">
///   <item>Captures the same metadata the legacy generator needed (x:Class, x:Name,
///   referenced types for AOT pinning, DataType references for binding-getter pinning).</item>
///   <item>Builds a full <see cref="JalxamlAstNode"/> tree so
///   <see cref="JalxamlCodeGenerator"/> can emit straight-line C# instead of the embedded
///   resource + runtime XAML reader path.</item>
/// </list>
/// When the document contains features the SG cannot statically reproduce (Razor blocks
/// today), <see cref="JalxamlParseResult.Root"/> is left null and the generator falls
/// back to <see cref="Jalium.UI.Markup.XamlBuilder.RunFallbackEmbeddedLoad"/> for that
/// component.
/// </summary>
public static class JalxamlParser
{
    private const string LegacyXamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string JaliumMarkupNamespace = "https://schemas.jalium.dev/jalxaml/markup";
    private const string JaliumNamespace = "http://schemas.jalium.com/jalxaml";
    private const string JaliumLegacyNamespace = "http://schemas.jalium.ui/2024";
    private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// Synthetic element name a simple <c>@if(...) { ... }</c> block is lifted into so the
    /// XML reader can parse the conditional region. It never resolves to a CLR type and is
    /// flattened out of the AST (its children inherit a combined
    /// <see cref="JalxamlAstNode.RazorIfCondition"/>) before codegen runs, so it never
    /// reaches type resolution or AOT pinning.
    /// </summary>
    internal const string RazorIfElementName = "__RazorIf";

    /// <summary>
    /// Synthetic element a <c>@section Name { ... }</c> definition is lifted into. Codegen
    /// turns it into a <c>XamlBuilder.RegisterRazorSection</c> factory registration and
    /// emits NO visual child (a section is a definition, not in-place content). Never
    /// resolves to a CLR type; never AOT-pinned.
    /// </summary>
    internal const string RazorSectionElementName = "__RazorSection";

    /// <summary>
    /// Synthetic element a <c>@RenderSection("Name")</c> call is lifted into. Codegen
    /// constructs a <see cref="Jalium.UI.Markup.RazorSectionHost"/> with
    /// <c>SectionName</c> set and adds it as a normal child (this is exactly what the
    /// runtime preprocessor emits for a not-yet-registered section).
    /// </summary>
    internal const string RazorRenderSectionElementName = "__RazorRenderSection";

    // Mapping from XML element names to C# fully-qualified type names. Covers framework
    // types whose simple name maps to a single CLR type at compile time. Anything not in
    // this table (e.g. user-defined controls, third-party controls) is resolved through
    // the explicit <c>clr-namespace:</c> XML namespace prefix; see <see cref="GetTypeName"/>.
    //
    // The codegen path requires the resolved name to be correct because it bakes
    // <c>new global::{name}()</c> straight into <c>InitializeComponent</c>. A wrong name
    // (e.g. <c>Jalium.UI.Shapes.Path</c> when the type lives under
    // <c>Jalium.UI.Controls.Shapes</c>) compiles into garbage and the document is rejected
    // by the C# compiler. Rather than guess, anything we cannot prove is in the mappings
    // returns null from <see cref="GetTypeName"/> and the SG falls back to the runtime
    // <c>LoadComponent</c> path for that file.
    private static readonly Dictionary<string, string> TypeMappings = new(StringComparer.Ordinal)
    {
        // Application / page / window. NB: Application lives in Jalium.UI (not Controls).
        { "Application", "Jalium.UI.Application" },
        { "Page", "Jalium.UI.Controls.Page" },
        { "Window", "Jalium.UI.Controls.Window" },
        { "ToggleSwitch", "Jalium.UI.Controls.ToggleSwitch" },

        // Buttons / inputs
        { "Button", "Jalium.UI.Controls.Button" },
        { "RepeatButton", "Jalium.UI.Controls.Primitives.RepeatButton" },
        { "ToggleButton", "Jalium.UI.Controls.Primitives.ToggleButton" },
        { "CheckBox", "Jalium.UI.Controls.CheckBox" },
        { "RadioButton", "Jalium.UI.Controls.RadioButton" },
        { "Hyperlink", "Jalium.UI.Documents.Hyperlink" },

        // Text
        { "TextBlock", "Jalium.UI.Controls.TextBlock" },
        { "TextBox", "Jalium.UI.Controls.TextBox" },
        { "PasswordBox", "Jalium.UI.Controls.PasswordBox" },
        { "RichTextBox", "Jalium.UI.Controls.RichTextBox" },
        { "AccessText", "Jalium.UI.Controls.AccessText" },
        { "Run", "Jalium.UI.Documents.Run" },
        { "Span", "Jalium.UI.Documents.Span" },
        { "Bold", "Jalium.UI.Documents.Bold" },
        { "Italic", "Jalium.UI.Documents.Italic" },

        // Items controls
        { "ListBox", "Jalium.UI.Controls.ListBox" },
        { "ListView", "Jalium.UI.Controls.ListView" },
        { "ComboBox", "Jalium.UI.Controls.ComboBox" },
        { "ComboBoxItem", "Jalium.UI.Controls.ComboBoxItem" },
        { "ListBoxItem", "Jalium.UI.Controls.ListBoxItem" },
        { "MenuItem", "Jalium.UI.Controls.MenuItem" },
        { "Menu", "Jalium.UI.Controls.Menu" },
        { "ContextMenu", "Jalium.UI.Controls.ContextMenu" },
        { "TreeView", "Jalium.UI.Controls.TreeView" },
        { "TreeViewItem", "Jalium.UI.Controls.TreeViewItem" },
        { "TabControl", "Jalium.UI.Controls.TabControl" },
        { "TabItem", "Jalium.UI.Controls.TabItem" },
        { "DataGrid", "Jalium.UI.Controls.DataGrid" },

        // Layout panels
        { "StackPanel", "Jalium.UI.Controls.StackPanel" },
        { "Grid", "Jalium.UI.Controls.Grid" },
        { "Canvas", "Jalium.UI.Controls.Canvas" },
        { "Border", "Jalium.UI.Controls.Border" },
        { "DockPanel", "Jalium.UI.Controls.DockPanel" },
        { "WrapPanel", "Jalium.UI.Controls.WrapPanel" },
        { "UniformGrid", "Jalium.UI.Controls.Primitives.UniformGrid" },
        { "Panel", "Jalium.UI.Controls.Panel" },
        { "VirtualizingStackPanel", "Jalium.UI.Controls.VirtualizingStackPanel" },

        // Grid / dock helpers
        { "RowDefinition", "Jalium.UI.Controls.RowDefinition" },
        { "ColumnDefinition", "Jalium.UI.Controls.ColumnDefinition" },

        // Containers
        { "ContentControl", "Jalium.UI.Controls.ContentControl" },
        { "ContentPresenter", "Jalium.UI.Controls.ContentPresenter" },
        { "ItemsControl", "Jalium.UI.Controls.ItemsControl" },
        { "ItemsPresenter", "Jalium.UI.Controls.Primitives.ItemsPresenter" },
        { "UserControl", "Jalium.UI.Controls.UserControl" },
        { "Frame", "Jalium.UI.Controls.Frame" },
        { "ScrollViewer", "Jalium.UI.Controls.ScrollViewer" },
        { "ScrollBar", "Jalium.UI.Controls.Primitives.ScrollBar" },
        { "ScrollContentPresenter", "Jalium.UI.Controls.ScrollContentPresenter" },
        { "Viewbox", "Jalium.UI.Controls.Viewbox" },
        { "Expander", "Jalium.UI.Controls.Expander" },
        { "GroupBox", "Jalium.UI.Controls.GroupBox" },
        { "Separator", "Jalium.UI.Controls.Separator" },

        // Sliders / progress / range
        { "Slider", "Jalium.UI.Controls.Slider" },
        { "ProgressBar", "Jalium.UI.Controls.ProgressBar" },
        { "ProgressRing", "Jalium.UI.Controls.ProgressRing" },

        // Misc
        { "NavigationView", "Jalium.UI.Controls.NavigationView" },
        { "WebView", "Jalium.UI.Controls.WebView" },
        { "Image", "Jalium.UI.Controls.Image" },
        { "MediaElement", "Jalium.UI.Controls.MediaElement" },
        { "Calendar", "Jalium.UI.Controls.Calendar" },
        { "DatePicker", "Jalium.UI.Controls.DatePicker" },
        { "ColorPicker", "Jalium.UI.Controls.ColorPicker" },
        { "Popup", "Jalium.UI.Controls.Primitives.Popup" },
        { "Thumb", "Jalium.UI.Controls.Primitives.Thumb" },
        { "Track", "Jalium.UI.Controls.Primitives.Track" },
        { "TickBar", "Jalium.UI.Controls.Primitives.TickBar" },
        { "ToolTip", "Jalium.UI.Controls.ToolTip" },
        { "ToolBar", "Jalium.UI.Controls.ToolBar" },
        { "ToolBarTray", "Jalium.UI.Controls.ToolBarTray" },
        { "StatusBar", "Jalium.UI.Controls.StatusBar" },

        // Templates / styles / triggers (Jalium.UI namespace)
        { "DataTemplate", "Jalium.UI.DataTemplate" },
        { "HierarchicalDataTemplate", "Jalium.UI.HierarchicalDataTemplate" },
        { "ItemContainerTemplate", "Jalium.UI.Controls.ItemContainerTemplate" },
        { "ControlTemplate", "Jalium.UI.ControlTemplate" },
        { "ItemsPanelTemplate", "Jalium.UI.Controls.ItemsPanelTemplate" },
        { "Style", "Jalium.UI.Style" },
        { "Setter", "Jalium.UI.Setter" },
        { "Trigger", "Jalium.UI.Trigger" },
        { "MultiTrigger", "Jalium.UI.MultiTrigger" },
        { "EventTrigger", "Jalium.UI.EventTrigger" },
        { "DataTrigger", "Jalium.UI.DataTrigger" },
        { "Condition", "Jalium.UI.Condition" },
        { "VisualState", "Jalium.UI.VisualState" },
        { "VisualStateGroup", "Jalium.UI.VisualStateGroup" },
        { "ResourceDictionary", "Jalium.UI.ResourceDictionary" },

        // Transforms (Jalium.UI.Media)
        { "TranslateTransform", "Jalium.UI.Media.TranslateTransform" },
        { "RotateTransform", "Jalium.UI.Media.RotateTransform" },
        { "ScaleTransform", "Jalium.UI.Media.ScaleTransform" },
        { "SkewTransform", "Jalium.UI.Media.SkewTransform" },
        { "MatrixTransform", "Jalium.UI.Media.MatrixTransform" },
        { "TransformGroup", "Jalium.UI.Media.TransformGroup" },

        // Easing functions (Jalium.UI.Media.Animation)
        { "SineEase", "Jalium.UI.Media.Animation.SineEase" },
        { "QuadraticEase", "Jalium.UI.Media.Animation.QuadraticEase" },
        { "CubicEase", "Jalium.UI.Media.Animation.CubicEase" },
        { "BackEase", "Jalium.UI.Media.Animation.BackEase" },
        { "ExponentialEase", "Jalium.UI.Media.Animation.ExponentialEase" },
        { "ElasticEase", "Jalium.UI.Media.Animation.ElasticEase" },
        { "BounceEase", "Jalium.UI.Media.Animation.BounceEase" },
        { "CircleEase", "Jalium.UI.Media.Animation.CircleEase" },

        // Brushes / media (Jalium.UI.Media)
        { "SolidColorBrush", "Jalium.UI.Media.SolidColorBrush" },
        { "LinearGradientBrush", "Jalium.UI.Media.LinearGradientBrush" },
        { "RadialGradientBrush", "Jalium.UI.Media.RadialGradientBrush" },
        { "GradientStop", "Jalium.UI.Media.GradientStop" },
        { "ImageBrush", "Jalium.UI.Media.ImageBrush" },
        { "TileBrush", "Jalium.UI.Media.TileBrush" },
        { "VisualBrush", "Jalium.UI.Media.VisualBrush" },
        { "DropShadowEffect", "Jalium.UI.Media.Effects.DropShadowEffect" },
        { "BlurEffect", "Jalium.UI.Media.Effects.BlurEffect" },

        // Geometry
        { "PathGeometry", "Jalium.UI.Media.PathGeometry" },
        { "PathFigure", "Jalium.UI.Media.PathFigure" },
        { "LineSegment", "Jalium.UI.Media.LineSegment" },
        { "BezierSegment", "Jalium.UI.Media.BezierSegment" },
        { "ArcSegment", "Jalium.UI.Media.ArcSegment" },

        // Animation
        { "Storyboard", "Jalium.UI.Media.Animation.Storyboard" },
        { "DoubleAnimation", "Jalium.UI.Media.Animation.DoubleAnimation" },
        { "ColorAnimation", "Jalium.UI.Media.Animation.ColorAnimation" },
    };

    /// <summary>
    /// Shapes live under <c>Jalium.UI.Controls.Shapes</c>. They are kept out of the main
    /// <see cref="TypeMappings"/> table because the lookup is short and case-insensitive.
    /// </summary>
    private static readonly Dictionary<string, string> ShapeTypeMappings = new(StringComparer.Ordinal)
    {
        { "Rectangle", "Jalium.UI.Controls.Shapes.Rectangle" },
        { "Ellipse", "Jalium.UI.Controls.Shapes.Ellipse" },
        { "Path", "Jalium.UI.Controls.Shapes.Path" },
        { "Line", "Jalium.UI.Controls.Shapes.Line" },
        { "Polygon", "Jalium.UI.Controls.Shapes.Polygon" },
        { "Polyline", "Jalium.UI.Controls.Shapes.Polyline" },
    };

    public static JalxamlParseResult? Parse(string content, string filePath)
    {
        // Lift lowerable structural Razor (simple @if / @section / @RenderSection) into
        // synthetic XML so the SG can emit straight-line C# — the runtime then applies
        // the exact same visibility binding / section host the streaming parser would,
        // with NO document re-parse (no XamlReader.LoadComponentFromString /
        // XamlReader.Parse). The lift is purely additive in risk: if it produces XML the
        // streaming pass cannot consume, or any block turns out non-pure, we drop it
        // entirely and reparse the ORIGINAL content via the legacy strip path, so the
        // worst case is identical to the pre-lift behaviour.
        if (TryLowerStructuralRazor(content, out var lifted))
        {
            var liftedResult = new JalxamlParseResult();
            var liftedStripped = StripRazorCodeBlocks(lifted, out var liftedStructural, out var liftedExpr);
            if (TryStreamingParse(liftedStripped, liftedResult) && liftedResult.Root != null)
            {
                // @if purity is flagged during the parse (FlattenRazorIf). @section nodes
                // survive the tree, so validate their single-root-element shape here.
                ValidateLiftedSections(liftedResult.Root!, liftedResult);
            }
            if (liftedResult.Root != null && !liftedResult.RazorLiftUnfaithful)
            {
                liftedResult.HasLoweredStructuralRazor = true;
                // No @if/@section survives the lift, so the strip pass sees no structural
                // Razor — the SG codegen path proceeds and lowers each construct.
                liftedResult.HasStructuralRazor = liftedStructural;
                liftedResult.HasRazorExpressions = liftedExpr;
                return liftedResult;
            }
            // Lift unusable for this document (unparseable, rooted on a synthetic
            // wrapper, or a non-pure @if/@section body) — fall through to the legacy
            // path on the ORIGINAL content so behaviour is identical to before.
        }

        var result = new JalxamlParseResult();

        // Strip Razor directives that would break the XML reader (text-content @if/@section/
        // @RenderSection/@{...} can contain literal '<' chars like in `i <= 5`). Value-expression
        // forms (@(expr), @identifier, $.x, #.x) are LEFT IN PLACE — the SG's attribute-value
        // emitter and content-text emitter detect them and lower to XamlBuilder.SetRazorBinding
        // calls. See JalxamlParseResult.HasStructuralRazor vs HasRazorExpressions for the split.
        var stripped = StripRazorCodeBlocks(content, out var hasStructural, out var hasExpressions);
        result.HasStructuralRazor = hasStructural;
        result.HasRazorExpressions = hasExpressions;

        if (!TryStreamingParse(stripped, result))
        {
            // The streaming pass blew up (typical trigger: Razor @{ ... } code blocks
            // containing XML fragments). Fall back to a regex-based metadata sweep over
            // the ORIGINAL content. TryStreamingParse already cleared partial state.
            ParseWithRegexFallback(content, result);
        }

        return result;
    }

    /// <summary>
    /// Streaming XML pass that builds <see cref="JalxamlParseResult.Root"/> and the
    /// AOT-pinning metadata from <paramref name="strippedXml"/> (Razor already
    /// stripped/lifted). Returns <c>false</c> and resets partial state if the reader
    /// throws, so the caller can choose a fallback without inheriting a half-built tree.
    /// </summary>
    private static bool TryStreamingParse(string strippedXml, JalxamlParseResult result)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                // Preserve whitespace inside text content (used for TextBlock content),
                // we filter pure-whitespace nodes when building the AST below.
                IgnoreWhitespace = false,
            };

            using var stringReader = new StringReader(strippedXml);
            using var reader = XmlReader.Create(stringReader, settings);

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    result.RootElementType = GetTypeName(reader.LocalName, reader.NamespaceURI);

                    var classAttr = GetClassAttributeValue(reader);
                    if (!string.IsNullOrEmpty(classAttr))
                        result.ClassName = classAttr;

                    // Snapshot xmlns prefix → namespace declarations from the root element so
                    // the SG can resolve attribute-value prefix references (TargetType="controls:Foo")
                    // at compile time. We do this BEFORE ParseElementTree because that call
                    // moves the reader past attributes.
                    if (reader.HasAttributes)
                    {
                        for (var i = 0; i < reader.AttributeCount; i++)
                        {
                            reader.MoveToAttribute(i);
                            // xmlns:X="..." declarations sit under the well-known
                            // http://www.w3.org/2000/xmlns/ namespace; default xmlns="..."
                            // appears with empty prefix and that same namespace.
                            if (reader.Prefix == "xmlns")
                            {
                                result.RootPrefixMappings[reader.LocalName] = reader.Value;
                            }
                            else if (string.IsNullOrEmpty(reader.Prefix) && reader.LocalName == "xmlns")
                            {
                                result.RootPrefixMappings[string.Empty] = reader.Value;
                            }
                        }
                        reader.MoveToElement();
                    }

                    // Build the AST root — this descends into the entire document.
                    result.Root = ParseElementTree(reader, result);

                    // Defensive: a component has a single x:Class root. An @if wrapper or
                    // a bare @section definition must never be that root (the former is
                    // not a real element; the latter produces no visual tree). A
                    // @RenderSection root IS valid — it resolves to a real RazorSectionHost.
                    // If a non-renderable synthetic bubbled up, the lift was malformed for
                    // this document — fail so the caller falls back to the legacy path.
                    if (result.Root != null &&
                        (result.Root.LocalName == RazorIfElementName ||
                         result.Root.LocalName == RazorSectionElementName))
                    {
                        ResetStreamingState(result);
                        return false;
                    }
                    break;
                }
            }

            return true;
        }
        catch
        {
            ResetStreamingState(result);
            return false;
        }
    }

    private static void ResetStreamingState(JalxamlParseResult result)
    {
        result.NamedElements.Clear();
        result.ReferencedElements.Clear();
        result.DataTypeReferences.Clear();
        result.RootPrefixMappings.Clear();
        result.ClassName = null;
        result.RootElementType = null;
        result.Root = null;
    }

    // ============================================================
    // AST construction. Mirrors the streaming parser but also captures attribute values,
    // children and property-element bodies so the generator has everything to emit C#.
    // ============================================================

    private static JalxamlAstNode ParseElementTree(XmlReader reader, JalxamlParseResult result, bool suppressNames = false)
    {
        var localName = reader.LocalName;
        var isSynthetic = localName == RazorIfElementName ||
                          localName == RazorSectionElementName ||
                          localName == RazorRenderSectionElementName;

        // Synthetic type resolution:
        //  - __RazorRenderSection  → a real RazorSectionHost (flows through normal codegen
        //    as a typed child with SectionName set).
        //  - __RazorSection        → sentinel (non-empty so FindFirstUnresolved doesn't
        //    flag it; codegen special-cases by LocalName and never news the sentinel).
        //  - __RazorIf             → left null; flattened out before codegen.
        string? resolved;
        string fallback;
        if (localName == RazorRenderSectionElementName)
        {
            resolved = "Jalium.UI.Markup.RazorSectionHost";
            fallback = "Jalium.UI.Markup.RazorSectionHost";
        }
        else if (localName == RazorSectionElementName)
        {
            resolved = RazorSectionElementName;
            fallback = "Jalium.UI.FrameworkElement";
        }
        else if (localName == RazorIfElementName)
        {
            resolved = null;
            fallback = "Jalium.UI.FrameworkElement";
        }
        else
        {
            resolved = GetTypeName(localName, reader.NamespaceURI);
            fallback = GetTypeNameWithFallback(localName, reader.NamespaceURI);
        }

        var node = new JalxamlAstNode
        {
            LocalName = localName,
            NamespaceUri = reader.NamespaceURI,
            Prefix = string.IsNullOrEmpty(reader.Prefix) ? null : reader.Prefix,
            ResolvedClrTypeName = resolved,
            FallbackClrTypeName = fallback,
        };

        if (reader is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
        {
            node.LineNumber = lineInfo.LineNumber;
            node.LinePosition = lineInfo.LinePosition;
        }

        // Track every element type for AOT pinning (legacy metadata path used by the SG to
        // emit `RegisterType<T>()` calls in the ModuleInitializer). The synthetic Razor
        // wrappers have no element-named CLR type (RazorSectionHost is referenced directly
        // by the emitted `new` instead), so they must never enter the referenced-type set.
        if (!isSynthetic)
            AddReferencedElement(result, reader.LocalName, reader.NamespaceURI);

        // Capture attributes. We also have to feed the legacy metadata structures because
        // the existing AOT-pinning emit reads them.
        if (reader.HasAttributes)
        {
            CaptureAttributes(reader, node, result, suppressNames);
        }

        // A @section body is registered as a compiled factory invoked by RazorSectionHost
        // — its x:Names are scoped to that subtree (like a template), NOT codebehind
        // fields on the defining component, matching the runtime which parses the section
        // separately. Suppress name capture for everything inside a __RazorSection.
        var childSuppressNames = suppressNames || localName == RazorSectionElementName;

        if (reader.IsEmptyElement)
        {
            return node;
        }

        var depth = reader.Depth;
        var textBuffer = new System.Text.StringBuilder();

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    if (reader.LocalName.Contains('.'))
                    {
                        // Property element: <Foo.Bar>...</Foo.Bar>
                        var pe = ParsePropertyElementBody(reader, result, childSuppressNames);
                        node.PropertyElements.Add(pe);
                    }
                    else
                    {
                        var child = ParseElementTree(reader, result, childSuppressNames);
                        // Flatten a lifted @if wrapper into this parent, stamping each
                        // conditional child with the combined condition. While building a
                        // <__RazorIf> subtree itself, keep nested wrappers intact so
                        // FlattenRazorIf can layer the conditions in the correct order.
                        if (child.LocalName == RazorIfElementName && node.LocalName != RazorIfElementName)
                            FlattenRazorIf(child, null, node.Children, result);
                        else
                            node.Children.Add(child);
                    }
                    break;
                }
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    textBuffer.Append(reader.Value);
                    break;
                case XmlNodeType.Whitespace:
                    // Drop pure-whitespace between elements; we don't want to construct
                    // empty TextBlock content placeholders.
                    break;
            }
        }

        var text = textBuffer.ToString();
        if (!string.IsNullOrWhiteSpace(text))
        {
            node.TextContent = text;
        }

        return node;
    }

    private static JalxamlAstPropertyElement ParsePropertyElementBody(XmlReader reader, JalxamlParseResult result, bool suppressNames = false)
    {
        var parts = reader.LocalName.Split('.');
        var ownerName = parts.Length > 0 ? parts[0] : reader.LocalName;
        var propertyName = parts.Length > 1 ? parts[1] : string.Empty;

        var pe = new JalxamlAstPropertyElement
        {
            OwnerName = ownerName,
            PropertyName = propertyName,
            NamespaceUri = reader.NamespaceURI,
            Prefix = string.IsNullOrEmpty(reader.Prefix) ? null : reader.Prefix,
        };

        if (reader.IsEmptyElement)
        {
            return pe;
        }

        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName.Contains('.'))
                {
                    // Nested property elements inside another property element are unusual;
                    // we still walk them to keep metadata collection running, but for now
                    // they don't get reflected back into the AST (the SG falls back if it
                    // cannot represent them). Skip the body without adding to the tree.
                    SkipPropertyElementBody(reader, result);
                }
                else
                {
                    var child = ParseElementTree(reader, result, suppressNames);
                    if (child.LocalName == RazorIfElementName)
                        FlattenRazorIf(child, null, pe.Children, result);
                    else
                        pe.Children.Add(child);
                }
            }
        }

        return pe;
    }

    private static void SkipPropertyElementBody(XmlReader reader, JalxamlParseResult result)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        var depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                // Continue collecting referenced types for AOT pinning.
                AddReferencedElement(result, reader.LocalName, reader.NamespaceURI);
                if (reader.LocalName.Contains('.'))
                {
                    SkipPropertyElementBody(reader, result);
                }
                else
                {
                    // Walk the regular tree (still side-effecting metadata).
                    _ = ParseElementTree(reader, result);
                }
            }
        }
    }

    private static void CaptureAttributes(XmlReader reader, JalxamlAstNode node, JalxamlParseResult result, bool suppressNames = false)
    {
        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);

            var local = reader.LocalName;
            var ns = reader.NamespaceURI;
            var prefix = string.IsNullOrEmpty(reader.Prefix) ? null : reader.Prefix;
            var value = reader.Value;

            if (string.Equals(local, "xmlns", StringComparison.Ordinal) ||
                string.Equals(prefix, "xmlns", StringComparison.Ordinal))
            {
                node.Attributes.Add(new JalxamlAstAttribute
                {
                    Kind = JalxamlAttributeKind.XmlnsDecl,
                    LocalName = local,
                    Prefix = prefix,
                    NamespaceUri = ns,
                    Value = value,
                });
                continue;
            }

            // x:Name / x:Class / x:Key / x:FieldModifier — feed metadata structures and
            // tag for the codegen to invoke ApplyXDirective.
            if (IsXamlMarkupNamespace(ns))
            {
                if (!suppressNames && string.Equals(local, "Name", StringComparison.Ordinal))
                {
                    result.NamedElements.Add(new NamedElement
                    {
                        Name = value,
                        TypeName = node.FallbackClrTypeName,
                    });
                }
                if (string.Equals(local, "Class", StringComparison.Ordinal))
                {
                    // Already captured at root via GetClassAttributeValue, but safe to record again.
                    if (string.IsNullOrEmpty(result.ClassName))
                    {
                        result.ClassName = value;
                    }
                }
                node.Attributes.Add(new JalxamlAstAttribute
                {
                    Kind = JalxamlAttributeKind.XDirective,
                    LocalName = local,
                    Prefix = prefix,
                    NamespaceUri = ns,
                    Value = value,
                });
                continue;
            }

            // Attached property: Grid.Row, DockPanel.Dock — local name contains '.'
            if (local.IndexOf('.') > 0)
            {
                var parts = local.Split('.');
                var owner = parts[0];
                var prop = parts.Length > 1 ? parts[1] : string.Empty;
                node.Attributes.Add(new JalxamlAstAttribute
                {
                    Kind = JalxamlAttributeKind.Attached,
                    LocalName = prop,
                    AttachedOwner = owner,
                    Prefix = prefix,
                    NamespaceUri = ns,
                    Value = value,
                });
                continue;
            }

            // Compatibility: an unprefixed `Name="Foo"` is treated as x:Name by the runtime.
            if (!suppressNames && string.Equals(local, "Name", StringComparison.Ordinal) && string.IsNullOrEmpty(prefix))
            {
                result.NamedElements.Add(new NamedElement
                {
                    Name = value,
                    TypeName = node.ResolvedClrTypeName ?? "Jalium.UI.FrameworkElement",
                });
            }

            // DataType="vm:Foo" — record for trim pinning, also keep as a regular attribute
            // so the runtime SetProperty path sees it.
            if (string.Equals(local, "DataType", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(value))
            {
                CaptureDataTypeReference(reader, value, result);
            }

            node.Attributes.Add(new JalxamlAstAttribute
            {
                Kind = JalxamlAttributeKind.Value,
                LocalName = local,
                Prefix = prefix,
                NamespaceUri = ns,
                Value = value,
            });
        }

        reader.MoveToElement();
    }

    private static bool IsXamlMarkupNamespace(string namespaceUri)
    {
        return string.Equals(namespaceUri, LegacyXamlNamespace, StringComparison.Ordinal) ||
               string.Equals(namespaceUri, JaliumMarkupNamespace, StringComparison.Ordinal);
    }

    private static void CaptureDataTypeReference(XmlReader reader, string rawValue, JalxamlParseResult result)
    {
        var typeRef = StripXTypeMarkup(rawValue.Trim());
        if (string.IsNullOrEmpty(typeRef))
            return;

        string localName;
        string namespaceUri;
        var colonIdx = typeRef.IndexOf(':');
        if (colonIdx > 0 && colonIdx < typeRef.Length - 1)
        {
            var prefix = typeRef.Substring(0, colonIdx);
            localName = typeRef.Substring(colonIdx + 1);
            namespaceUri = reader.LookupNamespace(prefix) ?? string.Empty;
        }
        else
        {
            localName = typeRef;
            namespaceUri = reader.LookupNamespace(string.Empty) ?? string.Empty;
        }

        AddDataTypeReference(result, localName, namespaceUri);
    }

    // ============================================================
    // Regex fallback (only triggered when XmlReader throws — usually because Razor blocks
    // confused the streaming pass). The codegen path is unavailable here; the SG falls back
    // to RunFallbackEmbeddedLoad for these documents.
    // ============================================================

    private static void ParseWithRegexFallback(string content, JalxamlParseResult result)
    {
        var classMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"x:Class\s*=\s*[""'](?<cls>[^""']+)[""']");
        if (classMatch.Success)
            result.ClassName = classMatch.Groups["cls"].Value;

        var defaultXmlns = ExtractDefaultXmlns(content);
        var prefixToXmlns = ExtractPrefixToXmlns(content);

        var nameMatches = System.Text.RegularExpressions.Regex.Matches(
            content, @"x:Name\s*=\s*[""'](?<name>[^""']+)[""']");
        foreach (System.Text.RegularExpressions.Match m in nameMatches)
        {
            var name = m.Groups["name"].Value;
            if (string.IsNullOrEmpty(name)) continue;

            var beforeMatch = content.Substring(0, m.Index);
            var lastTagStart = beforeMatch.LastIndexOf('<');
            var typeName = "Jalium.UI.FrameworkElement";
            if (lastTagStart >= 0)
            {
                var tagContent = beforeMatch.Substring(lastTagStart + 1);
                var spaceIdx = tagContent.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '/' });
                var elementName = spaceIdx >= 0 ? tagContent.Substring(0, spaceIdx) : tagContent;
                if (!string.IsNullOrEmpty(elementName) && !elementName.Contains('.'))
                    typeName = TypeMappings.TryGetValue(elementName, out var mapped) ? mapped : $"Jalium.UI.Controls.{elementName}";
            }

            result.NamedElements.Add(new NamedElement { Name = name, TypeName = typeName });
        }

        var elementMatches = System.Text.RegularExpressions.Regex.Matches(
            content, @"<(?<elem>[A-Za-z_][\w.]*?)(?<prefix>:[A-Za-z_][\w]*)?\b");
        foreach (System.Text.RegularExpressions.Match m in elementMatches)
        {
            var raw = m.Value.Substring(1);
            if (raw.Length == 0) continue;

            string elementName;
            string namespaceUri;

            var colonIdx = raw.IndexOf(':');
            if (colonIdx > 0)
            {
                var prefix = raw.Substring(0, colonIdx);
                elementName = raw.Substring(colonIdx + 1);
                if (!prefixToXmlns.TryGetValue(prefix, out namespaceUri!))
                    namespaceUri = string.Empty;
            }
            else
            {
                elementName = raw;
                namespaceUri = defaultXmlns;
            }

            if (elementName.IndexOf('.') >= 0)
                continue;

            AddReferencedElement(result, elementName, namespaceUri);
        }

        var dataTypeMatches = System.Text.RegularExpressions.Regex.Matches(
            content, @"\bDataType\s*=\s*[""'](?<val>[^""']+)[""']");
        foreach (System.Text.RegularExpressions.Match m in dataTypeMatches)
        {
            var rawValue = m.Groups["val"].Value.Trim();
            var typeRef = StripXTypeMarkup(rawValue);
            if (string.IsNullOrEmpty(typeRef))
                continue;

            string localName;
            string namespaceUri;
            var colonIdx = typeRef.IndexOf(':');
            if (colonIdx > 0 && colonIdx < typeRef.Length - 1)
            {
                var prefix = typeRef.Substring(0, colonIdx);
                localName = typeRef.Substring(colonIdx + 1);
                if (!prefixToXmlns.TryGetValue(prefix, out namespaceUri!))
                    namespaceUri = string.Empty;
            }
            else
            {
                localName = typeRef;
                namespaceUri = defaultXmlns;
            }

            AddDataTypeReference(result, localName, namespaceUri);
        }
    }

    private static string ExtractDefaultXmlns(string content)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            content, @"\bxmlns\s*=\s*[""'](?<ns>[^""']*)[""']");
        return match.Success ? match.Groups["ns"].Value : string.Empty;
    }

    private static Dictionary<string, string> ExtractPrefixToXmlns(string content)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var matches = System.Text.RegularExpressions.Regex.Matches(
            content, @"\bxmlns:(?<prefix>[A-Za-z_][\w]*)\s*=\s*[""'](?<ns>[^""']*)[""']");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var prefix = m.Groups["prefix"].Value;
            var ns = m.Groups["ns"].Value;
            if (!string.IsNullOrEmpty(prefix) && !map.ContainsKey(prefix))
            {
                map[prefix] = ns;
            }
        }
        return map;
    }

    /// <summary>
    /// 把 <c>{x:Type prefix:Foo}</c> 形式简化为 <c>prefix:Foo</c>;非 markup extension
    /// 直接原样返回。仅识别 <c>x:Type</c> 前缀,其它 markup extension 当原值处理。
    /// </summary>
    private static string StripXTypeMarkup(string rawValue)
    {
        if (rawValue.Length < 2 || rawValue[0] != '{' || rawValue[rawValue.Length - 1] != '}')
            return rawValue;

        var inner = rawValue.Substring(1, rawValue.Length - 2).Trim();
        const string XTypePrefix = "x:Type";
        if (inner.StartsWith(XTypePrefix, StringComparison.Ordinal) &&
            inner.Length > XTypePrefix.Length &&
            char.IsWhiteSpace(inner[XTypePrefix.Length]))
        {
            return inner.Substring(XTypePrefix.Length).TrimStart();
        }

        return string.Empty;
    }

    private static void AddDataTypeReference(JalxamlParseResult result, string typeName, string namespaceUri)
    {
        if (string.IsNullOrEmpty(typeName))
            return;

        for (var i = 0; i < result.DataTypeReferences.Count; i++)
        {
            var existing = result.DataTypeReferences[i];
            if (string.Equals(existing.ElementName, typeName, StringComparison.Ordinal) &&
                string.Equals(existing.NamespaceUri, namespaceUri, StringComparison.Ordinal))
            {
                return;
            }
        }

        result.DataTypeReferences.Add(new ReferencedElement
        {
            ElementName = typeName,
            NamespaceUri = namespaceUri ?? string.Empty
        });
    }

    private static void AddReferencedElement(JalxamlParseResult result, string elementName, string namespaceUri)
    {
        if (string.IsNullOrEmpty(elementName))
            return;

        // De-dup by (elementName, namespaceUri) — same type referenced many times across
        // the document only needs one typeof() pin.
        for (var i = 0; i < result.ReferencedElements.Count; i++)
        {
            var existing = result.ReferencedElements[i];
            if (string.Equals(existing.ElementName, elementName, StringComparison.Ordinal) &&
                string.Equals(existing.NamespaceUri, namespaceUri, StringComparison.Ordinal))
            {
                return;
            }
        }

        result.ReferencedElements.Add(new ReferencedElement
        {
            ElementName = elementName,
            NamespaceUri = namespaceUri ?? string.Empty
        });
    }

    /// <summary>
    /// Resolves a (localName, namespaceUri) pair to a CLR full type name. Returns null when
    /// the type cannot be proved correct at parse time — the codegen then bails out for
    /// that document and the SG emits the legacy <c>LoadComponent</c> fallback.
    /// </summary>
    /// <remarks>
    /// The codegen path bakes <c>new global::{name}()</c> into the generated source, so the
    /// returned name must be exact. Three cases are honoured:
    /// <list type="bullet">
    ///   <item><c>clr-namespace:NS;assembly=A</c> — the namespace is given explicitly; concatenate.</item>
    ///   <item>Default Jalium namespaces — look up in <see cref="TypeMappings"/> /
    ///   <see cref="ShapeTypeMappings"/>. Anything not in the tables returns null and
    ///   the document is handled at runtime.</item>
    ///   <item>Unknown XML namespace — return null.</item>
    /// </list>
    /// This conservative behaviour means user-defined controls reach the codegen path only
    /// when they're declared via <c>clr-namespace:</c>, which is the common idiom anyway.
    /// </remarks>
    public static string? GetTypeName(string elementName, string namespaceUri)
    {
        // Explicit clr-namespace declaration: trust the author. We can't verify the type
        // exists at parse time (no Roslyn symbol access here) but the C# compiler will
        // error-out cleanly if the type is missing — same observable behaviour as the
        // legacy regex fallback that emitted simple names.
        if (namespaceUri.StartsWith("clr-namespace:", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = namespaceUri.Substring("clr-namespace:".Length);
            var namespacePart = remainder
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(namespacePart))
                return $"{namespacePart}.{elementName}";
        }

        // Default jalium / presentation namespaces — must be in the curated mappings.
        if (string.Equals(namespaceUri, JaliumNamespace, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(namespaceUri, JaliumLegacyNamespace, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(namespaceUri, PresentationNamespace, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(namespaceUri))
        {
            if (TypeMappings.TryGetValue(elementName, out var typeName))
                return typeName;

            if (ShapeTypeMappings.TryGetValue(elementName, out var shapeTypeName))
                return shapeTypeName;

            // Not in the curated table — let the SG fall back to runtime LoadComponent
            // rather than emit a guessed (and probably wrong) namespace.
            return null;
        }

        // Unknown XML namespace.
        return null;
    }

    /// <summary>
    /// Like <see cref="GetTypeName"/> but always returns a non-null guess. Used for the
    /// generated x:Name field declarations: even on the runtime fallback path the SG must
    /// emit a strongly-typed field so the codebehind compiles. Falling back to
    /// <c>Jalium.UI.FrameworkElement</c> is too weak (the codebehind frequently references
    /// derived members like <c>ToggleSwitch.IsOn</c>); falling back to
    /// <c>Jalium.UI.Controls.{name}</c> matches the historical behaviour the codebehind
    /// authors are relying on.
    /// </summary>
    public static string GetTypeNameWithFallback(string elementName, string namespaceUri)
    {
        var resolved = GetTypeName(elementName, namespaceUri);
        if (!string.IsNullOrEmpty(resolved))
            return resolved!;

        if (string.IsNullOrEmpty(namespaceUri) ||
            string.Equals(namespaceUri, JaliumNamespace, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(namespaceUri, JaliumLegacyNamespace, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(namespaceUri, PresentationNamespace, StringComparison.OrdinalIgnoreCase))
        {
            return $"Jalium.UI.Controls.{elementName}";
        }

        return "Jalium.UI.FrameworkElement";
    }

    private static string? GetClassAttributeValue(XmlReader reader)
    {
        var classAttr = reader.GetAttribute("Class", LegacyXamlNamespace);
        if (!string.IsNullOrEmpty(classAttr))
            return classAttr;

        classAttr = reader.GetAttribute("Class", JaliumMarkupNamespace);
        if (!string.IsNullOrEmpty(classAttr))
            return classAttr;

        return GetPrefixedAttributeFallback(reader, "Class");
    }

    private static string? GetPrefixedAttributeFallback(XmlReader reader, string localName)
    {
        if (!reader.HasAttributes)
            return null;

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (!string.Equals(reader.LocalName, localName, StringComparison.Ordinal))
                continue;

            if (string.Equals(reader.Prefix, "x", StringComparison.Ordinal))
            {
                var value = reader.Value;
                reader.MoveToElement();
                return value;
            }
        }

        reader.MoveToElement();
        return null;
    }

    /// <summary>
    /// Strips Razor directives that would break the XML reader, while LEAVING value-expression
    /// Razor (<c>@(expr)</c>, <c>@identifier</c>, <c>$.x</c>, <c>#.x</c>) intact so the SG can
    /// detect and lower them downstream. Sets <paramref name="hasStructuralRazor"/> for
    /// directives the SG cannot lower (<c>@if</c>, <c>@section</c>, <c>@RenderSection</c>,
    /// stray <c>@{ ... }</c> blocks the build task didn't pre-expand) and
    /// <paramref name="hasRazorExpressions"/> for value-expression Razor.
    /// </summary>
    private static string StripRazorCodeBlocks(string content, out bool hasStructuralRazor, out bool hasRazorExpressions)
    {
        hasStructuralRazor = false;
        hasRazorExpressions = false;
        var sb = new System.Text.StringBuilder(content.Length);
        var i = 0;
        var inTag = false;
        var inAttr = false;
        char attrQuote = '\0';

        while (i < content.Length)
        {
            if (inAttr)
            {
                if (content[i] == attrQuote) { inAttr = false; attrQuote = '\0'; }
                else if ((content[i] == '@' && i + 1 < content.Length && content[i + 1] != '@') ||
                         (content[i] == '$' && i + 1 < content.Length && content[i + 1] == '.') ||
                         (content[i] == '#' && i + 1 < content.Length && content[i + 1] == '.'))
                {
                    hasRazorExpressions = true;
                }
                sb.Append(content[i]); i++; continue;
            }

            if (inTag)
            {
                if (content[i] == '"' || content[i] == '\'') { inAttr = true; attrQuote = content[i]; }
                else if (content[i] == '>') inTag = false;
                sb.Append(content[i]); i++; continue;
            }

            if (content[i] == '<' && i + 1 < content.Length &&
                (char.IsLetter(content[i + 1]) || content[i + 1] == '/' || content[i + 1] == '!'))
            {
                inTag = true; sb.Append(content[i]); i++; continue;
            }

            // Text-node Razor. Decide structural vs value-expression.
            if (content[i] == '@' && i + 1 < content.Length && content[i + 1] != '@')
            {
                // Structural directives: starts with @if(, @section, @RenderSection, @{ ...
                // These can contain literal '<' / '>' / '"' and MUST be stripped to keep
                // the document valid XML.
                if (IsStructuralKeyword(content, i + 1, out var afterKeyword))
                {
                    hasStructuralRazor = true;
                    i = afterKeyword;
                    // Swallow up to the next '<' (start of next element), preserving \n
                    // so XML line numbers stay roughly aligned.
                    while (i < content.Length && content[i] != '<')
                    {
                        if (content[i] == '\n') sb.Append('\n');
                        i++;
                    }
                    continue;
                }

                // Value-expression Razor (e.g. @ItemCount, @(Count > 0 ? "a" : "b")). Leave
                // these in the document — the SG's content-text emitter will detect them and
                // emit XamlBuilder.SetRazorBinding. Note: they appear inside text NODES, not
                // attribute values; the XML reader emits text nodes back to the SG verbatim,
                // so preserving '@' here is safe (no '<' inside an expression at text level).
                hasRazorExpressions = true;
                sb.Append(content[i]); i++; continue;
            }

            // Self/data reference in text content — also a value-expression.
            if ((content[i] == '$' || content[i] == '#') && i + 1 < content.Length && content[i + 1] == '.')
            {
                hasRazorExpressions = true;
                sb.Append(content[i]); i++; continue;
            }

            sb.Append(content[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Detect structural Razor keywords at <paramref name="position"/> (one PAST the '@').
    /// On match, returns the offset just after the keyword (so the caller can swallow the
    /// keyword body + arguments). On no match, returns false and <paramref name="afterKeyword"/>
    /// equals <paramref name="position"/>.
    /// </summary>
    private static bool IsStructuralKeyword(string content, int position, out int afterKeyword)
    {
        afterKeyword = position;
        // Stray @{ ... } block (TransformJalxamlRazorTask should have eliminated these,
        // but if the transform is disabled or fails for a doc, we still treat them as
        // structural so the runtime parser gets to handle them.
        if (position < content.Length && content[position] == '{')
        {
            afterKeyword = position + 1;
            return true;
        }
        // @if / @section / @RenderSection (case-sensitive — Razor convention).
        foreach (var kw in s_structuralKeywords)
        {
            if (position + kw.Length <= content.Length &&
                string.CompareOrdinal(content, position, kw, 0, kw.Length) == 0)
            {
                // Require the keyword to be followed by '(' or whitespace, otherwise it's
                // a normal identifier (e.g. @section_name should be treated as an expr).
                var next = position + kw.Length;
                if (next >= content.Length ||
                    content[next] == '(' || content[next] == ' ' || content[next] == '\t' ||
                    content[next] == '\r' || content[next] == '\n' || content[next] == '{')
                {
                    afterKeyword = next;
                    return true;
                }
            }
        }
        return false;
    }

    private static readonly string[] s_structuralKeywords =
    {
        "if", "section", "RenderSection",
        // Stray flow-control that escaped the build task. Same treatment.
        "for", "foreach", "while", "switch", "do", "try", "using", "lock"
    };

    /// <summary>
    /// Rewrite structural Razor the SG can lower into synthetic XML so the reader can
    /// parse it and codegen can emit straight-line C# (no document re-parse):
    /// <list type="bullet">
    ///   <item><c>@if(cond) { ... }</c> → <c>&lt;__RazorIf Condition="cond"&gt;...&lt;/__RazorIf&gt;</c>
    ///   — each conditional child later gets a <c>SetRazorIfVisibility</c> binding
    ///   (same as the streaming parser's <c>ShouldIncludeConditionalChild</c>).</item>
    ///   <item><c>@section Name { ... }</c> → <c>&lt;__RazorSection Name="Name"&gt;...&lt;/__RazorSection&gt;</c>
    ///   — codegen registers a compiled body factory (<c>RegisterRazorSection</c>),
    ///   replacing the runtime's "store XAML string, re-parse per host".</item>
    ///   <item><c>@RenderSection("Name")</c> → <c>&lt;__RazorRenderSection Name="Name"/&gt;</c>
    ///   — codegen builds a <see cref="Jalium.UI.Markup.RazorSectionHost"/> (exactly what
    ///   the runtime preprocessor emits for a not-yet-registered section).</item>
    /// </list>
    /// <para>
    /// Returns <c>false</c> — leaving <paramref name="rewritten"/> equal to
    /// <paramref name="content"/> — for any document this cannot faithfully lower so the
    /// caller keeps the legacy strip + runtime-fallback path: <c>else</c> / <c>else if</c>
    /// chains; stray <c>@{ ... }</c> or leaked loop/flow keywords
    /// (<c>@for</c>/<c>@foreach</c>/<c>@while</c>/<c>@switch</c>/<c>@do</c>/<c>@try</c>/
    /// <c>@using</c>/<c>@lock</c>); a <c>@RenderSection</c> whose name is not a string
    /// literal; unbalanced braces; or no structural Razor at all.
    /// </para>
    /// <para>
    /// Purity (an <c>@if</c> / <c>@section</c> body holding only child elements — no
    /// block-level text, no property element) is enforced after parsing in
    /// <see cref="FlattenRazorIf"/> / the section-node check, where the real tree
    /// distinguishes block-level text from text inside a child element. A non-pure block
    /// sets <see cref="JalxamlParseResult.RazorLiftUnfaithful"/> so <see cref="Parse"/>
    /// discards the lift and falls back to the legacy path — behaviour identical to before.
    /// </para>
    /// </summary>
    private static bool TryLowerStructuralRazor(string content, out string rewritten)
    {
        rewritten = content;
        if (content.IndexOf("@if", StringComparison.Ordinal) < 0 &&
            content.IndexOf("@section", StringComparison.Ordinal) < 0 &&
            content.IndexOf("@RenderSection", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        var sb = new System.Text.StringBuilder(content.Length + 64);
        var i = 0;
        var inTag = false;
        var inAttr = false;
        char attrQuote = '\0';
        // Open block stack — 'I' = @if, 'S' = @section. A text-level '}' closes the
        // innermost block and emits the matching close tag. For @if, the frame also
        // carries the chain conditions seen so far so a following `else if` / `else`
        // can compute its mutually-exclusive effective condition exactly as the runtime
        // (XamlReader RazorIfBlockEntry.ChainBranchConditions). Nesting is rebuilt later
        // from the XML nesting of <__RazorIf> by FlattenRazorIf.
        var blocks = new System.Collections.Generic.Stack<RazorBlock>();
        var liftedAny = false;

        while (i < content.Length)
        {
            char c = content[i];

            if (inAttr)
            {
                sb.Append(c);
                if (c == attrQuote) { inAttr = false; attrQuote = '\0'; }
                i++;
                continue;
            }

            if (inTag)
            {
                sb.Append(c);
                if (c == '"' || c == '\'') { inAttr = true; attrQuote = c; }
                else if (c == '>') inTag = false;
                i++;
                continue;
            }

            // XML comment / CDATA — copy verbatim, never interpret Razor inside.
            if (c == '<' && StartsWith(content, i, "<!--"))
            {
                var end = content.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0) return false;
                end += 3;
                sb.Append(content, i, end - i);
                i = end;
                continue;
            }
            if (c == '<' && StartsWith(content, i, "<![CDATA["))
            {
                var end = content.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                if (end < 0) return false;
                end += 3;
                sb.Append(content, i, end - i);
                i = end;
                continue;
            }

            if (c == '<' && i + 1 < content.Length &&
                (char.IsLetter(content[i + 1]) || content[i + 1] == '/' ||
                 content[i + 1] == '!' || content[i + 1] == '?'))
            {
                inTag = true;
                sb.Append(c);
                i++;
                continue;
            }

            if (c == '@' && i + 1 < content.Length)
            {
                // @@ → literal '@'.
                if (content[i + 1] == '@') { sb.Append("@@"); i += 2; continue; }

                // @if ( cond ) {  → open a conditional block.
                if (IsWordAt(content, i + 1, "if", out var afterIf))
                {
                    var openParen = SkipWhitespace(content, afterIf);
                    if (openParen < content.Length && content[openParen] == '(')
                    {
                        if (!TryReadBalancedParens(content, openParen, out var condition, out var afterParen))
                            return false;
                        var brace = SkipWhitespace(content, afterParen);
                        if (brace >= content.Length || content[brace] != '{')
                            return false;

                        var c1 = condition.Trim();
                        sb.Append('<').Append(RazorIfElementName)
                          .Append(" Condition=\"").Append(EscapeXmlAttribute(c1))
                          .Append("\">");
                        blocks.Push(new RazorBlock { Kind = 'I', Chain = new System.Collections.Generic.List<string> { c1 } });
                        liftedAny = true;
                        i = brace + 1;
                        continue;
                    }
                    return false; // `@if` without `(...)` body — not a form we lower.
                }

                // @section Name { ... }  → open a section-definition block.
                if (IsWordAt(content, i + 1, "section", out var afterSec))
                {
                    var nameStart = SkipWhitespace(content, afterSec);
                    var p = nameStart;
                    while (p < content.Length &&
                           (char.IsLetterOrDigit(content[p]) || content[p] == '_'))
                    {
                        p++;
                    }
                    if (p == nameStart) return false; // no name — cannot lower.
                    var sectionName = content.Substring(nameStart, p - nameStart);
                    var brace = SkipWhitespace(content, p);
                    if (brace >= content.Length || content[brace] != '{')
                        return false;

                    // Use a non-reserved attribute name: an unprefixed `Name=` would be
                    // captured as an x:Name and emit a bogus codebehind field.
                    sb.Append('<').Append(RazorSectionElementName)
                      .Append(" __SectionName=\"").Append(EscapeXmlAttribute(sectionName))
                      .Append("\">");
                    blocks.Push(new RazorBlock { Kind = 'S' });
                    liftedAny = true;
                    i = brace + 1;
                    continue;
                }

                // @RenderSection ( "Name" [, ...] )  → a section host placeholder.
                if (IsWordAt(content, i + 1, "RenderSection", out var afterRs))
                {
                    var op = SkipWhitespace(content, afterRs);
                    if (op >= content.Length || content[op] != '(')
                        return false;
                    if (!TryReadBalancedParens(content, op, out var args, out var afterArgs))
                        return false;
                    var renderName = ExtractRenderSectionName(args);
                    if (renderName == null) return false; // non-literal name — cannot lower.

                    // Emit the real RazorSectionHost property name so the normal codegen
                    // path sets it directly (no RenderSection-specific codegen needed).
                    sb.Append('<').Append(RazorRenderSectionElementName)
                      .Append(" SectionName=\"").Append(EscapeXmlAttribute(renderName))
                      .Append("\" />");
                    liftedAny = true;
                    i = afterArgs;
                    continue;
                }

                // Stray @{ ... } or a leaked loop/flow keyword — cannot lower.
                if (content[i + 1] == '{' || IsLeakedFlowKeywordAt(content, i + 1))
                    return false;

                // Value-expression Razor (@id, @(expr)) — copy verbatim; SG lowers it.
                sb.Append(c);
                i++;
                continue;
            }

            // Text-level '}' closes the innermost open block. Mirrors the runtime parser,
            // which also treats a '}' in text content as the block terminator.
            if (c == '}' && blocks.Count > 0)
            {
                var frame = blocks.Pop();
                i++;
                if (frame.Kind != 'I')
                {
                    sb.Append("</").Append(RazorSectionElementName).Append('>');
                    continue;
                }

                sb.Append("</").Append(RazorIfElementName).Append('>');

                // `} else` / `} else if(c) {` — open the next mutually-exclusive branch
                // as a sibling <__RazorIf> whose Condition is the effective expression
                // computed exactly as the runtime (negate every prior chain condition,
                // AND the new one for else-if). FlattenRazorIf later wraps + combines
                // these with ancestors, reproducing BuildCombinedIfConditionExpression.
                var afterBrace = SkipWhitespace(content, i);
                if (!IsWordAt(content, afterBrace, "else", out var afterElse))
                    continue; // chain ends here.

                var p = SkipWhitespace(content, afterElse);
                if (IsWordAt(content, p, "if", out var afterElseIf))
                {
                    var ep = SkipWhitespace(content, afterElseIf);
                    if (ep >= content.Length || content[ep] != '(')
                        return false;
                    if (!TryReadBalancedParens(content, ep, out var elseIfCond, out var afterEp))
                        return false;
                    var eb = SkipWhitespace(content, afterEp);
                    if (eb >= content.Length || content[eb] != '{')
                        return false;

                    var ck = elseIfCond.Trim();
                    sb.Append('<').Append(RazorIfElementName)
                      .Append(" Condition=\"")
                      .Append(EscapeXmlAttribute(BuildElseIfEffectiveCondition(frame.Chain!, ck)))
                      .Append("\">");
                    var newChain = new System.Collections.Generic.List<string>(frame.Chain!) { ck };
                    blocks.Push(new RazorBlock { Kind = 'I', Chain = newChain });
                    liftedAny = true;
                    i = eb + 1;
                    continue;
                }

                if (p < content.Length && content[p] == '{')
                {
                    sb.Append('<').Append(RazorIfElementName)
                      .Append(" Condition=\"")
                      .Append(EscapeXmlAttribute(BuildElseEffectiveCondition(frame.Chain!)))
                      .Append("\">");
                    // `else` adds no new condition; a later (illegal) else would simply
                    // re-negate the same chain — harmless, mirrors the runtime.
                    blocks.Push(new RazorBlock { Kind = 'I', Chain = frame.Chain });
                    liftedAny = true;
                    i = p + 1;
                    continue;
                }

                // `else` not followed by `if(` or `{` — malformed; defer to runtime.
                return false;
            }

            sb.Append(c);
            i++;
        }

        if (blocks.Count != 0 || !liftedAny)
            return false;

        rewritten = sb.ToString();
        return true;
    }

    /// <summary>
    /// Extract the section name from <c>@RenderSection</c> arguments. Mirrors the runtime
    /// <c>RazorCodeBlockPreprocessor.ExtractSectionNameFromArgs</c>: the name is the first
    /// string literal (<c>"Name"</c>, optionally followed by <c>, required: false</c> /
    /// <c>, false</c>). Returns null when the first argument is not a string literal — a
    /// dynamic name can't be resolved at compile time, so the document defers to runtime.
    /// </summary>
    private static string? ExtractRenderSectionName(string args)
    {
        var t = args.Trim();
        if (t.Length < 2) return null;
        var q = t[0];
        if (q != '"' && q != '\'') return null;
        var end = t.IndexOf(q, 1);
        if (end <= 0) return null;
        return t.Substring(1, end - 1);
    }

    /// <summary>One open structural block in the lift scanner.</summary>
    private sealed class RazorBlock
    {
        /// <summary>'I' = @if branch, 'S' = @section.</summary>
        public char Kind;

        /// <summary>
        /// For an @if branch: every branch condition seen so far in this if/else-if chain
        /// (c1 .. c_k for the currently-open k-th branch), mirroring the runtime
        /// <c>RazorIfBlockEntry.ChainBranchConditions</c>. Null for @section.
        /// </summary>
        public System.Collections.Generic.List<string>? Chain;
    }

    /// <summary>
    /// Effective condition for an <c>else if(ck)</c> branch following branches whose
    /// conditions are <paramref name="priorChain"/> (c1..c_{k-1} ... actually the popped
    /// branch's full chain). Byte-identical to the runtime
    /// (<c>XamlReader</c>: <c>!(c1) &amp;&amp; !(c2) &amp;&amp; ... &amp;&amp; (ck)</c>, always joined).
    /// </summary>
    private static string BuildElseIfEffectiveCondition(System.Collections.Generic.List<string> priorChain, string ck)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var cond in priorChain)
        {
            sb.Append("!(").Append(cond).Append(") && ");
        }
        sb.Append('(').Append(ck).Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Effective condition for a plain <c>else</c> following branches
    /// <paramref name="priorChain"/>. Byte-identical to the runtime: a single prior
    /// condition is emitted as <c>!(c1)</c> (NOT joined); multiple as
    /// <c>!(c1) &amp;&amp; !(c2) &amp;&amp; ...</c>.
    /// </summary>
    private static string BuildElseEffectiveCondition(System.Collections.Generic.List<string> priorChain)
    {
        if (priorChain.Count == 1)
            return "!(" + priorChain[0] + ")";
        var sb = new System.Text.StringBuilder();
        for (var k = 0; k < priorChain.Count; k++)
        {
            if (k > 0) sb.Append(" && ");
            sb.Append("!(").Append(priorChain[k]).Append(')');
        }
        return sb.ToString();
    }

    private static bool StartsWith(string s, int pos, string token)
        => pos + token.Length <= s.Length && string.CompareOrdinal(s, pos, token, 0, token.Length) == 0;

    /// <summary>
    /// True if <paramref name="word"/> sits at <paramref name="pos"/> as a whole token
    /// (not a longer identifier — <c>@iffy</c> must not match <c>if</c>). On match
    /// <paramref name="after"/> is the index just past the word.
    /// </summary>
    private static bool IsWordAt(string s, int pos, string word, out int after)
    {
        after = pos;
        if (!StartsWith(s, pos, word))
            return false;
        var next = pos + word.Length;
        if (next < s.Length)
        {
            var n = s[next];
            if (n == '_' || char.IsLetterOrDigit(n))
                return false;
        }
        after = next;
        return true;
    }

    private static int SkipWhitespace(string s, int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        return pos;
    }

    private static readonly string[] s_leakedFlowKeywords =
    {
        // @if / @section / @RenderSection are lowered, not bailed. These are the
        // loop/flow keywords TransformJalxamlRazorTask should have build-time expanded;
        // if one leaks, the doc can't be faithfully lowered.
        "for", "foreach", "while", "switch", "do", "try", "using", "lock"
    };

    private static bool IsLeakedFlowKeywordAt(string content, int position)
    {
        foreach (var kw in s_leakedFlowKeywords)
        {
            if (IsWordAt(content, position, kw, out _))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Read a balanced <c>( ... )</c> starting at <paramref name="openParen"/> (which must
    /// index the '('). String / char literals inside are skipped so a ')' or '"' in the
    /// condition text doesn't end the scan early. <paramref name="inner"/> is the text
    /// between the outer parens; <paramref name="afterClose"/> is the index past ')'.
    /// </summary>
    private static bool TryReadBalancedParens(string s, int openParen, out string inner, out int afterClose)
    {
        inner = string.Empty;
        afterClose = openParen;
        var depth = 0;
        var start = openParen + 1;
        var i = openParen;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"' || c == '\'')
            {
                var q = c;
                i++;
                while (i < s.Length && s[i] != q)
                {
                    if (s[i] == '\\' && i + 1 < s.Length) i++;
                    i++;
                }
                if (i >= s.Length) return false; // unterminated literal
                i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    inner = s.Substring(start, i - start);
                    afterClose = i + 1;
                    return true;
                }
            }
            i++;
        }
        return false;
    }

    private static string EscapeXmlAttribute(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Combine an inherited (outer) <c>@if</c> condition with this level's condition,
    /// matching the runtime <c>XamlReader.BuildCombinedIfConditionExpression</c> shape:
    /// each level parenthesised, outermost first, joined by <c> &amp;&amp; </c>.
    /// </summary>
    private static string CombineRazorIfCondition(string? inherited, string own)
    {
        var wrapped = "(" + own.Trim() + ")";
        return string.IsNullOrEmpty(inherited) ? wrapped : inherited + " && " + wrapped;
    }

    private static string? GetRazorIfConditionAttribute(JalxamlAstNode node)
    {
        foreach (var attr in node.Attributes)
        {
            if (attr.Kind == JalxamlAttributeKind.Value &&
                string.Equals(attr.LocalName, "Condition", StringComparison.Ordinal))
            {
                return attr.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Flatten a synthetic <see cref="RazorIfElementName"/> subtree into
    /// <paramref name="target"/>: each real descendant element is appended directly and
    /// stamped with the combined condition; nested <see cref="RazorIfElementName"/> nodes
    /// recurse with the condition extended. The wrapper itself never reaches codegen.
    /// <para>
    /// Purity gate: a faithful per-child visibility binding requires the block to hold
    /// only child elements. If the wrapper captured block-level text or a property
    /// element, <see cref="JalxamlParseResult.RazorLiftUnfaithful"/> is set so
    /// <see cref="Parse"/> abandons the lift and reparses via the legacy path (identical
    /// to the pre-lift behaviour). Text/expressions <em>inside</em> a child element are
    /// that child's own content and are unaffected.
    /// </para>
    /// </summary>
    private static void FlattenRazorIf(JalxamlAstNode razorIf, string? inherited, List<JalxamlAstNode> target, JalxamlParseResult result)
    {
        if (!string.IsNullOrWhiteSpace(razorIf.TextContent) || razorIf.PropertyElements.Count > 0)
            result.RazorLiftUnfaithful = true;

        var combined = CombineRazorIfCondition(inherited, GetRazorIfConditionAttribute(razorIf) ?? "true");
        foreach (var child in razorIf.Children)
        {
            if (child.LocalName == RazorIfElementName)
            {
                FlattenRazorIf(child, combined, target, result);
            }
            else
            {
                // An element can only ever sit on one @if path so a straight assign is
                // correct (FlattenRazorIf is the single writer of RazorIfCondition).
                child.RazorIfCondition = combined;
                target.Add(child);
            }
        }
    }

    /// <summary>
    /// Walk the lifted tree and validate every <see cref="RazorSectionElementName"/>
    /// node: a compiled section factory feeds a single-Content
    /// <see cref="Jalium.UI.Markup.RazorSectionHost"/> (mirroring the runtime's
    /// <c>&lt;Border&gt;{body}&lt;/Border&gt;</c> single child), so the body must be
    /// exactly one element — no block-level text, no property element, and not itself a
    /// bare section. A violation sets <see cref="JalxamlParseResult.RazorLiftUnfaithful"/>
    /// so <see cref="Parse"/> drops the lift and the document renders via the legacy
    /// runtime path exactly as before.
    /// </summary>
    private static void ValidateLiftedSections(JalxamlAstNode node, JalxamlParseResult result)
    {
        if (node.LocalName == RazorSectionElementName)
        {
            var bodyRoots = 0;
            foreach (var c in node.Children)
            {
                if (c.LocalName == RazorSectionElementName)
                {
                    result.RazorLiftUnfaithful = true; // nested bare section — no visual root
                    break;
                }
                bodyRoots++;
            }
            if (bodyRoots != 1 ||
                !string.IsNullOrWhiteSpace(node.TextContent) ||
                node.PropertyElements.Count > 0)
            {
                result.RazorLiftUnfaithful = true;
            }
        }

        foreach (var child in node.Children)
            ValidateLiftedSections(child, result);
        foreach (var pe in node.PropertyElements)
            foreach (var child in pe.Children)
                ValidateLiftedSections(child, result);
    }
}
