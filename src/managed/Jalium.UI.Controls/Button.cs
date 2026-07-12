using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a button control.
/// Uses ControlTemplate for rendering - visual appearance is defined in Button.jalxaml.
/// </summary>
public class Button : ButtonBase
{
    private static readonly List<WeakReference<Button>> s_defaultButtons = new();

    static Button()
    {
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnAnyKeyboardFocusChanged),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnAnyKeyboardFocusChanged),
            handledEventsToo: true);
    }

    #region Automation

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        return new ButtonAutomationPeer(this);
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsDefault dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDefaultProperty =
        DependencyProperty.Register(nameof(IsDefault), typeof(bool), typeof(Button),
            new PropertyMetadata(false, OnIsDefaultChanged));

    /// <summary>
    /// Identifies the IsCancel dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCancelProperty =
        DependencyProperty.Register(nameof(IsCancel), typeof(bool), typeof(Button),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsDefaultedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsDefaulted), typeof(bool), typeof(Button),
            new FrameworkPropertyMetadata(false));

    /// <summary>Identifies the read-only default-button activation state.</summary>
    public static readonly DependencyProperty IsDefaultedProperty =
        IsDefaultedPropertyKey.DependencyProperty;

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether this is the default button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDefault
    {
        get => (bool)GetValue(IsDefaultProperty)!;
        set => SetValue(IsDefaultProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this is the cancel button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCancel
    {
        get => (bool)GetValue(IsCancelProperty)!;
        set => SetValue(IsCancelProperty, value);
    }

    /// <summary>Gets whether pressing Enter currently invokes this button.</summary>
    public bool IsDefaulted => (bool)(GetValue(IsDefaultedProperty) ?? false);

    #endregion

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateIsDefaulted();
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        UpdateIsDefaulted();
    }

    private static void OnIsDefaultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (Button)d;
        lock (s_defaultButtons)
        {
            RemoveButtonNoLock(button);
            if ((bool)(e.NewValue ?? false))
            {
                s_defaultButtons.Add(new WeakReference<Button>(button));
            }
        }

        button.UpdateIsDefaulted();
    }

    private static void OnAnyKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        lock (s_defaultButtons)
        {
            for (var index = s_defaultButtons.Count - 1; index >= 0; index--)
            {
                if (!s_defaultButtons[index].TryGetTarget(out var button))
                {
                    s_defaultButtons.RemoveAt(index);
                    continue;
                }

                button.UpdateIsDefaulted();
            }
        }
    }

    private static void RemoveButtonNoLock(Button button)
    {
        for (var index = s_defaultButtons.Count - 1; index >= 0; index--)
        {
            if (!s_defaultButtons[index].TryGetTarget(out var current) || ReferenceEquals(current, button))
            {
                s_defaultButtons.RemoveAt(index);
            }
        }
    }

    private void UpdateIsDefaulted()
    {
        var focusedElement = Keyboard.FocusedElement;
        var defaulted = IsDefault && IsEnabled && focusedElement != null &&
            focusedElement is not TextBoxBase { AcceptsReturn: true } &&
            ShareVisualRoot(this, focusedElement);
        SetValue(IsDefaultedPropertyKey, defaulted);
    }

    private static bool ShareVisualRoot(UIElement button, IInputElement focusedElement)
    {
        if (focusedElement is not Visual focusedVisual)
        {
            return false;
        }

        static Visual GetRoot(Visual visual)
        {
            while (visual.VisualParent != null)
            {
                visual = visual.VisualParent;
            }

            return visual;
        }

        return ReferenceEquals(GetRoot(button), GetRoot(focusedVisual));
    }
}
