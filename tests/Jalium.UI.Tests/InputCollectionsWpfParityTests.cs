using System.Collections;
using System.Reflection;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class InputCollectionsWpfParityTests
{
    [Fact]
    public void TierOneSurface_MatchesWpfCollectionAndCorrectionListContracts()
    {
        var correctionList = typeof(ApplicationCommands).GetProperty(
            nameof(ApplicationCommands.CorrectionList),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.NotNull(correctionList);
        Assert.Equal(typeof(RoutedUICommand), correctionList!.PropertyType);
        Assert.NotNull(correctionList.GetMethod);
        Assert.Null(correctionList.SetMethod);

        AssertAddRangeContract(typeof(CommandBindingCollection));
        AssertAddRangeContract(typeof(InputBindingCollection));
        AssertAddRangeContract(typeof(InputGestureCollection));

        var seal = typeof(InputGestureCollection).GetMethod(
            nameof(InputGestureCollection.Seal),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        Assert.NotNull(seal);
        Assert.Equal(typeof(void), seal!.ReturnType);
    }

    [Fact]
    public void CorrectionList_IsStableOwnerTypedCommandWithoutDefaultGestures()
    {
        var command = ApplicationCommands.CorrectionList;

        Assert.Same(command, ApplicationCommands.CorrectionList);
        Assert.Equal("CorrectionList", command.Name);
        Assert.Equal("Correction List", command.Text);
        Assert.Equal(typeof(ApplicationCommands), command.OwnerType);
        Assert.Empty(command.InputGestures);
    }

    [Fact]
    public void CommandBindingAddRange_PreservesOrderAndPartiallyCommitsBeforeInvalidItem()
    {
        var prefix = new CommandBinding(ApplicationCommands.Copy);
        var first = new CommandBinding(ApplicationCommands.Cut);
        var second = new CommandBinding(ApplicationCommands.Paste);
        var collection = new CommandBindingCollection { prefix };

        collection.AddRange(new ArrayList { first, second });

        Assert.Equal(new[] { prefix, first, second }, collection);

        var acceptedBeforeFailure = new CommandBinding(ApplicationCommands.Undo);
        Assert.Throws<NotSupportedException>(() =>
            collection.AddRange(new ArrayList { acceptedBeforeFailure, null, new CommandBinding() }));
        Assert.Equal(new[] { prefix, first, second, acceptedBeforeFailure }, collection);

        var exception = Assert.Throws<ArgumentNullException>(() => collection.AddRange(null!));
        Assert.Equal("collection", exception.ParamName);
    }

    [Fact]
    public void InputBindingAddRange_PreservesOrderAndPartiallyCommitsBeforeInvalidItem()
    {
        var prefix = CreateBinding(ApplicationCommands.Copy, Key.C);
        var first = CreateBinding(ApplicationCommands.Cut, Key.X);
        var second = CreateBinding(ApplicationCommands.Paste, Key.V);
        var collection = new InputBindingCollection { prefix };

        collection.AddRange(new ArrayList { first, second });

        Assert.Equal(new[] { prefix, first, second }, collection);

        var acceptedBeforeFailure = CreateBinding(ApplicationCommands.Undo, Key.Z);
        Assert.Throws<NotSupportedException>(() =>
            collection.AddRange(new ArrayList { acceptedBeforeFailure, "not an input binding", second }));
        Assert.Equal(new[] { prefix, first, second, acceptedBeforeFailure }, collection);

        var exception = Assert.Throws<ArgumentNullException>(() => collection.AddRange(null!));
        Assert.Equal("collection", exception.ParamName);
    }

    [Fact]
    public void InputGestureAddRange_PreservesOrderAndPartiallyCommitsBeforeInvalidItem()
    {
        var prefix = new KeyGesture(Key.A, ModifierKeys.Control);
        var first = new KeyGesture(Key.B, ModifierKeys.Control);
        var second = new KeyGesture(Key.C, ModifierKeys.Control);
        var collection = new InputGestureCollection { prefix };

        collection.AddRange(new ArrayList { first, second });

        Assert.Equal(new InputGesture[] { prefix, first, second }, collection);

        var acceptedBeforeFailure = new KeyGesture(Key.D, ModifierKeys.Control);
        Assert.Throws<NotSupportedException>(() =>
            collection.AddRange(new ArrayList { acceptedBeforeFailure, new object(), second }));
        Assert.Equal(new InputGesture[] { prefix, first, second, acceptedBeforeFailure }, collection);

        var exception = Assert.Throws<ArgumentNullException>(() => collection.AddRange(null!));
        Assert.Equal("collection", exception.ParamName);
    }

    [Fact]
    public void InputGestureSeal_IsIdempotentAndRejectsEveryMutationPath()
    {
        var original = new KeyGesture(Key.A, ModifierKeys.Control);
        var replacement = new KeyGesture(Key.B, ModifierKeys.Control);
        var collection = new InputGestureCollection { original };
        var list = (IList)collection;

        collection.Seal();
        collection.Seal();

        Assert.True(collection.IsReadOnly);
        Assert.True(list.IsReadOnly);
        Assert.True(list.IsFixedSize);
        Assert.Same(original, collection[0]);

        Assert.Throws<NotSupportedException>(() => collection[0] = replacement);
        Assert.Throws<NotSupportedException>(() => collection.Add(replacement));
        Assert.Throws<NotSupportedException>(() => collection.AddRange(null!));
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Throws<NotSupportedException>(() => collection.Insert(0, replacement));
        Assert.Throws<NotSupportedException>(() => collection.Remove(original));
        Assert.Throws<NotSupportedException>(() => collection.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => list[0] = replacement);
        Assert.Throws<NotSupportedException>(() => list.Add(replacement));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list.Insert(0, replacement));
        Assert.Throws<NotSupportedException>(() => list.Remove(original));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));

        Assert.Single(collection);
        Assert.Same(original, collection[0]);
    }

    private static InputBinding CreateBinding(RoutedUICommand command, Key key)
    {
        return new KeyBinding(command, key, ModifierKeys.Control);
    }

    private static void AssertAddRangeContract(Type collectionType)
    {
        var addRange = collectionType.GetMethod(
            "AddRange",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: new[] { typeof(ICollection) },
            modifiers: null);

        Assert.NotNull(addRange);
        Assert.Equal(typeof(void), addRange!.ReturnType);
        Assert.Equal("collection", Assert.Single(addRange.GetParameters()).Name);
    }
}
