using System.Numerics;

namespace Jalium.UI.Gpu;

/// <summary>
/// 路径三角化器 - 将矢量路径转换为 GPU 可渲染的三角形网格
/// </summary>
public sealed class PathTessellator
{
    private readonly List<Vector2> _vertices = new();
    private readonly List<ushort> _indices = new();
    private readonly List<Vector2> _currentContour = new();
    private Vector2 _currentPoint;
    private Vector2 _startPoint;

    /// <summary>
    /// 细分精度（较小的值产生更多三角形但更平滑）
    /// </summary>
    public float Tolerance { get; set; } = 0.25f;

    /// <summary>
    /// 将 SVG 路径数据转换为三角形网格
    /// </summary>
    public TessellationResult Tessellate(string pathData)
    {
        Reset();

        if (string.IsNullOrEmpty(pathData))
            return new TessellationResult([], []);

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

        // 生成椭圆顶点
        var center = new Vector2(cx, cy);
        _vertices.Add(center);

        for (int i = 0; i <= segments; i++)
        {
            var angle = 2 * MathF.PI * i / segments;
            var point = new Vector2(
                cx + rx * MathF.Cos(angle),
                cy + ry * MathF.Sin(angle));
            _vertices.Add(point);
        }

        // 生成三角形扇形索引
        for (int i = 1; i <= segments; i++)
        {
            _indices.Add(0);
            _indices.Add((ushort)i);
            _indices.Add((ushort)(i % segments + 1));
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

        _vertices.Add(new Vector2(x, y));
        _vertices.Add(new Vector2(x + width, y));
        _vertices.Add(new Vector2(x + width, y + height));
        _vertices.Add(new Vector2(x, y + height));

        _indices.AddRange(new ushort[] { 0, 1, 2, 0, 2, 3 });

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
        var centerIndex = (ushort)_vertices.Count;
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
        for (int i = 1; i < vertexCount; i++)
        {
            _indices.Add(centerIndex);
            _indices.Add((ushort)i);
            _indices.Add((ushort)(i % (vertexCount - 1) + 1));
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
        _currentContour.Clear();
        _currentPoint = Vector2.Zero;
        _startPoint = Vector2.Zero;
    }

    private void ParseAndTessellate(string pathData)
    {
        var tokens = TokenizePath(pathData);
        var index = 0;

        char lastCommand = ' ';

        while (index < tokens.Count)
        {
            var token = tokens[index];

            // 检查是否是命令
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                lastCommand = token[0];
                index++;
                continue;
            }

            // 根据命令处理参数
            switch (char.ToUpper(lastCommand))
            {
                case 'M': // MoveTo
                    var mx = ParseFloat(tokens, ref index);
                    var my = ParseFloat(tokens, ref index);
                    if (char.IsLower(lastCommand))
                    {
                        mx += _currentPoint.X;
                        my += _currentPoint.Y;
                    }
                    MoveTo(mx, my);
                    lastCommand = char.IsLower(lastCommand) ? 'l' : 'L'; // 后续参数视为 LineTo
                    break;

                case 'L': // LineTo
                    var lx = ParseFloat(tokens, ref index);
                    var ly = ParseFloat(tokens, ref index);
                    if (char.IsLower(lastCommand))
                    {
                        lx += _currentPoint.X;
                        ly += _currentPoint.Y;
                    }
                    LineTo(lx, ly);
                    break;

                case 'H': // Horizontal LineTo
                    var hx = ParseFloat(tokens, ref index);
                    if (char.IsLower(lastCommand))
                        hx += _currentPoint.X;
                    LineTo(hx, _currentPoint.Y);
                    break;

                case 'V': // Vertical LineTo
                    var vy = ParseFloat(tokens, ref index);
                    if (char.IsLower(lastCommand))
                        vy += _currentPoint.Y;
                    LineTo(_currentPoint.X, vy);
                    break;

                case 'C': // CurveTo (三次贝塞尔)
                    var c1x = ParseFloat(tokens, ref index);
                    var c1y = ParseFloat(tokens, ref index);
                    var c2x = ParseFloat(tokens, ref index);
                    var c2y = ParseFloat(tokens, ref index);
                    var cx = ParseFloat(tokens, ref index);
                    var cy = ParseFloat(tokens, ref index);
                    if (char.IsLower(lastCommand))
                    {
                        c1x += _currentPoint.X;
                        c1y += _currentPoint.Y;
                        c2x += _currentPoint.X;
                        c2y += _currentPoint.Y;
                        cx += _currentPoint.X;
                        cy += _currentPoint.Y;
                    }
                    CubicBezierTo(c1x, c1y, c2x, c2y, cx, cy);
                    break;

                case 'Q': // QuadraticCurveTo
                    var qcx = ParseFloat(tokens, ref index);
                    var qcy = ParseFloat(tokens, ref index);
                    var qx = ParseFloat(tokens, ref index);
                    var qy = ParseFloat(tokens, ref index);
                    if (char.IsLower(lastCommand))
                    {
                        qcx += _currentPoint.X;
                        qcy += _currentPoint.Y;
                        qx += _currentPoint.X;
                        qy += _currentPoint.Y;
                    }
                    QuadraticBezierTo(qcx, qcy, qx, qy);
                    break;

                case 'A': // Arc
                    var rx = ParseFloat(tokens, ref index);
                    var ry = ParseFloat(tokens, ref index);
                    var rotation = ParseFloat(tokens, ref index);
                    var largeArc = ParseFloat(tokens, ref index) != 0;
                    var sweep = ParseFloat(tokens, ref index) != 0;
                    var ax = ParseFloat(tokens, ref index);
                    var ay = ParseFloat(tokens, ref index);
                    if (char.IsLower(lastCommand))
                    {
                        ax += _currentPoint.X;
                        ay += _currentPoint.Y;
                    }
                    ArcTo(rx, ry, rotation, largeArc, sweep, ax, ay);
                    break;

                case 'Z': // ClosePath
                    ClosePath();
                    break;

                default:
                    index++; // 跳过未知命令
                    break;
            }
        }

        // 三角化所有轮廓
        TriangulateContours();
    }

    private static List<string> TokenizePath(string pathData)
    {
        var tokens = new List<string>(pathData.Length);
        var current = new System.Text.StringBuilder();

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
            }
            else if (c == ',' || c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else if (c == '-' && current.Length > 0 && current[^1] != 'e' && current[^1] != 'E')
            {
                // 负号是新数字的开始（除非是科学计数法）
                tokens.Add(current.ToString());
                current.Clear();
                current.Append(c);
            }
            else
            {
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

    private void MoveTo(float x, float y)
    {
        // 关闭当前轮廓
        if (_currentContour.Count > 0)
        {
            // 将轮廓添加到顶点列表
            // 这里简化处理
        }

        _currentContour.Clear();
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
        // 简化实现：将圆弧转换为贝塞尔曲线
        // 完整实现需要更复杂的数学

        var endPoint = new Vector2(x, y);

        if (rx < 0.001f || ry < 0.001f || _currentPoint == endPoint)
        {
            LineTo(x, y);
            return;
        }

        // 简化：使用多段二次贝塞尔近似
        var segments = Math.Max(4, (int)(MathF.Max(rx, ry) / Tolerance));
        var center = (_currentPoint + endPoint) * 0.5f;
        var startAngle = MathF.Atan2(_currentPoint.Y - center.Y, _currentPoint.X - center.X);
        var endAngle = MathF.Atan2(endPoint.Y - center.Y, endPoint.X - center.X);

        var angleDiff = endAngle - startAngle;
        if (sweep && angleDiff < 0) angleDiff += MathF.PI * 2;
        if (!sweep && angleDiff > 0) angleDiff -= MathF.PI * 2;

        if (largeArc)
        {
            if (MathF.Abs(angleDiff) < MathF.PI)
            {
                angleDiff = angleDiff > 0 ? angleDiff - MathF.PI * 2 : angleDiff + MathF.PI * 2;
            }
        }

        var angleStep = angleDiff / segments;

        for (int i = 1; i <= segments; i++)
        {
            var angle = startAngle + angleStep * i;
            var point = new Vector2(
                center.X + rx * MathF.Cos(angle),
                center.Y + ry * MathF.Sin(angle));
            _currentContour.Add(point);
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

    private void TriangulateContours()
    {
        if (_currentContour.Count < 3)
            return;

        // 使用耳切法（Ear Clipping）进行三角化
        var baseIndex = (ushort)_vertices.Count;

        // 添加顶点
        _vertices.AddRange(_currentContour);

        // 创建顶点索引列表
        var indices = new List<int>(_currentContour.Count);
        for (int i = 0; i < _currentContour.Count; i++)
            indices.Add(i);

        // 耳切法
        while (indices.Count > 3)
        {
            bool earFound = false;

            for (int i = 0; i < indices.Count; i++)
            {
                var prev = indices[(i + indices.Count - 1) % indices.Count];
                var curr = indices[i];
                var next = indices[(i + 1) % indices.Count];

                if (IsEar(_currentContour, indices, prev, curr, next))
                {
                    // 添加三角形
                    _indices.Add((ushort)(baseIndex + prev));
                    _indices.Add((ushort)(baseIndex + curr));
                    _indices.Add((ushort)(baseIndex + next));

                    // 移除当前顶点
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound)
                break; // 无法继续（可能是自相交的路径）
        }

        // 添加最后一个三角形
        if (indices.Count == 3)
        {
            _indices.Add((ushort)(baseIndex + indices[0]));
            _indices.Add((ushort)(baseIndex + indices[1]));
            _indices.Add((ushort)(baseIndex + indices[2]));
        }
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

        var invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
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
public readonly record struct TessellationResult(Vector2[] Vertices, ushort[] Indices)
{
    public bool IsEmpty => Vertices.Length == 0;
}
