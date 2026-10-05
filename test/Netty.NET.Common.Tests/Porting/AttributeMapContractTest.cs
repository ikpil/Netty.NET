using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Netty.NET.Common.Tests.Porting;

public class AttributeMapContractTest
{
    private sealed record Value(int Number);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullKeysFailAtTheMapBoundary(bool lookup)
    {
        var map = new DefaultAttributeMap();
        var error = Assert.Throws<ArgumentNullException>(() =>
        {
            if (lookup) map.Attr<object>(null);
            else map.HasAttr<object>(null);
        });
        Assert.Equal("key", error.ParamName);
    }

    [Fact]
    public void CompareAndSetUsesReferenceIdentityForEqualValuesAndNull()
    {
        var map = new DefaultAttributeMap();
        var attribute = map.Attr(AttributeKey.ValueOf<Value>(Guid.NewGuid().ToString()));
        var first = new Value(7);
        var equal = new Value(7);
        var next = new Value(8);
        Assert.Equal(first, equal);
        Assert.True(attribute.CompareAndSet(null, first));
        Assert.False(attribute.CompareAndSet(equal, next));
        Assert.Same(first, attribute.Get());
        Assert.True(attribute.CompareAndSet(first, next));
        Assert.Same(next, attribute.GetAndSet(null));
        Assert.True(attribute.CompareAndSet(null, null));
        Assert.Null(attribute.SetIfAbsent(null));
        Assert.Null(attribute.Get());
    }

