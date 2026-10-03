using System;
using System.Collections.Generic;
using System.Linq;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class IndexedPriorityQueueContractTest
{
    private sealed class Node(int priority) : IPriorityQueueNode<Node>
    {
        private readonly Dictionary<DefaultPriorityQueue<Node>, int> indices = new();
        internal int Priority = priority;
        public int PriorityQueueIndex(DefaultPriorityQueue<Node> queue) => indices.GetValueOrDefault(queue, -1);
        public void PriorityQueueIndex(DefaultPriorityQueue<Node> queue, int index) => indices[queue] = index;
        public override bool Equals(object other) => other is Node node && node.Priority == Priority;
        public override int GetHashCode() => Priority;
    }

    private static DefaultPriorityQueue<Node> Create(bool descending = false) => new(
        Comparer<Node>.Create((a, b) => descending ? b.Priority.CompareTo(a.Priority) : a.Priority.CompareTo(b.Priority)), 0);

    [Fact]
    public void PriorityChangesAndRemovalKeepIndependentMembershipInTwoQueues()
    {
        var ascending = Create();
        var descending = Create(true);
        Node[] nodes = [new(1), new(2), new(3)];
        foreach (Node node in nodes) { ascending.Offer(node); descending.Offer(node); }
        nodes[1].Priority = 0;
        ascending.PriorityChanged(nodes[1]);
        descending.PriorityChanged(nodes[1]);
        Assert.Same(nodes[1], ascending.Peek());
        Assert.Same(nodes[2], descending.Peek());
        Assert.True(ascending.Remove(nodes[1]));
        Assert.Equal(-1, nodes[1].PriorityQueueIndex(ascending));
        Assert.True(descending.Contains(nodes[1]));
        ascending.Clear();
        Assert.Same(nodes[2], descending.Poll());
        Assert.Same(nodes[0], descending.Poll());
        Assert.Same(nodes[1], descending.Poll());
        foreach (Node node in nodes)
        {
            Assert.Equal(-1, node.PriorityQueueIndex(ascending));
            Assert.Equal(-1, node.PriorityQueueIndex(descending));
        }
    }

    [Fact]
    public void StaleIndexCannotRemoveOrReprioritizeAnEqualReplacement()
    {
        var queue = Create();
        var stale = new Node(1);
        var replacement = new Node(1);
        queue.Offer(stale);
        queue.ClearIgnoringIndexes();
        queue.Offer(replacement);
        Assert.True(stale.Equals(replacement));
        Assert.False(queue.Contains(stale));
        Assert.False(queue.Remove(stale));
        queue.PriorityChanged(stale);
        Assert.Same(replacement, queue.Poll());
        Assert.Equal(-1, replacement.PriorityQueueIndex(queue));
    }

    [Fact]
    public void QueueRequiresReferenceNodesInsteadOfProvidingAGeneralLinearScanFallback()
    {
        var queue = new DefaultPriorityQueue<object>(Comparer<object>.Create((_, _) => 0), 0);
        Assert.Throws<ArgumentException>(() => queue.Offer(new object()));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void NegativeInitialCapacityUsesTheClrArgumentException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultPriorityQueue<Node>(Comparer<Node>.Default, -1));
    }

    [Fact]
    public void RandomPriorityChangesAndRemovalsMatchAnIndependentSortedReference()
    {
        var queue = Create();
        var alive = new List<Node>();
        var random = new Random(42);
        for (int i = 0; i < 256; ++i) { var node = new Node(i); alive.Add(node); queue.Offer(node); }
        for (int step = 0; step < 1024; ++step)
        {
            Node node = alive[random.Next(alive.Count)];
            if (step % 3 == 0)
            {
                Assert.True(queue.Remove(node));
                alive.RemoveAll(item => ReferenceEquals(item, node));
                Assert.Equal(-1, node.PriorityQueueIndex(queue));
                node = new Node(10000 + step);
                alive.Add(node);
                queue.Offer(node);
            }
            else { node.Priority = -10000 - step; queue.PriorityChanged(node); }
            Node expected = alive.MinBy(item => item.Priority);
            Assert.Same(expected, queue.Peek());
            Node[] snapshot = queue.ToArray();
            Assert.Equal(alive.Count, snapshot.Length);
            for (int index = 0; index < snapshot.Length; ++index)
                Assert.Equal(index, snapshot[index].PriorityQueueIndex(queue));
        }
        alive.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        foreach (Node expected in alive) Assert.Same(expected, queue.Poll());
        Assert.False(queue.TryDequeue(out _));
    }
}
