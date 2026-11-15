using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Editor.LanguageServer.Client;
using Jalium.UI.Controls.Editor.LanguageServer.Protocol;
using Jalium.UI.Input;
using System.Reflection;
using System.Collections;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class EditControlTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults=new(()=>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();using var stream=new MemoryStream();
        return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;
    });
    [Fact]
    public void DefaultChromeAndStandardScrollBarsUseTheEditorMetrics()
    {
        var editor=Create();Layout(editor);
        var view=Part<EditControlView>(editor,"PART_EditorView");var border=Part<Border>(editor,"PART_Border");var vertical=Part<ScrollBar>(editor,"PART_VerticalScrollBar");var horizontal=Part<ScrollBar>(editor,"PART_HorizontalScrollBar");
        Assert.Same(editor,view.Owner);Assert.False(view.Focusable);Assert.Same(editor.Background,border.Background);Assert.NotNull(editor.EditorContentClip);
        Assert.True(editor.VerticalScrollMaximum>0);Assert.True(editor.HorizontalScrollMaximum>0);Assert.Equal(Visibility.Visible,vertical.Visibility);Assert.Equal(Visibility.Visible,horizontal.Visibility);
        Assert.Equal(editor.VerticalScrollMaximum,vertical.Maximum,3);Assert.Equal(editor.HorizontalScrollMaximum,horizontal.Maximum,3);Assert.Equal(editor.VerticalScrollBarBounds.X,Canvas.GetLeft(vertical),3);
        Assert.True(vertical.SmallChange>1);Assert.True(vertical.LargeChange>vertical.SmallChange);Assert.Equal(editor.VerticalScrollLargeChange,vertical.LargeChange,3);
        var ink=new Ink();editor.Render(ink);Assert.Contains(ink.Text,text=>text.Contains("line"));
        editor.IsEnabled=false;Assert.Equal(0.5,Part<Grid>(editor,"PART_Root").Opacity);editor.IsEnabled=true;Assert.Equal(1,Part<Grid>(editor,"PART_Root").Opacity);
    }
    [Fact]
    public void ScrollValuesUpdateTheEditorAndContinueFollowingProgrammaticNavigation()
    {
        var editor=Create();Layout(editor);var vertical=Part<ScrollBar>(editor,"PART_VerticalScrollBar");var horizontal=Part<ScrollBar>(editor,"PART_HorizontalScrollBar");
        vertical.SetCurrentValue(RangeBase.ValueProperty,editor.VerticalScrollMaximum/2);horizontal.SetCurrentValue(RangeBase.ValueProperty,editor.HorizontalScrollMaximum/2);
        Assert.Equal(vertical.Value,editor.VerticalScrollOffset,3);Assert.Equal(horizontal.Value,editor.HorizontalScrollOffset,3);
        editor.CaretOffset=editor.Text.Length;Layout(editor);Assert.Equal(editor.VerticalScrollOffset,vertical.Value,3);Assert.Equal(editor.HorizontalScrollOffset,horizontal.Value,3);
    }
    [Fact]
    public void RetiredPartsReleaseOwnersAndCannotChangeTheRetainedDocumentOrScrollPosition()
    {
        var editor=Create();Layout(editor);var document=editor.Document;editor.Select(3,7);var selected=editor.SelectedText;var oldView=Part<EditControlView>(editor,"PART_EditorView");var oldBar=Part<ScrollBar>(editor,"PART_VerticalScrollBar");oldBar.SetCurrentValue(RangeBase.ValueProperty,editor.VerticalScrollMaximum/2);var offset=editor.VerticalScrollOffset;
        var blank=new ControlTemplate(typeof(EditControl));blank.SetVisualTree(()=>new Border());editor.Template=blank;Layout(editor);Assert.Null(oldView.Owner);Assert.Same(document,editor.Document);Assert.Equal(selected,editor.SelectedText);
        oldBar.SetCurrentValue(RangeBase.ValueProperty,0.0);Assert.Equal(offset,editor.VerticalScrollOffset,3);var ink=new Ink();editor.Render(ink);Assert.Empty(ink.Text);
        editor.ClearValue(Control.TemplateProperty);Layout(editor);Assert.NotSame(oldView,editor.FindName("PART_EditorView"));Assert.Same(document,editor.Document);Assert.Equal(selected,editor.SelectedText);Assert.Equal(offset,editor.VerticalScrollOffset,3);
        var currentBar=Part<ScrollBar>(editor,"PART_VerticalScrollBar");Assert.Equal(editor.VerticalScrollMaximum,currentBar.Maximum,3);Assert.Equal(editor.VerticalScrollOffset,currentBar.Value,3);Assert.Equal(editor.VerticalScrollSmallChange,currentBar.SmallChange,3);
    }
    [Fact]
    public void OwnerlessViewAndMissingOptionalPartsAreSafe()
    {
        var view=new EditControlView();Layout(view);var ink=new Ink();view.Render(ink);Assert.Empty(ink.Text);
        var editor=Create();var template=new ControlTemplate(typeof(EditControl));template.SetVisualTree(()=>new EditControlView{Name="PART_EditorView"});editor.Template=template;Layout(editor);Assert.Same(editor,Part<EditControlView>(editor,"PART_EditorView").Owner);
        editor.IsReadOnly=true;editor.IsEnabled=false;Layout(editor);Assert.NotEmpty(editor.Text);
    }
    [Fact]
    public void CompletionUsesLazyItemsAndTheExistingAcceptanceAndKeyboardLogic()
    {
        var editor=Create();editor.Text="Con";editor.CaretOffset=3;Layout(editor);var integration=Integration(editor);
        ShowCompletion(integration,2000);Layout(editor);
        var list=Part<EditorCompletionList>(editor,"PART_CompletionList");
        Assert.Equal(Visibility.Visible,list.Visibility);Assert.Equal(2000,((IList)list.ItemsSource!).Count);Assert.Equal(0,list.SelectedIndex);
        Assert.InRange(editor.CompletionBounds.Right,0,360);Assert.InRange(editor.CompletionBounds.Bottom,0,200);
        LspKey(editor,Key.Down);Assert.Equal(1,list.SelectedIndex);Assert.Equal(3,editor.CaretOffset);
        LspKey(editor,Key.Escape);Layout(editor);Assert.Equal(Visibility.Collapsed,list.Visibility);
        ShowCompletion(integration,3);Layout(editor);var rows=Descendants<ListBoxItem>(list).ToArray();Assert.Equal(3,rows.Length);Assert.True(rows.Sum(row=>row.RenderSize.Height)+8<=editor.CompletionBounds.Height+0.1);
        ShowCompletion(integration,2);Layout(editor);var row=(EditorCompletionRow)((IList)editor.CompletionItems!)[1]!;
        row.AcceptCommand.Execute(null);Layout(editor);Assert.Equal("Console1",editor.Text);
        editor.Text="Con";editor.CaretOffset=3;ShowCompletion(integration,2);Layout(editor);LspKey(editor,Key.Enter);Layout(editor);Assert.Equal("Console0",editor.Text);Assert.Equal(Visibility.Collapsed,list.Visibility);
    }
    [Fact]
    public void RetiredCompletionCommandsAndUnavailableEditorsCannotEditTheDocument()
    {
        var editor=Create();editor.Text="Con";editor.CaretOffset=3;Layout(editor);var integration=Integration(editor);ShowCompletion(integration,2);Layout(editor);
        var oldList=Part<EditorCompletionList>(editor,"PART_CompletionList");var oldRow=(EditorCompletionRow)((IList)editor.CompletionItems!)[0]!;
        var template=new ControlTemplate(typeof(EditControl));template.SetVisualTree(()=>new Border());editor.Template=template;Layout(editor);
        Assert.Null(oldList.ItemsSource);Assert.False(oldRow.AcceptCommand.CanExecute(null));oldRow.AcceptCommand.Execute(null);Assert.Equal("Con",editor.Text);
        editor.ClearValue(Control.TemplateProperty);Layout(editor);ShowCompletion(integration,2);Layout(editor);editor.IsReadOnly=true;Assert.Equal(Visibility.Collapsed,editor.CompletionDisplay);Assert.False((bool)Read(integration,"IsCompletionActive")!);
        editor.IsReadOnly=false;ShowCompletion(integration,2);Layout(editor);editor.IsEnabled=false;Assert.False((bool)Read(integration,"IsCompletionActive")!);Assert.Equal(Visibility.Collapsed,editor.CompletionDisplay);
    }
    [Fact]
    public void HoverAndSignatureHintsExposeThemeableContentAndStayInsideTheViewport()
    {
        var editor=Create();Layout(editor);var integration=Integration(editor);
        Field(integration,"_activeHover",new Hover{Contents=new MarkupContent{Value="```csharp\n悬浮说明 English\n```"}});Layout(editor);
        var hover=Part<EditorTextHint>(editor,"PART_HoverHint");Assert.Equal("悬浮说明 English",hover.Text);Assert.Equal(Visibility.Visible,hover.Visibility);Assert.InRange(editor.HoverBounds.Right,0,360);
        Field(integration,"_activeHover",null);
        Field(integration,"_activeSignatureHelp",new SignatureHelp{ActiveParameter=1,Signatures=[new SignatureInformation{Label="Call(string name, int count)",Parameters=[new ParameterInformation{Label="string name"},new ParameterInformation{Label="int count"}]},new SignatureInformation{Label="Call()"}]});Layout(editor);
        var hint=Part<EditorTextHint>(editor,"PART_SignatureHint");Assert.Equal("1/2",hint.Counter);Assert.Equal(Visibility.Visible,hint.Visibility);Assert.Equal("int count",hint.Segments!.Cast<EditorSignatureSegment>().Single(x=>x.IsActive).Text);
        Assert.Equal(Visibility.Visible,Part<ItemsControl>(hint,"PART_Segments").Visibility);Assert.Equal(Visibility.Collapsed,Part<TextBlock>(hint,"PART_Text").Visibility);
        var oldSegments=hint.Segments;Layout(editor);Assert.Same(oldSegments,hint.Segments);Assert.InRange(editor.SignatureBounds.Bottom,0,200);
        editor.IsEnabled=false;Assert.Equal(Visibility.Collapsed,editor.SignatureDisplay);Assert.Null(Read(integration,"ActiveSignatureHelp"));
    }
    [Fact]
    public void FoldAndMinimapPreviewsUseTemplatePartsAndReleaseTheirEditorReferences()
    {
        var editor=Create();editor.Text="class Preview\n{\n    int value = 42;\n    void Print() => Console.WriteLine(value);\n}";editor.ShowMinimap=true;Layout(editor);
        PointAtSyntaxPreview(editor,true);Layout(editor);
        var hint=Part<EditorTextHint>(editor,"PART_PreviewHint");Assert.Equal(Visibility.Visible,hint.Visibility);Assert.Same(editor,hint.SyntaxOwner);Assert.NotEmpty(hint.PreviewLines!);
        var syntaxView=Part<EditorSyntaxPreviewView>(hint,"PART_Preview");Assert.Same(hint,syntaxView.Owner);var ink=new Ink();hint.Render(ink);Assert.Contains(ink.Text,text=>text.Contains("value"));
        PointAtSyntaxPreview(editor,false);Layout(editor);Assert.Null(hint.PreviewLines);Assert.Equal(Visibility.Visible,hint.Visibility);
        Field(editor,"_hasPointerPosition",false);Field(editor,"_hoveredFoldedHintSection",null);Field(editor,"_hasPointerPosition",true);Field(editor,"_isMinimapHovering",true);Field(editor,"_lastPointerPosition",new Point(330,100));
        typeof(EditControl).GetMethod("UpdateMinimapHoverTooltipPopup",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);Layout(editor);
        var popup=Part<Popup>(editor,"PART_MinimapPopup");var minimap=Part<EditorTextHint>(editor,"PART_MinimapHint");
        Assert.Same(minimap,popup.Child);Assert.Same(editor,popup.PlacementTarget);Assert.True(editor.IsMinimapPreviewOpen);Assert.True(popup.IsOpen);Assert.Equal(11,minimap.PreviewLines!.Count);
        var blank=new ControlTemplate(typeof(EditControl));blank.SetVisualTree(()=>new Border());editor.Template=blank;Layout(editor);
        Assert.False(popup.IsOpen);Assert.Null(popup.PlacementTarget);Assert.Null(minimap.SyntaxOwner);Assert.Null(minimap.PreviewLines);Assert.Null(hint.SyntaxOwner);
        var empty=new EditorSyntaxPreviewView();Layout(empty);empty.Render(new Ink());
        var replacement=new ControlTemplate(typeof(EditorTextHint));replacement.SetVisualTree(()=>new Border());hint.Template=replacement;Layout(hint);Assert.Null(syntaxView.Owner);
    }
    private static void PointAtSyntaxPreview(EditControl editor,bool folded)
    {
        InvokeEditor(editor,"UpdateFoldingState",true);
        var manager=typeof(EditControl).GetField("_foldingManager",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        var section=((IEnumerable)Read(manager,"Foldings")!).Cast<Jalium.UI.Controls.Editor.FoldingSection>().First();
        if(folded)editor.FoldAll();else editor.UnfoldAll();
        var width=editor.RenderSize.Width;var height=editor.RenderSize.Height;
        Point point;
        if(folded)
        {
            var label=(string)InvokeEditor(editor,"GetFoldedSectionHintText",section)!;
            var layout=InvokeEditor(editor,"CreateFoldedSectionHintLayout",label,editor.FontFamily.Source,editor.FontSize);
            object?[] args=[section,layout,width,height,default(Rect)];
            if(!(bool)InvokeEditor(editor,"TryGetFoldedSectionHintRect",args)!)throw new InvalidOperationException("Fold hint geometry unavailable");
            var rect=(Rect)args[4]!;point=new Point(rect.X+rect.Width/2,rect.Y+rect.Height/2);
        }
        else
        {
            object?[] args=[section,width,height,0.0,0.0,0.0];
            if(!(bool)InvokeEditor(editor,"TryGetScopeGuideGeometry",args)!)throw new InvalidOperationException("Scope geometry unavailable");
            point=new Point((double)args[3]!,((double)args[4]!+(double)args[5]!)/2);
        }
        Field(editor,"_hasPointerPosition",true);Field(editor,"_lastPointerPosition",point);
        InvokeEditor(editor,"RefreshEditorPresentation");
    }
    private static object? InvokeEditor(EditControl editor,string method,params object?[] args)=>typeof(EditControl).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,args);
    private static object Integration(EditControl editor)
    {
        typeof(EditControl).GetMethod("EnsureLspIntegration",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);
        var value=typeof(EditControl).GetField("_lspIntegration",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(editor)!;
        // An unconnected client supplies document conversion; no server process or network is started.
        var client=Activator.CreateInstance(typeof(EditControl).Assembly.GetType("Jalium.UI.Controls.Editor.LanguageServer.Client.LspClient")!,new LanguageServerConfig());
        var sync=Activator.CreateInstance(typeof(EditControl).Assembly.GetType("Jalium.UI.Controls.Editor.LanguageServer.Client.LspDocumentSync")!,client);
        Field(value,"_docSync",sync);return value;
    }
    private static void ShowCompletion(object integration,int count)
    {
        Field(integration,"_activeCompletionList",new CompletionList{Items=Enumerable.Range(0,count).Select(i=>new CompletionItem{Label=$"Console{i}",Detail="method",Kind=CompletionItemKind.Method}).ToArray()});
        Field(integration,"_isCompletionActive",true);Field(integration,"_activeCompletionIndex",0);
    }
    private static object? Read(object instance,string name)=>instance.GetType().GetProperty(name)!.GetValue(instance);
    private static void Field(object instance,string name,object? value)=>instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(instance,value);
    private static void LspKey(EditControl editor,Key key)=>editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,new Source{RootVisual=editor},1,key){RoutedEvent=UIElement.KeyDownEvent,Source=editor});
    private sealed class Source:PresentationSource
    {
        public override Visual? RootVisual{get;set;}
        public override bool IsDisposed=>false;
        protected override CompositionTarget? GetCompositionTargetCore()=>null;
    }
    private static EditControl Create()
    {
        var editor=new EditControl{ShowMinimap=false,Text=string.Join("\n",Enumerable.Range(0,160).Select(i=>$"line {i}: "+new string('x',100)))};editor.Resources.MergedDictionaries.Add(Defaults.Value);return editor;
    }
    private static IEnumerable<T> Descendants<T>(Visual root)where T:Visual
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;if(child is Visual visual)foreach(var nested in Descendants<T>(visual))yield return nested;}
    }
    private static T Part<T>(Control owner,string name)where T:class=>Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(FrameworkElement element){if(element is EditControl editor)typeof(EditControl).GetMethod("RefreshEditorPresentation",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);for(var i=0;i<4;i++){element.InvalidateMeasure();element.Measure(new Size(360,200));element.Arrange(new Rect(0,0,360,200));element.UpdateLayout();}}
    private sealed class Ink : DrawingContextAdapter
    {
        internal readonly List<string> Text = new();
        public override void DrawText(FormattedText text, Point origin) => Text.Add(text.Text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
