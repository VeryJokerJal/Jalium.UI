using System.Numerics;

namespace Jalium.UI.Gpu;

/// <summary>
/// 路径三角化器 - 将矢量路径转换为 GPU 可渲染的三角形网格
/// </summary>
public sealed class PathTessellator
{
    private readonly List<Vector2> _vertices = new();
    private readonly List<uint> _indices = new();
    private readonly List<List<Vector2>> _contours = new();
    private List<Vector2> _currentContour = new();
    private Vector2 _currentPoint;
    private Vector2 _startPoint;
    private Vector2 _lastControlPoint;
    private char _lastCommandUpper;

    private float _tolerance = 0.25f;

    /// <summary>
    /// 细分精度（较小的值产生更多三角形但更平滑）。最小值 0.01。
    /// </summary>
    public float Tolerance
    {
        get => _tolerance;
        set => _tolerance = MathF.Max(value, 0.01f);
    }

    /// <summary>
    /// 将 SVG 路径数据转换为三角形网格
    /// </summary>
    public TessellationResult Tessellate(string pathData)
    {
        Reset();

        if (string.IsNullOrEmpty(pathData))
            return TessellationResult.Empty;

        ParseAndTessellate(pathData);

        return new TessellationResult(
            _vertices.ToArray(),
            _indices.ToArray());
    }

    /// <summary>
    /// 将椭圆转换为三角形网格
    /// </summary>
    public TessellationResult TessellateEllipse(float cx, float cy, float rx, float ry)
    {
        Reset();

        // 根据大小和精度计算分段数
        var circumference = 2 * MathF.PI * MathF.Max(rx, ry);
        var segments = Math.Max(8, (int)(circumference / Tolerance));

        if (rx <= 0 || ry <= 0)
            return TessellationResult.Empty;

        // 生成椭圆顶点
        var center = new Vector2(cx, cy);
        _vertices.Add(center);

        for (int i = 0; i < segments; i++)
        {
            var angle = 2 * MathF.PI * i / segments;
            _vertices.Add(new Vector2(
                cx + rx * MathF.Cos(angle),
                cy + ry * MathF.Sin(angle)));
        }

        // 生成三角形扇形索引
        for (int i = 0; i < segments; i++)
        {
            _indices.Add(0);
            _indices.Add((uint)(i + 1));
            _indices.Add((uint)(i + 1) % (uint)segments + 1);
        }

        return new TessellationResult(
            _vertices.ToArray(),
            _indices.ToArray());
    }

    /// <summary>
    /// 将矩形转换为三角形网格
    /// </summary>
    public TessellationResult TessellateRect(float x, float y, float width, float height)
    {
        Reset();

        if (width <= 0 || height <= 0)
            return TessellationResult.Empty;

        _vertices.Add(new Vector2(x, y));
        _vertices.Add(new Vector2(x + width, y));
        _vertices.Add(new Vector2(x + width, y + height));
        _vertices.Add(new Vector2(x, y + height));

        _indices.AddRange(new uint[] { 0, 1, 2, 0, 2, 3 });

        return new TessellationResult(
            _vertices.ToArray(),
            _indices.ToArray());
    }

