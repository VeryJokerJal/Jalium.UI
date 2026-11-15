using Jalium.UI.Media;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Ribbon;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class RibbonTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() => { Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream=new MemoryStream(); return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!; });
    [Fact]
    public void HeadersSelectExactlyOneGroupsHostAndKeepTheOriginalControls()
    {
        var (ribbon, first, second, firstGroup, secondGroup) = Create(); Layout(ribbon);
        Assert.Equal(0,ribbon.SelectedIndex); Assert.Same(first,ribbon.SelectedTab);
        Assert.Same(firstGroup,Assert.Single(Part<ItemsControl>(ribbon,"PART_InlineGroups").Items));
        Part<Button>(second,"PART_HeaderButton").PerformClick(); Layout(ribbon);
        Assert.Equal(1,ribbon.SelectedIndex); Assert.Same(second,ribbon.SelectedTab); Assert.False(first.IsSelected); Assert.True(second.IsSelected);
        Assert.Same(secondGroup,Assert.Single(Part<ItemsControl>(ribbon,"PART_InlineGroups").Items));
        Assert.DoesNotContain(firstGroup,Walk(ribbon)); Assert.Contains(secondGroup,Walk(ribbon));
    }
    [Fact]
    public void ReplacingRootAndHeaderTemplatesReleasesRetiredPartsAndRetainsGroupIdentity()
    {
        var (ribbon, first, second, firstGroup, _) = Create(); Layout(ribbon); var retiredHost=Part<ItemsControl>(ribbon,"PART_InlineGroups");
        var blank=new ControlTemplate(typeof(Ribbon));blank.SetVisualTree(()=>new Border());ribbon.Template=blank;Layout(ribbon);Assert.Null(retiredHost.ItemsSource);
        ribbon.ClearValue(Control.TemplateProperty);Layout(ribbon);Assert.Same(firstGroup,Assert.Single(Part<ItemsControl>(ribbon,"PART_InlineGroups").Items));
        var retired=Part<Button>(second,"PART_HeaderButton");var tabBlank=new ControlTemplate(typeof(RibbonTab));tabBlank.SetVisualTree(()=>new Border());second.Template=tabBlank;Layout(ribbon);retired.PerformClick();Assert.Equal(0,ribbon.SelectedIndex);
        second.ClearValue(Control.TemplateProperty);Layout(ribbon);Part<Button>(second,"PART_HeaderButton").PerformClick();Assert.Equal(1,ribbon.SelectedIndex);
    }
    [Fact]
    public void MenuAndSplitButtonsUseCurrentStandardPartsAndRetireOldCallbacks()
    {
        var menu=new RibbonMenuButton{Label="选项"};menu.Resources.MergedDictionaries.Add(Defaults.Value);menu.Items.Add(new RibbonMenuItem{Header="动作"});Layout(menu);var old=Part<Button>(menu,"PART_HeaderButton");old.PerformClick();Assert.True(menu.IsDropDownOpen);
        var blank=new ControlTemplate(typeof(RibbonMenuButton));blank.SetVisualTree(()=>new Border());menu.Template=blank;Layout(menu);Assert.False(menu.IsDropDownOpen);old.PerformClick();Assert.False(menu.IsDropDownOpen);
        var calls=0;var split=new RibbonSplitButton{Label="执行",Command=new Jalium.UI.Input.RelayCommand(()=>calls++)};split.Resources.MergedDictionaries.Add(Defaults.Value);split.Items.Add(new RibbonMenuItem{Header="其他"});Layout(split);Part<Button>(split,"PART_HeaderButton").PerformClick();Assert.Equal(1,calls);Assert.False(split.IsDropDownOpen);Part<Button>(split,"PART_DropDownButton").PerformClick();Assert.Equal(1,calls);Assert.True(split.IsDropDownOpen);split.IsDropDownOpen=false;
    }
    [Fact]
    public void GallerySelectionAndBindableLabelsUpdateThroughTemplates()
    {
        var first=new RibbonGalleryItem{Content="first"};var second=new RibbonGalleryItem{Content="second"};var category=new RibbonGalleryCategory{Header="字体"};category.Items.Add(first);category.Items.Add(second);var gallery=new RibbonGallery();gallery.Items.Add(category);gallery.Resources.MergedDictionaries.Add(Defaults.Value);Layout(gallery);
        Part<Button>(second,"PART_Button").PerformClick();Assert.Same(second,gallery.SelectedItem);Assert.False(first.IsSelected);Assert.True(second.IsSelected);
        gallery.SelectedItem=first;Assert.True(first.IsSelected);Assert.False(second.IsSelected);
        Assert.NotNull(Assert.IsType<Style>(Defaults.Value[typeof(RibbonButton)]).BasedOn);
        var button=new RibbonButton{Label="粘贴"};button.Resources.MergedDictionaries.Add(Defaults.Value);Layout(button);Assert.NotNull(button.Template);Assert.Equal("粘贴",button.Content);button.Label="复制";Assert.Equal("复制",button.Content);
        var toggle=new RibbonToggleButton{Label="加粗"};toggle.Resources.MergedDictionaries.Add(Defaults.Value);Layout(toggle);Assert.Equal("加粗",toggle.Content);toggle.Label="B";Assert.Equal("B",toggle.Content);
    }
    [Fact]
    public void QuickAccessPlacementFollowsCallerPreferenceAndCompactWidth()
    {
        var (ribbon,_,_,_,_)=Create();var quick=new RibbonQuickAccessToolBar();quick.Items.Add(new RibbonButton{Label="保存"});ribbon.QuickAccessToolBar=quick;Layout(ribbon);
        var presenter=Part<ContentPresenter>(ribbon,"PART_QuickAccess");Assert.Equal(0,Grid.GetRow(presenter));Assert.Equal(1,Grid.GetColumn(presenter));
        ribbon.ShowQuickAccessToolBarOnTop=false;Layout(ribbon);Assert.Equal(3,Grid.GetRow(presenter));Assert.Equal(0,Grid.GetColumn(presenter));
        ribbon.ShowQuickAccessToolBarOnTop=true;ribbon.CompactWidth=800;Layout(ribbon);Assert.Equal(3,Grid.GetRow(presenter));
        ribbon.CompactWidth=600;Layout(ribbon);Assert.Equal(0,Grid.GetRow(presenter));
    }
    [Fact]
    public void PresentationStatesControlTheWholeChromeAndDisabledQuickAccessCommands()
    {
        var (ribbon,_,_,_,_)=Create();var calls=0;
        var save=new RibbonButton{Label="保存",Command=new Jalium.UI.Input.RelayCommand(()=>calls++)};
        var quick=new RibbonQuickAccessToolBar();quick.Items.Add(save);ribbon.QuickAccessToolBar=quick;Layout(ribbon);
        ribbon.IsMinimized=true;Layout(ribbon);Assert.Equal(Visibility.Collapsed,Part<ScrollViewer>(ribbon,"PART_GroupScroll").Visibility);
        ribbon.IsEnabled=false;Layout(ribbon);Assert.Equal(0.5,Part<Border>(ribbon,"PART_Root").Opacity);Assert.False(save.IsEnabled);save.PerformClick();Assert.Equal(0,calls);
        ribbon.IsEnabled=true;ribbon.IsMinimized=false;ribbon.Title=null;Layout(ribbon);Assert.Equal(Visibility.Collapsed,Part<ContentPresenter>(ribbon,"PART_Title").Visibility);
        ribbon.IsCollapsed=true;Layout(ribbon);Assert.Equal(Visibility.Collapsed,Part<Border>(ribbon,"PART_Root").Visibility);
    }
    [Fact]
    public void HeaderKeysNavigateBeforeTheScrollHostAndLeaveGroupEditorsAlone()
    {
        Keyboard.Initialize();
        var (ribbon,first,second,firstGroup,_)=Create();Layout(ribbon);
        var firstHeader=Part<Button>(first,"PART_HeaderButton");Assert.True(firstHeader.Focus());
        SendKey(firstHeader,Key.Right);Assert.Same(second,ribbon.SelectedTab);
        first.IsEnabled=false;SendKey(Part<Button>(second,"PART_HeaderButton"),Key.Right);Assert.Same(second,ribbon.SelectedTab);
        first.IsEnabled=true;SendKey(Part<Button>(second,"PART_HeaderButton"),Key.Home);Assert.Same(first,ribbon.SelectedTab);
        ribbon.FlowDirection=FlowDirection.RightToLeft;SendKey(firstHeader,Key.Left);Assert.Same(second,ribbon.SelectedTab);
        SendKey(Part<Button>(second,"PART_HeaderButton"),Key.Home);
        var editor=new TextBox{Text="abc"};firstGroup.Items.Add(editor);Layout(ribbon);Assert.True(editor.Focus());SendKey(editor,Key.Right);Assert.Same(first,ribbon.SelectedTab);
    }
    private static void SendKey(UIElement target,Key key)
    {
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,new Source{RootVisual=target},1,key){RoutedEvent=UIElement.KeyDownEvent,Source=target});
    }
    private sealed class Source:PresentationSource
    {
        public override Visual? RootVisual{get;set;}
        public override bool IsDisposed=>false;
        protected override CompositionTarget? GetCompositionTargetCore()=>null;
    }
    private static (Ribbon,RibbonTab,RibbonTab,RibbonGroup,RibbonGroup) Create()
    {
        var firstGroup=new RibbonGroup{Header="剪贴板"};firstGroup.Items.Add(new RibbonButton{Label="粘贴"});var secondGroup=new RibbonGroup{Header="插入"};secondGroup.Items.Add(new RibbonButton{Label="表格"});var first=new RibbonTab{Header="开始"};first.Items.Add(firstGroup);var second=new RibbonTab{Header="插入"};second.Items.Add(secondGroup);var ribbon=new Ribbon{Title="文档工具"};ribbon.Resources.MergedDictionaries.Add(Defaults.Value);ribbon.Items.Add(first);ribbon.Items.Add(second);return(ribbon,first,second,firstGroup,secondGroup);
    }
    private static T Part<T>(Control owner,string name) where T:class=>Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static IEnumerable<Visual> Walk(Visual root){for(var i=0;i<root.VisualChildrenCount;i++){var child=root.GetVisualChild(i);if(child is not null){yield return child;foreach(var item in Walk(child))yield return item;}}}
    private static void Layout(FrameworkElement root){for(var i=0;i<3;i++){root.InvalidateMeasure();root.Measure(new Size(720,360));root.Arrange(new Rect(0,0,720,360));root.UpdateLayout();}}
}
