using Jalium.UI.Threading;

namespace Jalium.UI.Media;

/// <summary>
/// Defines a geometric shape described using a StreamGeometryContext.
/// This geometry is lighter-weight than PathGeometry and is optimized for describing
/// geometries that don't need modification after creation.
/// </summary>
public sealed class StreamGeometry : Geometry
{
    public static readonly DependencyProperty FillRuleProperty =
        DependencyProperty.Register(nameof(FillRule), typeof(FillRule), typeof(StreamGeometry),
            new PropertyMetadata(FillRule.EvenOdd));

    private PathGeometry? _pathGeometry;
    private bool _isOpen;

    // 合成 / Composed 状态：把全部 figure 的 segment 一次性展开成 native FillPath /
    // StrokePath 接受的紧凑 float[]，与 (StartX, StartY, Bounds, HasCurves, IsClosed)
    // 一起持久化到字段。OnRender 每帧调用 DrawGeometry 时，渲染后端直接读取这个缓冲，
    // 跳过 PathFigure→PathSegment 列表遍历——把每条 path 的 marshaling 复杂度
    // 从 O(figures × segments) 压到 O(1)。
    //
    // Compose 在 Close()（StreamGeometryContext 关闭时）和 Freeze() 时各做一次，
    // 之后只要几何不再修改（Frozen 或者 user 不主动 Clear/Open），缓冲就一直有效。
    private float[]? _composedCommands;
    private int _composedLength;
    private float _composedStartX;
    private float _composedStartY;
    private Rect _composedBounds;
    private bool _hasCurves;
    private bool _composedIsClosed;
    private bool _composedIsFilled;
    private bool _isComposed;
    // 多 figure 走 fast path 的前提:所有 figure 必须**同质**(同一 IsClosed +
    // 同一 IsFilled)。绝大多数 SVG icon 和 menu glyph 都是 "全 closed + 全 filled"
    // —— 这一分支命中率高。混合 closed/open 或混合 filled/non-filled 的几何
    // 退回原 PathGeometry 路径(罕见,本身就不在 hot path 上)。
    private bool _multiFigureNoCompose;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamGeometry"/> class.
    /// </summary>
    public StreamGeometry()
    {
        _pathGeometry = new PathGeometry();
    }

    /// <summary>
    /// 合成态访问器（仅供渲染后端的 fast path 调用）。返回 false 时调用方应回落到
    /// <see cref="GetPathGeometry"/> + 逐 figure 处理的原路径。
    ///
    /// 当前 fast path 仅覆盖单 figure 几何 —— 这是 <c>StreamGeometry</c> 的最常见
    /// 用法（一条曲线 / 一个轮廓）。多 figure 几何用对象图保留写入语义，几何复杂度
    /// 比 single-figure 大但仍低于实际渲染开销，不是热点。
    /// </summary>
    internal bool TryGetComposedCommands(
        out float[] commands,
        out int length,
        out float startX,
        out float startY,
        out Rect bounds,
        out bool hasCurves,
        out bool isClosed,
        out bool isFilled)
    {
        if (_isComposed && _composedCommands != null && !_multiFigureNoCompose)
        {
            commands = _composedCommands;
            length = _composedLength;
            startX = _composedStartX;
            startY = _composedStartY;
            bounds = _composedBounds;
            hasCurves = _hasCurves;
            isClosed = _composedIsClosed;
            isFilled = _composedIsFilled;
            return true;
        }

        commands = Array.Empty<float>();
        length = 0;
        startX = 0;
        startY = 0;
        bounds = Rect.Empty;
        hasCurves = false;
        isClosed = false;
        isFilled = false;
        return false;
    }

