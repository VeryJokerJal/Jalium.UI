using System.Numerics;
using Jalium.UI.Media;

namespace Jalium.UI.Gpu;

/// <summary>
/// 文本布局器 - 计算文本的字形位置
/// </summary>
public sealed class TextLayoutEngine
{
    private readonly Dictionary<string, FontMetrics> _fontMetricsCache = new();
    private readonly Dictionary<(string fontId, char c), GlyphInfo> _glyphInfoCache = new();

    /// <summary>
    /// 布局文本并生成字形运行
    /// </summary>
    public TextLayoutResult Layout(string text, TextLayoutOptions options)
    {
        if (string.IsNullOrEmpty(text))
            return TextLayoutResult.Empty;

        var fontMetrics = GetFontMetrics(options.FontFamily, options.FontSize);
        var glyphs = new List<GlyphInstance>();
        var lines = new List<TextLine>();

        var x = 0f;
        var y = fontMetrics.Ascender;
        var lineStartIndex = 0;
        var lineWidth = 0f;
        var maxWidth = 0f;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // 换行符处理
            if (c == '\n')
            {
                lines.Add(new TextLine(lineStartIndex, i - lineStartIndex, lineWidth, y - fontMetrics.Ascender));
                maxWidth = MathF.Max(maxWidth, lineWidth);

                x = 0;
                y += fontMetrics.LineHeight;
                lineStartIndex = i + 1;
                lineWidth = 0;
                continue;
            }

            // 空格处理
            if (c == ' ')
            {
                x += fontMetrics.SpaceWidth;
                lineWidth = x;
                continue;
            }

            // 制表符处理
            if (c == '\t')
            {
                var tabWidth = fontMetrics.SpaceWidth * 4;
                x = MathF.Ceiling(x / tabWidth + 1) * tabWidth;
                lineWidth = x;
                continue;
            }

            // 获取字形信息
            var glyphInfo = GetGlyphInfo(options.FontFamily, c, options.FontSize);

            // 自动换行
            if (options.MaxWidth > 0 && x + glyphInfo.AdvanceWidth > options.MaxWidth && x > 0)
            {
                // 尝试在单词边界换行
                var breakIndex = FindWordBreak(text, lineStartIndex, i);
                if (breakIndex > lineStartIndex)
                {
                    // 移除当前行之后的字形
                    var removeCount = i - breakIndex;
                    glyphs.RemoveRange(glyphs.Count - removeCount, removeCount);

                    // 重新计算行宽度
                    lineWidth = 0;
                    for (int j = glyphs.Count - (i - lineStartIndex - removeCount); j < glyphs.Count; j++)
                    {
                        lineWidth += glyphs[j].AdvanceWidth;
                    }

                    lines.Add(new TextLine(lineStartIndex, breakIndex - lineStartIndex, lineWidth, y - fontMetrics.Ascender));
                    maxWidth = MathF.Max(maxWidth, lineWidth);

                    x = 0;
                    y += fontMetrics.LineHeight;
                    lineStartIndex = breakIndex;
                    lineWidth = 0;

                    // 跳过换行位置的空格
                    while (lineStartIndex < text.Length && text[lineStartIndex] == ' ')
                        lineStartIndex++;

                    i = lineStartIndex - 1; // -1 因为循环会 +1
                    continue;
                }

                // 强制换行
                lines.Add(new TextLine(lineStartIndex, i - lineStartIndex, lineWidth, y - fontMetrics.Ascender));
                maxWidth = MathF.Max(maxWidth, lineWidth);

                x = 0;
                y += fontMetrics.LineHeight;
                lineStartIndex = i;
                lineWidth = 0;
            }

            // 添加字形实例
            glyphs.Add(new GlyphInstance
            {
                Character = c,
                GlyphIndex = glyphInfo.GlyphIndex,
                Position = new Vector2(x + glyphInfo.BearingX, y - glyphInfo.BearingY),
                Size = new Vector2(glyphInfo.Width, glyphInfo.Height),
                UVRect = glyphInfo.UVRect,
                AdvanceWidth = glyphInfo.AdvanceWidth
            });

            x += glyphInfo.AdvanceWidth;

            // 字距调整
            if (options.EnableKerning && i + 1 < text.Length)
            {
                x += GetKerning(options.FontFamily, c, text[i + 1], options.FontSize);
            }

            lineWidth = x;
        }

        // 添加最后一行
        if (lineStartIndex < text.Length)
        {
            lines.Add(new TextLine(lineStartIndex, text.Length - lineStartIndex, lineWidth, y - fontMetrics.Ascender));
            maxWidth = MathF.Max(maxWidth, lineWidth);
        }

        // 应用对齐
        ApplyAlignment(glyphs, lines, options.TextAlignment, maxWidth, options.MaxWidth);

