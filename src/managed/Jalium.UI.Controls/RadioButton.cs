using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a button that can be selected, but not cleared, by a user.
/// RadioButtons in the same group are mutually exclusive.
/// </summary>
public class RadioButton : ToggleButton
{
    #region Automation

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        return new RadioButtonAutomationPeer(this);
    }

    #endregion

    #region Static Fields

    /// <summary>
    /// Tracks RadioButtons by group name for mutual exclusion.
    /// </summary>
    private static readonly Dictionary<string, List<WeakReference<RadioButton>>> _groupMap = new();

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the GroupName dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty GroupNameProperty =
        DependencyProperty.Register(nameof(GroupName), typeof(string), typeof(RadioButton),
            new PropertyMetadata(string.Empty, OnGroupNameChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the name of the group that the RadioButton belongs to.
    /// RadioButtons in the same group are mutually exclusive.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public string GroupName
    {
        get => (string)(GetValue(GroupNameProperty) ?? string.Empty);
        set => SetValue(GroupNameProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="RadioButton"/> class.
    /// </summary>
    private string? _registeredGroup;

    public RadioButton()
    {
        // Defer group registration until attached to visual tree,
        // because VisualParent is null in constructor and GetDefaultGroupName()
        // would return "__default__" which won't match later lookups.
        if (!string.IsNullOrEmpty(GroupName))
        {
            RegisterInGroup(GroupName);
        }
    }

    #endregion

    #region Visual Tree

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        // Re-register with correct group key now that VisualParent is known
        if (_registeredGroup != null)
        {
            UnregisterFromGroup(_registeredGroup);
        }
        if (VisualParent != null)
        {
            RegisterInGroup(GroupName);
        }
    }

    #endregion

    #region Group Management

    private void RegisterInGroup(string groupName)
    {
        var effectiveGroup = string.IsNullOrEmpty(groupName) ? GetDefaultGroupName() : groupName;

        if (!_groupMap.TryGetValue(effectiveGroup, out var list))
        {
            list = new List<WeakReference<RadioButton>>();
            _groupMap[effectiveGroup] = list;
        }

        // Clean up dead references and add ourselves
        list.RemoveAll(wr => !wr.TryGetTarget(out _));
        list.Add(new WeakReference<RadioButton>(this));
        _registeredGroup = effectiveGroup;
    }

    private void UnregisterFromGroup(string groupName)
    {
        if (_groupMap.TryGetValue(groupName, out var list))
        {
            list.RemoveAll(wr => !wr.TryGetTarget(out var target) || target == this);
            if (list.Count == 0)
            {
                _groupMap.Remove(groupName);
            }
        }
        _registeredGroup = null;
    }

    private string GetDefaultGroupName()
    {
        // Use parent as default group scope
        return VisualParent?.GetHashCode().ToString() ?? "__default__";
    }

    private void UncheckOthersInGroup()
    {
        var effectiveGroup = string.IsNullOrEmpty(GroupName) ? GetDefaultGroupName() : GroupName;

        if (_groupMap.TryGetValue(effectiveGroup, out var list))
        {
            foreach (var wr in list)
            {
                if (wr.TryGetTarget(out var radioButton) && radioButton != this && radioButton.IsChecked == true)
                {
                    radioButton.IsChecked = false;
                }
            }
        }
    }

    #endregion

    #region Toggle Handling

    /// <inheritdoc />
    protected override void OnToggle()
    {
        // RadioButton can only be checked, not unchecked by clicking
        if (IsChecked != true)
        {
            IsChecked = true;
        }
    }

    /// <inheritdoc />
    protected override void OnIsCheckedChanged(bool? oldValue, bool? newValue)
    {
        if (newValue == true)
        {
            // Uncheck other RadioButtons in the same group
            UncheckOthersInGroup();
        }

        base.OnIsCheckedChanged(oldValue, newValue);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnGroupNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RadioButton radioButton)
        {
            if (radioButton._registeredGroup != null)
            {
                radioButton.UnregisterFromGroup(radioButton._registeredGroup);
            }
            radioButton.RegisterInGroup((string?)e.NewValue ?? string.Empty);
        }
    }

    #endregion
}