    /// <summary>
    /// 将圆角矩形转换为三角形网格
    /// </summary>
    public TessellationResult TessellateRoundedRect(float x, float y, float width, float height,
        float radiusTL, float radiusTR, float radiusBR, float radiusBL)
    {
        Reset();

        // 限制圆角半径
        var maxRadius = MathF.Min(width, height) / 2;
        radiusTL = MathF.Min(radiusTL, maxRadius);
        radiusTR = MathF.Min(radiusTR, maxRadius);
        radiusBR = MathF.Min(radiusBR, maxRadius);
        radiusBL = MathF.Min(radiusBL, maxRadius);

        // 每个圆角的分段数
        var segments = Math.Max(4, (int)(MathF.PI * MathF.Max(MathF.Max(radiusTL, radiusTR), MathF.Max(radiusBR, radiusBL)) / 2 / Tolerance));

        // 添加中心点
        var centerIndex = (uint)_vertices.Count;
        _vertices.Add(new Vector2(x + width / 2, y + height / 2));

        // 生成边界顶点（从左上角顺时针）

        // 左上角
        AddCornerVertices(x + radiusTL, y + radiusTL, radiusTL, MathF.PI, MathF.PI * 1.5f, segments);

        // 右上角
        AddCornerVertices(x + width - radiusTR, y + radiusTR, radiusTR, MathF.PI * 1.5f, MathF.PI * 2, segments);

        // 右下角
        AddCornerVertices(x + width - radiusBR, y + height - radiusBR, radiusBR, 0, MathF.PI * 0.5f, segments);

        // 左下角
        AddCornerVertices(x + radiusBL, y + height - radiusBL, radiusBL, MathF.PI * 0.5f, MathF.PI, segments);

        // 生成三角形扇形
        var vertexCount = _vertices.Count;
        if (vertexCount <= 1)
        {
            return new TessellationResult(_vertices.ToArray(), _indices.ToArray());
        }

        for (int i = 1; i < vertexCount; i++)
        {
            _indices.Add(centerIndex);
            _indices.Add((uint)i);
            _indices.Add((uint)(i % (vertexCount - 1) + 1));
        }

        return new TessellationResult(
            _vertices.ToArray(),
            _indices.ToArray());
    }

    private void AddCornerVertices(float cx, float cy, float radius, float startAngle, float endAngle, int segments)
    {
        if (radius < 0.001f)
        {
            _vertices.Add(new Vector2(cx, cy));
            return;
        }

        var angleStep = (endAngle - startAngle) / segments;
        for (int i = 0; i <= segments; i++)
        {
            var angle = startAngle + i * angleStep;
            _vertices.Add(new Vector2(
                cx + radius * MathF.Cos(angle),
                cy + radius * MathF.Sin(angle)));
        }
    }

    private void Reset()
    {
        _vertices.Clear();
        _indices.Clear();
        _contours.Clear();
        _currentContour = new List<Vector2>();
        _currentPoint = Vector2.Zero;
        _startPoint = Vector2.Zero;
        _lastControlPoint = Vector2.Zero;
        _lastCommandUpper = '\0';
    }

