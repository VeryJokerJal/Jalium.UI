using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Creates editor controls for property items based on their type.
/// Checks for custom editors registered on the owning <see cref="PropertyGrid"/> first,
/// then falls back to built-in editors for common types.
/// </summary>
internal class PropertyEditorSelector
{
    private static readonly HashSet<Type> s_numericTypes = new()
    {
        typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort),
        typeof(int), typeof(uint),
        typeof(long), typeof(ulong),
        typeof(float), typeof(double),
        typeof(decimal)
    };

    private static readonly Thickness s_editorMargin = new Thickness(4, 3, 8, 3);

    /// <summary>
    /// Creates an appropriate editor <see cref="FrameworkElement"/> for the given property item.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Numeric/string editors use TypeDescriptor.GetConverter for the property's runtime type — generic TypeConverters require their generic types to be DAM-preserved.")]
    public FrameworkElement CreateEditor(PropertyItem item, PropertyGrid owner)
    {
        // 1. Check for custom editors registered on the owner
        var customFactory = owner.GetCustomEditor(item.PropertyType);
        if (customFactory != null)
            return customFactory(item, owner);

        // 2. Boolean → CheckBox
        if (item.PropertyType == typeof(bool))
            return CreateBoolEditor(item, owner);

        // 3. Nullable<bool> → CheckBox with three-state
        if (item.PropertyType == typeof(bool?))
            return CreateNullableBoolEditor(item, owner);

        // 4. Enum → ComboBox
        if (item.PropertyType.IsEnum)
            return CreateEnumEditor(item, owner);

        // 5. Color → ColorPicker
        if (item.PropertyType == typeof(Color))
            return CreateColorEditor(item, owner);

        // 6. Numeric types → TextBox with validation
        if (s_numericTypes.Contains(item.PropertyType))
            return CreateNumericEditor(item, owner);

        // 7. Complex/expandable types → Expander with sub-properties
        if (item.IsExpandable && item.Value != null)
            return CreateExpandableEditor(item, owner);

        // 8. Default: string / anything else → TextBox
        return CreateStringEditor(item, owner);
    }

    private static Style? ResolveStyle(PropertyGrid owner, string key)
        => owner.TryFindResource(key) as Style;

    private static FrameworkElement CreateBoolEditor(PropertyItem item, PropertyGrid owner)
    {
        // Use the default CheckBox theme style; just adjust layout to fit the row.
        var checkBox = new CheckBox
        {
            IsChecked = item.Value is true,
            IsEnabled = !item.IsReadOnly && !owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = s_editorMargin
        };

        checkBox.Checked += (_, _) =>
        {
            var oldValue = item.Value;
            item.Value = true;
            owner.CommitPropertyValue(item, oldValue);
        };
        checkBox.Unchecked += (_, _) =>
        {
            var oldValue = item.Value;
            item.Value = false;
            owner.CommitPropertyValue(item, oldValue);
        };

        return checkBox;
    }

    private static FrameworkElement CreateNullableBoolEditor(PropertyItem item, PropertyGrid owner)
    {
        var checkBox = new CheckBox
        {
            IsThreeState = true,
            IsChecked = item.Value as bool?,
            IsEnabled = !item.IsReadOnly && !owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = s_editorMargin
        };

        checkBox.Checked += (_, _) =>
        {
            var oldValue = item.Value;
            item.Value = true;
            owner.CommitPropertyValue(item, oldValue);
        };
        checkBox.Unchecked += (_, _) =>
        {
            var oldValue = item.Value;
            item.Value = false;
            owner.CommitPropertyValue(item, oldValue);
        };
        checkBox.Indeterminate += (_, _) =>
        {
            var oldValue = item.Value;
            item.Value = null;
            owner.CommitPropertyValue(item, oldValue);
        };

        return checkBox;
    }

    private static FrameworkElement CreateEnumEditor(PropertyItem item, PropertyGrid owner)
    {
        // Use the default ComboBox theme style; local values compress the row.
        var comboBox = new ComboBox
        {
            IsEnabled = !item.IsReadOnly && !owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = s_editorMargin,
            MinHeight = 24,
            Padding = new Thickness(8, 2, 4, 2),
            FontSize = 12
        };

        // Enumerate enum members via the underlying-type overload so AOT does not need to
        // synthesize an array of the (runtime-only) enum type; box each raw value back to the
        // already-loaded enum Type via Enum.ToObject so ToString()/Equals behave identically.
        var rawValues = Enum.GetValuesAsUnderlyingType(item.PropertyType);
        var selectedIndex = -1;
        var index = 0;

        foreach (var raw in rawValues)
        {
            var enumValue = Enum.ToObject(item.PropertyType, raw!);
            comboBox.Items.Add(new ComboBoxItem { Content = enumValue.ToString(), Tag = enumValue });
            if (Equals(enumValue, item.Value))
                selectedIndex = index;
            index++;
        }

        comboBox.SelectedIndex = selectedIndex;

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedIndex >= 0 && comboBox.SelectedIndex < comboBox.Items.Count)
            {
                var selectedItem = comboBox.Items[comboBox.SelectedIndex] as ComboBoxItem;
                if (selectedItem?.Tag != null)
                {
                    var oldValue = item.Value;
                    item.Value = selectedItem.Tag;
                    owner.CommitPropertyValue(item, oldValue);
                }
            }
        };

        return comboBox;
    }

    private static FrameworkElement CreateColorEditor(PropertyItem item, PropertyGrid owner)
    {
        var colorPicker = new ColorPicker
        {
            Color = item.Value is Color c ? c : Color.White,
            IsEnabled = !item.IsReadOnly && !owner.IsReadOnly,
            IsCompact = true,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = s_editorMargin
        };

        colorPicker.ColorChanged += (_, e) =>
        {
            var oldValue = item.Value;
            item.Value = e.NewColor;
            owner.CommitPropertyValue(item, oldValue);
        };

        return colorPicker;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses TypeDescriptor.GetConverter for the property's runtime type — generic TypeConverters require their generic types to be DAM-preserved.")]
    private static FrameworkElement CreateNumericEditor(PropertyItem item, PropertyGrid owner)
    {
        var textBox = new TextBox
        {
            Text = item.Value?.ToString() ?? string.Empty,
            IsReadOnly = item.IsReadOnly || owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = s_editorMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = ResolveStyle(owner, "PropertyGridEditorTextBoxStyle")
        };

        textBox.LostFocus += (_, _) =>
        {
            var text = textBox.Text;
            var converter = TypeDescriptor.GetConverter(item.PropertyType);
            if (converter.IsValid(text))
            {
                var oldValue = item.Value;
                var newValue = converter.ConvertFromString(text);
                item.Value = newValue;
                owner.CommitPropertyValue(item, oldValue);
            }
            else
            {
                // Revert to current value
                textBox.Text = item.Value?.ToString() ?? string.Empty;
            }
        };

        return textBox;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses TypeDescriptor.GetConverter for the property's runtime type — generic TypeConverters require their generic types to be DAM-preserved.")]
    private static FrameworkElement CreateStringEditor(PropertyItem item, PropertyGrid owner)
    {
        var textBox = new TextBox
        {
            Text = item.Value?.ToString() ?? string.Empty,
            IsReadOnly = item.IsReadOnly || owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = s_editorMargin,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = ResolveStyle(owner, "PropertyGridEditorTextBoxStyle")
        };

        textBox.LostFocus += (_, _) =>
        {
            var oldValue = item.Value;
            if (item.PropertyType == typeof(string))
            {
                item.Value = textBox.Text;
            }
            else
            {
                var converter = TypeDescriptor.GetConverter(item.PropertyType);
                if (converter.CanConvertFrom(typeof(string)) && converter.IsValid(textBox.Text))
                {
                    item.Value = converter.ConvertFromString(textBox.Text);
                }
                else
                {
                    textBox.Text = item.Value?.ToString() ?? string.Empty;
                    return;
                }
            }
            owner.CommitPropertyValue(item, oldValue);
        };

        return textBox;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "owner.CreatePropertyRow carries the RUC contract \"Defers to CreatePropertyRowInternal which uses TypeDescriptor.GetConverter for the property's runtime type.\" That same contract is already declared at this selector's public entry point CreateEditor; this method is reached only via CreateEditor for the opt-in PropertyGrid inspector, whose consumers are documented as responsible for keeping their property converters preserved under AOT. The deferred call lives in an Expanded-event closure, so the suppression is hoisted to the enclosing method.")]
    private FrameworkElement CreateExpandableEditor(PropertyItem item, PropertyGrid owner)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };

        // Show the value's ToString as a read-only summary
        var summaryText = new TextBlock
        {
            Text = item.Value?.ToString() ?? "(null)",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 3, 8, 3),
            FontSize = 12
        };

        if (owner.TryFindResource("TextFillColorTertiaryBrush") is Brush tertiary)
        {
            summaryText.Foreground = tertiary;
        }
        else
        {
            summaryText.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140));
        }

        var expander = new Expander
        {
            Header = summaryText,
            IsExpanded = false,
            Margin = new Thickness(0)
        };

        // Build sub-properties lazily on first expand
        bool subPropertiesBuilt = false;
        var subPanel = new StackPanel { Orientation = Orientation.Vertical };

        expander.Expanded += (_, _) =>
        {
            if (!subPropertiesBuilt)
            {
                subPropertiesBuilt = true;
                item.BuildSubProperties();

                foreach (var subItem in item.SubProperties)
                {
                    var row = owner.CreatePropertyRow(subItem);
                    subPanel.Children.Add(row);
                }
            }
        };

        expander.Content = subPanel;
        panel.Children.Add(expander);

        return panel;
    }
}
