using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Styling;

internal sealed record CssGeneratedContentValue(string? Text, string? Alternative = null, bool IsNone = false,
    CssContentSequence? Sequence = null, CssContentSequence? AlternativeSequence = null)
{
    internal static readonly CssGeneratedContentValue Normal = new(Text: null);
    internal static readonly CssGeneratedContentValue None = new(Text: null, IsNone: true);
}

/// <summary>Generated boxes have a style parent, but never become document children.</summary>
internal partial class CssGeneratedText : TextBlock
{
    internal CssNode Origin { get; }
    internal CssPseudoElement Kind { get; }
    internal string? Alternative { get; private set; }
    internal string? SpokenText { get; set; }
    internal Action? ProjectionInvalidated { get; set; }
    private bool _selected;
    private bool _hasContent;
    internal bool Selected => _selected;

    static CssGeneratedText()
    {
        VisibilityProperty.OverrideMetadata(typeof(CssGeneratedText), new PropertyMetadata(Visibility.Visible, null,
            static (target, value) => target is CssGeneratedText { _selected: true, _hasContent: true } ? value : Visibility.Collapsed));
        CssDisplayLayout.InlineProperty.OverrideMetadata(typeof(CssGeneratedText), new PropertyMetadata(true));
        CssGeneratedContent.ContentProperty.OverrideMetadata(typeof(CssGeneratedText),
            new PropertyMetadata(CssGeneratedContentValue.None, null, static (target, value) =>
                target is CssGeneratedText marker && marker.Kind.IsMarker()
                    ? CssListProperties.IsItem(marker.Origin) ? value : CssGeneratedContentValue.None
                    : value is CssGeneratedContentValue { Text: null, IsNone: false } ? CssGeneratedContentValue.None : value));
    }

    internal CssGeneratedText(CssNode origin, CssPseudoElement kind)
    {
        Origin = origin; Kind = kind; CssInlineOuter = true; Focusable = false;
        CoerceValue(VisibilityProperty);
    }

    internal void Select(bool selected)
    {
        if (_selected == selected) return;
        _selected = selected;
        CoerceValue(VisibilityProperty);
        InvalidateOrigin();
    }

    internal void SetContent(CssGeneratedContentValue value)
    {
        ClearAutomaticImage();
        SpokenText = null;
        _hasContent = value.Text is not null || Kind.IsMarker() && !value.IsNone;
        if (value.Sequence is null && value.AlternativeSequence is null)
            SetResolvedContent(value.Text ?? string.Empty, value.Alternative);
        else CssCounters.Invalidate(Origin);
        CoerceValue(VisibilityProperty);
        InvalidateOrigin();
    }

    internal void SetResolvedContent(string text, string? alternative, CssResolvedContent[]? contents = null, string? spokenText = null)
    {
        SpokenText = spokenText;
        SetImageContents(contents);
        if (Text != text) Text = text;
        if (Alternative == alternative)
        {
            if (spokenText is not null) GetExistingAutomationPeer()?.InvalidatePeer();
            return;
        }
        Alternative = alternative;
        if (alternative is not null) AutomationProperties.SetName(this, alternative);
        else ClearValue(AutomationProperties.NameProperty);
    }

    internal void SetAutomaticMarker(string? text, string? spokenText = null)
    {
        _hasContent = text is not null;
        SetResolvedContent(text ?? string.Empty, null, spokenText: spokenText);
        CoerceValue(VisibilityProperty);
    }

    private void InvalidateOrigin()
    {
        if (Origin.Target is UIElement origin)
        {
            origin.InvalidateMeasure(); origin.InvalidateVisual();
        }
        else if (VisualParent is UIElement parent)
        {
            parent.InvalidateMeasure(); parent.InvalidateVisual();
        }
        ProjectionInvalidated?.Invoke();
    }

