using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// The chrome (corner radius, padding, border, brushes) of a menu popup.
/// </summary>
/// <remarks>
/// 级联菜单的每一级都是**独立的弹出框**，各自建各自的 <see cref="Border"/>：一级由菜单本体建
/// （<see cref="ContextMenu"/> 的 popup），二级以后由展开它的 <see cref="MenuItem"/> /
/// <see cref="MenuFlyoutSubItem"/> 建。这个结构体把"上一级长什么样"原样传给下一级，
/// 保证整条链的圆角、内边距、描边、底色一致；否则各级各写各的常量，就会出现
/// "一级大圆角、二级近直角"的割裂。
/// </remarks>
internal readonly struct MenuPopupChrome
{
    /// <summary>
    /// 框架默认造型，与 MenuFlyout 的 presenter 对齐。
    /// </summary>
    private static readonly CornerRadius s_defaultCornerRadius = new(8);
    private static readonly Thickness s_defaultPadding = new(4);
    private static readonly Thickness s_defaultBorderThickness = new(1);

    public MenuPopupChrome(
        CornerRadius cornerRadius,
        Thickness padding,
        Thickness borderThickness,
        Brush? background,
        Brush? borderBrush)
    {
        CornerRadius = cornerRadius;
        Padding = padding;
        BorderThickness = borderThickness;
        Background = background;
        BorderBrush = borderBrush;
    }

    /// <summary>Gets the corner radius of the popup frame.</summary>
    public CornerRadius CornerRadius { get; }

    /// <summary>Gets the inner padding between the popup frame and its items.</summary>
    public Thickness Padding { get; }

    /// <summary>Gets the thickness of the popup frame outline.</summary>
    public Thickness BorderThickness { get; }

    /// <summary>Gets the popup background brush, or <see langword="null"/> to keep the current one.</summary>
    public Brush? Background { get; }

    /// <summary>Gets the popup outline brush, or <see langword="null"/> to keep the current one.</summary>
    public Brush? BorderBrush { get; }

    /// <summary>
    /// Creates the framework default chrome (8 / 4 / 1) used when no host popup supplies one.
    /// </summary>
    public static MenuPopupChrome CreateDefault(Brush? background, Brush? borderBrush)
    {
        return new MenuPopupChrome(
            s_defaultCornerRadius,
            s_defaultPadding,
            s_defaultBorderThickness,
            background,
            borderBrush);
    }

    /// <summary>
    /// Reads back the chrome of an already-built popup frame (the level that hosts this submenu).
    /// </summary>
    public static MenuPopupChrome FromBorder(Border border)
    {
        return new MenuPopupChrome(
            border.CornerRadius,
            border.Padding,
            border.BorderThickness,
            border.Background,
            border.BorderBrush);
    }

    /// <summary>
    /// Applies this chrome to the border that frames a submenu popup.
    /// </summary>
    /// <remarks>
    /// 画刷为 null 时保留 border 自己解析出来的值（宿主没显式设色时不要把它擦成空）。
    /// </remarks>
    public void ApplyTo(Border border)
    {
        border.CornerRadius = CornerRadius;
        border.Padding = Padding;
        border.BorderThickness = BorderThickness;

        if (Background != null)
        {
            border.Background = Background;
        }

        if (BorderBrush != null)
        {
            border.BorderBrush = BorderBrush;
        }
    }
}
