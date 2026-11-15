using System.Windows.Input;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>A keyboard-accessible swipe action using the standard button command pipeline.</summary>
public class SwipeActionButton : Button
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item),typeof(SwipeItem),typeof(SwipeActionButton),new PropertyMetadata(null,OnActionChanged));
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(nameof(Owner),typeof(SwipeControl),typeof(SwipeActionButton),new PropertyMetadata(null,OnActionChanged));
    private static readonly DependencyProperty SourceCommandProperty = DependencyProperty.Register("SourceCommand",typeof(ICommand),typeof(SwipeActionButton),new PropertyMetadata(null,OnSourceCommandChanged));
    private static readonly DependencyProperty SourceBackgroundProperty = DependencyProperty.Register("SourceBackground",typeof(Brush),typeof(SwipeActionButton),new PropertyMetadata(null,(d,_)=>d.CoerceValue(BackgroundProperty)));
    private static readonly DependencyProperty SourceForegroundProperty = DependencyProperty.Register("SourceForeground",typeof(Brush),typeof(SwipeActionButton),new PropertyMetadata(null,(d,_)=>d.CoerceValue(ForegroundProperty)));
    public SwipeItem? Item { get=>(SwipeItem?)GetValue(ItemProperty); set=>SetValue(ItemProperty,value); }
    public SwipeControl? Owner { get=>(SwipeControl?)GetValue(OwnerProperty); set=>SetValue(OwnerProperty,value); }
    private readonly RelayCommand _invoke;
    private ICommand? _observed;
    static SwipeActionButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SwipeActionButton),new PropertyMetadata(typeof(SwipeActionButton)));
        BackgroundProperty.OverrideMetadata(typeof(SwipeActionButton),new PropertyMetadata(null,null,(d,value)=>d.GetValue(SourceBackgroundProperty)??value));
        ForegroundProperty.OverrideMetadata(typeof(SwipeActionButton),new PropertyMetadata(null,null,(d,value)=>d.GetValue(SourceForegroundProperty)??value));
    }
    public SwipeActionButton()
    {
        _invoke=new RelayCommand(()=>{ if(Item is {} item && Owner is {} owner) owner.InvokeItem(item); },()=>Item is {} item && Owner is {} owner && owner.IsEnabled && owner.OwnsAction(this) && (item.Command?.CanExecute(item.CommandParameter)??true));
        SetCurrentValue(CommandProperty,_invoke); SetBinding(SourceCommandProperty,new Binding("Item.Command"){Source=this});
        SetBinding(SourceBackgroundProperty,new Binding("Item.Background"){Source=this});
        SetBinding(SourceForegroundProperty,new Binding("Item.Foreground"){Source=this});
        Loaded+=(_,_)=>ObserveCommand(); Unloaded+=(_,_)=>UnobserveCommand();
    }
    private static void OnActionChanged(DependencyObject d,DependencyPropertyChangedEventArgs e) => ((SwipeActionButton)d)._invoke?.RaiseCanExecuteChanged();
    private static void OnSourceCommandChanged(DependencyObject d,DependencyPropertyChangedEventArgs e) { var button=(SwipeActionButton)d; button.ObserveCommand(); button._invoke?.RaiseCanExecuteChanged(); }
    private void ObserveCommand() { UnobserveCommand(); if(IsLoaded && GetValue(SourceCommandProperty) is ICommand command) { _observed=command; command.CanExecuteChanged+=OnCommandChanged; } _invoke.RaiseCanExecuteChanged(); }
    private void UnobserveCommand() { if(_observed is not null) _observed.CanExecuteChanged-=OnCommandChanged; _observed=null; }
    internal void RefreshAvailability() => _invoke.RaiseCanExecuteChanged();
    private void OnCommandChanged(object? sender,EventArgs e) => _invoke.RaiseCanExecuteChanged();
}
