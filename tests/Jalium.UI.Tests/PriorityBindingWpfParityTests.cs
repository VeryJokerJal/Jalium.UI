using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class PriorityBindingWpfParityTests
{
    [Fact]
    public void PriorityBinding_ShouldSkipUnsetValue_AndUseNextBinding()
    {
        var viewModel = new PriorityViewModel
        {
            Primary = DependencyProperty.UnsetValue,
            Secondary = "SecondaryValue"
        };

        var target = new Border
        {
            DataContext = viewModel
        };

        var priorityBinding = new PriorityBinding();
        priorityBinding.Bindings.Add(new Binding("Primary"));
        priorityBinding.Bindings.Add(new Binding("Secondary"));

        target.SetBinding(FrameworkElement.TagProperty, priorityBinding);

        Assert.Equal("SecondaryValue", target.Tag);

        viewModel.Primary = "PrimaryValue";
        Assert.Equal("PrimaryValue", target.Tag);
    }

    [Fact]
    public void PriorityBinding_WhenAllInvalid_ShouldUseFallbackValue()
    {
        var target = new Border
        {
            DataContext = new PriorityViewModel()
        };

        var priorityBinding = new PriorityBinding
        {
            FallbackValue = "Fallback"
        };
        priorityBinding.Bindings.Add(new Binding("Missing")
        {
            FallbackValue = DependencyProperty.UnsetValue
        });

        target.SetBinding(FrameworkElement.TagProperty, priorityBinding);

        Assert.Equal("Fallback", target.Tag);
    }

    [Fact]
    public void PriorityBinding_ShouldConvertActiveNonStringValueToStringTarget()
    {
        var target = new TextBlock
        {
            DataContext = new PriorityViewModel { Primary = 42 }
        };

        var priorityBinding = new PriorityBinding();
        priorityBinding.Bindings.Add(new Binding("Primary"));

        target.SetBinding(TextBlock.TextProperty, priorityBinding);

        Assert.Equal("42", target.Text);
    }

    private sealed class PriorityViewModel : INotifyPropertyChanged
    {
        private object? _primary;
        private object? _secondary;

        public object? Primary
        {
            get => _primary;
            set
            {
                if (Equals(_primary, value))
                    return;

                _primary = value;
                OnPropertyChanged();
            }
        }

        public object? Secondary
        {
            get => _secondary;
            set
            {
                if (Equals(_secondary, value))
                    return;

                _secondary = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? memberName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
        }
    }
}
