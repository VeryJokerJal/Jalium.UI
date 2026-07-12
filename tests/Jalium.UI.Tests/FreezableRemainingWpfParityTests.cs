using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class FreezableRemainingWpfParityTests
{
    private static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
        "FreezableChild",
        typeof(ProbeFreezable),
        typeof(ProbeFreezable),
        new PropertyMetadata(null));

    [Fact]
    public void RemainingHooksHaveWpfVisibilityAndVirtualShape()
    {
        var freeze = typeof(Freezable).GetMethod(
            "Freeze",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            [typeof(Freezable), typeof(bool)],
            null)!;
        Assert.True(freeze.IsFamilyOrAssembly);

        var changed = typeof(Freezable).GetMethod(
            "OnChanged",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(changed.IsFamily);
        Assert.True(changed.IsVirtual);

        Assert.NotNull(typeof(Freezable).GetMethod(
            "ReadPreamble",
            BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(typeof(Freezable).GetMethod(
            "OnFreezablePropertyChanged",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(DependencyObject), typeof(DependencyObject), typeof(DependencyProperty)],
            null));
    }

    [Fact]
    public void StaticFreezeSupportsCheckAndCommitPhases()
    {
        var freezable = new ProbeFreezable();

        Assert.True(Freezable.Freeze(freezable, isChecking: true));
        Assert.False(freezable.IsFrozen);
        Assert.True(Freezable.Freeze(freezable, isChecking: false));
        Assert.True(freezable.IsFrozen);
        Assert.Equal(new[] { true, false }, freezable.FreezeChecks);
    }

    [Fact]
    public void PropertyOverloadPropagatesChildChangesAndOnChangedIsOverridable()
    {
        var parent = new ProbeFreezable();
        var child = new ProbeFreezable();
        parent.AttachChild(child);

        child.SignalChanged();

        Assert.Equal(1, parent.ChangeCount);
        parent.Read();
    }

    private sealed class ProbeFreezable : Freezable
    {
        public List<bool> FreezeChecks { get; } = new();
        public int ChangeCount { get; private set; }

        public void AttachChild(ProbeFreezable child) =>
            OnFreezablePropertyChanged(null, child, ChildProperty);

        public void SignalChanged() => WritePostscript();
        public void Read() => ReadPreamble();

        protected override Freezable CreateInstanceCore() => new ProbeFreezable();

        protected override bool FreezeCore(bool isChecking)
        {
            FreezeChecks.Add(isChecking);
            return true;
        }

        protected override void OnChanged()
        {
            ChangeCount++;
            base.OnChanged();
        }
    }
}
