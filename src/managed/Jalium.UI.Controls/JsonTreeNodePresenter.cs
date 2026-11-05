using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>Templated JSON node display and typed inline editor, independent of its tree container.</summary>
[TemplatePart(Name = "PART_ValueEditor", Type = typeof(TextBox))]
[TemplateVisualState(Name = "Display", GroupName = "EditingStates")]
[TemplateVisualState(Name = "Editing", GroupName = "EditingStates")]
[TemplateVisualState(Name = "Valid", GroupName = "ValidationStates")]
[TemplateVisualState(Name = "Invalid", GroupName = "ValidationStates")]
[TemplateVisualState(Name = "Matched", GroupName = "MatchStates")]
[TemplateVisualState(Name = "Unmatched", GroupName = "MatchStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "IndicatorVisible", GroupName = "IndicatorStates")]
[TemplateVisualState(Name = "IndicatorHidden", GroupName = "IndicatorStates")]
[TemplateVisualState(Name = "CountVisible", GroupName = "CountStates")]
[TemplateVisualState(Name = "CountHidden", GroupName = "CountStates")]
public class JsonTreeNodePresenter : Control
{
    static JsonTreeNodePresenter() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(JsonTreeNodePresenter), new PropertyMetadata(typeof(JsonTreeNodePresenter)));

    public static readonly DependencyProperty NodeProperty = DependencyProperty.Register(
        nameof(Node), typeof(JsonTreeNode), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnNodeChanged));
    public static readonly DependencyProperty ViewerProperty = DependencyProperty.Register(
        nameof(Viewer), typeof(JsonTreeViewer), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnViewerChanged));
    public static readonly DependencyProperty EditTextProperty = DependencyProperty.Register(
        nameof(EditText), typeof(string), typeof(JsonTreeNodePresenter),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable), typeof(bool), typeof(JsonTreeNodePresenter), new PropertyMetadata(false, OnPresentationChanged));
    public static readonly DependencyProperty ShowTypeIndicatorsProperty = DependencyProperty.Register(
        nameof(ShowTypeIndicators), typeof(bool), typeof(JsonTreeNodePresenter), new PropertyMetadata(true, OnPresentationChanged));
    public static readonly DependencyProperty ShowItemCountProperty = DependencyProperty.Register(
        nameof(ShowItemCount), typeof(bool), typeof(JsonTreeNodePresenter), new PropertyMetadata(true, OnPresentationChanged));
    public static readonly DependencyProperty ObjectBrushProperty = DependencyProperty.Register(
        nameof(ObjectBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty ArrayBrushProperty = DependencyProperty.Register(
        nameof(ArrayBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty StringBrushProperty = DependencyProperty.Register(
        nameof(StringBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty NumberBrushProperty = DependencyProperty.Register(
        nameof(NumberBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty BooleanBrushProperty = DependencyProperty.Register(
        nameof(BooleanBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty NullBrushProperty = DependencyProperty.Register(
        nameof(NullBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty KeyBrushProperty = DependencyProperty.Register(
        nameof(KeyBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty BracketBrushProperty = DependencyProperty.Register(
        nameof(BracketBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    public static readonly DependencyProperty SearchHighlightBrushProperty = DependencyProperty.Register(
        nameof(SearchHighlightBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null, OnPresentationChanged));
    private static readonly DependencyPropertyKey IsEditingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsEditing), typeof(bool), typeof(JsonTreeNodePresenter), new PropertyMetadata(false));
    public static readonly DependencyProperty IsEditingProperty = IsEditingPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasError), typeof(bool), typeof(JsonTreeNodePresenter), new PropertyMetadata(false));
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ErrorTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ErrorText), typeof(string), typeof(JsonTreeNodePresenter), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ErrorTextProperty = ErrorTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IndicatorTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IndicatorText), typeof(string), typeof(JsonTreeNodePresenter), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IndicatorTextProperty = IndicatorTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey KeyTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(KeyText), typeof(string), typeof(JsonTreeNodePresenter), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty KeyTextProperty = KeyTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey KeySeparatorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(KeySeparator), typeof(string), typeof(JsonTreeNodePresenter), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty KeySeparatorProperty = KeySeparatorPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ValueText), typeof(string), typeof(JsonTreeNodePresenter), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CountTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CountText), typeof(string), typeof(JsonTreeNodePresenter), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty CountTextProperty = CountTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ClosingTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ClosingText), typeof(string), typeof(JsonTreeNodePresenter), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ClosingTextProperty = ClosingTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey TypeBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(TypeBrush), typeof(Brush), typeof(JsonTreeNodePresenter), new PropertyMetadata(null));
    public static readonly DependencyProperty TypeBrushProperty = TypeBrushPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsMatchedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMatched), typeof(bool), typeof(JsonTreeNodePresenter), new PropertyMetadata(false));
    public static readonly DependencyProperty IsMatchedProperty = IsMatchedPropertyKey.DependencyProperty;
    public static readonly RoutedEvent ValueEditedEvent = JsonTreeViewer.NodeValueEditedEvent.AddOwner(typeof(JsonTreeNodePresenter));

    private TextBox? _editor;
    private bool _hasTemplate;
    private bool _endingEdit;
    private DispatcherOperation? _pendingFocus;
    private static readonly Brush FallbackBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE));

    public JsonTreeNodePresenter()
    {
        Loaded += (_, _) => Viewer?.RegisterNodePresenter(this);
        Unloaded += (_, _) => { _pendingFocus?.Abort(); Viewer?.UnregisterNodePresenter(Node, this); };
    }

    public JsonTreeNode? Node { get => (JsonTreeNode?)GetValue(NodeProperty); set => SetValue(NodeProperty, value); }
    public JsonTreeViewer? Viewer { get => (JsonTreeViewer?)GetValue(ViewerProperty); set => SetValue(ViewerProperty, value); }
    public string EditText { get => (string?)GetValue(EditTextProperty) ?? string.Empty; set => SetValue(EditTextProperty, value); }
    public bool IsEditable { get => (bool)GetValue(IsEditableProperty)!; set => SetValue(IsEditableProperty, value); }
    public bool ShowTypeIndicators { get => (bool)GetValue(ShowTypeIndicatorsProperty)!; set => SetValue(ShowTypeIndicatorsProperty, value); }
    public bool ShowItemCount { get => (bool)GetValue(ShowItemCountProperty)!; set => SetValue(ShowItemCountProperty, value); }
    public Brush? ObjectBrush { get => (Brush?)GetValue(ObjectBrushProperty); set => SetValue(ObjectBrushProperty, value); }
    public Brush? ArrayBrush { get => (Brush?)GetValue(ArrayBrushProperty); set => SetValue(ArrayBrushProperty, value); }
    public Brush? StringBrush { get => (Brush?)GetValue(StringBrushProperty); set => SetValue(StringBrushProperty, value); }
    public Brush? NumberBrush { get => (Brush?)GetValue(NumberBrushProperty); set => SetValue(NumberBrushProperty, value); }
    public Brush? BooleanBrush { get => (Brush?)GetValue(BooleanBrushProperty); set => SetValue(BooleanBrushProperty, value); }
    public Brush? NullBrush { get => (Brush?)GetValue(NullBrushProperty); set => SetValue(NullBrushProperty, value); }
    public Brush? KeyBrush { get => (Brush?)GetValue(KeyBrushProperty); set => SetValue(KeyBrushProperty, value); }
    public Brush? BracketBrush { get => (Brush?)GetValue(BracketBrushProperty); set => SetValue(BracketBrushProperty, value); }
    public Brush? SearchHighlightBrush { get => (Brush?)GetValue(SearchHighlightBrushProperty); set => SetValue(SearchHighlightBrushProperty, value); }
    public bool IsEditing => (bool)GetValue(IsEditingProperty)!;
    public bool HasError => (bool)GetValue(HasErrorProperty)!;
    public string ErrorText => (string)GetValue(ErrorTextProperty)!;
    public string IndicatorText => (string)GetValue(IndicatorTextProperty)!;
    public string KeyText => (string)GetValue(KeyTextProperty)!;
    public string KeySeparator => (string)GetValue(KeySeparatorProperty)!;
    public string ValueText => (string)GetValue(ValueTextProperty)!;
    public string CountText => (string)GetValue(CountTextProperty)!;
    public string ClosingText => (string)GetValue(ClosingTextProperty)!;
    public Brush? TypeBrush => (Brush?)GetValue(TypeBrushProperty)!;
    public bool IsMatched => (bool)GetValue(IsMatchedProperty)!;

    public event EventHandler<JsonTreeViewerNodeValueEditedEventArgs> ValueEdited
    {
        add => AddHandler(ValueEditedEvent, value);
        remove => RemoveHandler(ValueEditedEvent, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _hasTemplate = true;
        _editor = GetTemplateChild("PART_ValueEditor") as TextBox;
        if (_editor != null)
        {
            _editor.AddHandler(KeyDownEvent, new KeyEventHandler(OnEditorKeyDown));
            _editor.LostFocus += OnEditorLostFocus;
        }
        UpdatePresentation();
        Viewer?.RegisterNodePresenter(this);
        if (IsEditing) FocusEditor();
    }

    protected override void OnTemplateChanging()
    {
        _hasTemplate = false;
        _pendingFocus?.Abort();
        _pendingFocus = null;
        if (_editor != null)
        {
            _editor.RemoveHandler(KeyDownEvent, new KeyEventHandler(OnEditorKeyDown));
            _editor.LostFocus -= OnEditorLostFocus;
            BindingOperations.ClearBinding(_editor, TextBox.TextProperty);
        }
        _editor = null;
        base.OnTemplateChanging();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty)
        {
            if (!IsEnabled) CancelEdit();
            UpdateStates();
        }
    }

    /// <summary>Starts a pending edit without changing the node's value.</summary>
    public bool BeginEdit()
    {
        if (!IsEditable || !IsEnabled || Node == null || Node.NodeType is JsonNodeType.Object or JsonNodeType.Array) return false;
        if (IsEditing) { FocusEditor(); return true; }
        SetCurrentValue(EditTextProperty, Convert.ToString(Node.Value, CultureInfo.InvariantCulture) ?? (Node.NodeType == JsonNodeType.Null ? "null" : string.Empty));
        SetValue(HasErrorPropertyKey, false);
        SetValue(ErrorTextPropertyKey, string.Empty);
        SetValue(IsEditingPropertyKey, true);
        UpdateStates();
        FocusEditor();
        return true;
    }

    /// <summary>Commits a value of the existing JSON type; invalid input remains editable.</summary>
    public bool CommitEdit()
    {
        if (!IsEditing || _endingEdit || Node == null) return false;
        if (!TryParseValue(Node.NodeType, EditText, out var value))
        {
            SetValue(HasErrorPropertyKey, true);
            SetValue(ErrorTextPropertyKey, $"Enter a valid {Node.NodeType.ToString().ToLowerInvariant()} value.");
            UpdateStates();
            return false;
        }
        var node = Node;
        var previous = node.Value;
        _endingEdit = true;
        try
        {
            SetValue(IsEditingPropertyKey, false);
            SetValue(HasErrorPropertyKey, false);
            SetValue(ErrorTextPropertyKey, string.Empty);
            node.Value = value;
            UpdatePresentation();
        }
        finally { _endingEdit = false; }
        Viewer?.FocusNode(node);
        RaiseEvent(new JsonTreeViewerNodeValueEditedEventArgs(ValueEditedEvent, node, previous, value));
        return true;
    }

    /// <summary>Discards pending input, preserving changes made directly to the model.</summary>
    public void CancelEdit()
    {
        _pendingFocus?.Abort();
        _pendingFocus = null;
        _endingEdit = true;
        try
        {
            SetValue(IsEditingPropertyKey, false);
            SetValue(HasErrorPropertyKey, false);
            SetValue(ErrorTextPropertyKey, string.Empty);
            UpdateStates();
        }
        finally { _endingEdit = false; }
    }

    private void FocusEditor()
    {
        _pendingFocus?.Abort();
        if (_editor == null || !IsEditing) return;
        var editor = _editor;
        _pendingFocus = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _pendingFocus = null;
            if (IsEditing && ReferenceEquals(editor, _editor)) { editor.Focus(); editor.SelectAll(); }
        });
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == Key.Enter) { CommitEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelEdit(); if (Node != null) Viewer?.FocusNode(Node); e.Handled = true; }
    }
    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (IsEditing && !_endingEdit) CommitEdit();
    }

    private void UpdatePresentation()
    {
        var node = Node;
        SetValue(KeyTextPropertyKey, node?.Key ?? string.Empty);
        SetValue(KeySeparatorPropertyKey, node?.Key == null ? string.Empty : ": ");
        SetValue(IndicatorTextPropertyKey, node?.NodeType switch
        {
            JsonNodeType.Object => "{}", JsonNodeType.Array => "[]", JsonNodeType.String => "ab",
            JsonNodeType.Number => "#", JsonNodeType.Boolean => "tf", JsonNodeType.Null => "--", _ => string.Empty
        });
        SetValue(ValueTextPropertyKey, node?.NodeType switch
        {
            JsonNodeType.Object => "{", JsonNodeType.Array => "[", _ => node?.DisplayValue ?? string.Empty
        });
        SetValue(ClosingTextPropertyKey, node?.NodeType switch { JsonNodeType.Object => "}", JsonNodeType.Array => "]", _ => string.Empty });
        SetValue(CountTextPropertyKey, node?.NodeType switch
        {
            JsonNodeType.Object => $" {node.ChildCount} properties ", JsonNodeType.Array => $" {node.ChildCount} items ", _ => string.Empty
        });
        var brush = node?.NodeType switch
        {
            JsonNodeType.Object => ObjectBrush, JsonNodeType.Array => ArrayBrush, JsonNodeType.String => StringBrush,
            JsonNodeType.Number => NumberBrush, JsonNodeType.Boolean => BooleanBrush, JsonNodeType.Null => NullBrush, _ => Foreground
        };
        SetValue(TypeBrushPropertyKey, brush ?? (node != null ? Viewer?.GetBrushForType(node.NodeType) : null) ?? FallbackBrush);
        SetValue(IsMatchedPropertyKey, node?.IsMatchedBySearch == true);
        UpdateStates();
    }
    private void UpdateStates()
    {
        if (!_hasTemplate) return;
        VisualStateManager.GoToState(this, IsEditing ? "Editing" : "Display", false);
        VisualStateManager.GoToState(this, HasError ? "Invalid" : "Valid", false);
        VisualStateManager.GoToState(this, IsMatched ? "Matched" : "Unmatched", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, ShowTypeIndicators ? "IndicatorVisible" : "IndicatorHidden", false);
        VisualStateManager.GoToState(this, ShowItemCount ? "CountVisible" : "CountHidden", false);
    }
    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdatePresentation();
    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdatePresentation();
    private static void OnNodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (JsonTreeNodePresenter)d;
        row.CancelEdit();
        if (e.OldValue is JsonTreeNode oldNode)
        {
            PropertyChangedEventManager.RemoveHandler(oldNode, row.OnNodePropertyChanged, string.Empty);
            CollectionChangedEventManager.RemoveHandler(oldNode.Children, row.OnChildrenChanged);
            row.Viewer?.UnregisterNodePresenter(oldNode, row);
        }
        if (e.NewValue is JsonTreeNode newNode)
        {
            PropertyChangedEventManager.AddHandler(newNode, row.OnNodePropertyChanged, string.Empty);
            CollectionChangedEventManager.AddHandler(newNode.Children, row.OnChildrenChanged);
        }
        row.UpdatePresentation();
        row.Viewer?.RegisterNodePresenter(row);
    }
    private static void OnViewerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (JsonTreeNodePresenter)d;
        (e.OldValue as JsonTreeViewer)?.UnregisterNodePresenter(row.Node, row);
        row.UpdatePresentation();
        row.Viewer?.RegisterNodePresenter(row);
    }
    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (JsonTreeNodePresenter)d;
        if (!row.IsEditable) row.CancelEdit();
        row.UpdatePresentation();
        if (e.Property == IsEditableProperty && row.IsEditable) row.Viewer?.RegisterNodePresenter(row);
    }
    private static bool TryParseValue(JsonNodeType type, string text, out object? value)
    {
        value = null;
        switch (type)
        {
            case JsonNodeType.String: value = text; return true;
            case JsonNodeType.Null: return string.Equals(text.Trim(), "null", StringComparison.OrdinalIgnoreCase);
            case JsonNodeType.Boolean:
                if (bool.TryParse(text, out var boolean)) { value = boolean; return true; }
                return false;
            case JsonNodeType.Number:
                try
                {
                    using var document = JsonDocument.Parse(text);
                    var number = document.RootElement;
                    if (number.ValueKind != JsonValueKind.Number) return false;
                    if (number.TryGetInt64(out var integer)) { value = integer; return true; }
                    if (number.TryGetDouble(out var real) && double.IsFinite(real)) { value = real; return true; }
                    return false;
                }
                catch (JsonException) { return false; }
            default: return false;
        }
    }
}
