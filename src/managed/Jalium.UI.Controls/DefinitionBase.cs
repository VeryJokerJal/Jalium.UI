namespace Jalium.UI.Controls;

/// <summary>
/// Defines the functionality required to support a shared-size group
/// that is used by the ColumnDefinitionCollection and RowDefinitionCollection classes.
/// </summary>
public abstract class DefinitionBase : FrameworkContentElement
{
    /// <summary>
    /// 所属 Grid — 由 <see cref="Grid"/> 在 measure 阶段或集合 Add 时设置。
    /// 用于 Width/Height/MinWidth/... 等 layout DP 变化时通知 Grid 重新 layout。
    /// 没有这个引用，Grid 不会知道 RowDefinition.Height 或 ColumnDefinition.Width
    /// 在运行时被改变，会出现"列宽设置后不更新"的脏渲染问题。
    /// </summary>
    internal Grid? OwnerGrid { get; set; }

    /// <summary>Tracks collection membership independently from Grid attachment.</summary>
    internal object? OwnerCollection { get; set; }

    /// <summary>
    /// Layout 相关 DP 共享的 PropertyChangedCallback —
    /// 通知 OwnerGrid 重新 layout + 重绘。
    /// </summary>
    internal static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DefinitionBase def && def.OwnerGrid is { } grid)
        {
            grid.OnDefinitionChanged();
        }
    }

    /// <summary>
    /// Identifies the SharedSizeGroup dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SharedSizeGroupProperty =
        DependencyProperty.Register(nameof(SharedSizeGroup), typeof(string), typeof(DefinitionBase),
            new FrameworkPropertyMetadata(null, OnLayoutPropertyChanged), IsValidSharedSizeGroup);

    /// <summary>
    /// Gets or sets a value that identifies a ColumnDefinition or RowDefinition
    /// as a member of a defined group that shares sizing properties.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string? SharedSizeGroup
    {
        get => (string?)GetValue(SharedSizeGroupProperty);
        set => SetValue(SharedSizeGroupProperty, value);
    }

    private static bool IsValidSharedSizeGroup(object? value)
    {
        if (value == null)
        {
            return true;
        }

        if (value is not string text || text.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if ((index == 0 && char.IsDigit(character)) ||
                (!char.IsDigit(character) && !char.IsLetter(character) && character != '_'))
            {
                return false;
            }
        }

        return true;
    }
}