    internal override void OnFontResourcesChanged()
    {
        base.OnFontResourcesChanged();
        // Projected document markers can be measured before they are attached
        // to a visual parent. Their intrinsic width also belongs to the viewer.
        InvalidateOrigin();
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => new GeneratedTextPeer(this);

    private sealed class GeneratedTextPeer(CssGeneratedText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(TextBlock);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => owner.Alternative ?? owner.SpokenText ?? owner.Text;
        protected override bool IsControlElementCore() => owner.Alternative != string.Empty;
        protected override bool IsContentElementCore() => owner.Alternative != string.Empty;
    }

}

internal sealed class CssGeneratedMarker : CssGeneratedText
{
    static CssGeneratedMarker()
    {
        // Derived type initializers can run before the base initializer. Merge the
        // base coercion and content callback after its metadata has been registered.
        RuntimeHelpers.RunClassConstructor(typeof(CssGeneratedText).TypeHandle);
        CssGeneratedContent.ContentProperty.OverrideMetadata(typeof(CssGeneratedMarker),
            new PropertyMetadata(CssGeneratedContentValue.Normal));
    }
    internal CssGeneratedMarker(CssNode origin, CssPseudoElement kind = CssPseudoElement.Marker) : base(origin, kind) => SetContent(CssGeneratedContentValue.Normal);
}

internal static partial class CssGeneratedContent
{
    internal sealed class Slots
    {
        internal CssGeneratedText? Before, After, Marker;
        internal bool Updating;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, Slots> Elements = new();
    private static bool s_active;
    internal static DependencyProperty ContentProperty => ContentStorage.Property;
    private static class ContentStorage
    {
        internal static readonly DependencyProperty Property = DependencyProperty.RegisterAttached(
            "CssGeneratedContent", typeof(CssGeneratedContentValue), typeof(CssGeneratedContent),
            new PropertyMetadata(CssGeneratedContentValue.Normal, static (target, change) =>
            {
                if (target is CssGeneratedText text) text.SetContent((CssGeneratedContentValue)change.NewValue!);
                else if (target is FrameworkElement owner) CssReplacedContent.Set(owner, (CssGeneratedContentValue)change.NewValue!);
                else if (target is FrameworkContentElement content) CssReplacedContent.Set(content, (CssGeneratedContentValue)change.NewValue!);
            }));
    }

    internal static CssPseudoElement Kind(CssNode node) => CssSelection.IsProjection(node) ? CssPseudoElement.Selection : node.Target is CssGeneratedText text ? text.Kind :
        CssFirstLetter.Origin(node.Target) is not null ? CssPseudoElement.FirstLetter : CssFirstLine.Kind(node.Target);
    internal static CssNode? Origin(CssNode node) => CssSelection.Origin(node) ?? (node.Target is CssGeneratedText text ? text.Origin : CssFirstLetter.Origin(node.Target) ?? CssFirstLine.Origin(node.Target));
    internal static HitTestResult? ResolveHit(HitTestResult? hit)
    {
        if (hit is null) return null;
        if (hit.VisualHit is not Visual original) return hit;
        var visual = original;
        for (var depth = 0; depth < 64; depth++)
        {
            var owner = visual switch
            {
                ICssGeneratedContentBox generated => generated.ContentOwner,
                CssGeneratedText text => text.Origin.Target as Visual ?? text.VisualParent,
                ICssAnonymousBox anonymous => anonymous.Owner,
                ICssStyledBox styled => styled.StyleSource as Visual ?? (styled as Visual)?.InternalVisualParent,
                _ => null,
            };
            if (owner is null || ReferenceEquals(owner, visual)) break;
            visual = owner;
        }
        return ReferenceEquals(visual, hit.VisualHit) ? hit : HitTestResult.GetReusable(visual);
    }
    internal static Slots? Get(FrameworkElement owner) => s_active && Elements.TryGetValue(owner, out var slots) ? slots : null;
    internal static bool HasBoxes(FrameworkElement owner) => Get(owner) is { } slots &&
        (slots.Before is { Visibility: not Visibility.Collapsed } || slots.After is { Visibility: not Visibility.Collapsed } ||
         slots.Marker is { Visibility: not Visibility.Collapsed });

    internal static int VisualCount(FrameworkElement owner)
    {
        var slots = Get(owner); var count = 0;
        if (CssReplacedContent.IsActive(owner)) return 1 + (slots?.Marker is null ? 0 : 1);
        for (var i = 0; i < owner.NativeVisualChildrenCount; i++)
            if (owner.NativeGetVisualChild(i) is { } native && native is not CssGeneratedText) count++;
        return count + (slots?.Marker is null ? 0 : 1) + (slots?.Before is null ? 0 : 1) + (slots?.After is null ? 0 : 1);
    }

