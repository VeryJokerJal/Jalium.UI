using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation;

namespace Jalium.UI.Tests;

public class AutomationPeerTests
{
    public static IEnumerable<object[]> ControlPeerMappings()
    {
        yield return new object[] { (Func<UIElement>)(() => new Control()), typeof(FrameworkElementAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ContentControl()), typeof(ContentControlAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ItemsControl()), typeof(ItemsControlAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Jalium.UI.Controls.Primitives.ToggleButton()), typeof(ToggleButtonAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Jalium.UI.Controls.Primitives.RepeatButton()), typeof(RepeatButtonAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Jalium.UI.Controls.Primitives.Thumb()), typeof(ThumbAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ListBox()), typeof(ListBoxAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ListBoxItem()), typeof(ListBoxItemAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ComboBox()), typeof(ComboBoxAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new TreeView()), typeof(TreeViewAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new TreeViewItem()), typeof(TreeViewItemAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Slider()), typeof(SliderAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ProgressBar()), typeof(ProgressBarAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new TabControl()), typeof(TabControlAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new TabItem()), typeof(TabItemAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Menu()), typeof(MenuAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new MenuItem()), typeof(MenuItemAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ScrollViewer()), typeof(ScrollViewerAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Window()), typeof(WindowAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new DataGrid()), typeof(DataGridAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new DataGridRow()), typeof(DataGridRowAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new DataGridCell()), typeof(DataGridCellAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new DataGridColumnHeader()), typeof(DataGridColumnHeaderAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Expander()), typeof(ExpanderAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new GroupBox()), typeof(GroupBoxAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Image()), typeof(ImageAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new TextBlock()), typeof(TextBlockAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ToolTip()), typeof(ToolTipAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new HyperlinkButton()), typeof(HyperlinkButtonAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new RichTextBox()), typeof(RichTextBoxAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ListView()), typeof(ListViewAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new DatePicker()), typeof(DatePickerAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Calendar()), typeof(CalendarAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new TimePicker()), typeof(TimePickerAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new NavigationView()), typeof(NavigationViewAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new NavigationViewItem()), typeof(NavigationViewItemAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new NumberBox()), typeof(NumberBoxAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ToggleSwitch()), typeof(ToggleSwitchAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ColorPicker()), typeof(ColorPickerAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new InfoBar()), typeof(InfoBarAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new AutoCompleteBox()), typeof(AutoCompleteBoxAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Jalium.UI.Controls.Primitives.Popup()), typeof(PopupAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new GridSplitter()), typeof(GridSplitterAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Viewbox()), typeof(ViewboxAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new TitleBarButton()), typeof(TitleBarButtonAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Label()), typeof(LabelAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Separator()), typeof(SeparatorAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new UserControl()), typeof(UserControlAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new StatusBar()), typeof(StatusBarAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new StatusBarItem()), typeof(StatusBarItemAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new ToolBar()), typeof(ToolBarAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Frame()), typeof(FrameAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new Viewport3D()), typeof(Viewport3DAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new DocumentViewer()), typeof(DocumentViewerAutomationPeer) };
        yield return new object[] { (Func<UIElement>)(() => new GroupItem()), typeof(GroupItemAutomationPeer) };
    }

    #region AutomationPeer Base Tests

    [Fact]
    public void AutomationPeer_GetAutomationControlType_ReturnsCorrectType()
    {
        // Arrange
        var button = new Button();

        // Act
        var peer = button.GetAutomationPeer();

        // Assert
        Assert.NotNull(peer);
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
    }

    [Fact]
    public void AutomationPeer_GetClassName_ReturnsCorrectName()
    {
        // Arrange
        var button = new Button();
        var checkBox = new CheckBox();
        var textBox = new TextBox();

        // Act
        var buttonPeer = button.GetAutomationPeer();
        var checkBoxPeer = checkBox.GetAutomationPeer();
        var textBoxPeer = textBox.GetAutomationPeer();

        // Assert
        Assert.Equal("Button", buttonPeer!.GetClassName());
        Assert.Equal("CheckBox", checkBoxPeer!.GetClassName());
        Assert.Equal("TextBox", textBoxPeer!.GetClassName());
    }

    [Fact]
    public void AutomationPeer_GetName_ReturnsContentForButton()
    {
        // Arrange
        var button = new Button { Content = "Click Me" };

        // Act
        var peer = button.GetAutomationPeer();
        var name = peer!.GetName();

        // Assert
        Assert.Equal("Click Me", name);
    }

    [Fact]
    public void AutomationPeer_GetName_ReturnsAutomationPropertiesName()
    {
        // Arrange
        var button = new Button { Content = "Click Me" };
        AutomationProperties.SetName(button, "Custom Name");

        // Act
        var peer = button.GetAutomationPeer();
        var name = peer!.GetName();

        // Assert - AutomationProperties.Name should take precedence
        Assert.Equal("Custom Name", name);
    }

