using System;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

public class TypeParameterMatcherContractTest
{
    private class Parent<E> { }
    private sealed class Lists : Parent<List<string>> { }
    private sealed class ListArrays : Parent<List<string>[]> { }
    private sealed class ObjectArrays : Parent<object[]> { }
    private class VariableArrays<T> : Parent<T[]> { }
    private sealed class ConcreteVariableArrays : VariableArrays<string> { }
    private class Outer<E>
    {
        // A nested declaration lets the resolver encounter a variable belonging to
        // a declaring class outside this instance's own superclass chain.
        internal sealed class Inner : Parent<E> { }
    }
    private class Ancestor<A> { }
    private class Middle<A> : Ancestor<int> { }
    private sealed class Leaf : Middle<string> { }

    [Fact]
    public void ParameterizedTypesRetainTheirClrArgumentsIncludingInterfaceVariance()
    {
        TypeParameterMatcher matcher = TypeParameterMatcher.find(new Lists(), typeof(Parent<>), "E");
        Assert.False(matcher.match(new List<int>()));
        Assert.True(matcher.match(new List<string>()));
        Assert.False(matcher.match(new Dictionary<string, int>()));
        Assert.False(matcher.match(null));
        Assert.NotSame(matcher, TypeParameterMatcher.get(typeof(List<int>)));
        Assert.Same(matcher, TypeParameterMatcher.get(typeof(List<string>)));
        Assert.True(TypeParameterMatcher.get(typeof(IEnumerable<object>)).match(new List<string>()));
        Assert.False(TypeParameterMatcher.get(typeof(IEnumerable<string>)).match(new List<int>()));
    }

    [Fact]
    public void ParameterizedArraysRetainElementTypesAndPreserveArrayShapes()
    {
        TypeParameterMatcher matcher = TypeParameterMatcher.find(new ListArrays(), typeof(Parent<>), "E");
        Assert.True(matcher.match(new List<string>[1]));
        Assert.False(matcher.match(new List<int>[1]));
        Assert.False(matcher.match(new Dictionary<string, int>[1]));
        Assert.False(matcher.match(new List<int>[1, 1]));
        Assert.Same(matcher, TypeParameterMatcher.get(typeof(List<string>[])));
        Assert.NotSame(matcher, TypeParameterMatcher.get(typeof(List<int>[])));
        Assert.False(matcher.match(Array.CreateInstance(typeof(List<int>), new[] { 1 }, new[] { 1 })));
        Assert.True(TypeParameterMatcher.find(new ObjectArrays(), typeof(Parent<>), "E").match(new string[1]));
    }

    [Fact]
    public void ConstructedClrArrayVariablesResolveWithoutReproducingJvmErasure()
    {
        Assert.Equal(typeof(string[]), ReflectionUtil.resolveTypeParameter(
            new ConcreteVariableArrays(), typeof(Parent<>), "E"));
        TypeParameterMatcher matcher = TypeParameterMatcher.find(new ConcreteVariableArrays(), typeof(Parent<>), "E");
        Assert.True(matcher.match(new string[1]));
        Assert.False(matcher.match(new int[1]));
    }

    [Fact]
    public void AnEnclosingClrTypeArgumentRemainsAvailable()
    {
        TypeParameterMatcher matcher = TypeParameterMatcher.find(new Outer<string>.Inner(), typeof(Parent<>), "E");
        Assert.False(matcher.match(new object()));
        Assert.False(matcher.match(null));
        Assert.True(matcher.match("value"));
        Assert.Same(TypeParameterMatcher.get(typeof(string)), matcher);
    }

    [Fact]
    public void FindCacheSeparatesSuperclassesThatReuseTheSameParameterName()
    {
        TypeParameterMatcher first = TypeParameterMatcher.find(new Leaf(), typeof(Middle<>), "A");
        Assert.True(first.match("value"));
        Assert.False(first.match(1));
        TypeParameterMatcher second = TypeParameterMatcher.find(new Leaf(), typeof(Ancestor<>), "A");
        Assert.NotSame(first, second);
        Assert.True(second.match(1));
        Assert.False(second.match("value"));
        Assert.Same(first, TypeParameterMatcher.find(new Leaf(), typeof(Middle<>), "A"));
        Assert.Same(second, TypeParameterMatcher.find(new Leaf(), typeof(Ancestor<>), "A"));
        Assert.Equal(typeof(int), ReflectionUtil.resolveTypeParameter(new Leaf(), typeof(Ancestor<>), "A"));
    }

    [Fact]
    public void UnknownParametersAndUnrelatedSuperclassesFailWithTheOriginalInstanceType()
    {
        InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(() =>
            ReflectionUtil.resolveTypeParameter(new Lists(), typeof(Parent<>), "missing"));
        Assert.Contains("unknown type parameter 'missing'", unknown.Message);
        InvalidOperationException unrelated = Assert.Throws<InvalidOperationException>(() =>
            ReflectionUtil.resolveTypeParameter(new Lists(), typeof(Dictionary<,>), "TKey"));
        Assert.Contains(typeof(Lists).ToString(), unrelated.Message);
        Assert.Throws<ArgumentNullException>(() => TypeParameterMatcher.find(null, typeof(Parent<>), "E"));
        Assert.Throws<ArgumentNullException>(() => TypeParameterMatcher.get(null));
        Assert.Throws<InvalidOperationException>(() => TypeParameterMatcher.find(new Lists(), typeof(Parent<int>), "E"));
    }

    [Fact]
    public void GetCacheIsLocalToTheCallingThreadWhileTheObjectNoopRemainsShared()
    {
        TypeParameterMatcher current = TypeParameterMatcher.get(typeof(List<>));
        TypeParameterMatcher objectMatcher = TypeParameterMatcher.get(typeof(object));
        TypeParameterMatcher other = null, otherObject = null;
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                other = TypeParameterMatcher.get(typeof(List<>));
                otherObject = TypeParameterMatcher.get(typeof(object));
                Assert.Same(other, TypeParameterMatcher.get(typeof(List<>)));
            }
            catch (Exception exception) { failure = exception; }
            finally { FastThreadLocal.removeAll(); }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.NotSame(current, other);
        Assert.Same(objectMatcher, otherObject);
    }
}
