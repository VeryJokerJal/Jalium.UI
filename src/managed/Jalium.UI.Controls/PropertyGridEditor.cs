using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>A typed inspector editor with pending text, validation and replaceable templates.</summary>
[TemplatePart(Name = "PART_TextBox", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_Expander", Type = typeof(Expander))]
[TemplatePart(Name = "PART_ValueControl", Type = typeof(Control))]
[TemplatePart(Name = "PART_CustomContent", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_ActionButton", Type = typeof(ButtonBase))]
[TemplateVisualState(Name = "Display", GroupName = "EditingStates")]
[TemplateVisualState(Name = "Editing", GroupName = "EditingStates")]
[TemplateVisualState(Name = "Valid", GroupName = "ValidationStates")]
[TemplateVisualState(Name = "Invalid", GroupName = "ValidationStates")]
[TemplateVisualState(Name = "Editable", GroupName = "ReadOnlyStates")]
[TemplateVisualState(Name = "ReadOnly", GroupName = "ReadOnlyStates")]
public class PropertyGridEditor : Control
{
    static PropertyGridEditor() => DefaultStyleKeyProperty.OverrideMetadata(typeof(PropertyGridEditor), new PropertyMetadata(typeof(PropertyGridEditor)));
    public static readonly DependencyProperty PropertyProperty = DependencyProperty.Register(nameof(Property), typeof(PropertyItem), typeof(PropertyGridEditor), new PropertyMetadata(null, OnPropertyChanged));
    public PropertyItem? Property { get => (PropertyItem?)GetValue(PropertyProperty)!; set => SetValue(PropertyProperty, value); }
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(nameof(Owner), typeof(PropertyGrid), typeof(PropertyGridEditor), new PropertyMetadata(null, OnOwnerChanged));
    public PropertyGrid? Owner { get => (PropertyGrid?)GetValue(OwnerProperty)!; set => SetValue(OwnerProperty, value); }
    /// <summary>An optional command action owned by the current editor template.</summary>
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(PropertyGridEditor), new PropertyMetadata(null));
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public static readonly DependencyProperty ActionCommandParameterProperty = DependencyProperty.Register(nameof(ActionCommandParameter), typeof(object), typeof(PropertyGridEditor), new PropertyMetadata(null));
    public object? ActionCommandParameter { get => GetValue(ActionCommandParameterProperty); set => SetValue(ActionCommandParameterProperty, value); }
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(PropertyGridEditor), new PropertyMetadata(false, OnOptionsChanged));
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty)!; set => SetValue(IsReadOnlyProperty, value); }
    public static readonly DependencyProperty EditTextProperty = DependencyProperty.Register(nameof(EditText), typeof(string), typeof(PropertyGridEditor), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEditTextChanged));
    public string EditText { get => (string)GetValue(EditTextProperty)!; set => SetValue(EditTextProperty, value); }
    public static readonly DependencyProperty BooleanValueProperty = DependencyProperty.Register(nameof(BooleanValue), typeof(bool?), typeof(PropertyGridEditor), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBooleanChanged));
    public bool? BooleanValue { get => (bool?)GetValue(BooleanValueProperty)!; set => SetValue(BooleanValueProperty, value); }
    public static readonly DependencyProperty ColorValueProperty = DependencyProperty.Register(nameof(ColorValue), typeof(Color), typeof(PropertyGridEditor), new FrameworkPropertyMetadata(Color.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));
    public Color ColorValue { get => (Color)GetValue(ColorValueProperty)!; set => SetValue(ColorValueProperty, value); }
    public static readonly DependencyProperty SelectedEnumOptionProperty = DependencyProperty.Register(nameof(SelectedEnumOption), typeof(PropertyGridEnumOption), typeof(PropertyGridEditor), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnEnumChanged));
    public PropertyGridEnumOption? SelectedEnumOption { get => (PropertyGridEnumOption?)GetValue(SelectedEnumOptionProperty)!; set => SetValue(SelectedEnumOptionProperty, value); }
    private static readonly DependencyPropertyKey EditorKindPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EditorKind), typeof(PropertyGridEditorKind), typeof(PropertyGridEditor), new PropertyMetadata(PropertyGridEditorKind.Text));
    public static readonly DependencyProperty EditorKindProperty = EditorKindPropertyKey.DependencyProperty;
    public PropertyGridEditorKind EditorKind => (PropertyGridEditorKind)GetValue(EditorKindProperty)!;
    private static readonly DependencyPropertyKey IsNullablePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsNullable), typeof(bool), typeof(PropertyGridEditor), new PropertyMetadata(false));
    public static readonly DependencyProperty IsNullableProperty = IsNullablePropertyKey.DependencyProperty;
    public bool IsNullable => (bool)GetValue(IsNullableProperty)!;
    private static readonly DependencyPropertyKey EffectiveIsReadOnlyPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EffectiveIsReadOnly), typeof(bool), typeof(PropertyGridEditor), new PropertyMetadata(true));
    public static readonly DependencyProperty EffectiveIsReadOnlyProperty = EffectiveIsReadOnlyPropertyKey.DependencyProperty;
    public bool EffectiveIsReadOnly => (bool)GetValue(EffectiveIsReadOnlyProperty)!;
    private static readonly DependencyPropertyKey CanEditPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanEdit), typeof(bool), typeof(PropertyGridEditor), new PropertyMetadata(false));
    public static readonly DependencyProperty CanEditProperty = CanEditPropertyKey.DependencyProperty;
    public bool CanEdit => (bool)GetValue(CanEditProperty)!;
    private static readonly DependencyPropertyKey IsEditingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsEditing), typeof(bool), typeof(PropertyGridEditor), new PropertyMetadata(false));
    public static readonly DependencyProperty IsEditingProperty = IsEditingPropertyKey.DependencyProperty;
    public bool IsEditing => (bool)GetValue(IsEditingProperty)!;
    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasError), typeof(bool), typeof(PropertyGridEditor), new PropertyMetadata(false));
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;
    public bool HasError => (bool)GetValue(HasErrorProperty)!;
    private static readonly DependencyPropertyKey ErrorTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ErrorText), typeof(string), typeof(PropertyGridEditor), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ErrorTextProperty = ErrorTextPropertyKey.DependencyProperty;
    public string ErrorText => (string)GetValue(ErrorTextProperty)!;
    private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ValueText), typeof(string), typeof(PropertyGridEditor), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;
    public string ValueText => (string)GetValue(ValueTextProperty)!;
    private static readonly DependencyPropertyKey DisplayNamePropertyKey = DependencyProperty.RegisterReadOnly(nameof(DisplayName), typeof(string), typeof(PropertyGridEditor), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayNameProperty = DisplayNamePropertyKey.DependencyProperty;
    public string DisplayName => (string)GetValue(DisplayNameProperty)!;
    private static readonly DependencyPropertyKey EnumOptionsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EnumOptions), typeof(IReadOnlyList<PropertyGridEnumOption>), typeof(PropertyGridEditor), new PropertyMetadata(Array.Empty<PropertyGridEnumOption>()));
    public static readonly DependencyProperty EnumOptionsProperty = EnumOptionsPropertyKey.DependencyProperty;
    public IReadOnlyList<PropertyGridEnumOption> EnumOptions => (IReadOnlyList<PropertyGridEnumOption>)GetValue(EnumOptionsProperty)!;
    private static readonly DependencyPropertyKey SubPropertiesPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SubProperties), typeof(IReadOnlyList<PropertyItem>), typeof(PropertyGridEditor), new PropertyMetadata(Array.Empty<PropertyItem>()));
    public static readonly DependencyProperty SubPropertiesProperty = SubPropertiesPropertyKey.DependencyProperty;
    public IReadOnlyList<PropertyItem> SubProperties => (IReadOnlyList<PropertyItem>)GetValue(SubPropertiesProperty)!;
    private static readonly DependencyPropertyKey CustomContentPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CustomContent), typeof(object), typeof(PropertyGridEditor), new PropertyMetadata(null));
    public static readonly DependencyProperty CustomContentProperty = CustomContentPropertyKey.DependencyProperty;
    public object CustomContent => (object)GetValue(CustomContentProperty)!;
    private static readonly DependencyPropertyKey CustomTemplatePropertyKey = DependencyProperty.RegisterReadOnly(nameof(CustomTemplate), typeof(DataTemplate), typeof(PropertyGridEditor), new PropertyMetadata(null));
    public static readonly DependencyProperty CustomTemplateProperty = CustomTemplatePropertyKey.DependencyProperty;
    public DataTemplate CustomTemplate => (DataTemplate)GetValue(CustomTemplateProperty)!;
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(PropertyGridEditor), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnExpansionChanged));
    public bool IsExpanded { get => (bool)GetValue(IsExpandedProperty)!; set => SetValue(IsExpandedProperty, value); }
    private Control? _valueControl;
    private ContentPresenter? _customPresenter;
    private static readonly DependencyProperty OwnerReadOnlyProperty = DependencyProperty.Register("OwnerReadOnly", typeof(bool), typeof(PropertyGridEditor), new PropertyMetadata(false, OnOptionsChanged));
    private static readonly DependencyProperty OwnerEnabledProperty = DependencyProperty.Register("OwnerEnabled", typeof(bool), typeof(PropertyGridEditor), new PropertyMetadata(true, OnOptionsChanged));
    private static readonly DependencyProperty OwnerRevisionProperty = DependencyProperty.Register("OwnerRevision", typeof(int), typeof(PropertyGridEditor), new PropertyMetadata(0, OnRevisionChanged));
    private TextBox? _textBox;
    private Expander? _expander;
    private bool _hasTemplate;
    private bool _updating;
    private bool _committing;
    private bool _ownerActive;
    private bool _childrenBuilt;
    private bool _cannotReadValue;
    private DispatcherOperation? _pendingFocus;

    public PropertyGridEditor()
    {
        Loaded += (_, _) => Owner?.RegisterEditor(this);
        Unloaded += (_, _) => { Retire(); Owner?.UnregisterEditor(this); };
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _hasTemplate = true;
        _textBox = GetTemplateChild("PART_TextBox") as TextBox;
        _expander = GetTemplateChild("PART_Expander") as Expander;
        _valueControl = GetTemplateChild("PART_ValueControl") as Control;
        _customPresenter = GetTemplateChild("PART_CustomContent") as ContentPresenter;
        if (_textBox != null)
        {
            _textBox.GotFocus += OnTextGotFocus;
            _textBox.LostFocus += OnTextLostFocus;
            _textBox.AddHandler(KeyDownEvent, new KeyEventHandler(OnTextKeyDown));
        }
        if (IsExpanded) BuildChildren();
        Owner?.RegisterEditor(this);
        UpdateStates();
        if (IsEditing) FocusEditor();
    }
    protected override void OnTemplateChanging()
    {
        _hasTemplate = false;
        _pendingFocus?.Abort();
        _pendingFocus = null;
        if (_textBox != null)
        {
            _textBox.GotFocus -= OnTextGotFocus;
            _textBox.LostFocus -= OnTextLostFocus;
            _textBox.RemoveHandler(KeyDownEvent, new KeyEventHandler(OnTextKeyDown));
            BindingOperations.ClearBinding(_textBox, TextBox.TextProperty);
        }
        if (_expander != null) BindingOperations.ClearBinding(_expander, Expander.IsExpandedProperty);
        if (_valueControl is CheckBox checkBox) BindingOperations.ClearBinding(checkBox, ToggleButton.IsCheckedProperty);
        if (_valueControl is ComboBox comboBox) BindingOperations.ClearBinding(comboBox, Selector.SelectedItemProperty);
        if (_valueControl is ColorPicker colorPicker) BindingOperations.ClearBinding(colorPicker, ColorPicker.ColorProperty);
        if (_customPresenter != null)
        {
            BindingOperations.ClearBinding(_customPresenter, ContentPresenter.ContentProperty);
            BindingOperations.ClearBinding(_customPresenter, ContentPresenter.ContentTemplateProperty);
            _customPresenter.SetCurrentValue(ContentPresenter.ContentProperty, null);
        }
        _textBox = null;
        _expander = null;
        _valueControl = null;
        _customPresenter = null;
        base.OnTemplateChanging();
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateEditability();
    }
    internal void ActivateOwner() => _ownerActive = true;
    internal void Retire()
    {
        CancelEdit();
        _ownerActive = false;
        _pendingFocus?.Abort();
        _pendingFocus = null;
    }
    private bool CanCommit => CanEdit && IsEnabled && Property != null && (Owner == null || (_ownerActive && Owner.IsEnabled && Owner.OwnsProperty(Property)));
    public bool BeginEdit() => BeginEditCore(focusEditor: true);

    private bool BeginEditCore(bool focusEditor)
    {
        if (!CanCommit) return false;
        if (EditorKind != PropertyGridEditorKind.Text)
        {
            // Color templates may keep the mature picker collapsed until the
            // user edits the field, just as text templates defer their focus.
            if (EditorKind == PropertyGridEditorKind.Color && _expander != null) _expander.SetCurrentValue(Expander.IsExpandedProperty, true);
            return (_valueControl ?? _expander)?.Focus() == true;
        }
        if (!IsEditing)
        {
            SetValue(IsEditingPropertyKey, true);
            Owner?.NotifyEditingStarted(Property!);
        }
        UpdateStates();
        if (focusEditor) FocusEditor();
        return true;
    }
    internal void RestoreEdit(string text)
    {
        if (!BeginEdit()) return;
        SetCurrentValue(EditTextProperty, text);
    }
    public bool CommitEdit()
    {
        if (!IsEditing || !CanCommit || Property == null || _committing) return false;
        try
        {
            object? value;
            var type = Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;
            if (type == typeof(string)) value = EditText;
            else if (IsNullable && string.IsNullOrWhiteSpace(EditText)) value = null;
            else
            {
                // PropertyItem owns type conversion for reflected and descriptor
                // properties, including their nullable underlying type.
                value = EditText;
            }
            if (!TryAssign(value)) return false;
            EndEdit();
            return true;
        }
        catch (Exception ex) { SetError(ex.GetBaseException().Message); return false; }
    }
    public void CancelEdit()
    {
        _pendingFocus?.Abort();
        _pendingFocus = null;
        bool editing = IsEditing;
        SetValue(IsEditingPropertyKey, false);
        ClearError();
        RefreshValue();
        if (editing) Owner?.NotifyEditingEnded();
        UpdateStates();
    }
    private void EndEdit()
    {
        _pendingFocus?.Abort();
        _pendingFocus = null;
        bool editing = IsEditing;
        SetValue(IsEditingPropertyKey, false);
        RefreshValue();
        if (editing) Owner?.NotifyEditingEnded();
        UpdateStates();
    }
    private bool TryAssign(object? value)
    {
        if (_updating || !CanCommit || Property == null) return false;
        try
        {
            var old = Property.Value;
            _committing = true;
            Property.Value = value;
            ClearError();
            RefreshValue();
            Owner?.CommitPropertyValue(Property, old);
            return true;
        }
        catch (Exception ex) { SetError(ex.GetBaseException().Message); RefreshValue(); return false; }
        finally { _committing = false; }
    }
    private void CommitImmediate(object? value)
    {
        if (_updating || !CanCommit) return;
        Owner?.NotifyEditingStarted(Property!);
        TryAssign(value);
        Owner?.NotifyEditingEnded();
    }
    private void RefreshModel()
    {
        ClearError();
        _childrenBuilt = false;
        SetValue(SubPropertiesPropertyKey, Array.Empty<PropertyItem>());
        var item = Property;
        SetValue(DisplayNamePropertyKey, item?.DisplayName ?? string.Empty);
        SetValue(IsNullablePropertyKey, item != null && Nullable.GetUnderlyingType(item.PropertyType) != null);
        var kind = PropertyGridEditorKind.Text;
        DataTemplate? customTemplate = null;
        object? custom = null;
        try
        {
            kind = PropertyEditorSelector.GetKind(item);
            customTemplate = item != null ? Owner?.EditorTemplate ?? Owner?.EditorTemplateSelector?.SelectTemplate(item, this) : null;
            if (item != null && customTemplate == null && Owner?.GetCustomEditor(item.PropertyType) is { } factory) custom = factory(item, Owner);
        }
        catch (Exception ex) { SetError(ex.GetBaseException().Message); }
        SetValue(CustomTemplatePropertyKey, customTemplate!);
        SetValue(CustomContentPropertyKey, customTemplate != null ? item : custom);
        if (customTemplate != null || custom != null) kind = PropertyGridEditorKind.Custom;
        SetValue(EditorKindPropertyKey, kind);
        var options = new List<PropertyGridEnumOption>();
        if (kind == PropertyGridEditorKind.Enum && item != null)
        {
            if (IsNullable) options.Add(new(null, "(none)"));
            var type = Nullable.GetUnderlyingType(item.PropertyType) ?? item.PropertyType;
            foreach (var raw in Enum.GetValuesAsUnderlyingType(type))
            {
                var value = Enum.ToObject(type, raw!);
                options.Add(new(value, value.ToString()!));
            }
        }
        SetValue(EnumOptionsPropertyKey, options);
        RefreshValue();
        UpdateEditability();
        Owner?.RegisterEditor(this);
    }
    private void RefreshValue()
    {
        _updating = true;
        try
        {
            var value = Property?.Value;
            if (_cannotReadValue) ClearError();
            _cannotReadValue = false;
            var text = Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
            SetValue(ValueTextPropertyKey, value == null ? "(null)" : text);
            if (!IsEditing) SetCurrentValue(EditTextProperty, text);
            SetCurrentValue(BooleanValueProperty, value as bool?);
            SetCurrentValue(ColorValueProperty, value is Color color ? color : Color.White);
            SetCurrentValue(SelectedEnumOptionProperty, EnumOptions.FirstOrDefault(option => Equals(option.Value, value)));
        }
        catch (Exception ex) { _cannotReadValue = true; SetValue(ValueTextPropertyKey, "(unavailable)"); SetError(ex.GetBaseException().Message); }
        finally { _updating = false; }
        UpdateStates();
    }
    private void UpdateEditability()
    {
        bool readOnly = _cannotReadValue || IsReadOnly || (bool)GetValue(OwnerReadOnlyProperty)! || Property?.IsReadOnly != false || Property.HasReadOnlyValueTypeParent();
        SetValue(EffectiveIsReadOnlyPropertyKey, readOnly);
        SetValue(CanEditPropertyKey, !readOnly && IsEnabled && (bool)GetValue(OwnerEnabledProperty)!);
        if (!CanEdit && IsEditing) CancelEdit();
        UpdateStates();
    }
    private void OnPropertyValueChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "" or nameof(PropertyItem.Value))) return;
        if (!Dispatcher.CheckAccess())
        {
            var expected = Property;
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, () => { if (ReferenceEquals(Property, expected)) RefreshFromSource(); });
            return;
        }
        RefreshFromSource();
    }
    private void RefreshFromSource()
    {
        if (!_committing)
        {
            try
            {
                var kind = PropertyEditorSelector.GetKind(Property);
                if (EditorKind != PropertyGridEditorKind.Custom && kind != EditorKind) { RefreshModel(); return; }
            }
            catch (Exception ex) { _cannotReadValue = true; SetError(ex.GetBaseException().Message); }
        }
        RefreshValue();
        UpdateEditability();
        if (EditorKind == PropertyGridEditorKind.Expandable && !_committing) { _childrenBuilt = false; if (IsExpanded) BuildChildren(); }
    }
    private static void OnExpansionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { if ((bool)e.NewValue!) ((PropertyGridEditor)d).BuildChildren(); }
    private void BuildChildren()
    {
        if (_childrenBuilt || Property == null) return;
        try
        {
            // A repeated source reference is shown as a summary rather than allowing an
            // object cycle to create an unbounded inspector hierarchy.
            var value = Property.Value;
            for (var parent = Property; parent != null; parent = parent.ParentProperty)
                if (ReferenceEquals(value, parent.SourceObject)) { SetValue(SubPropertiesPropertyKey, Array.Empty<PropertyItem>()); return; }
            Property.BuildSubProperties();
            SetValue(SubPropertiesPropertyKey, Property.SubProperties.ToArray());
            _childrenBuilt = true;
        }
        catch (Exception ex) { SetValue(SubPropertiesPropertyKey, Array.Empty<PropertyItem>()); SetError(ex.GetBaseException().Message); }
    }
    private void OnTextGotFocus(object sender, RoutedEventArgs e) { if (!IsEditing) BeginEditCore(focusEditor: false); }
    private void OnTextLostFocus(object sender, RoutedEventArgs e) { if (IsEditing && !_committing) CommitEdit(); }
    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == Key.Enter) { CommitEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelEdit(); e.Handled = true; }
    }
    private void FocusEditor()
    {
        _pendingFocus?.Abort();
        if (_textBox == null || !IsEditing) return;
        var expected = _textBox;
        _pendingFocus = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _pendingFocus = null;
            if (IsEditing && ReferenceEquals(expected, _textBox)) { expected.Focus(); expected.SelectAll(); }
        });
    }
    private void SetError(string message) { SetValue(HasErrorPropertyKey, true); SetValue(ErrorTextPropertyKey, message); UpdateStates(); }
    private void ClearError() { SetValue(HasErrorPropertyKey, false); SetValue(ErrorTextPropertyKey, string.Empty); }
    private void UpdateStates()
    {
        if (!_hasTemplate) return;
        VisualStateManager.GoToState(this, IsEditing ? "Editing" : "Display", false);
        VisualStateManager.GoToState(this, HasError ? "Invalid" : "Valid", false);
        VisualStateManager.GoToState(this, EffectiveIsReadOnly ? "ReadOnly" : "Editable", false);
    }
    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (PropertyGridEditor)d;
        editor.CancelEdit();
        if (e.OldValue is PropertyItem old)
        {
            PropertyChangedEventManager.RemoveHandler(old, editor.OnPropertyValueChanged, string.Empty);
            editor.Owner?.UnregisterEditor(editor, old);
        }
        if (e.NewValue is PropertyItem item) PropertyChangedEventManager.AddHandler(item, editor.OnPropertyValueChanged, string.Empty);
        editor.RefreshModel();
    }
    private static void OnOwnerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (PropertyGridEditor)d;
        (e.OldValue as PropertyGrid)?.UnregisterEditor(editor);
        BindingOperations.ClearBinding(editor, OwnerReadOnlyProperty);
        BindingOperations.ClearBinding(editor, OwnerEnabledProperty);
        BindingOperations.ClearBinding(editor, OwnerRevisionProperty);
        if (editor.Owner != null)
        {
            editor.SetBinding(OwnerReadOnlyProperty, new Binding(nameof(PropertyGrid.IsReadOnly)) { Source = editor.Owner });
            editor.SetBinding(OwnerEnabledProperty, new Binding(nameof(PropertyGrid.IsEnabled)) { Source = editor.Owner });
            editor.SetBinding(OwnerRevisionProperty, new Binding(nameof(PropertyGrid.EditorRevision)) { Source = editor.Owner });
        }
        editor.RefreshModel();
    }
    private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGridEditor)d).UpdateEditability();
    private static void OnRevisionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (PropertyGridEditor)d;
        if (editor._ownerActive || !editor._hasTemplate) editor.RefreshModel();
    }
    private static void OnEditTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (PropertyGridEditor)d;
        if (editor._updating || editor._textBox?.IsKeyboardFocused != true) return;
        editor._pendingFocus?.Abort();
        editor._pendingFocus = null;
        if (!editor.IsEditing) editor.BeginEditCore(focusEditor: false);
    }
    private static void OnBooleanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGridEditor)d).CommitImmediate(e.NewValue);
    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGridEditor)d).CommitImmediate(e.NewValue);
    private static void OnEnumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PropertyGridEditor)d).CommitImmediate((e.NewValue as PropertyGridEnumOption)?.Value);
}
