using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Native;
using Jalium.UI.Media.Pipeline;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// 显示一个摄像头实时预览的控件。Windows 走 Media Foundation Capture，
/// Android 走 Camera2 NDK。完全不依赖 OpenCV / 第三方库。
/// </summary>
public class CameraView : Control
{
    private static INativeCameraSourceFactory? s_factory;
    private static readonly object s_factoryLock = new();

    /// <summary>
    /// 注入自定义 <see cref="INativeCameraSourceFactory"/>。
    /// </summary>
    public static void SetCameraFactory(INativeCameraSourceFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (s_factoryLock)
        {
            s_factory = factory;
        }
    }

    private static INativeCameraSourceFactory GetFactory()
    {
        var f = Volatile.Read(ref s_factory);
        if (f is not null) return f;
        lock (s_factoryLock)
        {
            s_factory ??= new NativeCameraSourceFactory();
            return s_factory;
        }
    }

    private INativeCameraSource? _source;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private ImageSource? _currentFrame;
    private readonly object _frameLock = new();

    /// <summary>
    /// 枚举系统当前可见的摄像头设备。
    /// </summary>
    public static IReadOnlyList<CameraDeviceInfo> EnumerateDevices() => GetFactory().EnumerateDevices();

    #region 依赖属性

    /// <summary>Identifies the <see cref="Source"/> dependency property.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(CameraDeviceInfo), typeof(CameraView),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>Identifies the <see cref="RequestedWidth"/> dependency property.</summary>
    public static readonly DependencyProperty RequestedWidthProperty =
        DependencyProperty.Register(nameof(RequestedWidth), typeof(int), typeof(CameraView),
            new PropertyMetadata(1280));

    /// <summary>Identifies the <see cref="RequestedHeight"/> dependency property.</summary>
    public static readonly DependencyProperty RequestedHeightProperty =
        DependencyProperty.Register(nameof(RequestedHeight), typeof(int), typeof(CameraView),
            new PropertyMetadata(720));

    /// <summary>Identifies the <see cref="RequestedFps"/> dependency property.</summary>
    public static readonly DependencyProperty RequestedFpsProperty =
        DependencyProperty.Register(nameof(RequestedFps), typeof(double), typeof(CameraView),
            new PropertyMetadata(30.0));

