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
        public int priorityQueueIndex(DefaultPriorityQueue<Node> queue) => indices.GetValueOrDefault(queue, -1);
        public void priorityQueueIndex(DefaultPriorityQueue<Node> queue, int index) => indices[queue] = index;
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
        foreach (Node node in nodes) { ascending.offer(node); descending.offer(node); }
        nodes[1].Priority = 0;
        ascending.priorityChanged(nodes[1]);
        descending.priorityChanged(nodes[1]);
        Assert.Same(nodes[1], ascending.peek());
        Assert.Same(nodes[2], descending.peek());
        Assert.True(ascending.remove(nodes[1]));
        Assert.Equal(-1, nodes[1].priorityQueueIndex(ascending));
        Assert.True(descending.contains(nodes[1]));
        ascending.clear();
        Assert.Same(nodes[2], descending.poll());
        Assert.Same(nodes[0], descending.poll());
        Assert.Same(nodes[1], descending.poll());
        foreach (Node node in nodes)
        {
            Assert.Equal(-1, node.priorityQueueIndex(ascending));
            Assert.Equal(-1, node.priorityQueueIndex(descending));
        }
    }

    [Fact]
    public void StaleIndexCannotRemoveOrReprioritizeAnEqualReplacement()
    {
        var queue = Create();
        var stale = new Node(1);
        var replacement = new Node(1);
        queue.offer(stale);
        queue.clearIgnoringIndexes();
        queue.offer(replacement);
        Assert.True(stale.Equals(replacement));
        Assert.False(queue.contains(stale));
        Assert.False(queue.remove(stale));
        queue.priorityChanged(stale);
        Assert.Same(replacement, queue.poll());
        Assert.Equal(-1, replacement.priorityQueueIndex(queue));
    }

    [Fact]
    public void QueueRequiresReferenceNodesInsteadOfProvidingAGeneralLinearScanFallback()
    {
        var queue = new DefaultPriorityQueue<object>(Comparer<object>.Create((_, _) => 0), 0);
        Assert.Throws<ArgumentException>(() => queue.offer(new object()));
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
        for (int i = 0; i < 256; ++i) { var node = new Node(i); alive.Add(node); queue.offer(node); }
        for (int step = 0; step < 1024; ++step)
        {
            Node node = alive[random.Next(alive.Count)];
            if (step % 3 == 0)
            {
                Assert.True(queue.remove(node));
                alive.RemoveAll(item => ReferenceEquals(item, node));
                Assert.Equal(-1, node.priorityQueueIndex(queue));
                node = new Node(10000 + step);
                alive.Add(node);
                queue.offer(node);
            }
            else { node.Priority = -10000 - step; queue.priorityChanged(node); }
            Node expected = alive.MinBy(item => item.Priority);
            Assert.Same(expected, queue.peek());
            Node[] snapshot = queue.toArray();
            Assert.Equal(alive.Count, snapshot.Length);
            for (int index = 0; index < snapshot.Length; ++index)
                Assert.Equal(index, snapshot[index].priorityQueueIndex(queue));
        }
        alive.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        foreach (Node expected in alive) Assert.Same(expected, queue.poll());
        Assert.False(queue.tryDequeue(out _));
    }
}
