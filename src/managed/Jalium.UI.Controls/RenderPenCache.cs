using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// 让一个控件跨帧复用同一支 <see cref="Pen"/>，只在画刷实例或线宽真的变了时才重建。
/// </summary>
/// <remarks>
/// 两点收益要分清：
/// <list type="bullet">
/// <item><see cref="Pen"/> 是带 8 个依赖属性的 <see cref="Animatable"/>，在 OnRender 里每帧
/// new 一支并不便宜，复用省的是这份 DependencyObject 分配。</item>
/// <item>渲染后端的原生画刷缓存按 <see cref="Brush"/> <b>实例身份</b>键控，看的是
/// <c>pen.Brush</c> 而不是画笔本身。所以「每帧 new Pen 但包着同一支画刷」并不会让那份缓存
/// 落空——真正让缓存落空的是每帧 new 出新的画刷实例。</item>
/// </list>
/// 这是可变结构体：必须以字段形式持有（<c>private RenderPenCache _borderPen;</c>），
/// 放进 readonly 字段或局部变量会让 <see cref="Get"/> 的写入丢失，每次调用都退化成新建。
/// </remarks>
internal struct RenderPenCache
{
    private Pen? _pen;
    private Brush? _brush;
    private double _thickness;

    /// <summary>返回一支画刷为 <paramref name="brush"/>、线宽为 <paramref name="thickness"/> 的画笔。</summary>
    public Pen Get(Brush brush, double thickness)
    {
        var pen = _pen;
        if (pen is null ||
            pen.IsFrozen ||
            !ReferenceEquals(_brush, brush) ||
            _thickness != thickness)
        {
            pen = new Pen(brush, thickness);
            _pen = pen;
            _brush = brush;
            _thickness = thickness;
        }

        return pen;
    }
}
