using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class DependencySourceBindingLifetimeTests
{
    [Fact]
    public void LiveTargetKeepsItsBindingAndClearDetachesTheSource()
    {
        var source = new Node { Value = "first" };
        var target = new TextBlock();
        target.SetBinding(TextBlock.TextProperty, new Binding(nameof(Node.Value)) { Source = source });
        Collect();
        source.Value = "second";
        Assert.Equal("second", target.Text);
        Assert.Equal(1, Subscribers(source));
        target.ClearBinding(TextBlock.TextProperty);
        Assert.Equal(0, Subscribers(source));
        GC.KeepAlive(target);
    }

    [Fact]
    public void LongLivedDependencySourceDoesNotKeepDiscardedTargetAlive()
    {
        var source = new Node { Value = "first" };
        var retired = BindAndRelease(source, nameof(Node.Value));
        Collect();
        Assert.False(retired.TryGetTarget(out _));
        source.Value = "second";
        Assert.Equal(0, Subscribers(source));
        GC.KeepAlive(source);
    }

    [Fact]
    public void NestedDependencyPathReplacesObserversAndReleasesDiscardedTarget()
    {
        var oldChild = new Node { Value = "old" };
        var newChild = new Node { Value = "new" };
        var source = new Node { Child = oldChild };
        var retired = BindNestedAndRelease(source, newChild);
        Assert.Equal(0, Subscribers(oldChild));
        Collect();
        Assert.False(retired.TryGetTarget(out _));
        source.Value = "purge";
        newChild.Value = "purge";
        Assert.Equal(0, Subscribers(source));
        Assert.Equal(0, Subscribers(newChild));
        GC.KeepAlive(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TextBlock> BindAndRelease(Node source, string path)
    {
        var target = new TextBlock();
        target.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = source });
        Assert.Equal("first", target.Text);
        return new(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TextBlock> BindNestedAndRelease(Node source, Node child)
    {
        var target = new TextBlock();
        target.SetBinding(TextBlock.TextProperty, new Binding("Child.Value") { Source = source });
        Assert.Equal("old", target.Text);
        source.Child = child;
        Assert.Equal("new", target.Text);
        child.Value = "updated";
        Assert.Equal("updated", target.Text);
        return new(target);
    }

    private static int Subscribers(DependencyObject source)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var state = typeof(DependencyObject).GetField("_optionalState", flags)!.GetValue(source);
        return state is null ? 0 : (state.GetType().GetField("PropertyChanged", flags)!.GetValue(state)
            as Delegate)?.GetInvocationList().Length ?? 0;
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    private sealed class Node : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(Node), new PropertyMetadata(""));
        public static readonly DependencyProperty ChildProperty = DependencyProperty.Register(nameof(Child), typeof(Node), typeof(Node), new PropertyMetadata(null));
        public string Value { get => (string)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }
        public Node? Child { get => (Node?)GetValue(ChildProperty); set => SetValue(ChildProperty, value); }
    }
}