    internal static Visual? VisualChild(FrameworkElement owner, int index)
    {
        var slots = Get(owner);
        if (CssReplacedContent.Image(owner) is { } replacement)
        {
            if (slots?.Marker is { } listMarker) { if (index == 0) return listMarker; index--; }
            return index == 0 ? replacement : throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (slots?.Marker is { } marker) { if (index == 0) return marker; index--; }
        if (slots?.Before is { } before) { if (index == 0) return before; index--; }
        for (var i = 0; i < owner.NativeVisualChildrenCount; i++)
            if (owner.NativeGetVisualChild(i) is { } native && native is not CssGeneratedText)
            { if (index == 0) return native; index--; }
        if (index == 0 && slots?.After is { } after) return after;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    internal static void Register() => CssPropertyRegistry.Register(new()
    {
        Name = "content", Kind = CssPropertyKind.Longhand, StorageProperty = ContentProperty,
        Parse = static (ref CssTokenReader reader, CssCompileContext context) =>
        {
            var keyword = reader;
            if (keyword.TryReadIdent(out var name) && keyword.AtEnd &&
                (name.Equals("normal", StringComparison.OrdinalIgnoreCase) || name.Equals("none", StringComparison.OrdinalIgnoreCase)))
            {
                reader = keyword;
                return new CssImmediateValue(ContentProperty, name.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? CssGeneratedContentValue.None : CssGeneratedContentValue.Normal);
            }
            if (!CssContentSequence.Read(ref reader, false, out var sequence, context)) return null;
            CssContentSequence? alternative = null;
            if (reader.TryReadSlash() && !CssContentSequence.Read(ref reader, true, out alternative)) return null;
            return reader.AtEnd ? new CssImmediateValue(ContentProperty, new CssGeneratedContentValue(
                sequence.IsDynamic ? string.Empty : sequence.Literal,
                alternative is null ? null : alternative.IsDynamic ? string.Empty : alternative.Literal,
                Sequence: sequence.IsDynamic ? sequence : null,
                AlternativeSequence: alternative?.IsDynamic == true ? alternative : null)) : null;
        },
    });

    internal static void Update(CssNode origin, CssPseudoElement targets, int rulesSignature = 0)
    {
        if (CssFirstLine.IsProjection(origin)) return;
        if (Kind(origin) is CssPseudoElement.Before or CssPseudoElement.After && origin.Target is CssGeneratedText generatedHost)
        {
            var nestedKind = generatedHost.Kind == CssPseudoElement.Before ? CssPseudoElement.BeforeMarker : CssPseudoElement.AfterMarker;
            var nestedItem = CssListProperties.IsItem(origin);
            var nestedPotential = (targets & (CssPseudoElement.Marker | nestedKind)) != 0;
            if (!Elements.TryGetValue(generatedHost, out var nestedSlots))
            {
                if (!nestedItem && !nestedPotential) return;
                s_active = true;
                nestedSlots = Elements.GetValue(generatedHost, static _ => new());
                generatedHost.HasCssGeneratedChildren = true;
            }
            if (nestedSlots.Updating) return;
            nestedSlots.Updating = true;
            try
            {
                if (nestedSlots.Marker is null && (nestedItem || nestedPotential))
                {
                    nestedSlots.Marker = new CssGeneratedMarker(origin, nestedKind);
                    generatedHost.AttachCssGeneratedChild(nestedSlots.Marker);
                    generatedHost.InvalidateMeasure();
                }
                if (nestedSlots.Marker is { } nestedMarker)
                {
                    nestedMarker.Select(nestedItem);
                    nestedMarker.CoerceValue(ContentProperty);
                    CssEngine.EvaluateElement(nestedMarker);
                }
            }
            finally { nestedSlots.Updating = false; }
            return;
        }
        if (Kind(origin) == CssPseudoElement.None)
        {
            var signature = CssFirstLine.OriginSignature(origin, rulesSignature);
            CssFirstLetter.Update(origin, targets, signature);
            CssFirstLine.Update(origin, targets, signature);
            CssSelection.Update(origin, targets);
            CssPlaceholder.Update(origin, targets);
        }
        var rawTargets = targets;
        if ((targets & CssPseudoElement.BeforeMarker) != 0) targets |= CssPseudoElement.Before;
        if ((targets & CssPseudoElement.AfterMarker) != 0) targets |= CssPseudoElement.After;
        targets &= CssPseudoElement.Before | CssPseudoElement.After | CssPseudoElement.Marker;
        if (Kind(origin) == CssPseudoElement.None)
        {
            RefreshProjectedMarkers(origin);
            RecordProjectedTargets(origin, rawTargets);
        }
        if (Kind(origin) == CssPseudoElement.None && origin.Target is FrameworkElement replacementOwner) CssReplacedContent.Refresh(replacementOwner);
        if (Kind(origin) == CssPseudoElement.None && origin.Target is FrameworkElement hostOwner) hostOwner.OnCssContentHostUpdated();
        if (Kind(origin) != CssPseudoElement.None || origin.Target is not FrameworkElement panel || !panel.IsCssContentHost) return;
        UpdateHostSlots(origin, panel, rawTargets, allowMarker: true);
        panel.OnCssContentHostUpdated();
    }

    private static void UpdateHostSlots(CssNode origin, FrameworkElement panel, CssPseudoElement rawTargets, bool allowMarker)
    {
        var targets = rawTargets;
        if ((targets & CssPseudoElement.BeforeMarker) != 0) targets |= CssPseudoElement.Before;
        if ((targets & CssPseudoElement.AfterMarker) != 0) targets |= CssPseudoElement.After;
        targets &= CssPseudoElement.Before | CssPseudoElement.After | (allowMarker ? CssPseudoElement.Marker : CssPseudoElement.None);
        var listItem = allowMarker && CssListProperties.IsItem(origin);
        if (!Elements.TryGetValue(panel, out var slots))
        {
            if (targets == CssPseudoElement.None && !listItem) return;
            s_active = true;
            slots = Elements.GetValue(panel, static _ => new());
            if (panel is not Panel) panel.HasCssGeneratedChildren = true;
        }
        if (slots.Updating) return;
        slots.Updating = true;
        try
        {
            Refresh(ref slots.Before, CssPseudoElement.Before);
            Refresh(ref slots.After, CssPseudoElement.After);
            if (allowMarker) Refresh(ref slots.Marker, CssPseudoElement.Marker);
        }
        finally { slots.Updating = false; }

        void Refresh(ref CssGeneratedText? text, CssPseudoElement kind)
        {
            var potential = (targets & kind) != 0;
            var selected = kind == CssPseudoElement.Marker ? listItem : potential && !CssReplacedContent.IsActive(panel) && !CssReplacedContent.IsActive(origin.Target);
            if (text is null && (selected || potential))
            {
                text = kind == CssPseudoElement.Marker ? new CssGeneratedMarker(origin) : new CssGeneratedText(origin, kind);
                panel.AttachCssGeneratedChild(text);
                panel.InvalidateMeasure();
            }
            if (text is null) return;
            text.Select(selected);
            if (kind == CssPseudoElement.Marker) text.CoerceValue(ContentProperty);
            CssEngine.EvaluateElement(text);
        }
    }
}

/// <summary>Source-ordered formatting children, without changing a panel's public collection.</summary>
internal readonly struct CssPanelChildren(FrameworkElement owner, bool includeInactive, bool expandContents)
{
    public CssPanelChildren(FrameworkElement owner, bool includeInactive = false) : this(owner, includeInactive, true) { }
    private readonly UIElement[]? _projection = expandContents ? CssContentsLayout.Project(owner, includeInactive) : null;
    private readonly CssGeneratedContent.Slots? _generated = CssGeneratedContent.Get(owner);
    private CssGeneratedContent.Slots? Generated => _generated;
    private CssGeneratedText? Before => Generated?.Before is { } before && (includeInactive || before.Visibility != Visibility.Collapsed) ? before : null;
    private CssGeneratedText? After => Generated?.After is { } after && (includeInactive || after.Visibility != Visibility.Collapsed) ? after : null;
    private CssGeneratedText? Marker => Generated?.Marker is { } marker && (includeInactive ||
        marker.Visibility != Visibility.Collapsed && CssListProperties.Inside(owner)) ? marker : null;
    private int NativeCount
    {
        get
        {
            if (CssReplacedContent.IsActive(owner)) return 1;
            if (owner is Panel panel) return panel.Children.Count;
            var count = 0;
            for (var i = 0; i < owner.NativeVisualChildrenCount; i++)
                if (owner.NativeGetVisualChild(i) is UIElement and not CssGeneratedText) count++;
            return count;
        }
    }
    private UIElement NativeAt(int index)
    {
        if (CssReplacedContent.Image(owner) is { } image) return index == 0 ? image : throw new ArgumentOutOfRangeException(nameof(index));
        if (owner is Panel panel) return panel.Children[index];
        for (var i = 0; i < owner.NativeVisualChildrenCount; i++)
            if (owner.NativeGetVisualChild(i) is UIElement native && native is not CssGeneratedText && index-- == 0) return native;
        throw new ArgumentOutOfRangeException(nameof(index));
    }
    internal int Count => _projection?.Length ?? NativeCount + (Before is null ? 0 : 1) + (After is null ? 0 : 1) + (Marker is null ? 0 : 1);
    internal int IndexOf(UIElement element)
    {
        for (var i = 0; i < Count; i++) if (ReferenceEquals(this[i], element)) return i;
        return -1;
    }
    internal UIElement this[int index]
    {
        get
        {
            if (_projection is not null) return _projection[index];
            if (Marker is { } marker) { if (index == 0) return marker; index--; }
            if (Before is { } before) { if (index == 0) return before; index--; }
            if ((uint)index < (uint)NativeCount) return NativeAt(index);
            if (index == NativeCount && After is { } after) return after;
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
    internal Enumerator EnumerateStruct() => new(this);
    public Enumerator GetEnumerator() => new(this);
    internal IEnumerable<UIElement> Enumerate()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }
    internal struct Enumerator(CssPanelChildren children)
    {
        private int _index = -1;
        public readonly UIElement Current => children[_index];
        public bool MoveNext() => ++_index < children.Count;
        public readonly Enumerator GetEnumerator() => this;
    }
}