    /// <summary>
    /// Gets or sets the fill rule for this geometry.
    /// </summary>
    public FillRule FillRule
    {
        get => (FillRule)GetValue(FillRuleProperty)!;
        set
        {
            SetValue(FillRuleProperty, value);
            if (_pathGeometry != null)
                _pathGeometry.FillRule = value;
            WritePostscript();
        }
    }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            // 合成态下 bounds 已经在 Compose 时一次性计算并 cache 进 _composedBounds，
            // 直接返回避免每次 PathGeometry.Bounds 的 O(figures × segments) 遍历。
            if (_isComposed) return _composedBounds;
            return _pathGeometry?.Bounds ?? Rect.Empty;
        }
    }

    /// <summary>
    /// Opens the StreamGeometry for populating.
    /// </summary>
    /// <returns>A StreamGeometryContext that can be used to describe the geometry.</returns>
    public StreamGeometryContext Open()
    {
        WritePreamble();
        if (_isOpen)
        {
            throw new InvalidOperationException("StreamGeometry is already open.");
        }

        _isOpen = true;
        _pathGeometry = new PathGeometry { FillRule = FillRule };
        // 重新打开会替换 _pathGeometry，原 composed buffer 失效。
        _isComposed = false;
        _composedCommands = null;
        _composedLength = 0;
        _multiFigureNoCompose = false;
        return new StreamGeometryContextImplementation(this);
    }

    /// <summary>
    /// Removes all figures from the geometry.
    /// </summary>
    public void Clear()
    {
        WritePreamble();
        if (_isOpen)
        {
            throw new InvalidOperationException("Cannot clear while StreamGeometry is open.");
        }

        _pathGeometry?.Figures.Clear();
        _isComposed = false;
        _composedCommands = null;
        _composedLength = 0;
        _multiFigureNoCompose = false;
    }

    /// <summary>
    /// Returns true if this geometry is empty.
    /// </summary>
    public override bool IsEmpty()
    {
        return _pathGeometry == null || _pathGeometry.Figures.Count == 0;
    }

    /// <summary>
    /// Returns true if this geometry may have curved segments.
    /// </summary>
    public override bool MayHaveCurves()
    {
        if (_pathGeometry == null) return false;

        foreach (var figure in _pathGeometry.Figures)
        {
            foreach (var segment in figure.Segments)
            {
                if (segment is BezierSegment ||
                    segment is QuadraticBezierSegment ||
                    segment is PolyBezierSegment ||
                    segment is PolyQuadraticBezierSegment ||
                    segment is ArcSegment)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the underlying PathGeometry.
    /// </summary>
    public PathGeometry? GetPathGeometry() => _pathGeometry;

    /// <inheritdoc />
    public override PathGeometry GetFlattenedPathGeometry(double tolerance, ToleranceType toleranceType)
        => _pathGeometry?.GetFlattenedPathGeometry(tolerance, toleranceType) ?? new PathGeometry();

    public new StreamGeometry Clone() => (StreamGeometry)base.Clone();
    public new StreamGeometry CloneCurrentValue() => (StreamGeometry)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new StreamGeometry();

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
            return false;
        if (_pathGeometry is null)
            return true;
        if (isChecking)
            return _pathGeometry.CanFreeze;
        _pathGeometry.Freeze();
        return true;
    }

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyGeometry((StreamGeometry)source, currentValue: false, frozen: false);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyGeometry((StreamGeometry)source, currentValue: true, frozen: false);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyGeometry((StreamGeometry)source, currentValue: false, frozen: true);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyGeometry((StreamGeometry)source, currentValue: true, frozen: true);
    }

    protected override void OnChanged()
    {
        if (_pathGeometry is not null)
            _pathGeometry.FillRule = FillRule;
        _isComposed = false;
        _composedCommands = null;
        _composedLength = 0;
        base.OnChanged();
    }

    private void CopyGeometry(StreamGeometry source, bool currentValue, bool frozen)
    {
        if (source._pathGeometry is null)
        {
            _pathGeometry = null;
        }
        else if (frozen)
        {
            _pathGeometry = (PathGeometry)(currentValue
                ? source._pathGeometry.GetCurrentValueAsFrozen()
                : source._pathGeometry.GetAsFrozen());
        }
        else
        {
            _pathGeometry = currentValue
                ? source._pathGeometry.CloneCurrentValue()
                : source._pathGeometry.Clone();
        }
        _isOpen = false;
        Compose();
    }

    /// <summary>
    /// Called by StreamGeometryContext when it is closed.
    /// </summary>
    internal void Close(PathFigure? currentFigure)
    {
        if (currentFigure != null && _pathGeometry != null)
        {
            _pathGeometry.Figures.Add(currentFigure);
        }

        _isOpen = false;

        // 关闭时立即合成。绝大多数 StreamGeometry 在构造完毕后不再修改，
        // 这一次扫表把后续每一帧的 marshal 都消去。
        Compose();
    }

    /// <summary>
    /// 把全部 figure 的 segment 一次性展开成 native FillPath / StrokePath 接受的紧凑
    /// float[]，并缓存 startPoint / bounds / hasCurves / isClosed / isFilled。
    ///
    /// 命令编码（与 native API 注释一致）：
    ///   tag 0 = LineTo   [0, x, y]
    ///   tag 1 = BezierTo [1, c1x, c1y, c2x, c2y, ex, ey]
    ///   tag 2 = MoveTo   [2, x, y]   （多 figure 分隔）
    ///   tag 3 = QuadTo   [3, cx, cy, ex, ey]
    ///   tag 5 = ClosePath[5]
    ///
    /// 单 figure 直接展开；多 figure 在所有 figure 共享同一 IsClosed/IsFilled 时也
    /// 走 fast path（用 tag=2 MoveTo 分隔每个 figure）；只有混合 closed/open 或混合
    /// filled/non-filled 的几何才退回 PathGeometry 路径。ArcSegment 走
    /// <see cref="StreamGeometryArcHelper.AppendArcAsCubicBeziers"/> 内联展开成
    /// cubic bezier，不再退回慢路径。
    /// </summary>
    internal void Compose()
    {
        _isComposed = false;
        _composedCommands = null;
        _composedLength = 0;
        _multiFigureNoCompose = false;

        var pg = _pathGeometry;
        if (pg == null || pg.Figures.Count == 0)
        {
            _composedBounds = Rect.Empty;
            _isComposed = true;
            _composedIsClosed = false;
            _composedIsFilled = false;
            return;
        }

        // 多 figure 同质性检测：所有 figure 必须共享同一 IsClosed/IsFilled
        // (绝大多数 SVG/menu icon 都是 closed+filled,这条命中率高)。
        bool firstFigureIsClosed = pg.Figures[0].IsClosed;
        bool firstFigureIsFilled = pg.Figures[0].IsFilled;
        for (int fi = 1; fi < pg.Figures.Count; ++fi)
        {
            var f = pg.Figures[fi];
            if (f.IsClosed != firstFigureIsClosed || f.IsFilled != firstFigureIsFilled)
            {
                _multiFigureNoCompose = true;
                return;
            }
        }

        // 一次扫遍估算 buffer 大小,同时统计是否含曲线。
        // 多 figure: 第 2 个及以后 figure 起始要写 MoveTo (tag=2, 3 floats)。
        // ArcSegment 上限按 4 段 cubic bezier 估算 (每段 7 floats)。
        bool hasCurves = false;
        int floatCapacity = 0;
        for (int fi = 0; fi < pg.Figures.Count; ++fi)
        {
            var f = pg.Figures[fi];
            if (fi > 0) floatCapacity += 3;  // MoveTo for 2nd+ figure
            foreach (var seg in f.Segments)
            {
                switch (seg)
                {
                    case LineSegment: floatCapacity += 3; break;
                    case PolyLineSegment pl: floatCapacity += pl.Points.Count * 3; break;
                    case BezierSegment: floatCapacity += 7; hasCurves = true; break;
                    case PolyBezierSegment pb: floatCapacity += (pb.Points.Count / 3) * 7; hasCurves = true; break;
                    case QuadraticBezierSegment: floatCapacity += 5; hasCurves = true; break;
                    case PolyQuadraticBezierSegment pq: floatCapacity += (pq.Points.Count / 2) * 5; hasCurves = true; break;
                    case ArcSegment: floatCapacity += 4 * 7; hasCurves = true; break;
                }
            }
            if (f.IsClosed) floatCapacity += 1;  // ClosePath
        }

        var buffer = floatCapacity > 0 ? new float[floatCapacity] : Array.Empty<float>();
        int idx = 0;

        var firstFigure = pg.Figures[0];
        double minX = firstFigure.StartPoint.X, minY = firstFigure.StartPoint.Y;
        double maxX = minX, maxY = minY;

        // 用 List<float> 包装 buffer 给 ArcSegment helper —— helper 签名是 IList<float>，
        // 不能直接传 float[]。复制成本一次性,且 helper 调用频率低(普通 path 无 Arc)。
        // 为性能,我们用 ArraySegmentList wrapping (轻量 IList<float> 接口)。
        var arcList = new BufferList(buffer);

        for (int fi = 0; fi < pg.Figures.Count; ++fi)
        {
            var figure = pg.Figures[fi];
            var current = figure.StartPoint;

            if (fi > 0)
            {
                buffer[idx++] = 2f;
                buffer[idx++] = (float)figure.StartPoint.X;
                buffer[idx++] = (float)figure.StartPoint.Y;
                UpdateBounds(figure.StartPoint, ref minX, ref minY, ref maxX, ref maxY);
            }

            foreach (var seg in figure.Segments)
            {
                switch (seg)
                {
                    case LineSegment line:
                        buffer[idx++] = 0f;
                        buffer[idx++] = (float)line.Point.X;
                        buffer[idx++] = (float)line.Point.Y;
                        UpdateBounds(line.Point, ref minX, ref minY, ref maxX, ref maxY);
                        current = line.Point;
                        break;

                    case PolyLineSegment polyLine:
                        foreach (var pt in polyLine.Points)
                        {
                            buffer[idx++] = 0f;
                            buffer[idx++] = (float)pt.X;
                            buffer[idx++] = (float)pt.Y;
                            UpdateBounds(pt, ref minX, ref minY, ref maxX, ref maxY);
                            current = pt;
                        }
                        break;

                    case BezierSegment bezier:
                        buffer[idx++] = 1f;
                        buffer[idx++] = (float)bezier.Point1.X;
                        buffer[idx++] = (float)bezier.Point1.Y;
                        buffer[idx++] = (float)bezier.Point2.X;
                        buffer[idx++] = (float)bezier.Point2.Y;
                        buffer[idx++] = (float)bezier.Point3.X;
                        buffer[idx++] = (float)bezier.Point3.Y;
                        UpdateBounds(bezier.Point3, ref minX, ref minY, ref maxX, ref maxY);
                        current = bezier.Point3;
                        break;

                    case PolyBezierSegment polyBezier:
                    {
                        var pts = polyBezier.Points;
                        for (int i = 0; i + 2 < pts.Count; i += 3)
                        {
                            buffer[idx++] = 1f;
                            buffer[idx++] = (float)pts[i].X;
                            buffer[idx++] = (float)pts[i].Y;
                            buffer[idx++] = (float)pts[i + 1].X;
                            buffer[idx++] = (float)pts[i + 1].Y;
                            buffer[idx++] = (float)pts[i + 2].X;
                            buffer[idx++] = (float)pts[i + 2].Y;
                            UpdateBounds(pts[i + 2], ref minX, ref minY, ref maxX, ref maxY);
                            current = pts[i + 2];
                        }
                        break;
                    }

                    case QuadraticBezierSegment quad:
                        buffer[idx++] = 3f;
                        buffer[idx++] = (float)quad.Point1.X;
                        buffer[idx++] = (float)quad.Point1.Y;
                        buffer[idx++] = (float)quad.Point2.X;
                        buffer[idx++] = (float)quad.Point2.Y;
                        UpdateBounds(quad.Point2, ref minX, ref minY, ref maxX, ref maxY);
                        current = quad.Point2;
                        break;

                    case PolyQuadraticBezierSegment polyQuad:
                    {
                        var pts = polyQuad.Points;
                        for (int i = 0; i + 1 < pts.Count; i += 2)
                        {
                            buffer[idx++] = 3f;
                            buffer[idx++] = (float)pts[i].X;
                            buffer[idx++] = (float)pts[i].Y;
                            buffer[idx++] = (float)pts[i + 1].X;
                            buffer[idx++] = (float)pts[i + 1].Y;
                            UpdateBounds(pts[i + 1], ref minX, ref minY, ref maxX, ref maxY);
                            current = pts[i + 1];
                        }
                        break;
                    }

                    case ArcSegment arc:
                    {
                        // 同步 BufferList 状态到当前 idx,append 后回写 idx。
                        arcList.SetCount(idx);
                        StreamGeometryArcHelper.AppendArcAsCubicBeziers(arcList, current, arc, 0, 0);
                        idx = arcList.Count;
                        UpdateBounds(arc.Point, ref minX, ref minY, ref maxX, ref maxY);
                        current = arc.Point;
                        break;
                    }
                }
            }

            if (figure.IsClosed)
            {
                buffer[idx++] = 5f;
            }
        }

        _composedCommands = buffer;
        _composedLength = idx;
        _composedStartX = (float)firstFigure.StartPoint.X;
        _composedStartY = (float)firstFigure.StartPoint.Y;
        _composedBounds = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        _hasCurves = hasCurves;
        _composedIsClosed = firstFigureIsClosed;
        _composedIsFilled = firstFigureIsFilled;
        _isComposed = true;
    }

    /// <summary>
    /// 把固定容量 float[] 包装成 IList&lt;float&gt; 供 ArcHelper 用。Count 由外部维护,
    /// Add 在底层数组上写入。零分配,纯包装。
    /// </summary>
    private sealed class BufferList : IList<float>
    {
        private readonly float[] _buffer;
        private int _count;

        public BufferList(float[] buffer) { _buffer = buffer; _count = 0; }

        public void SetCount(int count) => _count = count;
        public int Count => _count;
        public bool IsReadOnly => false;

        public float this[int index]
        {
            get => _buffer[index];
            set => _buffer[index] = value;
        }

        public void Add(float item) { _buffer[_count++] = item; }
        public void Clear() => _count = 0;
        public bool Contains(float item) => Array.IndexOf(_buffer, item, 0, _count) >= 0;
        public void CopyTo(float[] array, int arrayIndex) => Array.Copy(_buffer, 0, array, arrayIndex, _count);
        public IEnumerator<float> GetEnumerator()
        {
            for (int i = 0; i < _count; ++i) yield return _buffer[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(float item) => Array.IndexOf(_buffer, item, 0, _count);
        public void Insert(int index, float item) => throw new NotSupportedException();
        public bool Remove(float item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }

    private static void UpdateBounds(Point p, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (p.X < minX) minX = p.X;
        if (p.Y < minY) minY = p.Y;
        if (p.X > maxX) maxX = p.X;
        if (p.Y > maxY) maxY = p.Y;
    }
}

/// <summary>
/// Describes a geometry using drawing commands. This class is used with a StreamGeometry
/// object to create a lightweight geometry that does not support data binding, animation, or modification.
/// </summary>
public abstract class StreamGeometryContext : DispatcherObject, IDisposable
{
    internal StreamGeometryContext()
    {
    }

    public abstract void BeginFigure(Point startPoint, bool isFilled, bool isClosed);

    public abstract void LineTo(Point point, bool isStroked, bool isSmoothJoin);

    public abstract void PolyLineTo(IList<Point> points, bool isStroked, bool isSmoothJoin);

    public abstract void BezierTo(
        Point point1,
        Point point2,
        Point point3,
        bool isStroked,
        bool isSmoothJoin);

    public abstract void PolyBezierTo(IList<Point> points, bool isStroked, bool isSmoothJoin);

    public abstract void QuadraticBezierTo(
        Point point1,
        Point point2,
        bool isStroked,
        bool isSmoothJoin);

    public abstract void PolyQuadraticBezierTo(
        IList<Point> points,
        bool isStroked,
        bool isSmoothJoin);

    public abstract void ArcTo(
        Point point,
        Size size,
        double rotationAngle,
        bool isLargeArc,
        SweepDirection sweepDirection,
        bool isStroked,
        bool isSmoothJoin);

    public virtual void Close() => DisposeCore();

    void IDisposable.Dispose() => DisposeCore();

    internal virtual void DisposeCore() => SetClosedState();

    internal abstract void SetClosedState();
}

/// <summary>
/// Stores the mutable command-building state behind the public WPF-compatible
/// <see cref="StreamGeometryContext"/> contract.
/// </summary>
internal sealed class StreamGeometryContextImplementation : StreamGeometryContext
{
    private readonly StreamGeometry _owner;
    private PathFigure? _currentFigure;
    private bool _isClosed;

    internal StreamGeometryContextImplementation(StreamGeometry owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Starts a new figure at the specified point.
    /// </summary>
    /// <param name="startPoint">The starting point for the new figure.</param>
    /// <param name="isFilled">true if the figure should be filled; otherwise, false.</param>
    /// <param name="isClosed">true if the figure should be closed; otherwise, false.</param>
    public override void BeginFigure(Point startPoint, bool isFilled, bool isClosed)
    {
        ThrowIfClosed();

        // Save previous figure if any
        if (_currentFigure != null)
        {
            _owner.GetPathGeometry()?.Figures.Add(_currentFigure);
        }

        _currentFigure = new PathFigure
        {
            StartPoint = startPoint,
            IsFilled = isFilled,
            IsClosed = isClosed
        };
    }

    /// <summary>
    /// Draws a straight line to the specified point.
    /// </summary>
    /// <param name="point">The destination point.</param>
    /// <param name="isStroked">true if the line should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the join should be smooth; otherwise, false.</param>
    public override void LineTo(Point point, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new LineSegment(point, isStroked));
    }

    /// <summary>
    /// Draws one or more connected straight lines.
    /// </summary>
    /// <param name="points">The collection of points that specify the lines to draw.</param>
    /// <param name="isStroked">true if the lines should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the joins should be smooth; otherwise, false.</param>
    public override void PolyLineTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new PolyLineSegment(points, isStroked));
    }

    /// <summary>
    /// Draws a cubic Bezier curve to the specified point.
    /// </summary>
    /// <param name="point1">The first control point.</param>
    /// <param name="point2">The second control point.</param>
    /// <param name="point3">The destination point.</param>
    /// <param name="isStroked">true if the curve should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the join should be smooth; otherwise, false.</param>
    public override void BezierTo(Point point1, Point point2, Point point3, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new BezierSegment(point1, point2, point3, isStroked));
    }

    /// <summary>
    /// Draws one or more connected cubic Bezier curves.
    /// </summary>
    /// <param name="points">The collection of points (in groups of three) that specify the curves.</param>
    /// <param name="isStroked">true if the curves should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the joins should be smooth; otherwise, false.</param>
    public override void PolyBezierTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        var segment = new PolyBezierSegment { IsStroked = isStroked };
        foreach (Point point in points)
            segment.Points.Add(point);
        _currentFigure!.Segments.Add(segment);
    }

    /// <summary>
    /// Draws a quadratic Bezier curve to the specified point.
    /// </summary>
    /// <param name="point1">The control point.</param>
    /// <param name="point2">The destination point.</param>
    /// <param name="isStroked">true if the curve should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the join should be smooth; otherwise, false.</param>
    public override void QuadraticBezierTo(Point point1, Point point2, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new QuadraticBezierSegment(point1, point2, isStroked));
    }

    /// <summary>
    /// Draws one or more connected quadratic Bezier curves.
    /// </summary>
    /// <param name="points">The collection of points (in groups of two) that specify the curves.</param>
    /// <param name="isStroked">true if the curves should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the joins should be smooth; otherwise, false.</param>
    public override void PolyQuadraticBezierTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        var segment = new PolyQuadraticBezierSegment { IsStroked = isStroked };
        foreach (Point point in points)
            segment.Points.Add(point);
        _currentFigure!.Segments.Add(segment);
    }

    /// <summary>
    /// Draws an arc to the specified point.
    /// </summary>
    /// <param name="point">The destination point.</param>
    /// <param name="size">The size of the arc (the x and y radius of the ellipse).</param>
    /// <param name="rotationAngle">The rotation angle of the ellipse in degrees.</param>
    /// <param name="isLargeArc">true if the arc should be greater than 180 degrees; otherwise, false.</param>
    /// <param name="sweepDirection">The direction to draw the arc.</param>
    /// <param name="isStroked">true if the arc should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the join should be smooth; otherwise, false.</param>
    public override void ArcTo(Point point, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweepDirection, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new ArcSegment(point, size, rotationAngle, isLargeArc, sweepDirection, isStroked));
    }

    /// <summary>
    /// Closes the StreamGeometryContext and flushes its content so it can be rendered.
    /// </summary>
    internal override void SetClosedState()
    {
        if (_isClosed) return;

        _isClosed = true;
        _owner.Close(_currentFigure);
    }

    private void ThrowIfClosed()
    {
        if (_isClosed)
        {
            throw new InvalidOperationException("StreamGeometryContext is already closed.");
        }
    }

    private void ThrowIfNoFigure()
    {
        if (_currentFigure == null)
        {
            throw new InvalidOperationException("BeginFigure must be called before drawing segments.");
        }
    }
}
