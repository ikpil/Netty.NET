/*
 * Copyright 2013 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
using System;
using Netty.NET.Common.Internal;
using Xunit;

namespace Netty.NET.Common.Tests.Internal;

public class TypeParameterMatcherTest
{
    public class TypeX<A, B, C> { private A a; private B b; private C c; }
    public class TypeY<D, E, F> : TypeX<E, F, D> where D : C where E : A where F : B { }
    public abstract class TypeZ<G, H> : TypeY<CC, G, H> where G : AA where H : BB { }
    public class TypeQ<I> : TypeZ<AAA, I> where I : BBB { }
    public class A { }
    public class AA : A { }
    public class AAA : AA { }
    public class B { }
    public class BB : B { }
    public class BBB : BB { }
    public class C { }
    public class CC : C { }

    // CLR adaptation: named subclasses carry Java anonymous-class superclass
    // metadata. The raw binding uses object; constructed CLR generic arguments are
    // retained, including those belonging to an enclosing class.
    private sealed class AnonymousQ : TypeQ<BBB> { }
    private class T { }
    private class U<E> { private E a; }
    private sealed class AnonymousPrivateU : U<T> { }
    private sealed class AnonymousArrayU : U<byte[]> { }
    private sealed class RawU : U<object> { }
    private sealed class V<E>
    {
        private sealed class AnonymousInnerU : U<E> { }
        internal readonly U<E> u = new AnonymousInnerU();
    }
    private abstract class W<E> { private E e; }
    private sealed class X<T, E> : W<E> { private T t; }

    [Fact]
    public void TestConcreteClass()
    {
        Type m = ReflectionUtil.ResolveTypeParameter(new TypeQ<BBB>(), typeof(TypeX<,,>), "A");
        Assert.False(m.IsInstanceOfType(new object()));
        Assert.False(m.IsInstanceOfType(new A()));
        Assert.False(m.IsInstanceOfType(new AA()));
        Assert.True(m.IsInstanceOfType(new AAA()));
        Assert.False(m.IsInstanceOfType(new B()));
        Assert.False(m.IsInstanceOfType(new BB()));
        Assert.False(m.IsInstanceOfType(new BBB()));
        Assert.False(m.IsInstanceOfType(new C()));
        Assert.False(m.IsInstanceOfType(new CC()));
    }

    [Fact(Skip = "JVM type erasure leaves this parameter unresolved; CLR retains BBB.")]
    public void TestUnsolvedParameter() => Assert.Throws<InvalidOperationException>(() =>
        ReflectionUtil.ResolveTypeParameter(new TypeQ<BBB>(), typeof(TypeX<,,>), "B"));

    [Fact]
    public void TestAnonymousClass()
    {
        Type m = ReflectionUtil.ResolveTypeParameter(new AnonymousQ(), typeof(TypeX<,,>), "B");
        Assert.False(m.IsInstanceOfType(new object()));
        Assert.False(m.IsInstanceOfType(new A()));
        Assert.False(m.IsInstanceOfType(new AA()));
        Assert.False(m.IsInstanceOfType(new AAA()));
        Assert.False(m.IsInstanceOfType(new B()));
        Assert.False(m.IsInstanceOfType(new BB()));
        Assert.True(m.IsInstanceOfType(new BBB()));
        Assert.False(m.IsInstanceOfType(new C()));
        Assert.False(m.IsInstanceOfType(new CC()));
    }

    [Fact]
    public void TestAbstractClass()
    {
        Type m = ReflectionUtil.ResolveTypeParameter(new TypeQ<BBB>(), typeof(TypeX<,,>), "C");
        Assert.False(m.IsInstanceOfType(new object()));
        Assert.False(m.IsInstanceOfType(new A()));
        Assert.False(m.IsInstanceOfType(new AA()));
        Assert.False(m.IsInstanceOfType(new AAA()));
        Assert.False(m.IsInstanceOfType(new B()));
        Assert.False(m.IsInstanceOfType(new BB()));
        Assert.False(m.IsInstanceOfType(new BBB()));
        Assert.False(m.IsInstanceOfType(new C()));
        Assert.True(m.IsInstanceOfType(new CC()));
    }

    [Fact]
    public void TestInaccessibleClass()
    {
        Type m = ReflectionUtil.ResolveTypeParameter(new AnonymousPrivateU(), typeof(U<>), "E");
        Assert.False(m.IsInstanceOfType(new object()));
        Assert.True(m.IsInstanceOfType(new T()));
    }

    [Fact]
    public void TestArrayAsTypeParam()
    {
        Type m = ReflectionUtil.ResolveTypeParameter(new AnonymousArrayU(), typeof(U<>), "E");
        Assert.False(m.IsInstanceOfType(new object()));
        Assert.True(m.IsInstanceOfType(new byte[1]));
    }

    [Fact]
    public void TestRawType()
    {
        Type m = ReflectionUtil.ResolveTypeParameter(new RawU(), typeof(U<>), "E");
        Assert.True(m.IsInstanceOfType(new object()));
    }

    [Fact]
    public void TestInnerClass()
    {
        Type m = ReflectionUtil.ResolveTypeParameter(new V<string>().u, typeof(U<>), "E");
        Assert.False(m.IsInstanceOfType(new object()));
        Assert.True(m.IsInstanceOfType("value"));
    }

    [Fact(Skip = "JVM type erasure is not applicable to constructed CLR generic types.")]
    public void TestErasure() => Assert.Throws<InvalidOperationException>(() =>
    {
        Type m = ReflectionUtil.ResolveTypeParameter(new X<string, DateTime>(), typeof(W<>), "E");
        Assert.True(m.IsInstanceOfType(new DateTime()));
        Assert.False(m.IsInstanceOfType(new object()));
    });
}
