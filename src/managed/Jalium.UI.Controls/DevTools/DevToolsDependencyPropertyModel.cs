using System.ComponentModel;
using System.Collections;
using System.Collections.Specialized;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

public enum DevToolsPropertyOrigin { Default, Local, Style, Template, Inherited, Animated, Unavailable }

/// <summary>Live, weak target data that adapts a dependency property to the standard inspector editor.</summary>
/// <remarks>
/// Complex values use the framework ValueSerializer contract. Consumers must
/// preserve their custom converters when trimming. Dispose releases all source
/// and value subscriptions; neither the target nor its current value is owned.
/// </remarks>
public sealed class DevToolsDependencyPropertyModel : INotifyPropertyChanged, IDisposable
{
    private readonly WeakReference<DependencyObject> _target;
    private readonly Action<DependencyProperty, object?, object?> _changed;
    private readonly Action<DependencyProperty> _sourceChanged;
    private bool _disposed;
    private string _sourceText = string.Empty;
    private DevToolsPropertyOrigin _origin;
    private readonly string? _category;
    private readonly string? _displayName;
    private readonly ValueSerializer? _serializer;
    private Representation _representation;
    private Type _editorType;
    private Color? _previewColor;
    private WeakReference<object>? _observedValue;
    private EventHandler? _freezableChanged;
    private NotifyCollectionChangedEventHandler? _collectionChanged;
    private enum Representation { Raw, SolidBrush, Serialized, Summary }
    static DevToolsDependencyPropertyModel()
    {
        PropertyAccessorRegistry.Register(typeof(DevToolsDependencyPropertyModel), nameof(EditorProperty), value => ((DevToolsDependencyPropertyModel)value).EditorProperty);
        PropertyAccessorRegistry.Register(typeof(DevToolsDependencyPropertyModel), nameof(SourceText), value => ((DevToolsDependencyPropertyModel)value).SourceText);
        PropertyAccessorRegistry.Register(typeof(DevToolsDependencyPropertyModel), nameof(Origin), value => ((DevToolsDependencyPropertyModel)value).Origin);
        PropertyAccessorRegistry.Register(typeof(DevToolsDependencyPropertyModel), nameof(PreviewColor), value => ((DevToolsDependencyPropertyModel)value).PreviewColor);
    }
    public DevToolsDependencyPropertyModel(DependencyObject target, DependencyProperty property, string? category = null)
        : this(target, property, category, null) { }
    public DevToolsDependencyPropertyModel(DependencyObject target, DependencyProperty property, string? category, string? displayName)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(property);
        _target = new(target); Property = property;
        _category = category; _displayName = displayName;
        try { _serializer = ValueSerializer.GetSerializerFor(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType); }
        catch { /* Unknown consumer converters remain a read-only diagnostic value. */ }
        var weak = new WeakReference<DevToolsDependencyPropertyModel>(this);
        var targetReference = _target;
        Action<DependencyProperty, object?, object?>? handler = null;
        handler = (changed, _, _) =>
        {
            if (weak.TryGetTarget(out var owner)) { if (changed == owner.Property && !owner._disposed) owner.Refresh(); }
            else if (targetReference.TryGetTarget(out var source)) source.PropertyChangedInternal -= handler;
        };
        _changed = handler;
        Action<DependencyProperty>? sourceHandler = null;
        sourceHandler = changed =>
        {
            if (weak.TryGetTarget(out var owner)) { if (changed == owner.Property && !owner._disposed) owner.Refresh(notifyValue: false); }
            else if (targetReference.TryGetTarget(out var source)) source.ValueSourceInvalidatedInternal -= sourceHandler;
        };
        _sourceChanged = sourceHandler;
        var initialValue = TryReadValue();
        _representation = SelectRepresentation(initialValue); _editorType = EditorType(initialValue, _representation);
        EditorProperty = CreateEditorProperty();
        target.PropertyChangedInternal += handler;
        target.ValueSourceInvalidatedInternal += sourceHandler;
        Refresh();
    }
    public DependencyProperty Property { get; }
    /// <summary>The current editor representation; changes are notified when a runtime value requires a different editor type.</summary>
    public PropertyItem EditorProperty { get; private set; }
    public string SourceText => _sourceText;
    public DevToolsPropertyOrigin Origin => _origin;
    /// <summary>A value-only preview that does not retain the inspected brush.</summary>
    public Color? PreviewColor => _previewColor;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal bool TryGetTarget([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DependencyObject? target)
    {
        target = null;
        return !_disposed && _target.TryGetTarget(out target);
    }
    private DependencyObject Target()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _target.TryGetTarget(out var target) ? target : throw new InvalidOperationException("The inspected element is no longer available.");
    }
    private void Refresh(bool notifyValue = true)
    {
        var previousText = _sourceText; var previousOrigin = _origin;
        var previousColor = _previewColor;
        bool editorChanged = false;
        if (!_disposed && _target.TryGetTarget(out var target))
        {
            var value = TryReadValue(); ObserveValue(value);
            var representation = SelectRepresentation(value);
            var editorType = EditorType(value, representation);
            if (representation != _representation || editorType != _editorType)
            {
                _representation = representation; _editorType = editorType; EditorProperty = CreateEditorProperty(); editorChanged = true;
            }
            _previewColor = value is SolidColorBrush brush ? brush.Color : value is Color color ? color : null;
            try
            {
                var source = DependencyPropertyHelper.GetValueSource(target, Property);
                _sourceText = source.BaseValueSource + (source.IsExpression ? " · bound" : "") + (source.IsAnimated ? " · animated" : "") + (source.IsCoerced ? " · coerced" : "");
                _origin = source.IsAnimated ? DevToolsPropertyOrigin.Animated : source.BaseValueSource switch
                {
                    BaseValueSource.Local => DevToolsPropertyOrigin.Local,
                    BaseValueSource.Style or BaseValueSource.StyleTrigger or BaseValueSource.DefaultStyle or BaseValueSource.DefaultStyleTrigger or BaseValueSource.ImplicitStyleReference => DevToolsPropertyOrigin.Style,
                    BaseValueSource.ParentTemplate or BaseValueSource.ParentTemplateTrigger or BaseValueSource.TemplateTrigger => DevToolsPropertyOrigin.Template,
                    BaseValueSource.Inherited => DevToolsPropertyOrigin.Inherited,
                    _ => DevToolsPropertyOrigin.Default,
                };
            }
            catch { _sourceText = "Unavailable"; _origin = DevToolsPropertyOrigin.Unavailable; }
        }
        else { _sourceText = "Unavailable"; _origin = DevToolsPropertyOrigin.Unavailable; _previewColor = null; }
        if (editorChanged) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditorProperty)));
        if (notifyValue) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Property.Name));
        if (_sourceText != previousText) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SourceText)));
        if (_origin != previousOrigin) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Origin)));
        if (_previewColor != previousColor) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewColor)));
    }
    private object? TryReadValue()
    {
        try { return Target().GetValue(Property); }
        catch { return null; } // The actual editor read retains the exception and displays its error.
    }
    private Representation SelectRepresentation(object? value)
    {
        var type = Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Color) || value is Enum) return Representation.Raw;
        if (value is SolidColorBrush && type.IsAssignableFrom(typeof(SolidColorBrush))) return Representation.SolidBrush;
        try
        {
            if (_serializer != null && _serializer.CanConvertFromString(string.Empty, null) && (value == null || _serializer.CanConvertToString(value, null))) return Representation.Serialized;
        }
        catch { }
        return Representation.Summary;
    }
    private Type EditorType(object? value, Representation representation) => representation switch
    {
        Representation.Raw => (Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType).IsEnum
            ? Property.PropertyType : value is Enum ? value.GetType() : Property.PropertyType,
        Representation.SolidBrush => typeof(Color), _ => typeof(string),
    };
    private PropertyItem CreateEditorProperty() => new(this, new SourceDescriptor(Property, _category, _displayName, _representation, _editorType));
    private void ObserveValue(object? value)
    {
        if (_observedValue?.TryGetTarget(out var previous) == true && ReferenceEquals(previous, value)) return;
        DetachValue();
        if (value is not Freezable && value is not INotifyCollectionChanged) return;
        var weak = new WeakReference<DevToolsDependencyPropertyModel>(this);
        var weakValue = new WeakReference<object>(value); _observedValue = weakValue;
        if (value is Freezable freezable)
        {
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                if (weak.TryGetTarget(out var owner) && !owner._disposed) owner.Refresh();
                else if (weakValue.TryGetTarget(out var source) && source is Freezable old) old.Changed -= handler;
            };
            _freezableChanged = handler; freezable.Changed += handler;
        }
        if (value is INotifyCollectionChanged collection)
        {
            NotifyCollectionChangedEventHandler? handler = null;
            handler = (_, _) =>
            {
                if (weak.TryGetTarget(out var owner) && !owner._disposed) owner.Refresh();
                else if (weakValue.TryGetTarget(out var source) && source is INotifyCollectionChanged old) old.CollectionChanged -= handler;
            };
            _collectionChanged = handler; collection.CollectionChanged += handler;
        }
    }
    private void DetachValue()
    {
        if (_observedValue?.TryGetTarget(out var value) == true)
        {
            if (value is Freezable freezable && _freezableChanged != null) freezable.Changed -= _freezableChanged;
            if (value is INotifyCollectionChanged collection && _collectionChanged != null) collection.CollectionChanged -= _collectionChanged;
        }
        _observedValue = null; _freezableChanged = null; _collectionChanged = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachValue();
        if (_target.TryGetTarget(out var target))
        {
            target.PropertyChangedInternal -= _changed;
            target.ValueSourceInvalidatedInternal -= _sourceChanged;
        }
        Refresh();
    }
    private sealed class SourceDescriptor(DependencyProperty property, string? category, string? displayName, Representation representation, Type editorType) : PropertyDescriptor(property.Name,
        DescriptorAttributes(category, displayName))
    {
        private static Attribute[] DescriptorAttributes(string? category, string? displayName)
        {
            var attributes = new List<Attribute>(2);
            if (category != null) attributes.Add(new CategoryAttribute(category));
            if (displayName != null) attributes.Add(new DisplayNameAttribute(displayName));
            return attributes.ToArray();
        }
        public override Type ComponentType => typeof(DevToolsDependencyPropertyModel);
        public override Type PropertyType => editorType;
        public override bool IsReadOnly => property.ReadOnly || representation == Representation.Summary;
        public override object? GetValue(object? component)
        {
            var model = (DevToolsDependencyPropertyModel)component!; var value = model.Target().GetValue(property);
            return representation switch
            {
                Representation.Raw => value,
                Representation.SolidBrush => value is SolidColorBrush brush ? brush.Color : throw new InvalidOperationException("The property editor representation changed."),
                Representation.Serialized => value == null ? null : model._serializer!.ConvertToString(value, null),
                _ => value switch
                {
                    null => null, DependencyObject dependencyObject => dependencyObject.GetType().Name,
                    Type type => type.Name, ICollection collection => $"{value.GetType().Name} ({collection.Count})",
                    IEnumerable => value.GetType().Name, _ => value.ToString() ?? value.GetType().Name,
                },
            };
        }
        public override void SetValue(object? component, object? value)
        {
            var model = (DevToolsDependencyPropertyModel)component!;
            if (IsReadOnly) throw new InvalidOperationException("The inspected property is read-only.");
            if (representation != model._representation || editorType != model._editorType) throw new InvalidOperationException("The property editor representation changed.");
            var target = model.Target();
            if (representation == Representation.SolidBrush)
            {
                var brush = (SolidColorBrush)((SolidColorBrush)target.GetValue(property)!).CloneCurrentValue();
                brush.Color = (Color)value!; value = brush;
            }
            else if (representation == Representation.Serialized)
            {
                string text = (string?)value ?? string.Empty;
                value = string.IsNullOrWhiteSpace(text) && (!property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) != null)
                    ? null : model._serializer!.ConvertFromString(text, null);
            }
            // Preserve the platform attached setters' normalization rather than
            // bypassing their span contract through the generic DP store.
            if (target is UIElement element && property == Grid.RowSpanProperty) Grid.SetRowSpan(element, (int)value!);
            else if (target is UIElement columnElement && property == Grid.ColumnSpanProperty) Grid.SetColumnSpan(columnElement, (int)value!);
            else target.SetValue(property, value);
            model.Refresh();
        }
        public override bool CanResetValue(object component) => !property.ReadOnly && ((DevToolsDependencyPropertyModel)component).Target().HasLocalValue(property);
        public override void ResetValue(object component)
        {
            var model = (DevToolsDependencyPropertyModel)component;
            if (property.ReadOnly) throw new InvalidOperationException("The inspected property is read-only.");
            model.Target().ClearValue(property); model.Refresh();
        }
        public override bool ShouldSerializeValue(object component) => CanResetValue(component);
    }
}
