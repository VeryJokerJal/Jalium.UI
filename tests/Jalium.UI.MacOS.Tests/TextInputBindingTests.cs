using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class TextInputBindingTests
{
    [Theory]
    [InlineData(BindingMode.TwoWay, UpdateSourceTrigger.Default)]
    [InlineData(BindingMode.TwoWay, UpdateSourceTrigger.PropertyChanged)]
    [InlineData(BindingMode.TwoWay, UpdateSourceTrigger.LostFocus)]
    [InlineData(BindingMode.TwoWay, UpdateSourceTrigger.Explicit)]
    [InlineData(BindingMode.Default, UpdateSourceTrigger.Default)]
    [InlineData(BindingMode.Default, UpdateSourceTrigger.PropertyChanged)]
    [InlineData(BindingMode.Default, UpdateSourceTrigger.LostFocus)]
    [InlineData(BindingMode.Default, UpdateSourceTrigger.Explicit)]
    public void UserInputAndUndoPreserveBindingAndHonorCommitTrigger(BindingMode mode, UpdateSourceTrigger trigger)
    {
        var form = new Form();
        var text = new InputTextBox();
        var expression = text.SetBinding(TextBox.TextProperty, new Binding("Form.Value")
        {
            Source = new { Form = form }, Mode = mode, UpdateSourceTrigger = trigger,
        });
        text.Input("中文🙂");
        Assert.Equal("中文🙂", text.Text);
        var automatic = trigger is UpdateSourceTrigger.Default or UpdateSourceTrigger.PropertyChanged;
        Assert.Equal(automatic ? text.Text : "", form.Value);
        if (trigger == UpdateSourceTrigger.LostFocus)
        {
            text.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, text));
            Assert.Equal(text.Text, form.Value);
        }
        expression.UpdateSource();
        Assert.Equal(text.Text, form.Value);
        Assert.True(text.Undo());
        Assert.Equal("", text.Text);
        Assert.Equal(automatic ? "" : "中文🙂", form.Value);
        expression.UpdateSource();
        Assert.Equal("", form.Value);
        Assert.True(text.Redo());
        expression.UpdateSource();
        Assert.Equal("中文🙂", form.Value);
        Assert.Same(expression, text.GetBindingExpression(TextBox.TextProperty));
        form.Value = "model reset";
        Assert.Equal("model reset", text.Text);
    }

    [Fact]
    public void ExplicitOneWayBindingKeepsSourceReadOnlyDuringEditing()
    {
        var form = new Form { Value = "original" };
        var text = new InputTextBox();
        var expression = text.SetBinding(TextBox.TextProperty,
            new Binding(nameof(Form.Value)) { Source = form, Mode = BindingMode.OneWay });
        text.SelectAll();
        text.Input("draft");
        expression.UpdateSource();
        Assert.Equal("draft", text.Text);
        Assert.Equal("original", form.Value);
        form.Value = "model refresh";
        Assert.Equal(form.Value, text.Text);
        Assert.Same(expression, text.GetBindingExpression(TextBox.TextProperty));
    }

    [Fact]
    public void ProgrammaticCurrentValueDoesNotCommitAnExplicitBinding()
    {
        var form = new Form();
        var text = new InputTextBox();
        var expression = text.SetBinding(TextBox.TextProperty, new Binding(nameof(Form.Value))
        { Source = form, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
        text.SetCurrentValue(TextBox.TextProperty, "draft");
        Assert.Equal("", form.Value);
        expression.UpdateSource();
        Assert.Equal("draft", form.Value);
    }

    private sealed class InputTextBox : TextBox
    {
        public void Input(string value) => InsertText(value);
    }

    private sealed class Form : INotifyPropertyChanged
    {
        private string _value = "";
        public string Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
