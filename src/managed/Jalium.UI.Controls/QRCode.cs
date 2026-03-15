using System.Collections;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Error correction levels supported by the <see cref="QRCode"/> control.
/// </summary>
public enum QRCodeErrorCorrectionLevel
{
    L,
    M,
    Q,
    H
}

/// <summary>
/// Displays a QR code generated from text content.
/// </summary>
public class QRCode : Control
{
    private bool[,]? _modules;
    private string? _generationError;

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(QRCode),
            new PropertyMetadata(string.Empty, OnQrCodeDataChanged));

    /// <summary>
    /// Identifies the QuietZoneModules dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty QuietZoneModulesProperty =
        DependencyProperty.Register(nameof(QuietZoneModules), typeof(int), typeof(QRCode),
            new PropertyMetadata(4, OnQrCodeDataChanged, CoerceQuietZoneModules));

    /// <summary>
    /// Identifies the ErrorCorrectionLevel dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ErrorCorrectionLevelProperty =
        DependencyProperty.Register(nameof(ErrorCorrectionLevel), typeof(QRCodeErrorCorrectionLevel), typeof(QRCode),
            new PropertyMetadata(QRCodeErrorCorrectionLevel.Q, OnQrCodeDataChanged));

    /// <summary>
    /// Gets or sets the text encoded by the QR code.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the quiet zone in modules.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int QuietZoneModules
    {
        get => (int)GetValue(QuietZoneModulesProperty)!;
        set => SetValue(QuietZoneModulesProperty, value);
    }

    /// <summary>
    /// Gets or sets the QR code error correction level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public QRCodeErrorCorrectionLevel ErrorCorrectionLevel
    {
        get => (QRCodeErrorCorrectionLevel)(GetValue(ErrorCorrectionLevelProperty) ?? QRCodeErrorCorrectionLevel.Q);
        set => SetValue(ErrorCorrectionLevelProperty, value);
    }

    internal int ModuleCount => _modules?.GetLength(0) ?? 0;
    internal int TotalModuleCount => ModuleCount == 0 ? 0 : ModuleCount + (QuietZoneModules * 2);
    internal string? GenerationError => _generationError;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureModules();

        var chromeWidth = BorderThickness.TotalWidth + Padding.TotalWidth;
        var chromeHeight = BorderThickness.TotalHeight + Padding.TotalHeight;

        if (_modules == null)
        {
            return new Size(chromeWidth, chromeHeight);
        }

        var idealSide = Math.Max(96, TotalModuleCount * 4);
        var availableWidth = double.IsInfinity(availableSize.Width)
            ? idealSide
            : Math.Max(0, availableSize.Width - chromeWidth);
        var availableHeight = double.IsInfinity(availableSize.Height)
            ? idealSide
            : Math.Max(0, availableSize.Height - chromeHeight);

        double side;
        if (double.IsInfinity(availableSize.Width) && double.IsInfinity(availableSize.Height))
        {
            side = idealSide;
        }
        else if (double.IsInfinity(availableSize.Width))
        {
            side = Math.Min(idealSide, availableHeight);
        }
        else if (double.IsInfinity(availableSize.Height))
        {
            side = Math.Min(idealSide, availableWidth);
        }
        else
        {
            side = Math.Min(availableWidth, availableHeight);
        }

        side = Math.Max(TotalModuleCount, side);
        return new Size(side + chromeWidth, side + chromeHeight);
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
        {
            return;
        }

        EnsureModules();

        var outerRect = new Rect(RenderSize);
        var borderPen = CreateBorderPen();
        if (Background != null || borderPen != null)
        {
            dc.DrawRoundedRectangle(Background, borderPen, outerRect, CornerRadius);
        }

        if (_modules == null)
        {
            return;
        }

        var contentRect = GetContentRect();
        if (contentRect.Width <= 0 || contentRect.Height <= 0)
        {
            return;
        }

        var side = Math.Min(contentRect.Width, contentRect.Height);
        if (side <= 0)
        {
            return;
        }

        var qrRect = AlignSquare(contentRect, side);
        var fillBrush = Foreground ?? new SolidColorBrush(Color.Black);
        var quietZone = QuietZoneModules;
        var totalModules = TotalModuleCount;
        var rowCount = _modules.GetLength(0);
        var columnCount = _modules.GetLength(1);
        var moduleSize = Math.Max(1.0, Math.Floor(qrRect.Width / totalModules));
        var renderSide = moduleSize * totalModules;
        var snappedLeft = Math.Round(qrRect.X + ((qrRect.Width - renderSide) / 2));
        var snappedTop = Math.Round(qrRect.Y + ((qrRect.Height - renderSide) / 2));

        for (var row = 0; row < rowCount; row++)
        {
            var column = 0;
            while (column < columnCount)
            {
                if (!_modules[row, column])
                {
                    column++;
                    continue;
                }

                var runStart = column;
                while (column + 1 < columnCount && _modules[row, column + 1])
                {
                    column++;
                }

                var runLength = column - runStart + 1;
                var left = snappedLeft + ((runStart + quietZone) * moduleSize);
                var top = snappedTop + ((row + quietZone) * moduleSize);
                var width = runLength * moduleSize;

                dc.DrawRectangle(fillBrush, null, new Rect(left, top, width, moduleSize));
                column++;
            }
        }
    }

    private static void OnQrCodeDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not QRCode qrCode)
        {
            return;
        }

        qrCode.InvalidateQrCode();
        qrCode.InvalidateMeasure();
        qrCode.InvalidateVisual();
    }

    private static object CoerceQuietZoneModules(DependencyObject d, object? value)
    {
        return Math.Max(0, (int)(value ?? 0));
    }

    private void InvalidateQrCode()
    {
        _modules = null;
        _generationError = null;
    }

    private void EnsureModules()
    {
        if (_modules != null || string.IsNullOrWhiteSpace(Text))
        {
            return;
        }

        try
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using var data = generator.CreateQrCode(Text, MapErrorCorrectionLevel(ErrorCorrectionLevel));

            var moduleMatrix = data.ModuleMatrix;
            if (moduleMatrix.Count == 0)
            {
                return;
            }

            var raw = new bool[moduleMatrix.Count, moduleMatrix[0].Length];
            for (var row = 0; row < moduleMatrix.Count; row++)
            {
                var sourceRow = moduleMatrix[row];
                for (var column = 0; column < sourceRow.Length; column++)
                {
                    raw[row, column] = sourceRow[column];
                }
            }

            _modules = TrimQuietZone(raw);
        }
        catch (Exception ex)
        {
            _generationError = ex.Message;
            _modules = null;
        }
    }

    private Pen? CreateBorderPen()
    {
        if (BorderBrush == null)
        {
            return null;
        }

        var thickness = Math.Max(
            Math.Max(BorderThickness.Left, BorderThickness.Right),
            Math.Max(BorderThickness.Top, BorderThickness.Bottom));

        return thickness > 0 ? new Pen(BorderBrush, thickness) : null;
    }

    private Rect GetContentRect()
    {
        var left = BorderThickness.Left + Padding.Left;
        var top = BorderThickness.Top + Padding.Top;
        var width = Math.Max(0, RenderSize.Width - BorderThickness.TotalWidth - Padding.TotalWidth);
        var height = Math.Max(0, RenderSize.Height - BorderThickness.TotalHeight - Padding.TotalHeight);
        return new Rect(left, top, width, height);
    }

    private Rect AlignSquare(Rect contentRect, double side)
    {
        var x = contentRect.X;
        var y = contentRect.Y;

        switch (HorizontalContentAlignment)
        {
            case HorizontalAlignment.Center:
                x += (contentRect.Width - side) / 2;
                break;
            case HorizontalAlignment.Right:
                x += contentRect.Width - side;
                break;
        }

        switch (VerticalContentAlignment)
        {
            case VerticalAlignment.Center:
                y += (contentRect.Height - side) / 2;
                break;
            case VerticalAlignment.Bottom:
                y += contentRect.Height - side;
                break;
        }

        return new Rect(x, y, side, side);
    }

    private static bool[,] TrimQuietZone(bool[,] source)
    {
        var rowCount = source.GetLength(0);
        var columnCount = source.GetLength(1);
        var top = 0;
        var bottom = rowCount - 1;
        var left = 0;
        var right = columnCount - 1;

        while (top <= bottom && IsRowEmpty(source, top, left, right))
        {
            top++;
        }

        while (bottom >= top && IsRowEmpty(source, bottom, left, right))
        {
            bottom--;
        }

        while (left <= right && IsColumnEmpty(source, left, top, bottom))
        {
            left++;
        }

        while (right >= left && IsColumnEmpty(source, right, top, bottom))
        {
            right--;
        }

        if (top == 0 && left == 0 && bottom == rowCount - 1 && right == columnCount - 1)
        {
            return source;
        }

        var trimmedRows = Math.Max(0, bottom - top + 1);
        var trimmedColumns = Math.Max(0, right - left + 1);
        var trimmed = new bool[trimmedRows, trimmedColumns];

        for (var row = 0; row < trimmedRows; row++)
        {
            for (var column = 0; column < trimmedColumns; column++)
            {
                trimmed[row, column] = source[top + row, left + column];
            }
        }

        return trimmed;
    }

    private static bool IsRowEmpty(bool[,] source, int row, int left, int right)
    {
        for (var column = left; column <= right; column++)
        {
            if (source[row, column])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsColumnEmpty(bool[,] source, int column, int top, int bottom)
    {
        for (var row = top; row <= bottom; row++)
        {
            if (source[row, column])
            {
                return false;
            }
        }

        return true;
    }

    private static QRCoder.QRCodeGenerator.ECCLevel MapErrorCorrectionLevel(QRCodeErrorCorrectionLevel level)
    {
        return level switch
        {
            QRCodeErrorCorrectionLevel.L => QRCoder.QRCodeGenerator.ECCLevel.L,
            QRCodeErrorCorrectionLevel.M => QRCoder.QRCodeGenerator.ECCLevel.M,
            QRCodeErrorCorrectionLevel.H => QRCoder.QRCodeGenerator.ECCLevel.H,
            _ => QRCoder.QRCodeGenerator.ECCLevel.Q
        };
    }
}