    [Fact]
    public void AutomationPeer_IsEnabled_ReflectsControlState()
    {
        // Arrange
        var button = new Button { IsEnabled = true };
        var disabledButton = new Button { IsEnabled = false };

        // Act
        var enabledPeer = button.GetAutomationPeer();
        var disabledPeer = disabledButton.GetAutomationPeer();

        // Assert
        Assert.True(enabledPeer!.IsEnabled());
        Assert.False(disabledPeer!.IsEnabled());
    }

    [Fact]
    public void GetAutomationPeer_ReturnsSameInstance()
    {
        // Arrange
        var button = new Button();

        // Act
        var peer1 = button.GetAutomationPeer();
        var peer2 = button.GetAutomationPeer();

        // Assert
        Assert.Same(peer1, peer2);
    }

    [Theory]
    [MemberData(nameof(ControlPeerMappings))]
    public void Control_GetAutomationPeer_ReturnsExpectedPeerType(Func<UIElement> createControl, Type expectedPeerType)
    {
        // Arrange
        var control = createControl();

        // Act
        var peer = control.GetAutomationPeer();

        // Assert
        Assert.NotNull(peer);
        Assert.IsType(expectedPeerType, peer);
    }

    #endregion

    #region Button AutomationPeer Tests

    [Fact]
    public void ButtonAutomationPeer_GetControlType_ReturnsButton()
    {
        // Arrange
        var button = new Button();

        // Act
        var peer = button.GetAutomationPeer();

        // Assert
        Assert.Equal(AutomationControlType.Button, peer!.GetAutomationControlType());
    }

    [Fact]
    public void ButtonAutomationPeer_GetPattern_ReturnsInvokeProvider()
    {
        // Arrange
        var button = new Button();

        // Act
        var peer = button.GetAutomationPeer();
        var pattern = peer!.GetPattern(PatternInterface.Invoke);

        // Assert
        Assert.NotNull(pattern);
        Assert.IsAssignableFrom<IInvokeProvider>(pattern);
    }

    [Fact]
    public void ButtonAutomationPeer_Invoke_RaisesClickEvent()
    {
        // Arrange
        var button = new Button();
        var clicked = false;
        button.Click += (s, e) => clicked = true;

        // Act
        var peer = button.GetAutomationPeer();
        var invokeProvider = peer!.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        invokeProvider!.Invoke();

        // Assert
        Assert.True(clicked);
    }

    [Fact]
    public void ButtonAutomationPeer_Invoke_ThrowsWhenDisabled()
    {
        // Arrange
        var button = new Button { IsEnabled = false };

        // Act
        var peer = button.GetAutomationPeer();
        var invokeProvider = peer!.GetPattern(PatternInterface.Invoke) as IInvokeProvider;

        // Assert
        Assert.Throws<InvalidOperationException>(() => invokeProvider!.Invoke());
    }

    #endregion

    #region CheckBox AutomationPeer Tests

    [Fact]
    public void CheckBoxAutomationPeer_GetControlType_ReturnsCheckBox()
    {
        // Arrange
        var checkBox = new CheckBox();

        // Act
        var peer = checkBox.GetAutomationPeer();

        // Assert
        Assert.Equal(AutomationControlType.CheckBox, peer!.GetAutomationControlType());
    }

    [Fact]
    public void CheckBoxAutomationPeer_GetPattern_ReturnsToggleProvider()
    {
        // Arrange
        var checkBox = new CheckBox();

        // Act
        var peer = checkBox.GetAutomationPeer();
        var pattern = peer!.GetPattern(PatternInterface.Toggle);

        // Assert
        Assert.NotNull(pattern);
        Assert.IsAssignableFrom<IToggleProvider>(pattern);
    }

    [Fact]
    public void CheckBoxAutomationPeer_ToggleState_ReflectsCheckedState()
    {
        // Arrange
        var checkBox = new CheckBox { IsChecked = false };

        // Act
        var peer = checkBox.GetAutomationPeer();
        var toggleProvider = peer!.GetPattern(PatternInterface.Toggle) as IToggleProvider;

        // Assert
        Assert.Equal(ToggleState.Off, toggleProvider!.ToggleState);

        // Change state
        checkBox.IsChecked = true;
        Assert.Equal(ToggleState.On, toggleProvider.ToggleState);
    }

    [Fact]
    public void CheckBoxAutomationPeer_ToggleState_ReflectsIndeterminateState()
    {
        // Arrange
        var checkBox = new CheckBox { IsThreeState = true, IsChecked = null };

        // Act
        var peer = checkBox.GetAutomationPeer();
        var toggleProvider = peer!.GetPattern(PatternInterface.Toggle) as IToggleProvider;

        // Assert
        Assert.Equal(ToggleState.Indeterminate, toggleProvider!.ToggleState);
    }

