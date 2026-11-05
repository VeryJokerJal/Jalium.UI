using System.ComponentModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>A bindable chart legend item.</summary>
public class ChartLegendItem : INotifyPropertyChanged
{
    private string _label=string.Empty;
    private Brush _brush=new SolidColorBrush(Color.Gray);
    private bool _isVisible=true;
    public string Label { get=>_label; set { if(_label==value)return; _label=value; PropertyChanged?.Invoke(this,new(nameof(Label))); } }
    public Brush Brush { get=>_brush; set { if(ReferenceEquals(_brush,value))return; _brush=value; PropertyChanged?.Invoke(this,new(nameof(Brush))); } }
    public bool IsVisible { get=>_isVisible; set { if(_isVisible==value)return; _isVisible=value; PropertyChanged?.Invoke(this,new(nameof(IsVisible))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
