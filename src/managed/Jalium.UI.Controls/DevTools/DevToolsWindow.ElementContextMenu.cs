using System.IO;
using System.Text;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    // Reused between Inspector and Logical tabs — a single ContextMenu instance
    // cannot be hosted by two different PlacementTargets simultaneously, so we
    // build a fresh one per open.
    private void OpenElementContextMenu(Visual target, UIElement placementTarget)
    {
        if (target == null) return;

        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = Jalium.UI.Controls.Primitives.PlacementMode.MousePoint,
            StaysOpen = false,
        };

        // Everything is dispatched on a subsequent dispatcher turn so the popup
        // has fully closed (and its HWND released) before we do anything that
        // interacts with other top-level windows — SaveFileDialog modal pumps,
        // Clipboard APIs, and screenshot PrintWindow calls have all been
        // observed to no-op when fired during the Click bubble that is also
        // tearing down the ContextMenu popup.
        menu.Items.Add(MakeMenuItem("Reveal in Inspector", "→",
            () => RevealInInspector(target)));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Export element as XAML…", "⇣",
            () => Defer(() => ExportVisualAsXaml(target, recurse: false))));
        menu.Items.Add(MakeMenuItem("Export subtree as XAML…", "⇓",
            () => Defer(() => ExportVisualAsXaml(target, recurse: true))));
        menu.Items.Add(MakeMenuItem("Copy XAML to clipboard", "⧉",
            () => Defer(() => CopyVisualXamlToClipboard(target))));

        menu.Items.Add(new Separator());

        menu.Items.Add(MakeMenuItem("Save element screenshot…", "▢",
            () => Defer(() => SaveElementScreenshot(target))));
        menu.Items.Add(MakeMenuItem("Save whole window screenshot…", "◱",
            () => Defer(() => SaveWholeWindowScreenshot())));

        menu.Items.Add(new Separator());

        // Delete removes the element from the live tree; it is offered always but
        // disabled (with an explanatory tooltip) when the parent container has no
        // safe removal API. Undo only appears when there is something to restore.
        bool canDelete = CanDeleteElement(target, out var deleteReason);
        var deleteItem = MakeMenuItem("Delete element", "✕",
            () => Defer(() => DeleteElement(target)));
        if (!canDelete)
        {
            deleteItem.IsEnabled = false;
            deleteItem.ToolTip = deleteReason;
        }
        menu.Items.Add(deleteItem);

        if (_deleteRecord != null)
        {
            menu.Items.Add(MakeMenuItem($"Undo delete ({_deleteRecord.Label})", "↩",
                () => Defer(() => UndoDelete())));
        }

        // MousePoint placement pops the menu up at the current cursor location, which
        // is exactly where the right-click happened.
        menu.IsOpen = true;
    }

    /// <summary>
    /// Schedules <paramref name="action"/> for the next dispatcher turn. Used by
    /// context-menu actions that spawn a modal file dialog or touch the clipboard —
    /// running synchronously inside the MenuItem.Click bubble would race the popup
    /// teardown and make the action appear to do nothing.
    /// </summary>
    private void Defer(Action action)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try { action(); }
            catch (Exception ex)
            {
                // Surface the failure in the Tools tab status line so the user has
                // some feedback even when an export silently fails.
                SetToolStatus(_exportStatusText, $"Action failed: {ex.Message}", isError: true);
                SetToolStatus(_screenshotStatusText, $"Action failed: {ex.Message}", isError: true);
            }
        });
    }

    private static MenuItem MakeMenuItem(string header, string glyph, Action onClick)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = DevToolsTheme.FontSm,
            FontFamily = DevToolsTheme.UiFont,
            Foreground = DevToolsTheme.Accent,
            Width = 16,
            TextAlignment = TextAlignment.Center,
        };
        var mi = new MenuItem
        {
            Header = header,
            Icon = icon,
        };
        mi.Click += (_, _) => onClick();
        return mi;
    }

    // ── XAML export ──────────────────────────────────────────────────────

    private void ExportVisualAsXaml(Visual visual, bool recurse)
    {
        try
        {
            string xaml = BuildXamlFromVisual(visual, recurse, 0);

            var dialog = new SaveFileDialog
            {
                Title = recurse ? "Export subtree as XAML" : "Export element as XAML",
                Filter = "Jalium XAML (*.jalxaml)|*.jalxaml|XAML (*.xaml)|*.xaml|All files (*.*)|*.*",
                DefaultExt = "jalxaml",
                FileName = $"{visual.GetType().Name}-{DateTime.Now:yyyyMMdd-HHmmss}.jalxaml",
            };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName!, xaml, Encoding.UTF8);
            SetToolStatus(_exportStatusText, $"Saved XAML to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus(_exportStatusText, $"Export failed: {ex.Message}", isError: true);
        }
    }

    private void CopyVisualXamlToClipboard(Visual visual)
    {
        try
        {
            string xaml = BuildXamlFromVisual(visual, recurse: true, 0);
            Clipboard.SetText(xaml);
            SetToolStatus(_exportStatusText, "XAML copied to clipboard.");
        }
        catch (Exception ex)
        {
            SetToolStatus(_exportStatusText, $"Copy failed: {ex.Message}", isError: true);
        }
    }

    // ── Screenshot ───────────────────────────────────────────────────────

    private void SaveWholeWindowScreenshot()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                SetToolStatus(_screenshotStatusText, "Screenshot is Windows-only for now.", isError: true);
                return;
            }

            var hwnd = _targetWindow.Handle;
            if (hwnd == nint.Zero)
            {
                SetToolStatus(_screenshotStatusText, "Target window has no HWND yet.", isError: true);
                return;
            }

            if (!GetClientRect(hwnd, out var rect))
            {
                SetToolStatus(_screenshotStatusText, "GetClientRect failed.", isError: true);
                return;
            }
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0)
            {
                SetToolStatus(_screenshotStatusText, "Window has zero size.", isError: true);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save window screenshot",
                Filter = "PNG (*.png)|*.png|All files (*.*)|*.*",
                DefaultExt = "png",
                FileName = $"{_targetWindow.Title}-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            };
            if (dialog.ShowDialog() != true) return;

            byte[] pixels = CaptureHwndPixels(hwnd, w, h);
            WritePngFromBgra(dialog.FileName!, pixels, w, h);
            SetToolStatus(_screenshotStatusText, $"Saved to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus(_screenshotStatusText, $"Screenshot failed: {ex.Message}", isError: true);
        }
    }

    private void SaveElementScreenshot(Visual visual)
    {
        if (visual is not UIElement ui)
        {
            SetToolStatus(_screenshotStatusText, "Selected visual is not a UIElement.", isError: true);
            return;
        }
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                SetToolStatus(_screenshotStatusText, "Screenshot is Windows-only for now.", isError: true);
                return;
            }

            var hwnd = _targetWindow.Handle;
            if (hwnd == nint.Zero) return;
            GetClientRect(hwnd, out var rect);
            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0) return;

            var bounds = ui.VisualBounds;
            Visual? cur = ui.VisualParent;
            double offX = bounds.X, offY = bounds.Y;
            while (cur is UIElement uiCur)
            {
                var b = uiCur.VisualBounds;
                offX += b.X;
                offY += b.Y;
                cur = uiCur.VisualParent;
            }
            int x0 = Math.Max(0, (int)offX);
            int y0 = Math.Max(0, (int)offY);
            int x1 = Math.Min(w, (int)(offX + bounds.Width));
            int y1 = Math.Min(h, (int)(offY + bounds.Height));
            int cw = Math.Max(1, x1 - x0);
            int ch = Math.Max(1, y1 - y0);

            var dialog = new SaveFileDialog
            {
                Title = "Save element screenshot",
                Filter = "PNG (*.png)|*.png|All files (*.*)|*.*",
                DefaultExt = "png",
                FileName = $"{visual.GetType().Name}-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            };
            if (dialog.ShowDialog() != true) return;

            byte[] full = CaptureHwndPixels(hwnd, w, h);
            byte[] cropped = new byte[cw * ch * 4];
            for (int y = 0; y < ch; y++)
            {
                int srcOffset = ((y + y0) * w + x0) * 4;
                int dstOffset = y * cw * 4;
                Buffer.BlockCopy(full, srcOffset, cropped, dstOffset, cw * 4);
            }

            WritePngFromBgra(dialog.FileName!, cropped, cw, ch);
            SetToolStatus(_screenshotStatusText, $"Saved element crop to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            SetToolStatus(_screenshotStatusText, $"Screenshot failed: {ex.Message}", isError: true);
        }
    }

    private static void SetToolStatus(TextBlock? target, string message, bool isError = false)
    {
        if (target == null) return;
        target.Text = message;
        target.Foreground = isError ? DevToolsTheme.Error : DevToolsTheme.TextSecondary;
    }
}
