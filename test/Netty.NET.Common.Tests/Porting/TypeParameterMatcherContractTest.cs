using System;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Xunit;

namespace Netty.NET.Common.Tests.Porting;

[Collection("Thread-local globals")]
public class TypeParameterMatcherContractTest : IDisposable
{
    public TypeParameterMatcherContractTest() => FastThreadLocal.RemoveAll();
    public void Dispose() => FastThreadLocal.RemoveAll();

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
        Type matcher = ReflectionUtil.ResolveTypeParameter(new Lists(), typeof(Parent<>), "E");
        Assert.False(matcher.IsInstanceOfType(new List<int>()));
        Assert.True(matcher.IsInstanceOfType(new List<string>()));
        Assert.False(matcher.IsInstanceOfType(new Dictionary<string, int>()));
        Assert.False(matcher.IsInstanceOfType(null));
        Assert.NotSame(matcher, typeof(List<int>));
        Assert.Same(matcher, typeof(List<string>));
        Assert.True(typeof(IEnumerable<object>).IsInstanceOfType(new List<string>()));
        Assert.False(typeof(IEnumerable<string>).IsInstanceOfType(new List<int>()));
    }

    [Fact]
    public void ParameterizedArraysRetainElementTypesAndPreserveArrayShapes()
    {
        Type matcher = ReflectionUtil.ResolveTypeParameter(new ListArrays(), typeof(Parent<>), "E");
        Assert.True(matcher.IsInstanceOfType(new List<string>[1]));
        Assert.False(matcher.IsInstanceOfType(new List<int>[1]));
        Assert.False(matcher.IsInstanceOfType(new Dictionary<string, int>[1]));
        Assert.False(matcher.IsInstanceOfType(new List<int>[1, 1]));
        Assert.Same(matcher, typeof(List<string>[]));
        Assert.NotSame(matcher, typeof(List<int>[]));
        Assert.False(matcher.IsInstanceOfType(Array.CreateInstance(typeof(List<int>), new[] { 1 }, new[] { 1 })));
        Assert.True(ReflectionUtil.ResolveTypeParameter(new ObjectArrays(), typeof(Parent<>), "E").IsInstanceOfType(new string[1]));
    }

    [Fact]
    public void ConstructedClrArrayVariablesResolveWithoutReproducingJvmErasure()
    {
        Assert.Equal(typeof(string[]), ReflectionUtil.ResolveTypeParameter(
            new ConcreteVariableArrays(), typeof(Parent<>), "E"));
        Type matcher = ReflectionUtil.ResolveTypeParameter(new ConcreteVariableArrays(), typeof(Parent<>), "E");
        Assert.True(matcher.IsInstanceOfType(new string[1]));
        Assert.False(matcher.IsInstanceOfType(new int[1]));
    }

    [Fact]
    public void AnEnclosingClrTypeArgumentRemainsAvailable()
    {
        Type matcher = ReflectionUtil.ResolveTypeParameter(new Outer<string>.Inner(), typeof(Parent<>), "E");
        Assert.False(matcher.IsInstanceOfType(new object()));
        Assert.False(matcher.IsInstanceOfType(null));
        Assert.True(matcher.IsInstanceOfType("value"));
        Assert.Same(typeof(string), matcher);
    }

    [Fact]
    public void RequestedSuperclassesThatReuseTheSameParameterNameStayDistinct()
    {
        Type first = ReflectionUtil.ResolveTypeParameter(new Leaf(), typeof(Middle<>), "A");
        Assert.True(first.IsInstanceOfType("value"));
        Assert.False(first.IsInstanceOfType(1));
        Type second = ReflectionUtil.ResolveTypeParameter(new Leaf(), typeof(Ancestor<>), "A");
        Assert.NotSame(first, second);
        Assert.True(second.IsInstanceOfType(1));
        Assert.False(second.IsInstanceOfType("value"));
        Assert.Same(first, ReflectionUtil.ResolveTypeParameter(new Leaf(), typeof(Middle<>), "A"));
        Assert.Same(second, ReflectionUtil.ResolveTypeParameter(new Leaf(), typeof(Ancestor<>), "A"));
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
        Assert.Throws<ArgumentNullException>(() => ReflectionUtil.ResolveTypeParameter(null, typeof(Parent<>), "E"));
        Assert.Throws<ArgumentNullException>(() => ReflectionUtil.ResolveTypeParameter(new Lists(), null, "E"));
        Assert.Throws<InvalidOperationException>(() => ReflectionUtil.ResolveTypeParameter(new Lists(), typeof(Parent<int>), "E"));
    }

    [Fact]
    public void NativeTypesAndResolutionDoNotCreateThreadLocalState()
    {
        Type current = ReflectionUtil.ResolveTypeParameter(new Lists(), typeof(Parent<>), "E");
        Type other = null;
        Exception failure = null;
        Assert.Null(InternalThreadLocalMap.GetIfSet());
        var thread = new Thread(() =>
        {
            try
            {
                Assert.Null(InternalThreadLocalMap.GetIfSet());
                other = ReflectionUtil.ResolveTypeParameter(new Lists(), typeof(Parent<>), "E");
                Assert.True(other.IsInstanceOfType(new List<string>()));
                Assert.False(other.IsInstanceOfType(new List<int>()));
                Assert.Null(InternalThreadLocalMap.GetIfSet());
            }
            catch (Exception exception) { failure = exception; }
            finally { FastThreadLocal.RemoveAll(); }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.Same(current, other);
        Assert.Null(InternalThreadLocalMap.GetIfSet());
    }
}
