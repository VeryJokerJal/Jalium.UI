using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Root docking container with replaceable border, content host and target highlight.</summary>
[TemplatePart(Name="PART_Content",Type=typeof(ContentPresenter))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="Highlighted",GroupName="DockStates")]
[TemplateVisualState(Name="NotHighlighted",GroupName="DockStates")]
public class DockLayout : ContentControl
{
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanFloatProperty=DependencyProperty.Register(nameof(CanFloat),typeof(bool),typeof(DockLayout),new PropertyMetadata(true));
    public static readonly DependencyProperty HighlightBrushProperty=DependencyProperty.Register(nameof(HighlightBrush),typeof(Brush),typeof(DockLayout),new PropertyMetadata(null));
    private static readonly DependencyPropertyKey IsDockHighlightedPropertyKey=DependencyProperty.RegisterReadOnly(nameof(IsDockHighlighted),typeof(bool),typeof(DockLayout),new PropertyMetadata(false,(d,_)=>((DockLayout)d).UpdateStates()));
    public static readonly DependencyProperty IsDockHighlightedProperty=IsDockHighlightedPropertyKey.DependencyProperty;
    public bool CanFloat {get=>(bool)GetValue(CanFloatProperty)!;set=>SetValue(CanFloatProperty,value);}
    public Brush? HighlightBrush {get=>(Brush?)GetValue(HighlightBrushProperty);set=>SetValue(HighlightBrushProperty,value);}
    /// <summary>Gets whether DockManager currently marks this layout as a target.</summary>
    public bool IsDockHighlighted {get=>(bool)GetValue(IsDockHighlightedProperty)!;internal set=>SetValue(IsDockHighlightedPropertyKey,value);}
    private ContentPresenter? _content;
    static DockLayout()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(DockLayout),new PropertyMetadata(typeof(DockLayout)));
    public DockLayout()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty,TransitionPropertyCollection.None());
        DockManager.Register(this); Loaded+=(_,_)=>DockManager.Register(this); Unloaded+=(_,_)=>DockManager.Unregister(this);
    }
    public override void OnApplyTemplate(){ReleaseContent();base.OnApplyTemplate();_content=GetTemplateChild("PART_Content") as ContentPresenter;UpdateStates();}
    protected override void OnTemplateChanging(){ReleaseContent();base.OnTemplateChanging();}
    private void ReleaseContent(){if(_content is not null){_content.ClearBinding(ContentPresenter.ContentProperty);_content.ClearValue(ContentPresenter.ContentProperty);}_content=null;}
    protected override void OnIsEnabledChanged(bool oldValue,bool newValue){base.OnIsEnabledChanged(oldValue,newValue);UpdateStates();}
    private void UpdateStates()
    {
        if(TemplateRootInternal is null)return;
        VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);
        VisualStateManager.GoToState(this,IsDockHighlighted?"Highlighted":"NotHighlighted",false);
    }
}
