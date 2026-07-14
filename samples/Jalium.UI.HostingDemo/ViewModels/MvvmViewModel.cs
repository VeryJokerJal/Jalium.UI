namespace Jalium.UI.HostingDemo.ViewModels;

/// <summary>
/// 展示:<c>AddView&lt;MvvmPage, MvvmViewModel&gt;()</c> 的效果 —— Frame 导航到
/// <see cref="Views.MvvmPage"/> 时 <see cref="IViewFactory"/> 会自动把 <see cref="MvvmViewModel"/>
/// 实例设成 Page 的 DataContext。该 VM 有简单的递增逻辑。
/// </summary>
public sealed class MvvmViewModel : ViewModelBase
{
    private int _count;
    private string _lastAction = "就绪";

    public int Count
    {
        get => _count;
        private set => SetProperty(ref _count, value);
    }

    public string LastAction
    {
        get => _lastAction;
        private set => SetProperty(ref _lastAction, value);
    }

    public void Increment()
    {
        Count++;
        LastAction = $"点击第 {Count} 次,{DateTime.Now:HH:mm:ss}";
    }

    public void Reset()
    {
        Count = 0;
        LastAction = "已重置";
    }
}