    /// <summary>Identifies the <see cref="Stretch"/> dependency property.</summary>
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(CameraView),
            new PropertyMetadata(Stretch.Uniform));

    #endregion

    #region 路由事件

    /// <summary>当摄像头打开成功时触发。</summary>
    public static readonly RoutedEvent CameraOpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(CameraOpened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(CameraView));

    /// <summary>当摄像头打开 / 拉帧失败时触发。</summary>
    public static readonly RoutedEvent CameraFailedEvent =
        EventManager.RegisterRoutedEvent(nameof(CameraFailed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(CameraView));

    /// <summary>每收到一帧时触发（UI 线程）。</summary>
    public static readonly RoutedEvent CameraFrameArrivedEvent =
        EventManager.RegisterRoutedEvent(nameof(CameraFrameArrived), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(CameraView));

    /// <summary>当摄像头打开成功时触发。</summary>
    public event RoutedEventHandler CameraOpened
    {
        add => AddHandler(CameraOpenedEvent, value);
        remove => RemoveHandler(CameraOpenedEvent, value);
    }

    /// <summary>当摄像头操作失败时触发。</summary>
    public event RoutedEventHandler CameraFailed
    {
        add => AddHandler(CameraFailedEvent, value);
        remove => RemoveHandler(CameraFailedEvent, value);
    }

    /// <summary>每收到新帧时触发。</summary>
    public event RoutedEventHandler CameraFrameArrived
    {
        add => AddHandler(CameraFrameArrivedEvent, value);
        remove => RemoveHandler(CameraFrameArrivedEvent, value);
    }

    #endregion

    #region CLR 属性

    /// <summary>要采集的摄像头设备。</summary>
    public CameraDeviceInfo? Source
    {
        get => (CameraDeviceInfo?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>请求的分辨率宽度（像素）。实际可能是最接近的支持值。</summary>
    public int RequestedWidth
    {
        get => (int)GetValue(RequestedWidthProperty)!;
        set => SetValue(RequestedWidthProperty, value);
    }

    /// <summary>请求的分辨率高度（像素）。</summary>
    public int RequestedHeight
    {
        get => (int)GetValue(RequestedHeightProperty)!;
        set => SetValue(RequestedHeightProperty, value);
    }

    /// <summary>请求的帧率。</summary>
    public double RequestedFps
    {
        get => (double)GetValue(RequestedFpsProperty)!;
        set => SetValue(RequestedFpsProperty, value);
    }

    /// <summary>缩放模式。</summary>
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    #endregion

    /// <summary>
    /// 当前最近一帧（线程安全）。
    /// </summary>
    public ImageSource? CurrentFrame
    {
        get
        {
            lock (_frameLock) return _currentFrame;
        }
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CameraView view)
        {
            view.RestartCapture();
        }
    }

    /// <summary>
    /// 启动当前 <see cref="Source"/> 设备的采集。
    /// </summary>
    public void Start()
    {
        var device = Source;
        if (device is null) return;

        StopInternal();

        try
        {
            _source = GetFactory().Create();
            _source.Open(device.Id, RequestedWidth, RequestedHeight, RequestedFps);

            _captureCts = new CancellationTokenSource();
            var token = _captureCts.Token;
            _captureTask = Task.Run(() => CaptureLoop(token), token);

            RaiseEvent(new RoutedEventArgs(CameraOpenedEvent, this));
        }
        catch (Exception)
        {
            StopInternal();
            RaiseEvent(new RoutedEventArgs(CameraFailedEvent, this));
        }
    }

    /// <summary>停止采集并释放设备。</summary>
    public void Stop() => StopInternal();

    private void RestartCapture()
    {
        if (Source is null)
        {
            StopInternal();
            return;
        }
        Start();
    }

    private void CaptureLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                MediaFrame? frame;
                bool ok;
                try
                {
                    ok = _source!.TryReadFrame(out frame);
                }
                catch
                {
                    Dispatcher.MainDispatcher?.BeginInvoke(() =>
                        RaiseEvent(new RoutedEventArgs(CameraFailedEvent, this)));
                    break;
                }

                if (!ok || frame is null) break;

                ImageSource? image;
                using (frame)
                {
                    image = BitmapImage.FromMediaFrame(frame);
                }

                lock (_frameLock) _currentFrame = image;

                Dispatcher.MainDispatcher?.BeginInvoke(() =>
                {
                    RaiseEvent(new RoutedEventArgs(CameraFrameArrivedEvent, this));
                    InvalidateVisual();
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private void StopInternal()
    {
        try { _captureCts?.Cancel(); } catch { }
        try { _captureTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _captureCts?.Dispose();
        _captureCts = null;
        _captureTask = null;
        _source?.Dispose();
        _source = null;
        lock (_frameLock) _currentFrame = null;
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;

        ImageSource? image;
        lock (_frameLock) image = _currentFrame;

        if (image is null) return;

        var rect = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        dc.DrawImage(image, rect);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var image = CurrentFrame;
        if (image is null) return base.MeasureOverride(availableSize);

        var natural = new Size(image.Width, image.Height);
        return ComputeStretchSize(availableSize, natural);
    }

    private Size ComputeStretchSize(Size available, Size natural)
    {
        if (natural.Width <= 0 || natural.Height <= 0) return base.MeasureOverride(available);

        var w = available.Width;
        var h = available.Height;
        var nw = natural.Width;
        var nh = natural.Height;

        return Stretch switch
        {
            Stretch.None => natural,
            Stretch.Fill => available,
            Stretch.Uniform => UniformFit(nw, nh, w, h),
            Stretch.UniformToFill => UniformFill(nw, nh, w, h),
            _ => natural,
        };
    }

    private static Size UniformFit(double nw, double nh, double w, double h)
    {
        if (double.IsInfinity(w) && double.IsInfinity(h)) return new Size(nw, nh);
        var sx = double.IsInfinity(w) ? 1.0 : w / nw;
        var sy = double.IsInfinity(h) ? 1.0 : h / nh;
        var s = Math.Min(sx, sy);
        return new Size(nw * s, nh * s);
    }

    private static Size UniformFill(double nw, double nh, double w, double h)
    {
        if (double.IsInfinity(w) || double.IsInfinity(h)) return new Size(nw, nh);
        var sx = w / nw;
        var sy = h / nh;
        var s = Math.Max(sx, sy);
        return new Size(nw * s, nh * s);
    }
}
