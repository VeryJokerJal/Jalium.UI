using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>
/// 展示 MVVM 场景:<c>AddView&lt;MvvmPage, MvvmViewModel&gt;()</c> 让 DI 同时构造 Page
/// 和 VM,并让 <see cref="IViewFactory"/> 把 VM 挂到 DataContext(这里我们直接在构造
/// 函数接收 VM 并自己设了 DataContext,所以 ViewFactory 的 auto-attach 检测到已有值便跳过)。
/// </summary>
public sealed class MvvmPage : Page
{
    public MvvmPage(MvvmViewModel vm)
    {
        Title = "MVVM";
        DataContext = vm;

        var countText = new TextBlock
        {
            Text = $"Count = {vm.Count}",
            FontSize = 32,
            Foreground = new Media.SolidColorBrush(Media.Colors.White),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var actionText = PageLayout.Body(vm.LastAction);

        // VM 是 INotifyPropertyChanged 实现,简单手动订阅更新 UI
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MvvmViewModel.Count))
            {
                countText.Text = $"Count = {vm.Count}";
            }
            else if (e.PropertyName == nameof(MvvmViewModel.LastAction))
            {
                actionText.Text = vm.LastAction;
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(PageLayout.DemoButton("+1", vm.Increment));
        buttons.Children.Add(PageLayout.DemoButton("Reset", vm.Reset));

        Content = PageLayout.Build(
            "MVVM · View/ViewModel 配对",
            "MvvmPage 和 MvvmViewModel 都是 Transient,每次导航都是新实例。若把 MvvmViewModel 换成 Singleton(builder.Services.AddSingleton<MvvmViewModel>()),Count 就会在导航间保留。",
            countText,
            actionText,
            buttons);
    }
}