        return new TextLayoutResult
        {
            Glyphs = glyphs.ToArray(),
            Lines = lines.ToArray(),
            Bounds = new Vector2(maxWidth, y + fontMetrics.Descender),
            FontMetrics = fontMetrics
        };
    }

    private void ApplyAlignment(List<GlyphInstance> glyphs, List<TextLine> lines, TextAlignment alignment, float maxWidth, float containerWidth)
    {
        if (alignment == TextAlignment.Left)
            return;

        var targetWidth = containerWidth > 0 ? containerWidth : maxWidth;
        var glyphIndex = 0;

        foreach (var line in lines)
        {
            float offset = alignment switch
            {
                TextAlignment.Center => (targetWidth - line.Width) / 2,
                TextAlignment.Right => targetWidth - line.Width,
                _ => 0
            };

            if (offset > 0)
            {
                for (int i = 0; i < line.Length; i++)
                {
                    if (glyphIndex < glyphs.Count)
                    {
                        var glyph = glyphs[glyphIndex];
                        glyphs[glyphIndex] = glyph with { Position = glyph.Position + new Vector2(offset, 0) };
                        glyphIndex++;
                    }
                }
            }
            else
            {
                glyphIndex += line.Length;
            }
        }
    }

    private static int FindWordBreak(string text, int start, int end)
    {
        // 从后向前查找空格
        for (int i = end - 1; i > start; i--)
        {
            if (text[i] == ' ')
                return i + 1;
        }
        return start;
    }

    private FontMetrics GetFontMetrics(string fontFamily, float fontSize)
    {
        var key = $"{fontFamily}_{fontSize}";
        if (_fontMetricsCache.TryGetValue(key, out var cached))
            return cached;

        // 默认字体度量（基于常见的 TrueType 字体）
        // 实际实现需要读取字体文件
        var metrics = new FontMetrics
        {
            FontFamily = fontFamily,
            FontSize = fontSize,
            Ascender = fontSize * 0.8f,
            Descender = fontSize * 0.2f,
            LineHeight = fontSize * 1.2f,
            SpaceWidth = fontSize * 0.25f,
            UnitsPerEm = 1000
        };

        _fontMetricsCache[key] = metrics;
        return metrics;
    }

    private GlyphInfo GetGlyphInfo(string fontFamily, char c, float fontSize)
    {
        var key = (fontFamily, c);
        if (_glyphInfoCache.TryGetValue(key, out var cached))
        {
            // 缩放到当前字体大小
            var scale = fontSize / 1000f; // 假设缓存的是 1000 单位大小
            return cached with
            {
                Width = cached.Width * scale,
                Height = cached.Height * scale,
                BearingX = cached.BearingX * scale,
                BearingY = cached.BearingY * scale,
                AdvanceWidth = cached.AdvanceWidth * scale
            };
        }

        // 生成默认字形信息
        // 实际实现需要从字体文件读取
        var charWidth = GetDefaultCharWidth(c);
        var info = new GlyphInfo
        {
            GlyphIndex = (ushort)c,
            Width = charWidth * fontSize,
            Height = fontSize * 0.75f,
            BearingX = 0,
            BearingY = fontSize * 0.75f,
            AdvanceWidth = charWidth * fontSize,
            UVRect = CalculateGlyphUV(c)
        };

        _glyphInfoCache[key] = info;
        return info;
    }

    private static float GetDefaultCharWidth(char c)
    {
        // 简化的字符宽度估算
        return c switch
        {
            'i' or 'l' or '!' or '|' or 'I' => 0.3f,
            'm' or 'w' or 'M' or 'W' => 0.9f,
            >= 'A' and <= 'Z' => 0.7f,
            >= 'a' and <= 'z' => 0.5f,
            >= '0' and <= '9' => 0.6f,
            _ => 0.5f
        };
    }

    private static Rect CalculateGlyphUV(char c)
    {
        // 简化的 UV 计算
        // 假设字形图集是 16x16 的网格
        var index = c % 256;
        var col = index % 16;
        var row = index / 16;

        return new Rect(col / 16f, row / 16f, 1f / 16f, 1f / 16f);
    }

    private float GetKerning(string fontFamily, char left, char right, float fontSize)
    {
        // 简化实现，实际需要从字体文件读取
        // 常见的字距调整对
        var pair = (left, right);
        var kerning = pair switch
        {
            ('A', 'V') or ('A', 'W') or ('A', 'Y') => -0.1f,
            ('V', 'A') or ('W', 'A') or ('Y', 'A') => -0.1f,
            ('T', 'o') or ('T', 'a') or ('T', 'e') => -0.1f,
            ('L', 'T') or ('L', 'V') or ('L', 'W') or ('L', 'Y') => -0.1f,
            ('P', '.') or ('P', ',') => -0.15f,
            ('F', '.') or ('F', ',') => -0.15f,
            _ => 0f
        };

        return kerning * fontSize;
    }
}

