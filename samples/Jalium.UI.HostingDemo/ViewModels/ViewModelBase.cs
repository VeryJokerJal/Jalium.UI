using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Jalium.UI.HostingDemo.ViewModels;

/// <summary>
/// 极简的 <see cref="INotifyPropertyChanged"/> 基类,给 VM 用。生产项目通常用
/// CommunityToolkit.Mvvm,这里为了零依赖展示框架本身的 hosting 集成。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Notify([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