    [Fact]
    public void CheckBoxAutomationPeer_Toggle_ChangesState()
    {
        // Arrange
        var checkBox = new CheckBox { IsChecked = false };

        // Act
        var peer = checkBox.GetAutomationPeer();
        var toggleProvider = peer!.GetPattern(PatternInterface.Toggle) as IToggleProvider;
        toggleProvider!.Toggle();

        // Assert
        Assert.True(checkBox.IsChecked);
    }

    [Fact]
    public void CheckBoxAutomationPeer_Toggle_ThrowsWhenDisabled()
    {
        // Arrange
        var checkBox = new CheckBox { IsEnabled = false };

        // Act
        var peer = checkBox.GetAutomationPeer();
        var toggleProvider = peer!.GetPattern(PatternInterface.Toggle) as IToggleProvider;

        // Assert
        Assert.Throws<InvalidOperationException>(() => toggleProvider!.Toggle());
    }

    #endregion

    #region RadioButton AutomationPeer Tests

    [Fact]
    public void RadioButtonAutomationPeer_GetControlType_ReturnsRadioButton()
    {
        // Arrange
        var radioButton = new RadioButton();

        // Act
        var peer = radioButton.GetAutomationPeer();

        // Assert
        Assert.Equal(AutomationControlType.RadioButton, peer!.GetAutomationControlType());
    }

    [Fact]
    public void RadioButtonAutomationPeer_GetPattern_ReturnsSelectionItemProvider()
    {
        // Arrange
        var radioButton = new RadioButton();

        // Act
        var peer = radioButton.GetAutomationPeer();
        var pattern = peer!.GetPattern(PatternInterface.SelectionItem);

        // Assert
        Assert.NotNull(pattern);
        Assert.IsAssignableFrom<ISelectionItemProvider>(pattern);
    }

    [Fact]
    public void RadioButtonAutomationPeer_IsSelected_ReflectsCheckedState()
    {
        // Arrange
        var radioButton = new RadioButton { IsChecked = false };

        // Act
        var peer = radioButton.GetAutomationPeer();
        var selectionItemProvider = peer!.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;

        // Assert
        Assert.False(selectionItemProvider!.IsSelected);

        // Change state
        radioButton.IsChecked = true;
        Assert.True(selectionItemProvider.IsSelected);
    }

    [Fact]
    public void RadioButtonAutomationPeer_Select_ChecksRadioButton()
    {
        // Arrange
        var radioButton = new RadioButton { IsChecked = false };

        // Act
        var peer = radioButton.GetAutomationPeer();
        var selectionItemProvider = peer!.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
        selectionItemProvider!.Select();

        // Assert
        Assert.True(radioButton.IsChecked);
    }

    #endregion

    #region TextBox AutomationPeer Tests

    [Fact]
    public void TextBoxAutomationPeer_GetControlType_ReturnsEdit()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        var peer = textBox.GetAutomationPeer();

