using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ComboBoxEditableTests
{
    [Fact]
    public void ComboBox_IsEditable_DependencyProperty_ShouldExist()
    {
        var field = typeof(ComboBox).GetField("IsEditableProperty", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(typeof(DependencyProperty), field!.FieldType);

        var dp = field.GetValue(null) as DependencyProperty;
        Assert.NotNull(dp);
        Assert.Equal("IsEditable", dp!.Name);
        Assert.Equal(typeof(bool), dp.PropertyType);
    }

    [Fact]
    public void ComboBox_IsEditable_ShouldBeSettable()
    {
        var comboBox = new ComboBox();

        comboBox.IsEditable = true;

        Assert.True(comboBox.IsEditable);
    }

    [Fact]
    public void ComboBox_IsEditable_True_ShouldShowEditableTextBoxWithBoundText()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                IsEditable = true,
                Text = "Custom Value"
            };

            var host = new StackPanel
            {
                Width = 400,
                Height = 300
            };

            host.Children.Add(comboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));

            var editableTextBox = FindDescendant<TextBox>(comboBox);

            Assert.NotNull(editableTextBox);
            Assert.Equal(Visibility.Visible, editableTextBox!.Visibility);
            Assert.Equal("Custom Value", editableTextBox.Text);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ComboBox_IsEditable_False_ShouldKeepEditableTextBoxCollapsed()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                IsEditable = false
            };

            var host = new StackPanel
            {
                Width = 400,
                Height = 300
            };

            host.Children.Add(comboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));

            var editableTextBox = FindDescendant<TextBox>(comboBox);

            Assert.NotNull(editableTextBox);
            Assert.Equal(Visibility.Collapsed, editableTextBox!.Visibility);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ComboBox_IsEditable_MouseDownOnComboBody_ShouldFocusEditableTextBoxInsteadOfOpeningDropDown()
    {
        ResetApplicationState();
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();

        var app = new Application();

        try
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                IsEditable = true
            };

            var host = new StackPanel
            {
                Width = 400,
                Height = 300
            };

            host.Children.Add(comboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));

            var editableTextBox = FindDescendant<TextBox>(comboBox);

            Assert.NotNull(editableTextBox);

            editableTextBox!.Text = "Hello";
            editableTextBox.CaretIndex = editableTextBox.Text.Length;
            comboBox.RaiseEvent(CreateMouseDown(new Point(8, 8)));

            Assert.False(comboBox.IsDropDownOpen);
            Assert.True(editableTextBox.IsKeyboardFocused);
            Assert.Equal(0, editableTextBox.CaretIndex);
            Assert.True(comboBox.IsKeyboardFocusWithin);
        }
        finally
        {
            Keyboard.ClearFocus();
            UIElement.ForceReleaseMouseCapture();
            ResetApplicationState();
        }
    }

    [Fact]
    public void ComboBox_IsEditable_MouseDownOnRightEdge_ShouldOpenDropDown()
    {
        ResetApplicationState();
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();

        var app = new Application();

        try
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                IsEditable = true
            };

            comboBox.Items.Add("One");
            comboBox.Items.Add("Two");

            var host = new StackPanel
            {
                Width = 400,
                Height = 300
            };

            host.Children.Add(comboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));

            comboBox.RaiseEvent(CreateMouseDown(new Point(216, 8)));

            Assert.True(comboBox.IsDropDownOpen);
        }
        finally
        {
            Keyboard.ClearFocus();
            UIElement.ForceReleaseMouseCapture();
            ResetApplicationState();
        }
    }

    [Fact]
    public void ComboBox_IsEditable_Focus_ShouldRedirectToEditableTextBox()
    {
        ResetApplicationState();
        Keyboard.Initialize();
        Keyboard.ClearFocus();

        var app = new Application();

        try
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                IsEditable = true
            };

            var host = new StackPanel
            {
                Width = 400,
                Height = 300
            };

            host.Children.Add(comboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));

            var editableTextBox = FindDescendant<TextBox>(comboBox);

            Assert.NotNull(editableTextBox);
            comboBox.Focus();
            Assert.True(editableTextBox!.IsKeyboardFocused);
            Assert.Same(editableTextBox, Keyboard.FocusedElement);
            Assert.False(comboBox.IsKeyboardFocused);
            Assert.True(comboBox.IsKeyboardFocusWithin);
        }
        finally
        {
            Keyboard.ClearFocus();
            ResetApplicationState();
        }
    }

    [Fact]
    public void ComboBox_IsEditable_ShouldMatchNonEditableHeight()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var normalComboBox = new ComboBox
            {
                Width = 220,
                PlaceholderText = "Normal"
            };

            var editableComboBox = new ComboBox
            {
                Width = 220,
                IsEditable = true,
                Text = "Editable"
            };

            var host = new StackPanel
            {
                Width = 400,
                Height = 300
            };

            host.Children.Add(normalComboBox);
            host.Children.Add(editableComboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));

            Assert.Equal(normalComboBox.RenderSize.Height, editableComboBox.RenderSize.Height, 3);

            var editableTextBox = FindDescendant<TextBox>(editableComboBox);
            Assert.NotNull(editableTextBox);
            Assert.Equal(0, editableTextBox!.MinHeight);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ComboBox_IsEditable_ShouldUseIBeamCursor_WhileToggleUsesArrow()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                IsEditable = true
            };

            var host = new StackPanel
            {
                Width = 400,
                Height = 300
            };

            host.Children.Add(comboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));

            var toggleButton = FindDescendant<ToggleButton>(comboBox);
            var dropDownArea = FindNamedDescendant<Grid>(comboBox, "PART_DropDownArea");

            Assert.Same(Jalium.UI.Cursors.IBeam, comboBox.Cursor);
            Assert.NotNull(toggleButton);
            Assert.NotNull(dropDownArea);
            Assert.Same(Jalium.UI.Cursors.Arrow, dropDownArea!.Cursor);
            Assert.Same(Jalium.UI.Cursors.Arrow, toggleButton!.Cursor);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ComboBox_IsEditable_False_ShouldHideDropDownDivider()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var comboBox = new ComboBox
            {
                Width = 220,
                IsEditable = false
            };

            var host = new StackPanel
            {
                Width = 400,
                Height = 300
            };

            host.Children.Add(comboBox);
            host.Measure(new Size(400, 300));
            host.Arrange(new Rect(0, 0, 400, 300));

            var divider = FindNamedDescendant<Border>(comboBox, "PART_DropDownDivider");

            Assert.NotNull(divider);
            Assert.Equal(0, divider!.Opacity, 3);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ComboBox_Disabled_MouseDown_ShouldNotOpenDropDown()
    {
        var comboBox = new ComboBox
        {
            IsEnabled = false
        };

        comboBox.RaiseEvent(CreateMouseDown(new Point(8, 8)));

        Assert.False(comboBox.IsDropDownOpen);
    }

    [Fact]
    public void ComboBox_DisablingControl_ShouldCloseDropDown()
    {
        var comboBox = new ComboBox
        {
            IsDropDownOpen = true
        };

        Assert.True(comboBox.IsDropDownOpen);

        comboBox.IsEnabled = false;

        Assert.False(comboBox.IsDropDownOpen);
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        if (root is T match)
        {
            return match;
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child == null)
            {
                continue;
            }

            var found = FindDescendant<T>(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindNamedDescendant<T>(Visual root, string name) where T : FrameworkElement
    {
        if (root is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
        {
            return match;
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child == null)
            {
                continue;
            }

            var found = FindNamedDescendant<T>(child, name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0);
    }
}
