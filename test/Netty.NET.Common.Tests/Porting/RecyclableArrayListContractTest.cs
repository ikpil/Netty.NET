using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Tests.Porting;

public class RecyclableArrayListContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32)]
    public void FactoriesProvideAnEmptyNativeList(int capacity)
    {
        var list = RecyclableArrayList.NewInstance(capacity);
        try
        {
            Assert.IsAssignableFrom<IList<object>>(list);
            Assert.IsAssignableFrom<IList>(list);
            Assert.Empty(list);
            Assert.False(list.InsertSinceRecycled);
        }
        finally { list.Recycle(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EmptyRangesLeaveTheInsertionHistoryUnchanged(bool indexed, bool previouslyInserted)
    {
        var list = RecyclableArrayList.NewInstance();
        try
        {
            if (previouslyInserted) { list.Add(new object()); list.Clear(); }
            if (indexed) list.InsertRange(0, Array.Empty<object>());
            else list.AddRange(Array.Empty<object>());
            Assert.Empty(list);
            Assert.Equal(previouslyInserted, list.InsertSinceRecycled);
        }
        finally { list.Recycle(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ARejectedRangeDoesNotPartiallyInsertOrChangeHistory(bool indexed, bool previouslyInserted)
    {
        var list = RecyclableArrayList.NewInstance();
        object original = new object();
        try
        {
            if (previouslyInserted) list.Add(original);
            object[] invalid = { new object(), null, new object() };
            var error = Assert.Throws<ArgumentException>(() =>
            {
                if (indexed) list.InsertRange(0, invalid); else list.AddRange(invalid);
            });
            Assert.Equal("items", error.ParamName);
            Assert.Equal(previouslyInserted ? new[] { original } : Array.Empty<object>(), list.ToArray());
            Assert.Equal(previouslyInserted, list.InsertSinceRecycled);
        }
        finally { list.Recycle(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullRangeSourcesUseNativeArgumentExceptions(bool indexed)
    {
        var list = RecyclableArrayList.NewInstance();
        try
        {
            var error = Assert.Throws<ArgumentNullException>(() =>
            {
                if (indexed) list.InsertRange(0, null); else list.AddRange(null);
            });
            Assert.Equal("items", error.ParamName);
            Assert.Empty(list);
            Assert.False(list.InsertSinceRecycled);
        }
        finally { list.Recycle(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void NativeInterfaceWritesCannotBypassNullAndHistoryRules(int surface)
    {
        var list = RecyclableArrayList.NewInstance();
        try
        {
            Action<object> write = surface switch
            {
                0 => list.Add,
                1 => ((ICollection<object>)list).Add,
                2 => item => ((IList)list).Add(item),
                3 => item => list.Insert(0, item),
                4 => item => ((IList<object>)list).Insert(0, item),
                _ => item => ((IList)list).Insert(0, item)
            };
            Assert.Throws<ArgumentNullException>(() => write(null));
            Assert.Empty(list);
            Assert.False(list.InsertSinceRecycled);
            object original = new object(), replacement = new object();
            write(original);
            Assert.Same(original, Assert.Single(list));
            Assert.True(list.InsertSinceRecycled);
            Assert.Throws<ArgumentNullException>(() => list[0] = null);
            Assert.Throws<ArgumentNullException>(() => ((IList<object>)list)[0] = null);
            Assert.Throws<ArgumentNullException>(() => ((IList)list)[0] = null);
            Assert.Same(original, list[0]);
            ((IList)list)[0] = replacement;
            Assert.Same(replacement, list[0]);
        }
        finally { list.Recycle(); }
    }

    [Fact]
    public void NativeReadCopyRemoveAndClearKeepOrderAndHistory()
    {
        var list = RecyclableArrayList.NewInstance();
        object first = new object(), last = new object();
        try
        {
            list.AddRange(new[] { first, (object)42, last });
            Assert.Equal(3, list.Count);
            Assert.True(list.Contains(42));
            Assert.Equal(2, list.IndexOf(last));
            var copy = new object[4];
            list.CopyTo(copy, 1);
            Assert.Equal(new[] { null, first, (object)42, last }, copy);
            Assert.True(list.Remove(42));
            list.RemoveAt(0);
            Assert.Same(last, Assert.Single(list));
            list.Clear();
            Assert.Empty(list);
            Assert.True(list.InsertSinceRecycled);
        }
        finally { list.Recycle(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARangeSourceIsEnumeratedOnce(bool indexed)
    {
        var list = RecyclableArrayList.NewInstance();
        object first = new object(), second = new object();
        int enumerations = 0;
        IEnumerable<object> Values()
        {
            if (++enumerations != 1) throw new InvalidOperationException("enumerated twice");
            yield return first;
            yield return second;
        }
        try
        {
            if (indexed) list.InsertRange(0, Values()); else list.AddRange(Values());
            Assert.Equal(1, enumerations);
            Assert.Equal(new[] { first, second }, list.ToArray());
            Assert.True(list.InsertSinceRecycled);
        }
        finally { list.Recycle(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingEnumerablesDoNotPublishTheirPrefix(bool indexed)
    {
        var list = RecyclableArrayList.NewInstance();
        IEnumerable<object> Values()
        {
            yield return new object();
            throw new InvalidOperationException("source failed");
        }
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (indexed) list.InsertRange(0, Values()); else list.AddRange(Values());
            });
            Assert.Empty(list);
            Assert.False(list.InsertSinceRecycled);
        }
        finally { list.Recycle(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfRangesUseTheOriginalSnapshot(bool indexed)
    {
        var list = RecyclableArrayList.NewInstance();
        object first = new object(), second = new object();
        try
        {
            list.AddRange(new[] { first, second });
            if (indexed) list.InsertRange(1, list); else list.AddRange(list);
            Assert.Equal(indexed ? new[] { first, first, second, second } : new[] { first, second, first, second }, list.ToArray());
        }
        finally { list.Recycle(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidIndexesDoNotRecordAnInsertion(bool empty)
    {
        var list = RecyclableArrayList.NewInstance();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => list.InsertRange(1, empty ? Array.Empty<object>() : new[] { new object() }));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(-1, new object()));
            Assert.Throws<ArgumentOutOfRangeException>(() => list[0] = new object());
            Assert.Empty(list);
            Assert.False(list.InsertSinceRecycled);
        }
        finally { list.Recycle(); }
    }

    private sealed class Payload : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuardedPoolReturnClearsReferencesAndHistoryWithoutOwningPayloads(bool externalReturn)
    {
        RunInFastThreadLocalThreadExtension.Run(() =>
        {
            var list = RecyclableArrayList.NewInstance(32);
            var payload = new Payload();
            list.Add(payload);
            object[] exported = list.ToArray();
            if (externalReturn)
            {
                Exception failure = null;
                var thread = new Thread(() => { try { list.Recycle(); } catch (Exception e) { failure = e; } });
                thread.Start();
                Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
                Assert.Null(failure);
            }
            else list.Recycle();
            Assert.Throws<InvalidOperationException>(() => list.Recycle());
            var next = RecyclableArrayList.NewInstance();
            try
            {
                Assert.Same(list, next);
                Assert.Empty(next);
                Assert.False(next.InsertSinceRecycled);
                Assert.Same(payload, Assert.Single(exported));
                Assert.Equal(0, payload.Disposals);
                next.Add(new object());
                Assert.True(next.InsertSinceRecycled);
            }
            finally { next.Recycle(); }
        });
    }

    [Fact]
    public void NegativeCapacityUsesTheNativeArgumentName()
    {
        Assert.Equal("minCapacity", Assert.Throws<ArgumentOutOfRangeException>(() => RecyclableArrayList.NewInstance(-1)).ParamName);
    }
}
