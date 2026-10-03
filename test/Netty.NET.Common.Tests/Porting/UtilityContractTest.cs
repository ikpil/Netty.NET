using System;
using System.Collections.Generic;
using System.IO;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class UtilityContractTest
{
    [Fact]
    public void ObjectChecksDistinguishRootFromDeepNulls()
    {
        IEnumerable<object> values = new object[] { null };
        Assert.Same(values, ObjectUtil.CheckNotNull(values, "values"));
        Assert.Throws<ArgumentNullException>(() => ObjectUtil.DeepCheckNotNull("values", new object[] { null }));
        ICollection<object> collection = new List<object> { null };
        Assert.Same(collection, ObjectUtil.CheckNonEmpty(collection, "collection"));
        Assert.Throws<ArgumentException>(() => ObjectUtil.CheckNonEmpty(new List<object>(), "collection"));
        Assert.Equal("\u00a0", ObjectUtil.CheckNonEmptyAfterTrim(" \u00a0 ", "value"));
    }

    [Fact]
    public void BoundedStreamPreservesEofAndClosesUnderlyingStream()
    {
        var inner = new MemoryStream(new byte[] { 1 });
        using (var bounded = new BoundedInputStream(inner))
        {
            Assert.Equal(1, bounded.ReadByte());
            Assert.Equal(-1, bounded.ReadByte());
            Assert.Equal(0, bounded.Read(new byte[4], 0, 4));
        }
        Assert.False(inner.CanRead);
        Assert.Throws<ArgumentException>(() => new BoundedInputStream(new MemoryStream(), 0));
    }

    [Fact]
    public void EmptyPriorityQueueNeverAcceptsElements()
    {
        var queue = EmptyPriorityQueue<object>.Instance();
        Assert.False(queue.TryEnqueue(new object()));
        Assert.False(queue.TryDequeue(out var item));
        Assert.Null(item);
        Assert.Empty(queue.ToArray());
        Assert.Empty(queue);
    }

    [Fact]
    public void PriorityQueueHandlesValueTypeEntriesAndNonzeroCapacity()
    {
        var queue = new PriorityQueue<int, int>(4);
        foreach (int value in new[] { 4, 0, 2, 1 }) queue.Enqueue(value, value);
        foreach (int expected in new[] { 0, 1, 2, 4 })
        {
            Assert.True(queue.TryDequeue(out var value, out _));
            Assert.Equal(expected, value);
        }
        Assert.False(queue.TryDequeue(out _, out _));
        queue.Enqueue(5, 5);
        queue.Clear();
        Assert.Empty(queue.UnorderedItems);
    }
}