/// <summary>
/// 文本布局选项
/// </summary>
public sealed class TextLayoutOptions
{
    public string FontFamily { get; set; } = "default";
    public float FontSize { get; set; } = 14;
    public float MaxWidth { get; set; } = 0; // 0 表示无限制
    public float MaxHeight { get; set; } = 0;
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
    public bool EnableKerning { get; set; } = true;
    public float LineSpacing { get; set; } = 1.0f;
    public TextWrapping TextWrapping { get; set; } = TextWrapping.NoWrap;
}

/// <summary>
/// 文本布局结果
/// </summary>
public sealed class TextLayoutResult
{
    public required GlyphInstance[] Glyphs { get; init; }
    public required TextLine[] Lines { get; init; }
    public required Vector2 Bounds { get; init; }
    public required FontMetrics FontMetrics { get; init; }

    public static TextLayoutResult Empty => new()
    {
        Glyphs = [],
        Lines = [],
        Bounds = Vector2.Zero,
        FontMetrics = new FontMetrics()
    };
}

/// <summary>
/// 字形实例 - 单个字符的渲染信息
/// </summary>
public record struct GlyphInstance
{
    public char Character;
    public ushort GlyphIndex;
    public Vector2 Position;
    public Vector2 Size;
    public Rect UVRect;
    public float AdvanceWidth;
}

/// <summary>
/// 文本行
/// </summary>
public readonly record struct TextLine(int StartIndex, int Length, float Width, float Y);

/// <summary>
/// 字体度量信息
/// </summary>
public sealed class FontMetrics
{
    public string FontFamily { get; init; } = "";
    public float FontSize { get; init; }
    public float Ascender { get; init; }
    public float Descender { get; init; }
    public float LineHeight { get; init; }
    public float SpaceWidth { get; init; }
    public int UnitsPerEm { get; init; }
}

/// <summary>
/// 字形信息
/// </summary>
public record struct GlyphInfo
{
    public ushort GlyphIndex;
    public float Width;
    public float Height;
    public float BearingX;
    public float BearingY;
    public float AdvanceWidth;
    public Rect UVRect;
}

/// <summary>
/// 字形图集生成器
/// </summary>
public sealed class GlyphAtlasGenerator
{
    private readonly int _atlasWidth;
    private readonly int _atlasHeight;
    private int _currentX;
    private int _currentY;
    private int _rowHeight;

    public GlyphAtlasGenerator(int width = 1024, int height = 1024)
    {
        _atlasWidth = width;
        _atlasHeight = height;
    }

    /// <summary>
    /// 生成字形图集
    /// </summary>
    public GlyphAtlas Generate(string fontFamily, float fontSize, string characters)
    {
        var glyphs = new Dictionary<char, AtlasGlyph>();
        var padding = 2;

        _currentX = padding;
        _currentY = padding;
        _rowHeight = 0;

        foreach (var c in characters.Distinct())
        {
            if (char.IsWhiteSpace(c))
                continue;

            var glyphWidth = (int)(GetDefaultCharWidth(c) * fontSize) + padding * 2;
            var glyphHeight = (int)(fontSize * 0.9f) + padding * 2;

            // 检查是否需要换行
            if (_currentX + glyphWidth > _atlasWidth)
            {
                _currentX = padding;
                _currentY += _rowHeight + padding;
                _rowHeight = 0;
            }

            // 检查是否超出图集
            if (_currentY + glyphHeight > _atlasHeight)
            {
                // 图集已满，停止添加
                break;
            }

            // 计算 UV 坐标
            var uv = new Rect(
                (float)_currentX / _atlasWidth,
                (float)_currentY / _atlasHeight,
                (float)glyphWidth / _atlasWidth,
                (float)glyphHeight / _atlasHeight);

            glyphs[c] = new AtlasGlyph
            {
                Character = c,
                X = _currentX,
                Y = _currentY,
                Width = glyphWidth,
                Height = glyphHeight,
                UVRect = uv
            };

            _currentX += glyphWidth + padding;
            _rowHeight = Math.Max(_rowHeight, glyphHeight);
        }

        return new GlyphAtlas
        {
            FontFamily = fontFamily,
            FontSize = fontSize,
            Width = _atlasWidth,
            Height = _atlasHeight,
            Glyphs = glyphs
        };
    }

    private static float GetDefaultCharWidth(char c)
    {
        return c switch
        {
            'i' or 'l' or '!' or '|' or 'I' => 0.3f,
            'm' or 'w' or 'M' or 'W' => 0.9f,
            >= 'A' and <= 'Z' => 0.7f,
            >= 'a' and <= 'z' => 0.5f,
            >= '0' and <= '9' => 0.6f,
            _ => 0.5f
        };
    }
}

/// <summary>
/// 字形图集
/// </summary>
public sealed class GlyphAtlas
{
    public required string FontFamily { get; init; }
    public required float FontSize { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Dictionary<char, AtlasGlyph> Glyphs { get; init; }
}

/// <summary>
/// 图集中的字形
/// </summary>
public record struct AtlasGlyph
{
    public char Character;
    public int X;
    public int Y;
    public int Width;
    public int Height;
    public Rect UVRect;
}
