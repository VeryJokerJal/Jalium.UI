using System.Text;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls.TextEffects;

/// <summary>
/// UI Automation peer for <see cref="TextEffectPresenter"/>. Surfaces the fully-laid-out
/// text to screen readers by concatenating every live cell's grapheme, so the animation
/// does not obscure the content from assistive tech.
/// </summary>
public sealed class TextEffectPresenterAutomationPeer : FrameworkElementAutomationPeer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextEffectPresenterAutomationPeer"/> class.
    /// </summary>
    public TextEffectPresenterAutomationPeer(TextEffectPresenter owner) : base(owner) { }

    private TextEffectPresenter PresenterOwner => (TextEffectPresenter)Owner;

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Text;

    /// <inheritdoc />
    protected override string GetClassNameCore() => nameof(TextEffectPresenter);

    /// <inheritdoc />
    protected override string GetNameCore()
    {
        var cells = PresenterOwner.Cells;
        if (cells.Count == 0)
        {
            return base.GetNameCore();
        }

        var sb = new StringBuilder(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            sb.Append(cells[i].Text);
        }
        return sb.ToString();
    }
}
