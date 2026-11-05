using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MediaElementTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults=new(()=>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();using var stream=new MemoryStream();
        return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;
    });
    [Fact]
    public void StatusAndSubtitleChromeAreBoundTemplateContent()
    {
        using var media=Create();Layout(media);Assert.IsAssignableFrom<Control>(media);Assert.NotNull(media.MediaContentClip);Assert.Same(media,Part<MediaVideoSurface>(media,"PART_VideoSurface").Owner);
        Assert.Equal(Visibility.Collapsed,Part<TextBlock>(media,"PART_Status").Visibility);Assert.IsType<SolidColorBrush>(media.SubtitleForeground);Assert.IsType<SolidColorBrush>(media.SubtitleBackground);
        Set(media,"_hasAudio",true);Refresh(media);Assert.Equal("Audio Only",Part<TextBlock>(media,"PART_Status").Text);media.AudioOnlyText="仅音频";Assert.Equal("仅音频",Part<TextBlock>(media,"PART_Status").Text);
        Set(media,"_hasVideo",true);Refresh(media);Assert.Equal("Loading...",media.DisplayStatusText);Assert.Equal("Loading",VisualStateManager.GetCurrentStateName(media,"ContentStates"));
        Set(media,"_subtitleText","中文字幕 English");Refresh(media);Layout(media);Assert.Equal("中文字幕 English",Part<TextBlock>(media,"PART_Subtitle").Text);Assert.True(Part<TextBlock>(media,"PART_Subtitle").LineHeight>=media.SubtitleFontSize);Assert.Equal(Visibility.Visible,Part<Border>(media,"PART_SubtitleFrame").Visibility);
        media.IsEnabled=false;Assert.Equal(.65,Part<Grid>(media,"PART_Root").Opacity);
    }
    [Fact]
    public void FramePixelsAreDrawnByTheTemplateSurfaceWithoutDuplicatingTheVideo()
    {
        using var media=Create();var bitmap=new WriteableBitmap(40,20,96,96,PixelFormat.Bgra32,null);Set(media,"_frameBitmap",bitmap);Set(media,"_hasVideo",true);Set(media,"_videoWidth",40);Set(media,"_videoHeight",20);Refresh(media);Layout(media);
        var ink=new Ink();media.Render(ink);Assert.Single(ink.Images);Assert.Same(bitmap,ink.Images[0]);Assert.Equal("Video",VisualStateManager.GetCurrentStateName(media,"ContentStates"));Assert.Equal(Visibility.Collapsed,media.DisplayStatusVisibility);
    }
    [Fact]
    public void RetemplatingReleasesOnlyThePresentationAndKeepsPlaybackAndSubtitleState()
    {
        using var media=Create();Set(media,"_hasAudio",true);Set(media,"_subtitleText","保留字幕");Refresh(media);Layout(media);var retired=Part<MediaVideoSurface>(media,"PART_VideoSurface");
        var blank=new ControlTemplate(typeof(MediaElement));blank.SetVisualTree(()=>new Border());media.Template=blank;Layout(media);Assert.Null(retired.Owner);Assert.True(media.HasAudio);Assert.Equal("保留字幕",media.CurrentSubtitle);
        media.ClearValue(Control.TemplateProperty);Layout(media);Assert.Same(media,Part<MediaVideoSurface>(media,"PART_VideoSurface").Owner);Assert.NotSame(retired,media.FindName("PART_VideoSurface"));Assert.Equal("保留字幕",Part<TextBlock>(media,"PART_Subtitle").Text);
    }
    [Fact]
    public void OwnerlessSurfaceAndMissingOptionalPartsAreSafe()
    {
        var view=new MediaVideoSurface();Layout(view);view.Render(new Ink());using var media=Create();var template=new ControlTemplate(typeof(MediaElement));template.SetVisualTree(()=>new MediaVideoSurface{Name="PART_VideoSurface"});media.Template=template;Layout(media);Assert.Same(media,Part<MediaVideoSurface>(media,"PART_VideoSurface").Owner);media.LoadingText="加载中";Refresh(media);
    }
    private static MediaElement Create(){var media=new MediaElement{Width=360,Height=200,LoadedBehavior=MediaState.Manual};media.Resources.MergedDictionaries.Add(Defaults.Value);return media;}
    private static void Set(MediaElement media,string field,object value)=>typeof(MediaElement).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(media,value);
    private static void Refresh(MediaElement media)=>typeof(MediaElement).GetMethod("InvalidateMediaView",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(media,null);
    private static T Part<T>(Control owner,string name)where T:class=>Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(FrameworkElement element){for(var i=0;i<3;i++){element.InvalidateMeasure();element.Measure(new Size(360,200));element.Arrange(new Rect(0,0,360,200));element.UpdateLayout();}}
    private sealed class Ink : DrawingContextAdapter
    {
        internal readonly List<string> Text = new();
        internal readonly List<ImageSource> Images = new();
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) => Images.Add(image);
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
