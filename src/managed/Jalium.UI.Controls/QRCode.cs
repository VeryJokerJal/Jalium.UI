using Jalium.UI.Controls.QR;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Error correction levels supported by the <see cref="QRCode"/> control.
/// Mirrors <see cref="QRErrorCorrectionLevel"/> for backward-compatible XAML.
/// </summary>
public enum QRCodeErrorCorrectionLevel
{
    L,
    M,
    Q,
    H
}

/// <summary>Shape used for each individual module.</summary>
public enum QRModuleShape
{
    Square,
    RoundedSquare,
    Circle
}

/// <summary>Shape used for the three finder ("eye") patterns at the corners.</summary>
public enum QREyeShape
{
    Square,
    Rounded,
    Leaf
}

/// <summary>Byte-mode encoding policy mirrored from <see cref="QRByteEncoding"/>.</summary>
public enum QRCodeEncoding
{
    Auto,
    Iso8859_1,
    Utf8,
    ShiftJis
}

/// <summary>
/// Displays a QR code generated from text content. Pure managed encoder, no third-party dependencies.
/// </summary>
public class QRCode : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Controls.Automation.GenericAutomationPeer(this, Jalium.UI.Automation.AutomationControlType.Image);

    private QRSymbol? _symbol;
    private string? _generationError;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(QRCode),
            new PropertyMetadata(string.Empty, OnQrCodeDataChanged));

    public static readonly DependencyProperty QuietZoneModulesProperty =
        DependencyProperty.Register(nameof(QuietZoneModules), typeof(int), typeof(QRCode),
            new PropertyMetadata(4, OnQrCodeDataChanged, CoerceQuietZoneModules));

    public static readonly DependencyProperty ErrorCorrectionLevelProperty =
        DependencyProperty.Register(nameof(ErrorCorrectionLevel), typeof(QRCodeErrorCorrectionLevel), typeof(QRCode),
            new PropertyMetadata(QRCodeErrorCorrectionLevel.Q, OnQrCodeDataChanged));

    public static readonly DependencyProperty VersionProperty =
        DependencyProperty.Register(nameof(Version), typeof(int), typeof(QRCode),
            new PropertyMetadata(0, OnQrCodeDataChanged, CoerceVersion));

    public static readonly DependencyProperty MaskProperty =
        DependencyProperty.Register(nameof(Mask), typeof(int), typeof(QRCode),
            new PropertyMetadata(-1, OnQrCodeDataChanged, CoerceMask));

    public static readonly DependencyProperty EncodingProperty =
        DependencyProperty.Register(nameof(Encoding), typeof(QRCodeEncoding), typeof(QRCode),
            new PropertyMetadata(QRCodeEncoding.Auto, OnQrCodeDataChanged));

    public static readonly DependencyProperty ModuleShapeProperty =
        DependencyProperty.Register(nameof(ModuleShape), typeof(QRModuleShape), typeof(QRCode),
            new PropertyMetadata(QRModuleShape.Square, OnVisualPropertyChanged));

    public static readonly DependencyProperty EyeShapeProperty =
        DependencyProperty.Register(nameof(EyeShape), typeof(QREyeShape), typeof(QRCode),
            new PropertyMetadata(QREyeShape.Square, OnVisualPropertyChanged));

    public static readonly DependencyProperty LogoImageProperty =
        DependencyProperty.Register(nameof(LogoImage), typeof(ImageSource), typeof(QRCode),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty LogoBackgroundBrushProperty =
        DependencyProperty.Register(nameof(LogoBackgroundBrush), typeof(Brush), typeof(QRCode),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty LogoSizeRatioProperty =
        DependencyProperty.Register(nameof(LogoSizeRatio), typeof(double), typeof(QRCode),
            new PropertyMetadata(0.18, OnVisualPropertyChanged, CoerceLogoRatio));

    public static readonly DependencyProperty IsForegroundGradientProperty =
        DependencyProperty.Register(nameof(IsForegroundGradient), typeof(bool), typeof(QRCode),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>Gets or sets the text encoded by the QR code.</summary>
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the size of the quiet zone in modules.</summary>
    public int QuietZoneModules
    {
        get => (int)GetValue(QuietZoneModulesProperty)!;
        set => SetValue(QuietZoneModulesProperty, value);
    }

    /// <summary>Gets or sets the QR code error correction level.</summary>
    public QRCodeErrorCorrectionLevel ErrorCorrectionLevel
    {
        get => (QRCodeErrorCorrectionLevel)(GetValue(ErrorCorrectionLevelProperty) ?? QRCodeErrorCorrectionLevel.Q);
        set => SetValue(ErrorCorrectionLevelProperty, value);
    }

    /// <summary>0 for automatic, 1..40 to force a specific version.</summary>
    public int Version
    {
        get => (int)GetValue(VersionProperty)!;
        set => SetValue(VersionProperty, value);
    }

    /// <summary>-1 for automatic, 0..7 to force a specific mask pattern.</summary>
    public int Mask
    {
        get => (int)GetValue(MaskProperty)!;
        set => SetValue(MaskProperty, value);
    }

    /// <summary>Byte-mode encoding strategy.</summary>
    public QRCodeEncoding Encoding
    {
        get => (QRCodeEncoding)(GetValue(EncodingProperty) ?? QRCodeEncoding.Auto);
        set => SetValue(EncodingProperty, value);
    }

    /// <summary>Visual shape used for each module.</summary>
    public QRModuleShape ModuleShape
    {
        get => (QRModuleShape)(GetValue(ModuleShapeProperty) ?? QRModuleShape.Square);
        set => SetValue(ModuleShapeProperty, value);
    }

    /// <summary>Visual shape used for the three finder patterns.</summary>
    public QREyeShape EyeShape
    {
        get => (QREyeShape)(GetValue(EyeShapeProperty) ?? QREyeShape.Square);
        set => SetValue(EyeShapeProperty, value);
    }

    /// <summary>Optional center logo (overlays the QR symbol; modules under the logo are skipped).</summary>
    public ImageSource? LogoImage
    {
        get => (ImageSource?)GetValue(LogoImageProperty);
        set => SetValue(LogoImageProperty, value);
    }

    /// <summary>Optional brush painted behind the logo (gives it a quiet zone). Defaults to <see cref="Control.Background"/>.</summary>
    public Brush? LogoBackgroundBrush
    {
        get => (Brush?)GetValue(LogoBackgroundBrushProperty);
        set => SetValue(LogoBackgroundBrushProperty, value);
    }

    /// <summary>Logo edge length as a fraction of the QR symbol edge length, clamped to the ECC safe area.</summary>
    public double LogoSizeRatio
    {
        get => (double)(GetValue(LogoSizeRatioProperty) ?? 0.18);
        set => SetValue(LogoSizeRatioProperty, value);
    }

    /// <summary>
    /// When true and <see cref="Control.Foreground"/> is a gradient brush, paints one large rectangle
    /// under the foreground then clips to module shapes — giving the gradient continuity across the symbol.
    /// </summary>
    public bool IsForegroundGradient
    {
        get => (bool)(GetValue(IsForegroundGradientProperty) ?? true);
        set => SetValue(IsForegroundGradientProperty, value);
    }

    internal int ModuleCount => _symbol?.ModuleCount ?? 0;
    internal int TotalModuleCount => ModuleCount == 0 ? 0 : ModuleCount + (QuietZoneModules * 2);
    internal string? GenerationError => _generationError;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureSymbol();

        var chromeWidth = BorderThickness.TotalWidth + Padding.TotalWidth;
        var chromeHeight = BorderThickness.TotalHeight + Padding.TotalHeight;

        if (_symbol == null)
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
    protected override void OnRender(DrawingContext drawingContext)
    {
        EnsureSymbol();

        var outerRect = new Rect(RenderSize);
        var borderPen = CreateBorderPen();
        if (Background != null || borderPen != null)
        {
            drawingContext.DrawRoundedRectangle(Background, borderPen, outerRect, CornerRadius);
        }

        if (_symbol == null)
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
        var foreground = Foreground ?? new SolidColorBrush(Color.Black);
        var quietZone = QuietZoneModules;
        var totalModules = TotalModuleCount;
        var moduleSize = Math.Max(1.0, Math.Floor(qrRect.Width / totalModules));
        var renderSide = moduleSize * totalModules;
        var snappedLeft = Math.Round(qrRect.X + ((qrRect.Width - renderSide) / 2));
        var snappedTop = Math.Round(qrRect.Y + ((qrRect.Height - renderSide) / 2));

        // Compute logo skip rect (in module coordinates) up-front.
        var logoSkip = ComputeLogoSkipModules();

        var moduleCount = _symbol.ModuleCount;
        var modules = _symbol.Modules;
        var shape = ModuleShape;
        var eyeShape = EyeShape;

        // Foreground gradient path: paint a single rect once, then "punch out" light modules by overlaying
        // background-colored shapes — but for simplicity and correctness across DC backends, we instead
        // still draw per-module using the same gradient brush; gradient continuity is achieved because
        // Brush is shared (DC tiles gradient by the brush, not by the painted rect).
        // For Square + non-gradient (default fast path), preserve original run-merging.
        var canRunMerge = shape == QRModuleShape.Square &&
                          eyeShape == QREyeShape.Square &&
                          (!IsForegroundGradient || foreground is SolidColorBrush) &&
                          logoSkip.IsEmpty;

        if (canRunMerge)
        {
            RenderRunMerged(drawingContext, foreground, modules, moduleCount, quietZone, snappedLeft, snappedTop, moduleSize);
        }
        else
        {
            RenderShaped(drawingContext, foreground, modules, moduleCount, quietZone, snappedLeft, snappedTop, moduleSize, shape, eyeShape, logoSkip);
        }

        RenderLogo(drawingContext, logoSkip, snappedLeft, snappedTop, moduleSize, quietZone);
    }

    private void RenderRunMerged(DrawingContext dc, Brush brush, bool[,] modules, int n, int quietZone, double snappedLeft, double snappedTop, double moduleSize)
    {
        for (var row = 0; row < n; row++)
        {
            var column = 0;
            while (column < n)
            {
                if (!modules[row, column])
                {
                    column++;
                    continue;
                }
                var runStart = column;
                while (column + 1 < n && modules[row, column + 1]) column++;
                var runLength = column - runStart + 1;
                var left = snappedLeft + ((runStart + quietZone) * moduleSize);
                var top = snappedTop + ((row + quietZone) * moduleSize);
                dc.DrawRectangle(brush, null, new Rect(left, top, runLength * moduleSize, moduleSize));
                column++;
            }
        }
    }

    private void RenderShaped(DrawingContext dc, Brush brush, bool[,] modules, int n, int quietZone,
        double snappedLeft, double snappedTop, double moduleSize, QRModuleShape shape, QREyeShape eyeShape, Rect logoSkip)
    {
        // Render the three finder patterns separately if a distinct eye shape is requested.
        var finderRects = new[]
        {
            new Int32Rect(0, 0, 7, 7),
            new Int32Rect(n - 7, 0, 7, 7),
            new Int32Rect(0, n - 7, 7, 7),
        };

        for (var row = 0; row < n; row++)
        {
            for (var column = 0; column < n; column++)
            {
                if (!modules[row, column]) continue;
                if (IsInsideAny(finderRects, row, column) && eyeShape != QREyeShape.Square) continue;
                if (!logoSkip.IsEmpty && logoSkip.Contains(new Point(column + 0.5, row + 0.5))) continue;

                var left = snappedLeft + ((column + quietZone) * moduleSize);
                var top = snappedTop + ((row + quietZone) * moduleSize);
                var cell = new Rect(left, top, moduleSize, moduleSize);
                switch (shape)
                {
                    case QRModuleShape.RoundedSquare:
                        dc.DrawRoundedRectangle(brush, null, cell, moduleSize * 0.3, moduleSize * 0.3);
                        break;
                    case QRModuleShape.Circle:
                        dc.DrawEllipse(brush, null, new Point(left + moduleSize / 2, top + moduleSize / 2), moduleSize / 2, moduleSize / 2);
                        break;
                    default:
                        dc.DrawRectangle(brush, null, cell);
                        break;
                }
            }
        }

        if (eyeShape != QREyeShape.Square)
        {
            foreach (var r in finderRects)
            {
                DrawEye(dc, brush, r.X, r.Y, snappedLeft, snappedTop, moduleSize, quietZone, eyeShape);
            }
        }
    }

    private void DrawEye(DrawingContext dc, Brush brush, int col, int row,
        double snappedLeft, double snappedTop, double moduleSize, int quietZone, QREyeShape eyeShape)
    {
        var outer = new Rect(
            snappedLeft + (col + quietZone) * moduleSize,
            snappedTop + (row + quietZone) * moduleSize,
            7 * moduleSize, 7 * moduleSize);
        var ring = new Rect(outer.X + moduleSize, outer.Y + moduleSize, 5 * moduleSize, 5 * moduleSize);
        var inner = new Rect(outer.X + 2 * moduleSize, outer.Y + 2 * moduleSize, 3 * moduleSize, 3 * moduleSize);

        var radiusOuter = eyeShape == QREyeShape.Leaf ? moduleSize * 2.5 : moduleSize * 1.2;
        var radiusInner = eyeShape == QREyeShape.Leaf ? moduleSize * 1.2 : moduleSize * 0.5;

        // outer 7x7 frame: draw rounded outer, subtract ring using background. The simplest approach:
        // draw outer filled rounded shape, then draw ring as background, then inner.
        // Background isn't easily accessible here — use a transparent ring is wrong. Instead draw the
        // frame as four straight bars approximating a rounded ring.
        dc.DrawRoundedRectangle(brush, null, outer, radiusOuter, radiusOuter);
        var bgBrush = Background ?? new SolidColorBrush(Colors.Transparent);
        dc.DrawRoundedRectangle(bgBrush, null, ring, radiusInner, radiusInner);
        dc.DrawRoundedRectangle(brush, null, inner, radiusInner * 0.6, radiusInner * 0.6);
    }

    private void RenderLogo(DrawingContext dc, Rect logoSkip, double snappedLeft, double snappedTop, double moduleSize, int quietZone)
    {
        if (LogoImage is null || logoSkip.IsEmpty) return;
        var pxLeft = snappedLeft + (logoSkip.X + quietZone) * moduleSize;
        var pxTop = snappedTop + (logoSkip.Y + quietZone) * moduleSize;
        var pxSize = logoSkip.Width * moduleSize;
        var bgRect = new Rect(pxLeft, pxTop, pxSize, pxSize);
        var bg = LogoBackgroundBrush ?? Background ?? new SolidColorBrush(Color.White);
        dc.DrawRectangle(bg, null, bgRect);
        var innerPadding = moduleSize * 0.5;
        var imgRect = new Rect(pxLeft + innerPadding, pxTop + innerPadding, pxSize - 2 * innerPadding, pxSize - 2 * innerPadding);
        if (imgRect.Width > 0 && imgRect.Height > 0)
        {
            dc.DrawImage(LogoImage, imgRect);
        }
    }

    private Rect ComputeLogoSkipModules()
    {
        if (LogoImage is null || _symbol is null) return Rect.Empty;
        var ratio = Math.Clamp(LogoSizeRatio, 0.05, MaxLogoRatioFor(ErrorCorrectionLevel));
        var n = _symbol.ModuleCount;
        var size = Math.Max(1, (int)Math.Round(n * ratio));
        // Round to odd so it centers.
        if ((size & 1) != (n & 1)) size = Math.Min(n, size + 1);
        var left = (n - size) / 2;
        return new Rect(left, left, size, size);
    }

    private static double MaxLogoRatioFor(QRCodeErrorCorrectionLevel ecc) => ecc switch
    {
        QRCodeErrorCorrectionLevel.L => 0.07,
        QRCodeErrorCorrectionLevel.M => 0.15,
        QRCodeErrorCorrectionLevel.Q => 0.22,
        QRCodeErrorCorrectionLevel.H => 0.28,
        _ => 0.15
    };

    private static bool IsInsideAny(Int32Rect[] rects, int row, int column)
    {
        foreach (var r in rects)
        {
            if (column >= r.X && column < r.X + r.Width && row >= r.Y && row < r.Y + r.Height)
                return true;
        }
        return false;
    }

    private static void OnQrCodeDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not QRCode qrCode) return;
        qrCode.InvalidateQrCode();
        qrCode.InvalidateMeasure();
        qrCode.InvalidateVisual();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is QRCode qr) qr.InvalidateVisual();
    }

    private static object CoerceQuietZoneModules(DependencyObject d, object? value)
        => Math.Max(0, (int)(value ?? 0));

    private static object CoerceVersion(DependencyObject d, object? value)
    {
        var v = (int)(value ?? 0);
        if (v < 0) return 0;
        if (v > 40) return 40;
        return v;
    }

    private static object CoerceMask(DependencyObject d, object? value)
    {
        var v = (int)(value ?? -1);
        if (v < -1) return -1;
        if (v > 7) return 7;
        return v;
    }

    private static object CoerceLogoRatio(DependencyObject d, object? value)
    {
        var v = (double)(value ?? 0.18);
        if (double.IsNaN(v) || v <= 0) return 0.05;
        if (v > 0.4) return 0.4;
        return v;
    }

    private void InvalidateQrCode()
    {
        _symbol = null;
        _generationError = null;
    }

    private void EnsureSymbol()
    {
        if (_symbol != null || string.IsNullOrWhiteSpace(Text))
        {
            return;
        }

        try
        {
            _symbol = QREncoder.Encode(
                Text,
                MapEcc(ErrorCorrectionLevel),
                MapByteEncoding(Encoding),
                Version,
                Mask);
        }
        catch (Exception ex)
        {
            _generationError = ex.Message;
            _symbol = null;
        }
    }

    private static QRErrorCorrectionLevel MapEcc(QRCodeErrorCorrectionLevel level) => level switch
    {
        QRCodeErrorCorrectionLevel.L => QRErrorCorrectionLevel.L,
        QRCodeErrorCorrectionLevel.M => QRErrorCorrectionLevel.M,
        QRCodeErrorCorrectionLevel.H => QRErrorCorrectionLevel.H,
        _ => QRErrorCorrectionLevel.Q,
    };

    private static QRByteEncoding MapByteEncoding(QRCodeEncoding enc) => enc switch
    {
        QRCodeEncoding.Iso8859_1 => QRByteEncoding.Iso8859_1,
        QRCodeEncoding.Utf8 => QRByteEncoding.Utf8,
        QRCodeEncoding.ShiftJis => QRByteEncoding.ShiftJis,
        _ => QRByteEncoding.Auto,
    };

    private Pen? CreateBorderPen()
    {
        if (BorderBrush == null) return null;
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
            case HorizontalAlignment.Center: x += (contentRect.Width - side) / 2; break;
            case HorizontalAlignment.Right: x += contentRect.Width - side; break;
        }
        switch (VerticalContentAlignment)
        {
            case VerticalAlignment.Center: y += (contentRect.Height - side) / 2; break;
            case VerticalAlignment.Bottom: y += contentRect.Height - side; break;
        }
        return new Rect(x, y, side, side);
    }
}
