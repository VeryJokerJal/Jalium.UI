using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Identifies the edge from which contextual actions are revealed.</summary>
public enum SwipeDirection { None, Left, Right, Top, Bottom }

/// <summary>Reveals template-owned contextual action buttons while translating its content.</summary>
[TemplatePart(Name = "PART_Actions", Type = typeof(ItemsControl))]
[TemplatePart(Name = "PART_Content", Type = typeof(ContentPresenter))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Closed", GroupName = "SwipeStates")]
[TemplateVisualState(Name = "Dragging", GroupName = "SwipeStates")]
[TemplateVisualState(Name = "Opened", GroupName = "SwipeStates")]
[TemplateVisualState(Name = "None", GroupName = "DirectionStates")]
[TemplateVisualState(Name = "Left", GroupName = "DirectionStates")]
[TemplateVisualState(Name = "Right", GroupName = "DirectionStates")]
[TemplateVisualState(Name = "Top", GroupName = "DirectionStates")]
[TemplateVisualState(Name = "Bottom", GroupName = "DirectionStates")]
public class SwipeControl : ContentControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the LeftItems dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LeftItemsProperty =
        DependencyProperty.Register(nameof(LeftItems), typeof(SwipeItems), typeof(SwipeControl),
            new PropertyMetadata(null, OnItemsChanged));

    /// <summary>
    /// Identifies the RightItems dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RightItemsProperty =
        DependencyProperty.Register(nameof(RightItems), typeof(SwipeItems), typeof(SwipeControl),
            new PropertyMetadata(null, OnItemsChanged));

    /// <summary>
    /// Identifies the TopItems dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TopItemsProperty =
        DependencyProperty.Register(nameof(TopItems), typeof(SwipeItems), typeof(SwipeControl),
            new PropertyMetadata(null, OnItemsChanged));

    /// <summary>
    /// Identifies the BottomItems dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BottomItemsProperty =
        DependencyProperty.Register(nameof(BottomItems), typeof(SwipeItems), typeof(SwipeControl),
            new PropertyMetadata(null, OnItemsChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the items that can be invoked when the control is swiped from the left side.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public SwipeItems? LeftItems
    {
        get => (SwipeItems?)GetValue(LeftItemsProperty);
        set => SetValue(LeftItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the items that can be invoked when the control is swiped from the right side.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public SwipeItems? RightItems
    {
        get => (SwipeItems?)GetValue(RightItemsProperty);
        set => SetValue(RightItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the items that can be invoked when the control is swiped from the top.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public SwipeItems? TopItems
    {
        get => (SwipeItems?)GetValue(TopItemsProperty);
        set => SetValue(TopItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the items that can be invoked when the control is swiped from the bottom.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public SwipeItems? BottomItems
    {
        get => (SwipeItems?)GetValue(BottomItemsProperty);
        set => SetValue(BottomItemsProperty, value);
    }

    #endregion

    public static readonly DependencyProperty SwipeThresholdProperty = DependencyProperty.Register(nameof(SwipeThreshold), typeof(double), typeof(SwipeControl), new PropertyMetadata(60d), Positive);
    public static readonly DependencyProperty MaximumSwipeDistanceProperty = DependencyProperty.Register(nameof(MaximumSwipeDistance), typeof(double), typeof(SwipeControl), new PropertyMetadata(200d), Positive);
    public static readonly DependencyProperty RevealDistanceProperty = DependencyProperty.Register(nameof(RevealDistance), typeof(double), typeof(SwipeControl), new PropertyMetadata(120d), Positive);
    public static readonly DependencyProperty ActionItemTemplateProperty = DependencyProperty.Register(nameof(ActionItemTemplate), typeof(DataTemplate), typeof(SwipeControl), new PropertyMetadata(null));
    public static readonly DependencyProperty ActionContainerStyleProperty = DependencyProperty.Register(nameof(ActionContainerStyle), typeof(Style), typeof(SwipeControl), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey IsOpenPropertyKey = ReadOnly(nameof(IsOpen), typeof(bool), false);
    private static readonly DependencyPropertyKey RevealedDirectionPropertyKey = ReadOnly(nameof(RevealedDirection), typeof(SwipeDirection), SwipeDirection.None);
    private static readonly DependencyPropertyKey HorizontalOffsetPropertyKey = ReadOnly(nameof(HorizontalOffset), typeof(double), 0d);
    private static readonly DependencyPropertyKey VerticalOffsetPropertyKey = ReadOnly(nameof(VerticalOffset), typeof(double), 0d);
    private static readonly DependencyPropertyKey RevealWidthPropertyKey = ReadOnly(nameof(RevealWidth), typeof(double), 0d);
    private static readonly DependencyPropertyKey RevealHeightPropertyKey = ReadOnly(nameof(RevealHeight), typeof(double), double.NaN);
    private static readonly DependencyPropertyKey ActionRowsPropertyKey = ReadOnly(nameof(ActionRows), typeof(int), 1);
    private static readonly DependencyPropertyKey ActionColumnsPropertyKey = ReadOnly(nameof(ActionColumns), typeof(int), 0);
    private static readonly DependencyPropertyKey RevealedItemsPropertyKey = ReadOnly(nameof(RevealedItems), typeof(IReadOnlyList<SwipeItem>), Array.Empty<SwipeItem>());
    private static readonly DependencyPropertyKey ContentTranslationPropertyKey = ReadOnly(nameof(ContentTranslation), typeof(TranslateTransform), null);
    public static readonly DependencyProperty IsOpenProperty = IsOpenPropertyKey.DependencyProperty, RevealedDirectionProperty = RevealedDirectionPropertyKey.DependencyProperty,
        HorizontalOffsetProperty = HorizontalOffsetPropertyKey.DependencyProperty, VerticalOffsetProperty = VerticalOffsetPropertyKey.DependencyProperty,
        RevealWidthProperty = RevealWidthPropertyKey.DependencyProperty, RevealHeightProperty = RevealHeightPropertyKey.DependencyProperty,
        ActionRowsProperty = ActionRowsPropertyKey.DependencyProperty, ActionColumnsProperty = ActionColumnsPropertyKey.DependencyProperty,
        RevealedItemsProperty = RevealedItemsPropertyKey.DependencyProperty, ContentTranslationProperty = ContentTranslationPropertyKey.DependencyProperty;
    public double SwipeThreshold { get => (double)GetValue(SwipeThresholdProperty)!; set => SetValue(SwipeThresholdProperty,value); }
    public double MaximumSwipeDistance { get => (double)GetValue(MaximumSwipeDistanceProperty)!; set => SetValue(MaximumSwipeDistanceProperty,value); }
    public double RevealDistance { get => (double)GetValue(RevealDistanceProperty)!; set => SetValue(RevealDistanceProperty,value); }
    public DataTemplate? ActionItemTemplate { get => (DataTemplate?)GetValue(ActionItemTemplateProperty); set => SetValue(ActionItemTemplateProperty,value); }
    public Style? ActionContainerStyle { get => (Style?)GetValue(ActionContainerStyleProperty); set => SetValue(ActionContainerStyleProperty,value); }
    public bool IsOpen => (bool)GetValue(IsOpenProperty)!;
    public SwipeDirection RevealedDirection => (SwipeDirection)GetValue(RevealedDirectionProperty)!;
    public double HorizontalOffset => (double)GetValue(HorizontalOffsetProperty)!;
    public double VerticalOffset => (double)GetValue(VerticalOffsetProperty)!;
    public double RevealWidth => (double)GetValue(RevealWidthProperty)!;
    public double RevealHeight => (double)GetValue(RevealHeightProperty)!;
    public int ActionRows => (int)GetValue(ActionRowsProperty)!;
    public int ActionColumns => (int)GetValue(ActionColumnsProperty)!;
    public IReadOnlyList<SwipeItem> RevealedItems => (IReadOnlyList<SwipeItem>)GetValue(RevealedItemsProperty)!;
    public TranslateTransform ContentTranslation => (TranslateTransform)GetValue(ContentTranslationProperty)!;
    private HashSet<SwipeItem>? _mentoredItems;
    private ItemsControl? _actions;
    private ContentPresenter? _content;
    private bool _dragging;
    private Point _dragStart;
    private double _startX, _startY;
    private SwipeDirection _gestureDirection;
    static SwipeControl() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SwipeControl), new PropertyMetadata(typeof(SwipeControl)));
    public SwipeControl()
    {
        SetValue(ContentTranslationPropertyKey,new TranslateTransform());
        AddHandler(MouseDownEvent,new MouseButtonEventHandler(OnSwipeMouseDown));
        AddHandler(MouseMoveEvent,new MouseEventHandler(OnSwipeMouseMove));
        AddHandler(MouseUpEvent,new MouseButtonEventHandler(OnSwipeMouseUp));
        AddHandler(KeyDownEvent,new KeyEventHandler(OnSwipeKeyDown));
        Loaded += (_,_) => { SyncItemMentors(); RefreshItems(); };
        Unloaded += (_,_) => { Close(); ReleaseItemMentors(); };
    }
    private static bool Positive(object? value) => value is double number && double.IsFinite(number) && number > 0;
    private static DependencyPropertyKey ReadOnly(string name, Type type, object? value) => DependencyProperty.RegisterReadOnly(name,type,typeof(SwipeControl),new PropertyMetadata(value));
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate(); _actions=GetTemplateChild("PART_Actions") as ItemsControl; _content=GetTemplateChild("PART_Content") as ContentPresenter; RefreshActionAvailability(_actions); UpdateStates();
    }
    protected override void OnTemplateChanging()
    {
        _dragging=false; if(IsMouseCaptured) ReleaseMouseCapture(); ReleaseParts(); base.OnTemplateChanging();
    }
    private void ReleaseParts()
    {
        if(_actions is not null) { _actions.ClearBinding(ItemsControl.ItemsSourceProperty); _actions.ClearValue(ItemsControl.ItemsSourceProperty); }
        if(_content is not null) { _content.ClearBinding(ContentPresenter.ContentProperty); _content.ClearValue(ContentPresenter.ContentProperty); }
        _actions=null; _content=null;
    }
    protected override void OnIsEnabledChanged(bool oldValue,bool newValue) { base.OnIsEnabledChanged(oldValue,newValue); if(!newValue) Close(); UpdateStates(); }
    protected override void OnLostMouseCapture() { base.OnLostMouseCapture(); if(_dragging) Close(); }
    /// <summary>Reveals an available edge; returns false for a disabled control or an empty collection.</summary>
    public bool Open(SwipeDirection direction)
    {
        if(!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if(!IsEnabled || ItemsFor(direction) is not { Count: > 0 }) return false;
        _dragging=false; if(IsMouseCaptured) ReleaseMouseCapture(); SetValue(IsOpenPropertyKey,true); SetOffsets(direction,RevealDistance); RefreshItems(); return true;
    }
    /// <summary>Closes actions and releases any active pointer capture.</summary>
    public void Close()
    {
        _dragging=false; if(IsMouseCaptured) ReleaseMouseCapture(); SetValue(IsOpenPropertyKey,false); SetOffsets(SwipeDirection.None,0);
    }
    /// <summary>Refreshes the snapshot after mutating an existing SwipeItems list.</summary>
    public void RefreshItems()
    {
        SyncItemMentors();
        SetValue(RevealedItemsPropertyKey,ItemsFor(RevealedDirection)?.ToArray() ?? Array.Empty<SwipeItem>());
        if(RevealedItems.Count==0 && RevealedDirection!=SwipeDirection.None) Close(); else UpdateStates();
    }
    /// <summary>Invokes a configured action once, honoring command availability and BehaviorOnInvoked.</summary>
    public bool InvokeItem(SwipeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if(!IsEnabled || !(LeftItems?.Contains(item)==true || RightItems?.Contains(item)==true || TopItems?.Contains(item)==true || BottomItems?.Contains(item)==true) || item.Command is { } command && !command.CanExecute(item.CommandParameter)) return false;
        item.RaiseInvoked(); if(item.BehaviorOnInvoked!=BehaviorOnInvoked.RemainOpen) Close(); return true;
    }
    private static void RefreshActionAvailability(Visual? root)
    {
        if(root is null) return;
        if(root is SwipeActionButton button) button.RefreshAvailability();
        for(var index=0;index<root.VisualChildrenCount;index++) RefreshActionAvailability(root.GetVisualChild(index));
    }
    internal bool OwnsAction(SwipeActionButton button)
    {
        if(_actions is null || !IsOpen) return false;
        for(Visual? current=button;current is not null;current=current.VisualParent) if(ReferenceEquals(current,_actions)) return true;
        return false;
    }
    private void SyncItemMentors()
    {
        var current=new HashSet<SwipeItem>();
        foreach(var items in new[]{LeftItems,RightItems,TopItems,BottomItems}) if(items is not null) foreach(var item in items) current.Add(item);
        if(_mentoredItems is not null) foreach(var item in _mentoredItems) if(!current.Contains(item) && ReferenceEquals(item.BindingMentor,this)) item.SetBindingMentor(null);
        foreach(var item in current) item.SetBindingMentor(this);
        _mentoredItems=current.Count==0?null:current;
    }
    private void ReleaseItemMentors()
    {
        if(_mentoredItems is not null) foreach(var item in _mentoredItems) if(ReferenceEquals(item.BindingMentor,this)) item.SetBindingMentor(null);
        _mentoredItems=null;
    }
    private SwipeItems? ItemsFor(SwipeDirection direction) => direction switch { SwipeDirection.Left=>LeftItems, SwipeDirection.Right=>RightItems, SwipeDirection.Top=>TopItems, SwipeDirection.Bottom=>BottomItems, _=>null };
    private static void OnItemsChanged(DependencyObject d,DependencyPropertyChangedEventArgs e) => ((SwipeControl)d).RefreshItems();
    private void SetOffsets(SwipeDirection direction,double distance)
    {
        var horizontal=direction is SwipeDirection.Left or SwipeDirection.Right;
        var limit=horizontal?ActualWidth:ActualHeight; distance=Math.Min(distance,MaximumSwipeDistance); if(limit>0) distance=Math.Min(distance,limit);
        if(distance<=0) direction=SwipeDirection.None;
        var changed=direction!=RevealedDirection; SetValue(RevealedDirectionPropertyKey,direction);
        var x=direction==SwipeDirection.Left?distance:direction==SwipeDirection.Right?-distance:0;
        var y=direction==SwipeDirection.Top?distance:direction==SwipeDirection.Bottom?-distance:0;
        SetValue(HorizontalOffsetPropertyKey,x); SetValue(VerticalOffsetPropertyKey,y); ContentTranslation.X=x; ContentTranslation.Y=y;
        SetValue(RevealWidthPropertyKey,horizontal?distance:double.NaN); SetValue(RevealHeightPropertyKey,horizontal?double.NaN:distance);
        SetValue(ActionRowsPropertyKey,horizontal?1:0); SetValue(ActionColumnsPropertyKey,horizontal?0:1);
        if(changed) RefreshItems(); UpdateStates();
    }
    private void UpdateStates()
    {
        if(TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);
        VisualStateManager.GoToState(this,_dragging?"Dragging":IsOpen?"Opened":"Closed",false);
        VisualStateManager.GoToState(this,RevealedDirection.ToString(),false);
    }
    private void OnSwipeMouseDown(object sender,MouseButtonEventArgs e)
    {
        if(!IsEnabled || e.ChangedButton!=MouseButton.Left) return;
        _dragging=true; _dragStart=e.GetPosition(this); _startX=HorizontalOffset; _startY=VerticalOffset; _gestureDirection=RevealedDirection; CaptureMouse(); e.Handled=true;
    }
    private void OnSwipeMouseMove(object sender,MouseEventArgs e)
    {
        if(!_dragging) return;
        if(e.LeftButton!=MouseButtonState.Pressed) { Close(); return; }
        var position=e.GetPosition(this); var dx=_startX+position.X-_dragStart.X; var dy=_startY+position.Y-_dragStart.Y;
        if(_gestureDirection==SwipeDirection.None && Math.Max(Math.Abs(dx),Math.Abs(dy))>=4)
            _gestureDirection=Math.Abs(dx)>=Math.Abs(dy)?(dx>0?SwipeDirection.Left:SwipeDirection.Right):(dy>0?SwipeDirection.Top:SwipeDirection.Bottom);
        var direction=_gestureDirection;
        if(ItemsFor(direction) is not { Count:>0 }) { SetOffsets(SwipeDirection.None,0); return; }
        var distance=direction switch { SwipeDirection.Left=>Math.Max(0,dx),SwipeDirection.Right=>Math.Max(0,-dx),SwipeDirection.Top=>Math.Max(0,dy),SwipeDirection.Bottom=>Math.Max(0,-dy),_=>0 };
        SetValue(IsOpenPropertyKey,false); SetOffsets(direction,distance); e.Handled=true;
    }
    private void OnSwipeMouseUp(object sender,MouseButtonEventArgs e)
    {
        if(!_dragging || e.ChangedButton!=MouseButton.Left) return;
        _dragging=false; if(IsMouseCaptured) ReleaseMouseCapture(); var direction=RevealedDirection;
        if(Math.Max(Math.Abs(HorizontalOffset),Math.Abs(VerticalOffset))>=SwipeThreshold && ItemsFor(direction) is { Count:>0 } items)
        { if(items.Mode==SwipeMode.Execute) { if(!InvokeItem(items[0])) Close(); } else Open(direction); }
        else Close(); e.Handled=true;
    }
    private void OnSwipeKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape && (IsOpen || _dragging)) { Close(); Focus(); e.Handled=true; return; }
        if(!IsFocused || (e.KeyboardModifiers & ModifierKeys.Alt)==0) return;
        var direction=e.Key switch { Key.Right=>SwipeDirection.Left,Key.Left=>SwipeDirection.Right,Key.Down=>SwipeDirection.Top,Key.Up=>SwipeDirection.Bottom,_=>SwipeDirection.None };
        if(direction!=SwipeDirection.None && Open(direction)) e.Handled=true;
    }
}
