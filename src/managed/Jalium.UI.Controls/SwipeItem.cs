using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the behavior of a SwipeItem after it is invoked.
/// </summary>
public enum BehaviorOnInvoked
{
    /// <summary>
    /// The SwipeControl is automatically closed after the item is invoked.
    /// </summary>
    Auto,

    /// <summary>
    /// The SwipeControl is closed after the item is invoked.
    /// </summary>
    Close,

    /// <summary>
    /// The SwipeControl remains open after the item is invoked.
    /// </summary>
    RemainOpen
}

/// <summary>
/// Represents an individual command in a SwipeControl.
/// </summary>
public sealed class SwipeItem : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SwipeItem),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the IconSource dependency property.
    /// </summary>
    public static readonly DependencyProperty IconSourceProperty =
        DependencyProperty.Register(nameof(IconSource), typeof(object), typeof(SwipeItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(SwipeItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(SwipeItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(SwipeItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(SwipeItem),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the BehaviorOnInvoked dependency property.
    /// </summary>
    public static readonly DependencyProperty BehaviorOnInvokedProperty =
        DependencyProperty.Register(nameof(BehaviorOnInvoked), typeof(BehaviorOnInvoked), typeof(SwipeItem),
            new PropertyMetadata(BehaviorOnInvoked.Auto));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the text label for the swipe item.
    /// </summary>
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon for the swipe item.
    /// </summary>
    public object? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the background color of the swipe item.
    /// </summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground color of the swipe item.
    /// </summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to execute when the item is invoked.
    /// </summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the parameter to pass to the Command.
    /// </summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the behavior after the item is invoked.
    /// </summary>
    public BehaviorOnInvoked BehaviorOnInvoked
    {
        get => (BehaviorOnInvoked)(GetValue(BehaviorOnInvokedProperty) ?? BehaviorOnInvoked.Auto);
        set => SetValue(BehaviorOnInvokedProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the swipe item is invoked.
    /// </summary>
    public event EventHandler? Invoked;

    /// <summary>
    /// Raises the Invoked event.
    /// </summary>
    internal void RaiseInvoked()
    {
        Invoked?.Invoke(this, EventArgs.Empty);
        Command?.Execute(CommandParameter);
    }

    #endregion
}
