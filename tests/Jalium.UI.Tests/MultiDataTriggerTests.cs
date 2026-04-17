using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public class MultiDataTriggerTests
{
    #region BindingCondition Tests

    [Fact]
    public void BindingCondition_CanSetBinding()
    {
        // Arrange & Act
        var condition = new BindingCondition
        {
            Binding = new Binding("PropertyName"),
            Value = "TestValue"
        };

        // Assert
        Assert.NotNull(condition.Binding);
        Assert.NotNull(condition.Binding.Path);
        Assert.Equal("PropertyName", condition.Binding.Path.Path);
        Assert.Equal("TestValue", condition.Value);
    }

    #endregion

    #region MultiDataTrigger Basic Tests

    [Fact]
    public void MultiDataTrigger_CanAddConditions()
    {
        // Arrange
        var trigger = new MultiDataTrigger();

        // Act
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Property1"),
            Value = true
        });
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Property2"),
            Value = "Active"
        });

        // Assert
        Assert.Equal(2, trigger.Conditions.Count);
    }

    [Fact]
    public void MultiDataTrigger_CanAddSetters()
    {
        // Arrange
        var trigger = new MultiDataTrigger();

        // Act
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        // Assert
        Assert.Single(trigger.Setters);
    }

    #endregion

    #region Activation Tests

    [Fact]
    public void MultiDataTrigger_ActivatesWhenAllConditionsTrue()
    {
        // Arrange
        var vm = new TestViewModel { IsEnabled = true, Status = "Active" };
        var element = new TestElement { DataContext = vm };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Status"),
            Value = "Active"
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        // Act
        trigger.AttachForTest(element);

        // Assert
        Assert.True(trigger.IsActiveForElement(element));
        Assert.Equal(0.5, element.GetValue(TestElement.TestOpacityProperty));
    }

    [Fact]
    public void MultiDataTrigger_DoesNotActivateWhenPartialConditionsTrue()
    {
        // Arrange
        var vm = new TestViewModel { IsEnabled = true, Status = "Inactive" };
        var element = new TestElement { DataContext = vm };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Status"),
            Value = "Active"
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        // Act
        trigger.AttachForTest(element);

        // Assert
        Assert.False(trigger.IsActiveForElement(element));
        Assert.Equal(1.0, element.GetValue(TestElement.TestOpacityProperty)); // Default value
    }

    [Fact]
    public void MultiDataTrigger_DoesNotActivateWhenNoConditionsTrue()
    {
        // Arrange
        var vm = new TestViewModel { IsEnabled = false, Status = "Inactive" };
        var element = new TestElement { DataContext = vm };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Status"),
            Value = "Active"
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        // Act
        trigger.AttachForTest(element);

        // Assert
        Assert.False(trigger.IsActiveForElement(element));
    }

    [Fact]
    public void MultiDataTrigger_DeactivatesWhenConditionBecomesFalse()
    {
        // Arrange
        var vm = new TestViewModel { IsEnabled = true, Status = "Active" };
        var element = new TestElement { DataContext = vm };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Status"),
            Value = "Active"
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        trigger.AttachForTest(element);
        Assert.True(trigger.IsActiveForElement(element));

        // Act - Change a condition to make trigger inactive
        vm.Status = "Inactive";

        // Assert
        Assert.False(trigger.IsActiveForElement(element));
    }

    [Fact]
    public void MultiDataTrigger_ActivatesWhenConditionBecomesTrue()
    {
        // Arrange
        var vm = new TestViewModel { IsEnabled = true, Status = "Inactive" };
        var element = new TestElement { DataContext = vm };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Status"),
            Value = "Active"
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        trigger.AttachForTest(element);
        Assert.False(trigger.IsActiveForElement(element));

        // Act - Change condition to make trigger active
        vm.Status = "Active";

        // Assert
        Assert.True(trigger.IsActiveForElement(element));
        Assert.Equal(0.5, element.GetValue(TestElement.TestOpacityProperty));
    }

    #endregion

    #region Detach Tests

    [Fact]
    public void MultiDataTrigger_DetachRestoresOriginalValue()
    {
        // Arrange
        var vm = new TestViewModel { IsEnabled = true, Status = "Active" };
        var element = new TestElement { DataContext = vm };
        element.SetValue(TestElement.TestOpacityProperty, 0.8);

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Status"),
            Value = "Active"
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        trigger.AttachForTest(element);
        // WPF precedence: local value outranks trigger layers.
        Assert.Equal(0.8, element.GetValue(TestElement.TestOpacityProperty));

        // Act
        trigger.DetachForTest(element);

        // Assert - Should restore to original value
        Assert.Equal(0.8, element.GetValue(TestElement.TestOpacityProperty));
    }

    [Fact]
    public void MultiDataTrigger_DetachClearsState()
    {
        // Arrange
        var vm = new TestViewModel { IsEnabled = true, Status = "Active" };
        var element = new TestElement { DataContext = vm };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        trigger.AttachForTest(element);

        // Act
        trigger.DetachForTest(element);

        // Assert
        Assert.False(trigger.IsActiveForElement(element));
    }

    #endregion

    #region Single Condition Tests

    [Fact]
    public void MultiDataTrigger_WorksWithSingleCondition()
    {
        // Arrange
        var vm = new TestViewModel { IsEnabled = true };
        var element = new TestElement { DataContext = vm };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        // Act
        trigger.AttachForTest(element);

        // Assert
        Assert.True(trigger.IsActiveForElement(element));
        Assert.Equal(0.5, element.GetValue(TestElement.TestOpacityProperty));
    }

    #endregion

    #region Multi-Element Tests

    [Fact]
    public void MultiDataTrigger_WorksIndependentlyOnMultipleElements()
    {
        // Arrange
        var vm1 = new TestViewModel { IsEnabled = true, Status = "Active" };
        var vm2 = new TestViewModel { IsEnabled = true, Status = "Inactive" };
        var element1 = new TestElement { DataContext = vm1 };
        var element2 = new TestElement { DataContext = vm2 };

        var trigger = new MultiDataTrigger();
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("IsEnabled"),
            Value = true
        });
        trigger.Conditions.Add(new BindingCondition
        {
            Binding = new Binding("Status"),
            Value = "Active"
        });
        trigger.Setters.Add(new Setter(TestElement.TestOpacityProperty, 0.5));

        // Act
        trigger.AttachForTest(element1);
        trigger.AttachForTest(element2);

        // Assert
        Assert.True(trigger.IsActiveForElement(element1));
        Assert.False(trigger.IsActiveForElement(element2));
        Assert.Equal(0.5, element1.GetValue(TestElement.TestOpacityProperty));
        Assert.Equal(1.0, element2.GetValue(TestElement.TestOpacityProperty)); // Default
    }

    #endregion

    #region Test Helpers

    private class TestElement : FrameworkElement
    {
        public static readonly DependencyProperty TestOpacityProperty =
            DependencyProperty.Register("TestOpacity", typeof(double), typeof(TestElement),
                new PropertyMetadata(1.0));

        public double TestOpacity
        {
            get => (double)(GetValue(TestOpacityProperty) ?? 1.0);
            set => SetValue(TestOpacityProperty, value);
        }
    }

    private class TestViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _status = string.Empty;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    #endregion
}

/// <summary>
/// Extension methods to expose internal methods for testing.
/// </summary>
public static class TriggerTestExtensions
{
    public static void AttachForTest(this Trigger trigger, FrameworkElement element)
    {
        // Use reflection to call internal Attach method
        var method = typeof(Trigger).GetMethod("Attach",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method?.Invoke(trigger, new object[] { element });
    }

    public static void DetachForTest(this Trigger trigger, FrameworkElement element)
    {
        // Use reflection to call internal Detach method
        var method = typeof(Trigger).GetMethod("Detach",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method?.Invoke(trigger, new object[] { element });
    }
}
