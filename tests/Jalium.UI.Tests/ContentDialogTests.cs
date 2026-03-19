using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ContentDialogTests
{
    [Fact]
    public void ShowAsync_DetachedDialog_ShouldResolveMainWindow()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            var dialog = new ContentDialog
            {
                Title = "Dialog",
                PrimaryButtonText = "OK"
            };

            var showTask = dialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            Assert.False(showTask.IsCompleted);
            Assert.Same(dialog, window.ActiveContentDialog);
            Assert.Equal(Visibility.Visible, dialog.Visibility);
            Assert.NotNull(GetPrivateField<ContentDialogOverlayHost>(dialog, "_popupHost"));

            dialog.Hide();
            ProcessUiQueue();

            Assert.True(showTask.IsCompletedSuccessfully);
            Assert.Equal(ContentDialogResult.None, showTask.Result);
            Assert.Null(window.ActiveContentDialog);
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void ShowAsync_InPlaceDialog_ShouldUseVisualTreeWindow()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var root = new Grid();
            var dialog = new ContentDialog
            {
                Title = "Dialog",
                PrimaryButtonText = "Save"
            };
            root.Children.Add(dialog);

            var window = CreateWindow(root);
            app.MainWindow = window;

            var showTask = dialog.ShowAsync(ContentDialogPlacement.InPlace);
            MeasureWindow(window);
            ProcessUiQueue();

            Assert.False(showTask.IsCompleted);
            Assert.Contains(dialog, window.ActiveInPlaceDialogs);
            Assert.Null(window.ActiveContentDialog);
            Assert.Equal(Visibility.Visible, dialog.Visibility);

            dialog.Hide();
            ProcessUiQueue();

            Assert.True(showTask.IsCompletedSuccessfully);
            Assert.Equal(ContentDialogResult.None, showTask.Result);
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void ShowAsync_InPlaceDialog_ShouldBypassCachedUnderlyingHit()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var background = new Border
            {
                Width = 800,
                Height = 600
            };

            var root = new Grid();
            root.Children.Add(background);

            var dialog = new ContentDialog
            {
                Title = "Dialog",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel"
            };
            root.Children.Add(dialog);

            var window = CreateWindow(root);
            app.MainWindow = window;

            MeasureWindow(window);

            var cachedHit = InvokeHitTestElement(window, new Point(400, 300));
            Assert.Same(background, cachedHit);

            var showTask = dialog.ShowAsync(ContentDialogPlacement.InPlace);
            MeasureWindow(window);
            ProcessUiQueue();

            var dialogCard = Assert.IsType<Border>(dialog.FindName("PART_DialogCard"));
            var hitPoint = new Point(
                dialogCard.VisualBounds.X + (dialogCard.VisualBounds.Width / 2),
                dialogCard.VisualBounds.Y + (dialogCard.VisualBounds.Height / 2));

            var hit = InvokeHitTestElement(window, hitPoint);

            Assert.False(showTask.IsCompleted);
            Assert.NotSame(background, hit);
            Assert.True(IsDescendantOf(hit, dialog), $"Expected hit inside dialog, actual={hit?.GetType().Name ?? "<null>"} point={hitPoint}");

            dialog.Hide();
            ProcessUiQueue();
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void PopupModalOverlay_ShouldBlockUnderlyingButtonClick()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            int clickCount = 0;
            var button = new Button
            {
                Content = "Behind",
                Width = 120,
                Height = 40
            };
            button.Click += (_, _) => clickCount++;

            var root = new Grid();
            root.Children.Add(button);

            var window = CreateWindow(root);
            app.MainWindow = window;

            var dialog = new ContentDialog
            {
                Title = "Dialog",
                CloseButtonText = "Cancel"
            };

            _ = dialog.ShowAsync();
            MeasureWindow(window);

            InvokeMouseButtonDown(window, MouseButton.Left, x: 10, y: 10);
            InvokeMouseButtonUp(window, MouseButton.Left, x: 10, y: 10);

            Assert.Equal(0, clickCount);

            dialog.Hide();
            ProcessUiQueue();
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void ExplicitSizeConstraints_ShouldOnlyResizeDialogCard()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var background = new Border
            {
                Width = 800,
                Height = 600
            };

            var root = new Grid();
            root.Children.Add(background);

            var window = CreateWindow(root);
            app.MainWindow = window;

            var dialog = new ContentDialog
            {
                Title = "Dialog",
                CloseButtonText = "Close",
                Width = 640,
                Height = 520,
                MaxHeight = 500
            };

            _ = dialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            var dialogCard = Assert.IsType<Border>(dialog.FindName("PART_DialogCard"));
            Assert.Equal(800, dialog.ActualWidth);
            Assert.Equal(600, dialog.ActualHeight);
            Assert.Equal(640, dialogCard.ActualWidth);
            Assert.Equal(500, dialogCard.ActualHeight);
            Assert.Equal(80, dialogCard.VisualBounds.X);
            Assert.Equal(50, dialogCard.VisualBounds.Y);

            var hit = InvokeHitTestElement(window, new Point(790, 10));
            Assert.NotSame(background, hit);
            Assert.True(IsDescendantOf(hit, dialog), $"Expected hit inside dialog overlay, actual={hit?.GetType().Name ?? "<null>"}");

            dialog.Hide();
            ProcessUiQueue();
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void ContentWidth_ShouldDriveDialogCardWidth_WhenWidthIsUnset()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            var content = new Border
            {
                Width = 640,
                Height = 120
            };

            var dialog = new ContentDialog
            {
                Title = "Dialog",
                CloseButtonText = "Close",
                Content = content
            };

            _ = dialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            var dialogCard = Assert.IsType<Border>(dialog.FindName("PART_DialogCard"));
            Assert.InRange(dialogCard.ActualWidth, 640, 751);
            Assert.Equal(
                Math.Round((800 - dialogCard.ActualWidth) / 2, MidpointRounding.AwayFromZero),
                dialogCard.VisualBounds.X);

            dialog.Hide();
            ProcessUiQueue();
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void EnterAndEscape_ShouldUseDialogButtons()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            int primaryClicks = 0;
            var primaryDialog = new ContentDialog
            {
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            primaryDialog.PrimaryButtonClick += (_, _) => primaryClicks++;

            var primaryTask = primaryDialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();
            InvokeKeyDown(window, Key.Enter, nint.Zero);
            ProcessUiQueue();

            Assert.True(primaryTask.IsCompletedSuccessfully);
            Assert.Equal(ContentDialogResult.Primary, primaryTask.Result);
            Assert.Equal(1, primaryClicks);

            int closeClicks = 0;
            var closeDialog = new ContentDialog
            {
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            closeDialog.CloseButtonClick += (_, _) => closeClicks++;

            var closeTask = closeDialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();
            InvokeKeyDown(window, Key.Escape, nint.Zero);
            ProcessUiQueue();

            Assert.True(closeTask.IsCompletedSuccessfully);
            Assert.Equal(ContentDialogResult.None, closeTask.Result);
            Assert.Equal(1, closeClicks);
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void SecondaryButton_ShouldReturnSecondaryResult()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            var dialog = new ContentDialog
            {
                SecondaryButtonText = "Later"
            };

            var showTask = dialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            var secondaryButton = Assert.IsType<Button>(dialog.FindName("PART_SecondaryButton"));
            secondaryButton.PerformClick();
            ProcessUiQueue();

            Assert.True(showTask.IsCompletedSuccessfully);
            Assert.Equal(ContentDialogResult.Secondary, showTask.Result);
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void TabNavigation_ShouldStayWithinDialog()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var textBox = new TextBox();
            var dialog = new ContentDialog
            {
                Content = textBox,
                CloseButtonText = "Back",
                PrimaryButtonText = "Next",
                DefaultButton = ContentDialogButton.Primary
            };

            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            _ = dialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            var primaryButton = Assert.IsType<Button>(dialog.FindName("PART_PrimaryButton"));
            Assert.True(primaryButton.Focus());

            InvokeKeyDown(window, Key.Tab, nint.Zero);
            Assert.Same(textBox, Keyboard.FocusedElement);

            Window.SetKeyStateProviderForTesting(key => key == 0x10 ? unchecked((short)0x8000) : (short)0);
            InvokeKeyDown(window, Key.Tab, (nint)(1 << 16));
            Window.SetKeyStateProviderForTesting(null);
            Assert.Same(primaryButton, Keyboard.FocusedElement);

            dialog.Hide();
            ProcessUiQueue();
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void ButtonClickAndClosing_CancelOrDeferral_ShouldKeepDialogOpen()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            var clickCanceledDialog = new ContentDialog
            {
                PrimaryButtonText = "Apply"
            };
            clickCanceledDialog.PrimaryButtonClick += (_, e) =>
            {
                var deferral = e.GetDeferral();
                Dispatcher.GetForCurrentThread().BeginInvokeCritical(() =>
                {
                    e.Cancel = true;
                    deferral.Complete();
                });
            };

            var clickCanceledTask = clickCanceledDialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            var primaryButton = Assert.IsType<Button>(clickCanceledDialog.FindName("PART_PrimaryButton"));
            primaryButton.PerformClick();
            ProcessUiQueue();
            ProcessUiQueue();

            Assert.False(clickCanceledTask.IsCompleted);
            Assert.Equal(Visibility.Visible, clickCanceledDialog.Visibility);

            clickCanceledDialog.Hide();
            ProcessUiQueue();

            var closingCanceledDialog = new ContentDialog
            {
                PrimaryButtonText = "Apply"
            };
            closingCanceledDialog.Closing += (_, e) =>
            {
                e.Cancel = true;
            };

            var closingCanceledTask = closingCanceledDialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            var secondPrimaryButton = Assert.IsType<Button>(closingCanceledDialog.FindName("PART_PrimaryButton"));
            secondPrimaryButton.PerformClick();
            ProcessUiQueue();

            Assert.False(closingCanceledTask.IsCompleted);
            Assert.Equal(Visibility.Visible, closingCanceledDialog.Visibility);

            closingCanceledDialog.OnHostWindowClosed();
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void CommandCanExecute_ShouldDriveButtonEnabledState_AndExecuteOnClose()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            int executionCount = 0;
            bool canExecute = false;
            var command = new RelayCommand(
                () => executionCount++,
                () => canExecute);

            var dialog = new ContentDialog
            {
                PrimaryButtonText = "Run",
                PrimaryButtonCommand = command
            };

            var showTask = dialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            var primaryButton = Assert.IsType<Button>(dialog.FindName("PART_PrimaryButton"));
            Assert.False(primaryButton.IsEnabled);

            canExecute = true;
            command.RaiseCanExecuteChanged();

            Assert.True(primaryButton.IsEnabled);
            primaryButton.PerformClick();
            ProcessUiQueue();

            Assert.True(showTask.IsCompletedSuccessfully);
            Assert.Equal(ContentDialogResult.Primary, showTask.Result);
            Assert.Equal(1, executionCount);
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void SecondDialogOnSameWindow_ShouldThrow()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            var firstDialog = new ContentDialog { CloseButtonText = "Dismiss" };
            var secondDialog = new ContentDialog { CloseButtonText = "Dismiss" };

            _ = firstDialog.ShowAsync();
            MeasureWindow(window);

            Action showSecondDialog = () => { _ = secondDialog.ShowAsync(); };
            var ex = Record.Exception(showSecondDialog);
            ex = Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("Only one ContentDialog", ex.Message);

            firstDialog.OnHostWindowClosed();
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void MultipleInPlaceDialogs_ShouldCoexist()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var root = new Grid();
            var dialog1 = new ContentDialog
            {
                Title = "Dialog 1",
                PrimaryButtonText = "OK"
            };
            var dialog2 = new ContentDialog
            {
                Title = "Dialog 2",
                CloseButtonText = "Cancel"
            };
            root.Children.Add(dialog1);
            root.Children.Add(dialog2);

            var window = CreateWindow(root);
            app.MainWindow = window;

            var task1 = dialog1.ShowAsync(ContentDialogPlacement.InPlace);
            MeasureWindow(window);
            ProcessUiQueue();

            Assert.False(task1.IsCompleted);
            Assert.Contains(dialog1, window.ActiveInPlaceDialogs);

            var task2 = dialog2.ShowAsync(ContentDialogPlacement.InPlace);
            MeasureWindow(window);
            ProcessUiQueue();

            Assert.False(task2.IsCompleted);
            Assert.Contains(dialog2, window.ActiveInPlaceDialogs);
            Assert.Equal(2, window.ActiveInPlaceDialogs.Count);

            dialog1.Hide();
            ProcessUiQueue();

            Assert.True(task1.IsCompletedSuccessfully);
            Assert.DoesNotContain(dialog1, window.ActiveInPlaceDialogs);
            Assert.Contains(dialog2, window.ActiveInPlaceDialogs);
            Assert.False(task2.IsCompleted);

            dialog2.Hide();
            ProcessUiQueue();

            Assert.True(task2.IsCompletedSuccessfully);
            Assert.Empty(window.ActiveInPlaceDialogs);
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    [Fact]
    public void ThemeTemplate_ShouldResolveStyle_TitleTemplate_AndFullSizeLayout()
    {
        ResetApplicationState();
        ResetInputState();
        var app = new Application();

        try
        {
            var window = CreateWindow(new Grid());
            app.MainWindow = window;

            var titleTemplate = new DataTemplate();
            titleTemplate.SetVisualTree(() => new TextBlock
            {
                Name = "TemplateTitleMarker",
                Text = "Templated Title"
            });

            var dialog = new ContentDialog
            {
                Title = "Dialog",
                TitleTemplate = titleTemplate,
                PrimaryButtonText = "OK",
                DefaultButton = ContentDialogButton.Primary,
                FullSizeDesired = true
            };

            _ = dialog.ShowAsync();
            MeasureWindow(window);
            ProcessUiQueue();

            Assert.NotNull(dialog.TryFindResource(typeof(ContentDialog)) as Style);
            Assert.NotNull(dialog.TryFindResource("ContentDialogAccentButtonStyle") as Style);

            var primaryButton = Assert.IsType<Button>(dialog.FindName("PART_PrimaryButton"));
            Assert.NotNull(primaryButton.Style);

            var dialogCard = Assert.IsType<Border>(dialog.FindName("PART_DialogCard"));
            Assert.Equal(HorizontalAlignment.Stretch, dialogCard.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Stretch, dialogCard.VerticalAlignment);

            var titleMarker = FindDescendant<TextBlock>(dialog, tb => tb.Name == "TemplateTitleMarker");
            Assert.NotNull(titleMarker);
            Assert.Equal("Templated Title", titleMarker!.Text);

            dialog.Hide();
            ProcessUiQueue();
        }
        finally
        {
            ResetApplicationState();
            ResetInputState();
        }
    }

    private static Window CreateWindow(UIElement content)
    {
        return new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 800,
            Height = 600,
            Content = content
        };
    }

    private static void MeasureWindow(Window window)
    {
        window.Measure(new Size(800, 600));
        window.Arrange(new Rect(0, 0, 800, 600));
    }

    private static void ProcessUiQueue()
    {
        Dispatcher.GetForCurrentThread().ProcessQueue();
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static void ResetInputState()
    {
        Keyboard.Initialize();
        Keyboard.ClearFocus();
        UIElement.ForceReleaseMouseCapture();
        Window.SetKeyStateProviderForTesting(null);
    }

    private static void InvokeMouseButtonDown(Window window, MouseButton button, int x, int y, int clickCount = 1)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        nint wParam = (nint)0x0001;
        nint lParam = PackPointToLParam(x, y);
        method!.Invoke(window, new object[] { button, wParam, lParam, clickCount });
    }

    private static void InvokeMouseButtonUp(Window window, MouseButton button, int x, int y)
    {
        var method = typeof(Window).GetMethod("OnMouseButtonUp", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        nint wParam = nint.Zero;
        nint lParam = PackPointToLParam(x, y);
        method!.Invoke(window, new object[] { button, wParam, lParam });
    }

    private static void InvokeKeyDown(Window window, Key key, nint lParam)
    {
        var method = typeof(Window).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(window, new object[] { (nint)(int)key, lParam });
    }

    private static nint PackPointToLParam(int x, int y)
    {
        int packed = (y << 16) | (x & 0xFFFF);
        return (nint)packed;
    }

    private static UIElement? InvokeHitTestElement(Window window, Point point)
    {
        var method = typeof(Window).GetMethod("HitTestElement", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(window, new object[] { point, "content-dialog-test-hit" }) as UIElement;
    }

    private static bool IsDescendantOf(UIElement? element, UIElement ancestor)
    {
        for (Visual? current = element; current != null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static T GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var value = field!.GetValue(instance);
        Assert.NotNull(value);
        return Assert.IsType<T>(value);
    }

    private static T? FindDescendant<T>(Visual root, Func<T, bool> predicate) where T : class
    {
        if (root is T match && predicate(match))
        {
            return match;
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
            {
                var result = FindDescendant(child, predicate);
                if (result != null)
                {
                    return result;
                }
            }
        }

        return null;
    }
}
