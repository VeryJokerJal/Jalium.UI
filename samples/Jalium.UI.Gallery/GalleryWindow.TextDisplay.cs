using Jalium.UI.Controls;
using Jalium.UI.Threading;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildTextDisplaySection() => new GallerySampleTextDisplaySection();
}

/// <summary>Text, Markdown and platform-independent icon examples.</summary>
public class GallerySampleTextDisplaySection : GallerySampleHeader
{
    public GallerySampleTextDisplaySection() => DefaultStyleKey = typeof(GallerySampleTextDisplaySection);
}

/// <summary>Replays Markdown chunks with detachable template parts and a bounded timer lifetime.</summary>
[TemplatePart(Name = "PART_Markdown", Type = typeof(Markdown))]
[TemplatePart(Name = "PART_Replay", Type = typeof(Button))]
[TemplateVisualState(Name = "Ready", GroupName = "PlaybackStates")]
[TemplateVisualState(Name = "Playing", GroupName = "PlaybackStates")]
[TemplateVisualState(Name = "Completed", GroupName = "PlaybackStates")]
public class GallerySampleMarkdownReplay : GallerySampleDemo
{
    public static readonly DependencyProperty ScriptProperty = DependencyProperty.Register(nameof(Script), typeof(string), typeof(GallerySampleMarkdownReplay), new PropertyMetadata("## Streaming\nWatch **bold**, `code` and the table settle without flicker.\n\n| step | state |\n| :--- | ----: |\n| 1 | typing |\n| 2 | done |\n\n```csharp\nvar x = 1;\n```\n", (d, _) => { var replay = (GallerySampleMarkdownReplay)d; if (replay.IsPlaying) replay.Replay(); }));
    public static readonly DependencyProperty IntervalProperty = DependencyProperty.Register(nameof(Interval), typeof(TimeSpan), typeof(GallerySampleMarkdownReplay), new PropertyMetadata(TimeSpan.FromMilliseconds(18)), value => value is TimeSpan interval && interval > TimeSpan.Zero);
    public static readonly DependencyProperty ChunkSizeProperty = DependencyProperty.Register(nameof(ChunkSize), typeof(int), typeof(GallerySampleMarkdownReplay), new PropertyMetadata(3), value => value is int count && count > 0);
    private static readonly DependencyPropertyKey IsPlayingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsPlaying), typeof(bool), typeof(GallerySampleMarkdownReplay), new PropertyMetadata(false));
    public static readonly DependencyProperty IsPlayingProperty = IsPlayingPropertyKey.DependencyProperty;
    public string Script { get => (string)GetValue(ScriptProperty)!; set => SetValue(ScriptProperty, value); }
    public TimeSpan Interval { get => (TimeSpan)GetValue(IntervalProperty)!; set => SetValue(IntervalProperty, value); }
    public int ChunkSize { get => (int)GetValue(ChunkSizeProperty)!; set => SetValue(ChunkSizeProperty, value); }
    public bool IsPlaying => (bool)GetValue(IsPlayingProperty)!;
    private Markdown? _markdown;
    private Button? _replay;
    private DispatcherTimer? _timer;
    private int _cursor;
    public GallerySampleMarkdownReplay()
    {
        DefaultStyleKey = typeof(GallerySampleMarkdownReplay);
        Loaded += (_, _) => Replay(); Unloaded += (_, _) => StopCore();
    }
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate();
        _markdown = GetTemplateChild("PART_Markdown") as Markdown;
        _replay = GetTemplateChild("PART_Replay") as Button;
        if (_replay is not null) _replay.Click += OnReplay;
        VisualStateManager.GoToState(this, "Ready", false);
        if (IsLoaded) Replay();
    }
    protected override void OnTemplateChanging()
    {
        ReleaseParts(); base.OnTemplateChanging();
    }
    public void Replay()
    {
        Stop();
        if (_markdown is null) return;
        _cursor = 0; _markdown.Clear(); _markdown.IsStreaming = true;
        if (string.IsNullOrEmpty(Script)) { Complete(); return; }
        if (_timer is null) { _timer = new DispatcherTimer(); _timer.Tick += OnTick; }
        _timer.Interval = Interval; SetValue(IsPlayingPropertyKey, true);
        VisualStateManager.GoToState(this, "Playing", false); _timer.Start();
    }
    public void Stop()
    {
        StopCore(); VisualStateManager.GoToState(this, "Ready", false);
    }
    private void StopCore()
    {
        _timer?.Stop(); if (_markdown is not null) _markdown.IsStreaming = false;
        SetValue(IsPlayingPropertyKey, false);
    }
    private void OnReplay(object sender, RoutedEventArgs e) => Replay();
    private void OnTick(object? sender, EventArgs e)
    {
        if (_markdown is null || _cursor >= Script.Length) { Complete(); return; }
        var count = Math.Min(ChunkSize, Script.Length - _cursor);
        _markdown.AppendText(Script.Substring(_cursor, count)); _cursor += count;
        if (_cursor >= Script.Length) Complete();
    }
    private void Complete() { Stop(); VisualStateManager.GoToState(this, "Completed", false); }
    private void ReleaseParts()
    {
        // State transitions apply templates, so teardown only stops the timer.
        StopCore(); if (_replay is not null) _replay.Click -= OnReplay;
        _replay = null; _markdown = null;
    }
}
