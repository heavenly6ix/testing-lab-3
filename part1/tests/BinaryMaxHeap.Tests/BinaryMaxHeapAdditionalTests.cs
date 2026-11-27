using Heaps;
using NUnit.Framework;

namespace Part1.Heaps.Tests;

[TestFixture]
public class BinaryMaxHeapAdditionalTests
{
    private static void Invariant<T>(BinaryMaxHeap<T> heap, Comparer<T>? comparer = null) where T : IComparable<T>
    {
        comparer ??= Comparer<T>.Default;
        var a = heap.ToArray();
        Assert.That(heap.Count, Is.EqualTo(a.Length));
        for (int child = 1; child < a.Length; child++)
            Assert.That(comparer.Compare(a[(child - 1) / 2], a[child]), Is.GreaterThanOrEqualTo(0), $"parent of index {child}");
    }
    private static List<T> Drain<T>(BinaryMaxHeap<T> heap) where T : IComparable<T>
    {
        var result = new List<T>();
        while (!heap.IsEmpty) { result.Add(heap.ExtractMax()); Invariant(heap); }
        return result;
    }
    private static BinaryMaxHeap<int> Heap(params int[] values)
    {
        var heap = new BinaryMaxHeap<int>(); heap.Initialize(values); return heap;
    }

    [Test]
    public void Indexer_IncreaseBelowRoot_MovesAboveSmallerParent()
    {
        var heap = Heap(100, 50, 90, 20, 30, 80, 70);
        var before = heap.ToArray(); before[3] = 60;
        heap[3] = 60;
        Invariant(heap);
        Assert.That(heap.Peek(), Is.EqualTo(100));
        Assert.That(Drain(heap), Is.EqualTo(before.OrderDescending()));
    }

    [Test]
    public void Indexer_DecreaseInternalNode_MovesBelowLargerChildren()
    {
        var heap = Heap(100, 50, 90, 20, 30, 80, 70);
        var before = heap.ToArray(); before[1] = 10;
        heap[1] = 10;
        Invariant(heap);
        Assert.That(Drain(heap), Is.EqualTo(before.OrderDescending()));
    }

    [Test]
    public void Initialize_Null_ThrowsArgumentNullAndPreservesPreviousState()
    {
        var heap = Heap(5, 2);
        Assert.Throws<ArgumentNullException>(() => heap.Initialize(null!));
        Assert.That(Drain(heap), Is.EqualTo(new[] { 5, 2 }));
    }

    [Test]
    public void Initialize_RepeatedIncludingEmpty_AlwaysReplacesState()
    {
        var heap = Heap(9, 7, 7);
        foreach (var source in new[] { new[] { 2, 1 }, Array.Empty<int>(), new[] { 4, 4, 3 }, Array.Empty<int>() })
        {
            heap.Initialize(source);
            Invariant(heap);
            Assert.That(heap.ToArray(), Is.EquivalentTo(source));
        }
        heap.Add(8);
        Assert.That(heap.ExtractMax(), Is.EqualTo(8));
        Assert.That(heap.IsEmpty, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MixedOperations_AgreeWithMultisetAfterEveryStep(bool reversed)
    {
        var comparer = reversed ? Comparer<int>.Create((a, b) => b.CompareTo(a)) : Comparer<int>.Default;
        var heap = new BinaryMaxHeap<int>(comparer); var model = new List<int>();
        void Check()
        {
            Invariant(heap, comparer);
            Assert.That(heap.ToArray(), Is.EquivalentTo(model));
            Assert.That(heap.IsEmpty, Is.EqualTo(model.Count == 0));
            if (model.Count > 0) Assert.That(heap.Peek(), Is.EqualTo(model.OrderByDescending(x => x, comparer).First()));
        }
        void Add(int value) { heap.Add(value); model.Add(value); Check(); }
        void Replace(int index, int value)
        {
            model.Remove(heap[index]); model.Add(value); heap[index] = value; Check();
        }
        void Extract()
        {
            var expected = model.OrderByDescending(x => x, comparer).First();
            Assert.That(heap.ExtractMax(), Is.EqualTo(expected)); model.Remove(expected); Check();
        }
        foreach (int n in new[] { 100, 50, 90, 20, 30, 80, 70, 20 }) Add(n);
        Replace(3, reversed ? 95 : 60); // crosses parent but not the root
        Replace(1, reversed ? 200 : -5); // crosses children
        Extract(); Add(42); Extract(); Add(42);
        while (model.Count > 0) Extract();
        Add(7); Extract();
    }

    private sealed record Item(string Id, int Priority) : IComparable<Item>
    {
        public int CompareTo(Item? other) => other is null ? 1 : Priority.CompareTo(other.Priority);
    }

    [Test]
    public void EqualPriorityItems_AreAllPreservedWithoutAssumingStableOrder()
    {
        var source = new[] { new Item("one", 5), new Item("two", 5), new Item("three", 1), new Item("four", 5) };
        var heap = new BinaryMaxHeap<Item>();
        foreach (var item in source) { heap.Add(item); Invariant(heap); }
        var result = Drain(heap);
        Assert.That(result.Select(x => x.Priority), Is.EqualTo(new[] { 5, 5, 5, 1 }));
        Assert.That(result.Select(x => x.Id), Is.EquivalentTo(source.Select(x => x.Id)));
    }

    [Test]
    public void NullElements_DefaultReferenceComparerOrdersAndPreservesThem()
    {
        // Runtime permits null reference elements; Comparer<string>.Default sorts null below strings.
        var heap = new BinaryMaxHeap<string>();
        heap.Initialize(new[] { "b", null!, "a", null! });
        heap.Add(null!); Invariant(heap);
        Assert.That(Drain(heap), Is.EqualTo(new[] { "b", "a", null, null, null }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Union_AliasedInputs_CurrentDestructivePolicyConsumesObjectOnce(bool sameRefVariable)
    {
        var first = Heap(5, 5, 1); var second = first; var retainedAlias = first;
        var caller = new BinaryMaxHeap<int>();
        var result = sameRefVariable ? caller.Union(ref first, ref first) : caller.Union(ref first, ref second);
        Assert.That(first, Is.Null);
        if (!sameRefVariable) Assert.That(second, Is.Null);
        Assert.That(retainedAlias.IsEmpty, Is.True);
        Assert.That(Drain(result), Is.EqualTo(new[] { 5, 5, 1 }));
    }

    [Test]
    public void Union_CustomComparers_CurrentPolicyUsesDefaultInResult()
    {
        var reversed = Comparer<int>.Create((a, b) => b.CompareTo(a));
        var first = new BinaryMaxHeap<int>(reversed); first.Initialize(new[] { 8, 2 });
        var second = new BinaryMaxHeap<int>(reversed); second.Initialize(new[] { 7, 1, 2 });
        var result = new BinaryMaxHeap<int>(reversed).Union(ref first, ref second);
        Assert.That(first, Is.Null); Assert.That(second, Is.Null);
        Invariant(result); // Default comparer, not the reversed input comparer.
        Assert.That(Drain(result), Is.EqualTo(new[] { 8, 7, 2, 2, 1 }));
    }
}
