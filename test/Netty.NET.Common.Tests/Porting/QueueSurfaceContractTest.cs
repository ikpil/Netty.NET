using System;
using System.Collections.Generic;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class QueueSurfaceContractTest
{
    [Fact]
    public void SharedMembershipQueuesDoNotExposeAnUnusedMessagePassingConsumerSurface()
    {
        Type[] types = [typeof(IQueue<>), typeof(LinkedBlockingQueue<>),
            typeof(DefaultPriorityQueue<>), typeof(EmptyPriorityQueue<>)];
        foreach (Type type in types) Assert.Null(type.GetMethod("Drain"));
        var assembly = typeof(IQueue<>).Assembly;
        Assert.Null(assembly.GetType("Netty.NET.Common.Functional.IConsumer`1"));
        Assert.Null(assembly.GetType("Netty.NET.Common.Collections.BlockingMessageQueue`1"));
    }

    private sealed class Receiver
    {
        internal readonly List<int> Calls = new();
        internal void First() => Calls.Add(1);
        internal void Second() => Calls.Add(2);
        internal void Third() => Calls.Add(3);
    }

    [Fact]
    public void NativeCallbacksRetainBoundedAdmissionFirstRemovalAndFifoIdentity()
    {
        var receiver = new Receiver();
        Action first = receiver.First, equivalentFirst = receiver.First;
        Action second = receiver.Second, third = receiver.Third;
        var queue = new LinkedBlockingQueue<Action>(3);
        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));
        Assert.True(queue.TryEnqueue(first));
        Assert.False(queue.TryEnqueue(third));
        Assert.Equal(3, queue.Count);
        Assert.True(queue.TryRemove(equivalentFirst));
        Assert.True(queue.TryEnqueue(third));
        Assert.Same(second, queue.Take());
        second();
        Assert.True(queue.TryDequeue(out Action retained));
        Assert.Same(first, retained);
        retained();
        Assert.Same(third, queue.Take());
        third();
        Assert.Equal(new[] { 2, 1, 3 }, receiver.Calls);
        Assert.False(queue.TryDequeue(out _));
        Assert.True(queue.IsEmpty());
    }

    [Fact]
    public void EmptyPriorityQueueUsesExplicitAbsenceForClrValueTypes()
    {
        IPriorityQueue<int> queue = EmptyPriorityQueue<int>.Instance();
        Assert.Same(queue, EmptyPriorityQueue<int>.Instance());
        Assert.False(queue.TryEnqueue(0));
        Assert.False(queue.TryPeek(out int peeked));
        Assert.Equal(0, peeked);
        Assert.False(queue.TryDequeue(out int removed));
        Assert.Equal(0, removed);
        Assert.False(queue.TryRemove(0));
        Assert.False(queue.Remove(0));
        Assert.False(queue.Contains(0));
        queue.PriorityChanged(0);
        queue.Clear();
        queue.ClearIgnoringIndexes();
        Assert.Equal(0, queue.Count);
        Assert.True(queue.IsEmpty());
        Assert.Same(Array.Empty<int>(), queue.ToArray());
        Assert.Empty(queue);
        Assert.Equal("EmptyPriorityQueue", queue.ToString());
        foreach (string method in new[] { "Size", "Offer", "Poll", "Peek", "Element" })
            Assert.Null(queue.GetType().GetMethod(method));
        Assert.Null(queue.GetType().GetMethod("Remove", Type.EmptyTypes));
    }

    [Fact]
    public void EmptyAndMutablePriorityQueuesRetainSymmetricIdentityAsDictionaryKeys()
    {
        var empty = EmptyPriorityQueue<Node>.Instance();
        var mutable = new DefaultPriorityQueue<Node>(Comparer<Node>.Create((_, _) => 0), 0);
        Assert.False(empty.Equals(mutable));
        Assert.False(mutable.Equals(empty));
        Assert.Same(empty, EmptyPriorityQueue<Node>.Instance());
        Assert.NotSame((object)empty, EmptyPriorityQueue<int>.Instance());
        var entries = new Dictionary<IPriorityQueue<Node>, string> { [empty] = "disabled", [mutable] = "enabled" };
        var node = new Node();
        Assert.True(mutable.TryEnqueue(node));
        int index = node.Index;
        empty.PriorityChanged(node);
        Assert.False(empty.Remove(node));
        Assert.False(empty.Contains(node));
        Assert.Equal(index, node.Index);
        mutable.Clear();
        Assert.Equal("disabled", entries[empty]);
        Assert.Equal("enabled", entries[mutable]);
        Assert.Equal(2, entries.Count);
    }

    private sealed class Node : IPriorityQueueNode<Node>
    {
        internal int Index = INDEX_NOT_IN_QUEUE;
        private const int INDEX_NOT_IN_QUEUE = -1;
        public int PriorityQueueIndex(DefaultPriorityQueue<Node> queue) => Index;
        public void PriorityQueueIndex(DefaultPriorityQueue<Node> queue, int index) => Index = index;
    }
}