        // Assert
        Assert.Equal(AutomationControlType.Edit, peer!.GetAutomationControlType());
    }

    [Fact]
    public void TextBoxAutomationPeer_GetPattern_ReturnsValueProvider()
    {
        // Arrange
        var textBox = new TextBox();

        // Act
        var peer = textBox.GetAutomationPeer();
        var pattern = peer!.GetPattern(PatternInterface.Value);

        // Assert
        Assert.NotNull(pattern);
        Assert.IsAssignableFrom<IValueProvider>(pattern);
    }

    [Fact]
    public void TextBoxAutomationPeer_Value_ReflectsText()
    {
        // Arrange
        var textBox = new TextBox { Text = "Hello World" };

        // Act
        var peer = textBox.GetAutomationPeer();
        var valueProvider = peer!.GetPattern(PatternInterface.Value) as IValueProvider;

        // Assert
        Assert.Equal("Hello World", valueProvider!.Value);
    }

    [Fact]
    public void TextBoxAutomationPeer_SetValue_ChangesText()
    {
        // Arrange
        var textBox = new TextBox { Text = "Initial" };

        // Act
        var peer = textBox.GetAutomationPeer();
        var valueProvider = peer!.GetPattern(PatternInterface.Value) as IValueProvider;
        valueProvider!.SetValue("New Value");

        // Assert
        Assert.Equal("New Value", textBox.Text);
    }

    [Fact]
    public void TextBoxAutomationPeer_SetValue_ThrowsWhenReadOnly()
    {
        // Arrange
        var textBox = new TextBox { IsReadOnly = true };

        // Act
        var peer = textBox.GetAutomationPeer();
        var valueProvider = peer!.GetPattern(PatternInterface.Value) as IValueProvider;

        // Assert
        Assert.Throws<InvalidOperationException>(() => valueProvider!.SetValue("Test"));
    }

    [Fact]
    public void TextBoxAutomationPeer_IsReadOnly_ReflectsState()
    {
        // Arrange
        var readOnlyTextBox = new TextBox { IsReadOnly = true };
        var editableTextBox = new TextBox { IsReadOnly = false };

        // Act
        var readOnlyPeer = readOnlyTextBox.GetAutomationPeer();
        var editablePeer = editableTextBox.GetAutomationPeer();
        var readOnlyProvider = readOnlyPeer!.GetPattern(PatternInterface.Value) as IValueProvider;
        var editableProvider = editablePeer!.GetPattern(PatternInterface.Value) as IValueProvider;

        // Assert
        Assert.True(readOnlyProvider!.IsReadOnly);
        Assert.False(editableProvider!.IsReadOnly);
    }

    [Fact]
    public void TextBoxAutomationPeer_GetName_ReturnsPlaceholder()
    {
        // Arrange
        var textBox = new TextBox { PlaceholderText = "Enter your name" };

        // Act
        var peer = textBox.GetAutomationPeer();
        var name = peer!.GetName();

        // Assert
        Assert.Equal("Enter your name", name);
    }

    #endregion

    #region PasswordBox AutomationPeer Tests

    [Fact]
    public void PasswordBoxAutomationPeer_GetControlType_ReturnsEdit()
    {
        // Arrange
        var passwordBox = new PasswordBox();

        // Act
        var peer = passwordBox.GetAutomationPeer();

        // Assert
        Assert.Equal(AutomationControlType.Edit, peer!.GetAutomationControlType());
    }

    [Fact]
    public void PasswordBoxAutomationPeer_GetName_ReturnsPlaceholder()
    {
        // Arrange
        var passwordBox = new PasswordBox { PlaceholderText = "Enter your password" };

        // Act
        var peer = passwordBox.GetAutomationPeer();
        var name = peer!.GetName();

        // Assert
        Assert.Equal("Enter your password", name);
    }

    [Fact]
    public void PasswordBoxAutomationPeer_Value_ReturnsEmptyForSecurity()
    {
        // Arrange
        var passwordBox = new PasswordBox { Password = "secret123" };

        // Act
        var peer = passwordBox.GetAutomationPeer();
        var valueProvider = peer!.GetPattern(PatternInterface.Value) as IValueProvider;

        // Assert - Should NOT expose the password
        Assert.Equal(string.Empty, valueProvider!.Value);
    }

    [Fact]
    public void PasswordBoxAutomationPeer_SetValue_SetsPassword()
    {
        // Arrange
        var passwordBox = new PasswordBox();

        // Act
        var peer = passwordBox.GetAutomationPeer();
        var valueProvider = peer!.GetPattern(PatternInterface.Value) as IValueProvider;
        valueProvider!.SetValue("newPassword");

        // Assert
        Assert.Equal("newPassword", passwordBox.Password);
    }

    #endregion

    #region AutomationProperties Tests

    [Fact]
    public void AutomationProperties_Name_CanBeSet()
    {
        // Arrange
        var element = new Button();

        // Act
        AutomationProperties.SetName(element, "Accessible Name");
        var name = AutomationProperties.GetName(element);

        // Assert
        Assert.Equal("Accessible Name", name);
    }

    [Fact]
    public void AutomationProperties_AutomationId_CanBeSet()
    {
        // Arrange
        var element = new Button();

        // Act
        AutomationProperties.SetAutomationId(element, "btnSubmit");
        var id = AutomationProperties.GetAutomationId(element);

        // Assert
        Assert.Equal("btnSubmit", id);
    }

    [Fact]
    public void AutomationProperties_HelpText_CanBeSet()
    {
        // Arrange
        var element = new Button();

        // Act
        AutomationProperties.SetHelpText(element, "Click to submit the form");
        var helpText = AutomationProperties.GetHelpText(element);

        // Assert
        Assert.Equal("Click to submit the form", helpText);
    }

    [Fact]
    public void AutomationPeer_GetAutomationId_ReturnsSetValue()
    {
        // Arrange
        var button = new Button();
        AutomationProperties.SetAutomationId(button, "myButton");

        // Act
        var peer = button.GetAutomationPeer();
        var automationId = peer!.GetAutomationId();

        // Assert
        Assert.Equal("myButton", automationId);
    }

    [Fact]
    public void AutomationPeer_GetHelpText_ReturnsSetValue()
    {
        // Arrange
        var button = new Button();
        AutomationProperties.SetHelpText(button, "Help text here");

        // Act
        var peer = button.GetAutomationPeer();
        var helpText = peer!.GetHelpText();

        // Assert
        Assert.Equal("Help text here", helpText);
    }

    #endregion
}