    private void ParseAndTessellate(string pathData)
    {
        var tokens = TokenizePath(pathData);
        var index = 0;

        char lastCommand = ' ';

        while (index < tokens.Count)
        {
            var token = tokens[index];

            // 检查是否是命令字母
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                lastCommand = token[0];
                index++;

                // Z/z takes no parameters - execute immediately
                if (char.ToUpper(lastCommand) == 'Z')
                {
                    ClosePath();
                    _lastCommandUpper = 'Z';
                }

                continue;
            }

            bool isRelative = char.IsLower(lastCommand);

            // 根据命令处理参数
            switch (char.ToUpper(lastCommand))
            {
                case 'M': // MoveTo
                    var mx = ParseFloat(tokens, ref index);
                    var my = ParseFloat(tokens, ref index);
                    if (isRelative)
                    {
                        mx += _currentPoint.X;
                        my += _currentPoint.Y;
                    }
                    MoveTo(mx, my);
                    lastCommand = isRelative ? 'l' : 'L'; // 后续参数视为 LineTo
                    _lastCommandUpper = 'M';
                    break;

                case 'L': // LineTo
                    var lx = ParseFloat(tokens, ref index);
                    var ly = ParseFloat(tokens, ref index);
                    if (isRelative)
                    {
                        lx += _currentPoint.X;
                        ly += _currentPoint.Y;
                    }
                    LineTo(lx, ly);
                    _lastCommandUpper = 'L';
                    break;

                case 'H': // Horizontal LineTo
                    var hx = ParseFloat(tokens, ref index);
                    if (isRelative)
                        hx += _currentPoint.X;
                    LineTo(hx, _currentPoint.Y);
                    _lastCommandUpper = 'H';
                    break;

                case 'V': // Vertical LineTo
                    var vy = ParseFloat(tokens, ref index);
                    if (isRelative)
                        vy += _currentPoint.Y;
                    LineTo(_currentPoint.X, vy);
                    _lastCommandUpper = 'V';
                    break;

                case 'C': // CurveTo (三次贝塞尔)
                {
                    var c1x = ParseFloat(tokens, ref index);
                    var c1y = ParseFloat(tokens, ref index);
                    var c2x = ParseFloat(tokens, ref index);
                    var c2y = ParseFloat(tokens, ref index);
                    var cx = ParseFloat(tokens, ref index);
                    var cy = ParseFloat(tokens, ref index);
                    if (isRelative)
                    {
                        c1x += _currentPoint.X;
                        c1y += _currentPoint.Y;
                        c2x += _currentPoint.X;
                        c2y += _currentPoint.Y;
                        cx += _currentPoint.X;
                        cy += _currentPoint.Y;
                    }
                    _lastControlPoint = new Vector2(c2x, c2y);
                    CubicBezierTo(c1x, c1y, c2x, c2y, cx, cy);
                    _lastCommandUpper = 'C';
                    break;
                }

                case 'S': // Smooth CurveTo
                {
                    var s2x = ParseFloat(tokens, ref index);
                    var s2y = ParseFloat(tokens, ref index);
                    var sx = ParseFloat(tokens, ref index);
                    var sy = ParseFloat(tokens, ref index);
                    if (isRelative)
                    {
                        s2x += _currentPoint.X;
                        s2y += _currentPoint.Y;
                        sx += _currentPoint.X;
                        sy += _currentPoint.Y;
                    }
                    // Reflect previous control point
                    var s1 = (_lastCommandUpper is 'C' or 'S')
                        ? ReflectPoint(_lastControlPoint, _currentPoint)
                        : _currentPoint;
                    _lastControlPoint = new Vector2(s2x, s2y);
                    CubicBezierTo(s1.X, s1.Y, s2x, s2y, sx, sy);
                    _lastCommandUpper = 'S';
                    break;
                }

                case 'Q': // QuadraticCurveTo
                {
                    var qcx = ParseFloat(tokens, ref index);
                    var qcy = ParseFloat(tokens, ref index);
                    var qx = ParseFloat(tokens, ref index);
                    var qy = ParseFloat(tokens, ref index);
                    if (isRelative)
                    {
                        qcx += _currentPoint.X;
                        qcy += _currentPoint.Y;
                        qx += _currentPoint.X;
                        qy += _currentPoint.Y;
                    }
                    _lastControlPoint = new Vector2(qcx, qcy);
                    QuadraticBezierTo(qcx, qcy, qx, qy);
                    _lastCommandUpper = 'Q';
                    break;
                }

                case 'T': // Smooth QuadraticCurveTo
                {
                    var tx = ParseFloat(tokens, ref index);
                    var ty = ParseFloat(tokens, ref index);
                    if (isRelative)
                    {
                        tx += _currentPoint.X;
                        ty += _currentPoint.Y;
                    }
                    var tc = (_lastCommandUpper is 'Q' or 'T')
                        ? ReflectPoint(_lastControlPoint, _currentPoint)
                        : _currentPoint;
                    _lastControlPoint = tc;
                    QuadraticBezierTo(tc.X, tc.Y, tx, ty);
                    _lastCommandUpper = 'T';
                    break;
                }

                case 'A': // Arc
                    var rx = ParseFloat(tokens, ref index);
                    var ry = ParseFloat(tokens, ref index);
                    var rotation = ParseFloat(tokens, ref index);
                    var largeArc = ParseFloat(tokens, ref index) != 0;
                    var sweep = ParseFloat(tokens, ref index) != 0;
                    var ax = ParseFloat(tokens, ref index);
                    var ay = ParseFloat(tokens, ref index);
                    if (isRelative)
                    {
                        ax += _currentPoint.X;
                        ay += _currentPoint.Y;
                    }
                    ArcTo(rx, ry, rotation, largeArc, sweep, ax, ay);
                    _lastCommandUpper = 'A';
                    break;

                default:
                    index++; // 跳过未知命令
                    break;
            }
        }