    [Fact]
    public void EmptySlotsAndMixedGenericKeysRemainIndependentBetweenMaps()
    {
        var first = new DefaultAttributeMap();
        var second = new DefaultAttributeMap();
        var textKey = AttributeKey.ValueOf<string>(Guid.NewGuid().ToString());
        var objectKey = AttributeKey.ValueOf<object>(Guid.NewGuid().ToString());
        Assert.False(first.HasAttr(textKey));
        var text = first.Attr(textKey);
        Assert.True(first.HasAttr(textKey));
        Assert.Null(text.Get());
        Assert.NotSame(text, second.Attr(textKey));
        text.Set("text");
        var payload = new object();
        first.Attr(objectKey).Set(payload);
        Assert.Same(payload, first.Attr(objectKey).Get());
        Assert.Equal("text", text.GetAndSet(null));
        Assert.Same(text, first.Attr(textKey));
        Assert.True(first.HasAttr(textKey));
        Assert.Null(second.Attr(textKey).Get());
        Assert.False(second.HasAttr(objectKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedSlotsRemainUsableWithoutRemovingTheirReplacement(bool returnOld)
    {
        var map = new DefaultAttributeMap();
        var key = AttributeKey.ValueOf<object>(Guid.NewGuid().ToString());
        var old = map.Attr(key);
        var initial = new object();
        old.Set(initial);
        if (returnOld) Assert.Same(initial, old.GetAndRemove());
        else old.Remove();
        Assert.Null(old.Get());
        Assert.False(map.HasAttr(key));
        var current = map.Attr(key);
        Assert.NotSame(old, current);
        var currentValue = new object();
        var detachedValue = new object();
        current.Set(currentValue);
        old.Set(detachedValue);
        Assert.Same(detachedValue, old.GetAndRemove());
        old.Remove();
        Assert.Same(current, map.Attr(key));
        Assert.Same(currentValue, current.Get());
    }

    [Fact]
    public void ConcurrentSetIfAbsentPublishesOneSlotAndOneWinningReference()
    {
        var map = new DefaultAttributeMap();
        var key = AttributeKey.ValueOf<object>(Guid.NewGuid().ToString());
        var slots = new IAttribute<object>[8];
        var values = new object[8];
        var previous = new object[8];
        for (int index = 0; index < values.Length; index++) values[index] = new object();
        RunContenders(index =>
        {
            slots[index] = map.Attr(key);
            previous[index] = slots[index].SetIfAbsent(values[index]);
        });
        object winner = map.Attr(key).Get();
        int winners = 0;
        for (int index = 0; index < slots.Length; index++)
        {
            Assert.Same(slots[0], slots[index]);
            if (previous[index] == null) { winners++; Assert.Same(values[index], winner); }
            else Assert.Same(winner, previous[index]);
        }
        Assert.Equal(1, winners);
    }

    [Fact]
    public void ConcurrentExchangeConservesEveryReferenceExactlyOnce()
    {
        var map = new DefaultAttributeMap();
        var attribute = map.Attr(AttributeKey.ValueOf<object>(Guid.NewGuid().ToString()));
        var initial = new object();
        attribute.Set(initial);
        var values = new object[8];
        var previous = new object[8];
        for (int index = 0; index < values.Length; index++) values[index] = new object();
        RunContenders(index => previous[index] = attribute.GetAndSet(values[index]));
        var expected = new HashSet<object>(ReferenceEqualityComparer.Instance) { initial };
        foreach (object value in values) Assert.True(expected.Add(value));
        foreach (object value in previous) Assert.True(expected.Remove(value));
        Assert.True(expected.Remove(attribute.Get()));
        Assert.Empty(expected);
    }

    [Fact]
    public void ConcurrentInsertionOfDifferentGenericKeysKeepsPublishedSlots()
    {
        var map = new DefaultAttributeMap();
        var textKeys = new AttributeKey<string>[32];
        var objectKeys = new AttributeKey<object>[32];
        for (int index = 0; index < textKeys.Length; index++)
        {
            textKeys[index] = AttributeKey.ValueOf<string>(Guid.NewGuid().ToString());
            objectKeys[index] = AttributeKey.ValueOf<object>(Guid.NewGuid().ToString());
        }
        var texts = new IAttribute<string>[8, 32];
        var objects = new IAttribute<object>[8, 32];
        RunContenders(worker =>
        {
            for (int offset = 0; offset < 32; offset++)
            {
                int index = (offset + worker * 7) % 32;
                texts[worker, index] = map.Attr(textKeys[index]);
                objects[worker, index] = map.Attr(objectKeys[index]);
            }
        });
        for (int index = 0; index < 32; index++)
        {
            Assert.True(map.HasAttr(textKeys[index]));
            Assert.True(map.HasAttr(objectKeys[index]));
            for (int worker = 0; worker < 8; worker++)
            {
                Assert.Same(texts[0, index], texts[worker, index]);
                Assert.Same(objects[0, index], objects[worker, index]);
            }
        }
    }

    [Fact]
    public void DelayedRemovalCannotDeleteAReplacementForTheSameKey()
    {
        var map = new DefaultAttributeMap();
        var key = AttributeKey.ValueOf<object>(Guid.NewGuid().ToString());
        var old = map.Attr(key);
        // Pause after the original removal CAS detaches its owner, before the
        // conditional map deletion. Reflection controls that otherwise tiny window.
        FieldInfo owner = old.GetType().GetField("attributeMap", BindingFlags.Instance | BindingFlags.NonPublic);
        if (owner != null) owner.SetValue(old, null);
        else
        {
            object wrapper = old.GetType().GetField("_attributeMap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(old);
            wrapper.GetType().GetMethod("GetAndSet").Invoke(wrapper, new object[] { null });
        }
        var current = map.Attr(key);
        Assert.NotSame(old, current);
        var payload = new object();
        current.Set(payload);
        typeof(DefaultAttributeMap).GetMethod("RemoveAttributeIfMatch", BindingFlags.Instance | BindingFlags.NonPublic)
            .MakeGenericMethod(typeof(object)).Invoke(map, new object[] { key, old });
        Assert.Same(current, map.Attr(key));
        Assert.Same(payload, current.Get());
    }

    private static void RunContenders(Action<int> action)
    {
        using var ready = new CountdownEvent(8);
        using var start = new ManualResetEventSlim();
        var threads = new Thread[8];
        var errors = new Exception[8];
        for (int index = 0; index < threads.Length; index++)
        {
            int worker = index;
            threads[index] = new Thread(() =>
            {
                try
                {
                    ready.Signal();
                    if (!start.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                    action(worker);
                }
                catch (Exception error) { errors[worker] = error; }
            }) { IsBackground = true };
            threads[index].Start();
        }
        try { Assert.True(ready.Wait(TimeSpan.FromSeconds(5))); }
        finally
        {
            start.Set();
            foreach (Thread thread in threads) Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        }
        Assert.All(errors, error => Assert.Null(error));
    }
}
