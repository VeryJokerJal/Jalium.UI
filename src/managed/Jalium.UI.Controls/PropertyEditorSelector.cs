using System.ComponentModel;
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

    /// <summary>
    /// Creates an appropriate editor <see cref="FrameworkElement"/> for the given property item.
    /// </summary>
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

    private static FrameworkElement CreateBoolEditor(PropertyItem item, PropertyGrid owner)
    {
        var checkBox = new CheckBox
        {
            IsChecked = item.Value is true,
            IsEnabled = !item.IsReadOnly && !owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2)
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
            Margin = new Thickness(4, 2, 4, 2)
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
        var comboBox = new ComboBox
        {
            IsEnabled = !item.IsReadOnly && !owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var values = Enum.GetValues(item.PropertyType);
        var selectedIndex = -1;
        var index = 0;

        foreach (var enumValue in values)
        {
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
            Margin = new Thickness(4, 2, 4, 2)
        };

        colorPicker.ColorChanged += (_, e) =>
        {
            var oldValue = item.Value;
            item.Value = e.NewColor;
            owner.CommitPropertyValue(item, oldValue);
        };

        return colorPicker;
    }

    private static FrameworkElement CreateNumericEditor(PropertyItem item, PropertyGrid owner)
    {
        var textBox = new TextBox
        {
            Text = item.Value?.ToString() ?? string.Empty,
            IsReadOnly = item.IsReadOnly || owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch
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

    private static FrameworkElement CreateStringEditor(PropertyItem item, PropertyGrid owner)
    {
        var textBox = new TextBox
        {
            Text = item.Value?.ToString() ?? string.Empty,
            IsReadOnly = item.IsReadOnly || owner.IsReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch
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

    private FrameworkElement CreateExpandableEditor(PropertyItem item, PropertyGrid owner)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };

        // Show the value's ToString as a read-only summary
        var summaryText = new TextBlock
        {
            Text = item.Value?.ToString() ?? "(null)",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2),
            Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128))
        };

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