        // Finalize the last contour and triangulate all contours
        FinalizeCurrentContour();
        TriangulateAllContours();
    }

    private static Vector2 ReflectPoint(Vector2 control, Vector2 current)
    {
        return new Vector2(2 * current.X - control.X, 2 * current.Y - control.Y);
    }

    private static List<string> TokenizePath(string pathData)
    {
        var tokens = new List<string>(pathData.Length);
        var current = new System.Text.StringBuilder();
        bool hasDecimalPoint = false;

        for (int i = 0; i < pathData.Length; i++)
        {
            var c = pathData[i];

            if (char.IsLetter(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                tokens.Add(c.ToString());
                hasDecimalPoint = false;
            }
            else if (c == ',' || c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                hasDecimalPoint = false;
            }
            else if (c == '-' && current.Length > 0 && current[^1] != 'e' && current[^1] != 'E')
            {
                // 负号是新数字的开始（除非是科学计数法）
                tokens.Add(current.ToString());
                current.Clear();
                current.Append(c);
                hasDecimalPoint = false;
            }
            else if (c == '.' && hasDecimalPoint)
            {
                // 第二个小数点表示新数字的开始，如 "1.5.3" → "1.5", ".3"
                tokens.Add(current.ToString());
                current.Clear();
                current.Append(c);
                // hasDecimalPoint 保持 true，因为新 token 已经包含小数点
            }
            else
            {
                if (c == '.')
                    hasDecimalPoint = true;
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static float ParseFloat(List<string> tokens, ref int index)
    {
        if (index >= tokens.Count)
            return 0;

        var token = tokens[index++];
        return float.TryParse(token, out var result) ? result : 0;
    }

    private void FinalizeCurrentContour()
    {
        if (_currentContour.Count >= 3)
        {
            _contours.Add(_currentContour);
        }
        _currentContour = new List<Vector2>();
    }

    private void MoveTo(float x, float y)
    {
        // Finalize previous contour before starting a new one
        FinalizeCurrentContour();

        _currentPoint = new Vector2(x, y);
        _startPoint = _currentPoint;
        _currentContour.Add(_currentPoint);
    }

    private void LineTo(float x, float y)
    {
        _currentPoint = new Vector2(x, y);
        _currentContour.Add(_currentPoint);
    }

    private void CubicBezierTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
    {
        var p0 = _currentPoint;
        var p1 = new Vector2(c1x, c1y);
        var p2 = new Vector2(c2x, c2y);
        var p3 = new Vector2(x, y);

        // 自适应细分
        SubdivideCubicBezier(p0, p1, p2, p3, 0);

        _currentPoint = p3;
    }

    private void SubdivideCubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int depth)
    {
        const int maxDepth = 10;

        if (depth > maxDepth)
        {
            _currentContour.Add(p3);
            return;
        }

        // 计算平坦度（控制点到直线的距离）
        var d1 = PointToLineDistance(p1, p0, p3);
        var d2 = PointToLineDistance(p2, p0, p3);

        if (d1 + d2 < Tolerance)
        {
            _currentContour.Add(p3);
            return;
        }

        // De Casteljau 细分
        var p01 = (p0 + p1) * 0.5f;
        var p12 = (p1 + p2) * 0.5f;
        var p23 = (p2 + p3) * 0.5f;
        var p012 = (p01 + p12) * 0.5f;
        var p123 = (p12 + p23) * 0.5f;
        var p0123 = (p012 + p123) * 0.5f;

        SubdivideCubicBezier(p0, p01, p012, p0123, depth + 1);
        SubdivideCubicBezier(p0123, p123, p23, p3, depth + 1);
    }

    private void QuadraticBezierTo(float cx, float cy, float x, float y)
    {
        var p0 = _currentPoint;
        var p1 = new Vector2(cx, cy);
        var p2 = new Vector2(x, y);

        // 转换为三次贝塞尔
        var c1 = p0 + 2f / 3f * (p1 - p0);
        var c2 = p2 + 2f / 3f * (p1 - p2);

        CubicBezierTo(c1.X, c1.Y, c2.X, c2.Y, x, y);
    }

    private void ArcTo(float rx, float ry, float rotation, bool largeArc, bool sweep, float x, float y)
    {
        var endPoint = new Vector2(x, y);

        if (rx < 0.001f || ry < 0.001f || _currentPoint == endPoint)
        {
            LineTo(x, y);
            return;
        }

        // SVG endpoint-to-center parameterization (same algorithm as RenderTargetDrawingContext)
        rx = MathF.Abs(rx);
        ry = MathF.Abs(ry);

        var dx = (_currentPoint.X - endPoint.X) / 2f;
        var dy = (_currentPoint.Y - endPoint.Y) / 2f;

        var rotRad = rotation * MathF.PI / 180f;
        var cosA = MathF.Cos(rotRad);
        var sinA = MathF.Sin(rotRad);

        var x1p = cosA * dx + sinA * dy;
        var y1p = -sinA * dx + cosA * dy;

        // Ensure radii are large enough
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;

        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1f)
        {
            var sqrtLambda = MathF.Sqrt(lambda);
            rx *= sqrtLambda;
            ry *= sqrtLambda;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        // Calculate center
        var sign = (largeArc != sweep) ? 1f : -1f;
        var sq = MathF.Max(0f, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
        var coef = sign * MathF.Sqrt(sq);

        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;

        var cx = cosA * cxp - sinA * cyp + (_currentPoint.X + endPoint.X) / 2f;
        var cy = sinA * cxp + cosA * cyp + (_currentPoint.Y + endPoint.Y) / 2f;

        var startAngle = MathF.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var endAngle = MathF.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);

        var deltaAngle = endAngle - startAngle;
        if (sweep && deltaAngle < 0)
            deltaAngle += 2f * MathF.PI;
        else if (!sweep && deltaAngle > 0)
            deltaAngle -= 2f * MathF.PI;

        var segments = Math.Max(8, (int)(MathF.Abs(deltaAngle) * MathF.Max(rx, ry) / Tolerance));
        for (int i = 1; i <= segments; i++)
        {
            var t = (float)i / segments;
            var angle = startAngle + deltaAngle * t;

            var px = rx * MathF.Cos(angle);
            var py = ry * MathF.Sin(angle);

            var ptX = cosA * px - sinA * py + cx;
            var ptY = sinA * px + cosA * py + cy;

            _currentContour.Add(new Vector2(ptX, ptY));
        }

        _currentPoint = endPoint;
    }

    private void ClosePath()
    {
        if (_currentContour.Count > 0 && _currentPoint != _startPoint)
        {
            _currentContour.Add(_startPoint);
        }
        _currentPoint = _startPoint;
    }

    private void TriangulateAllContours()
    {
        foreach (var contour in _contours)
        {
            TriangulateSingleContour(contour);
        }
    }

    private void TriangulateSingleContour(List<Vector2> contour)
    {
        if (contour.Count < 3)
            return;

        // 使用耳切法（Ear Clipping）进行三角化
        var baseIndex = (uint)_vertices.Count;

        // 添加顶点
        _vertices.AddRange(contour);

        // 创建顶点索引列表
        var indices = new List<int>(contour.Count);
        for (int i = 0; i < contour.Count; i++)
            indices.Add(i);

        // 确保顶点顺序为逆时针（耳切法需要一致的缠绕方向）
        if (ComputeSignedArea(contour) < 0)
            indices.Reverse();

        // 耳切法
        while (indices.Count > 3)
        {
            bool earFound = false;

            for (int i = 0; i < indices.Count; i++)
            {
                var prev = indices[(i + indices.Count - 1) % indices.Count];
                var curr = indices[i];
                var next = indices[(i + 1) % indices.Count];

                if (IsEar(contour, indices, prev, curr, next))
                {
                    // 添加三角形
                    _indices.Add((uint)(baseIndex + prev));
                    _indices.Add((uint)(baseIndex + curr));
                    _indices.Add((uint)(baseIndex + next));

                    // 移除当前顶点
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound)
            {
                // 自相交或退化路径 — 尝试强制三角化剩余顶点作为 fan
                // 这比丢弃它们产生更好的视觉效果
                System.Diagnostics.Debug.WriteLine(
                    $"[PathTessellator] Ear-clipping stuck with {indices.Count} remaining vertices, using fan fallback");
                while (indices.Count > 2)
                {
                    _indices.Add((uint)(baseIndex + indices[0]));
                    _indices.Add((uint)(baseIndex + indices[1]));
                    _indices.Add((uint)(baseIndex + indices[2]));
                    indices.RemoveAt(1);
                }
                break;
            }
        }

        // 添加最后一个三角形
        if (indices.Count == 3)
        {
            _indices.Add((uint)(baseIndex + indices[0]));
            _indices.Add((uint)(baseIndex + indices[1]));
            _indices.Add((uint)(baseIndex + indices[2]));
        }
    }

    private static float ComputeSignedArea(List<Vector2> contour)
    {
        float area = 0;
        for (int i = 0; i < contour.Count; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Count];
            area += (b.X - a.X) * (b.Y + a.Y);
        }
        return area;
    }

    private static bool IsEar(List<Vector2> vertices, List<int> indices, int prev, int curr, int next)
    {
        var a = vertices[prev];
        var b = vertices[curr];
        var c = vertices[next];

        // 检查是否是凸顶点
        if (Cross(b - a, c - b) <= 0)
            return false;

        // 检查是否有其他顶点在三角形内
        for (int i = 0; i < indices.Count; i++)
        {
            var idx = indices[i];
            if (idx == prev || idx == curr || idx == next)
                continue;

            if (PointInTriangle(vertices[idx], a, b, c))
                return false;
        }

        return true;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var v0 = c - a;
        var v1 = b - a;
        var v2 = p - a;

        var dot00 = Vector2.Dot(v0, v0);
        var dot01 = Vector2.Dot(v0, v1);
        var dot02 = Vector2.Dot(v0, v2);
        var dot11 = Vector2.Dot(v1, v1);
        var dot12 = Vector2.Dot(v1, v2);

        var denom = dot00 * dot11 - dot01 * dot01;
        if (MathF.Abs(denom) < 1e-6f)
            return false; // Degenerate triangle — no point can be "inside"

        var invDenom = 1 / denom;
        var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        var v = (dot00 * dot12 - dot01 * dot02) * invDenom;

        return u >= 0 && v >= 0 && u + v <= 1;
    }

    private static float PointToLineDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        var line = lineEnd - lineStart;
        var len = line.Length();
        if (len < 0.0001f)
            return Vector2.Distance(point, lineStart);

        var t = Vector2.Dot(point - lineStart, line) / (len * len);
        t = Math.Clamp(t, 0, 1);

        var projection = lineStart + t * line;
        return Vector2.Distance(point, projection);
    }
}

/// <summary>
/// 三角化结果
/// </summary>
public readonly record struct TessellationResult(Vector2[] Vertices, uint[] Indices)
{
    public static readonly TessellationResult Empty = new([], []);
    public bool IsEmpty => Vertices.Length == 0;
}
