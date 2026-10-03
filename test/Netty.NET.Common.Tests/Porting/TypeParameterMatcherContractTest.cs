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
        TypeParameterMatcher matcher = TypeParameterMatcher.Find(new Lists(), typeof(Parent<>), "E");
        Assert.False(matcher.Match(new List<int>()));
        Assert.True(matcher.Match(new List<string>()));
        Assert.False(matcher.Match(new Dictionary<string, int>()));
        Assert.False(matcher.Match(null));
        Assert.NotSame(matcher, TypeParameterMatcher.Get(typeof(List<int>)));
        Assert.Same(matcher, TypeParameterMatcher.Get(typeof(List<string>)));
        Assert.True(TypeParameterMatcher.Get(typeof(IEnumerable<object>)).Match(new List<string>()));
        Assert.False(TypeParameterMatcher.Get(typeof(IEnumerable<string>)).Match(new List<int>()));
    }

    [Fact]
    public void ParameterizedArraysRetainElementTypesAndPreserveArrayShapes()
    {
        TypeParameterMatcher matcher = TypeParameterMatcher.Find(new ListArrays(), typeof(Parent<>), "E");
        Assert.True(matcher.Match(new List<string>[1]));
        Assert.False(matcher.Match(new List<int>[1]));
        Assert.False(matcher.Match(new Dictionary<string, int>[1]));
        Assert.False(matcher.Match(new List<int>[1, 1]));
        Assert.Same(matcher, TypeParameterMatcher.Get(typeof(List<string>[])));
        Assert.NotSame(matcher, TypeParameterMatcher.Get(typeof(List<int>[])));
        Assert.False(matcher.Match(Array.CreateInstance(typeof(List<int>), new[] { 1 }, new[] { 1 })));
        Assert.True(TypeParameterMatcher.Find(new ObjectArrays(), typeof(Parent<>), "E").Match(new string[1]));
    }

    [Fact]
    public void ConstructedClrArrayVariablesResolveWithoutReproducingJvmErasure()
    {
        Assert.Equal(typeof(string[]), ReflectionUtil.ResolveTypeParameter(
            new ConcreteVariableArrays(), typeof(Parent<>), "E"));
        TypeParameterMatcher matcher = TypeParameterMatcher.Find(new ConcreteVariableArrays(), typeof(Parent<>), "E");
        Assert.True(matcher.Match(new string[1]));
        Assert.False(matcher.Match(new int[1]));
    }

    [Fact]
    public void AnEnclosingClrTypeArgumentRemainsAvailable()
    {
        TypeParameterMatcher matcher = TypeParameterMatcher.Find(new Outer<string>.Inner(), typeof(Parent<>), "E");
        Assert.False(matcher.Match(new object()));
        Assert.False(matcher.Match(null));
        Assert.True(matcher.Match("value"));
        Assert.Same(TypeParameterMatcher.Get(typeof(string)), matcher);
    }

    [Fact]
    public void FindCacheSeparatesSuperclassesThatReuseTheSameParameterName()
    {
        TypeParameterMatcher first = TypeParameterMatcher.Find(new Leaf(), typeof(Middle<>), "A");
        Assert.True(first.Match("value"));
        Assert.False(first.Match(1));
        TypeParameterMatcher second = TypeParameterMatcher.Find(new Leaf(), typeof(Ancestor<>), "A");
        Assert.NotSame(first, second);
        Assert.True(second.Match(1));
        Assert.False(second.Match("value"));
        Assert.Same(first, TypeParameterMatcher.Find(new Leaf(), typeof(Middle<>), "A"));
        Assert.Same(second, TypeParameterMatcher.Find(new Leaf(), typeof(Ancestor<>), "A"));
        Assert.Equal(typeof(int), ReflectionUtil.ResolveTypeParameter(new Leaf(), typeof(Ancestor<>), "A"));
    }

    [Fact]
    public void UnknownParametersAndUnrelatedSuperclassesFailWithTheOriginalInstanceType()
    {
        InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(() =>
            ReflectionUtil.ResolveTypeParameter(new Lists(), typeof(Parent<>), "missing"));
        Assert.Contains("unknown type parameter 'missing'", unknown.Message);
        InvalidOperationException unrelated = Assert.Throws<InvalidOperationException>(() =>
            ReflectionUtil.ResolveTypeParameter(new Lists(), typeof(Dictionary<,>), "TKey"));
        Assert.Contains(typeof(Lists).ToString(), unrelated.Message);
        Assert.Throws<ArgumentNullException>(() => TypeParameterMatcher.Find(null, typeof(Parent<>), "E"));
        Assert.Throws<ArgumentNullException>(() => TypeParameterMatcher.Get(null));
        Assert.Throws<InvalidOperationException>(() => TypeParameterMatcher.Find(new Lists(), typeof(Parent<int>), "E"));
    }

    [Fact]
    public void GetCacheIsLocalToTheCallingThreadWhileTheObjectNoopRemainsShared()
    {
        TypeParameterMatcher current = TypeParameterMatcher.Get(typeof(List<>));
        TypeParameterMatcher objectMatcher = TypeParameterMatcher.Get(typeof(object));
        TypeParameterMatcher other = null, otherObject = null;
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                other = TypeParameterMatcher.Get(typeof(List<>));
                otherObject = TypeParameterMatcher.Get(typeof(object));
                Assert.Same(other, TypeParameterMatcher.Get(typeof(List<>)));
            }
            catch (Exception exception) { failure = exception; }
            finally { FastThreadLocal.RemoveAll(); }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.NotSame(current, other);
        Assert.Same(objectMatcher, otherObject);
    }
}
