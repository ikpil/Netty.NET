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

namespace Netty.NET.Common.Tests.Internal;

public class TypeParameterMatcherTest
{
    public class A
    {
    }

    public class AA : A
    {
    }

    public class AAA : AA
    {
    }

    public class B
    {
    }

    public class BB : B
    {
    }

    public class BBB : BB
    {
    }

    public class BBBB : BBB
    {
    }


    public class C
    {
    }

    public class CC : C
    {
    }

    public class TypeX<A, B, C>
    {
        A a;
        B b;
        C c;
    }

    public class TypeY<D, E, F> : TypeX<E, F, D>
        where D : C
        where E : A
        where F : B
    {
    }

    public abstract class TypeZ<G, H> : TypeY<CC, G, H>
        where G : AA
        where H : BB
    {
    }

    public class TypeQ<I> : TypeZ<AAA, I>
        where I : BBB
    {
    }

    private class T
    {
    }

    private class U<E>
    {
        E a;
    }

    public class V<E>
    {
        U<E> u = new U<E>() { };
    }

    public abstract class W<E>
    {
        E e;
    }

    public class X<T, E> : W<E>
    {
        T t;
    }


    [Fact]
    public void testConcreteClass()
    {
        TypeParameterMatcher m = TypeParameterMatcher.find(new TypeQ<BBB>(), TypeX.class,"A");
        Assert.False(m.match(new object()));
        Assert.False(m.match(new A()));
        Assert.False(m.match(new AA()));
        Assert.True(m.match(new AAA()));
        Assert.False(m.match(new B()));
        Assert.False(m.match(new BB()));
        Assert.False(m.match(new BBB()));
        Assert.False(m.match(new C()));
        Assert.False(m.match(new CC()));
    }

    [Fact]
    public void testUnsolvedParameter()
    {
        Assert.Throws<Exception>(() =>
        {
            TypeParameterMatcher.find(new TypeQ(), TypeX.class, "B");
        });
    }

    [Fact]
    public void testAnonymousClass()
    {
        TypeParameterMatcher m = TypeParameterMatcher.find(new TypeQ<BBB>(), TypeX.class, "B");
        Assert.False(m.match(new object()));
        Assert.False(m.match(new A()));
        Assert.False(m.match(new AA()));
        Assert.False(m.match(new AAA()));
        Assert.False(m.match(new B()));
        Assert.False(m.match(new BB()));
        Assert.True(m.match(new BBB()));
        Assert.False(m.match(new C()));
        Assert.False(m.match(new CC()));
    }

    [Fact]
    public void testAbstractClass()
    {
        TypeParameterMatcher m = TypeParameterMatcher.find(new TypeQ<>(), TypeX.class, "C");
        Assert.False(m.match(new object()));
        Assert.False(m.match(new A()));
        Assert.False(m.match(new AA()));
        Assert.False(m.match(new AAA()));
        Assert.False(m.match(new B()));
        Assert.False(m.match(new BB()));
        Assert.False(m.match(new BBB()));
        Assert.False(m.match(new C()));
        Assert.True(m.match(new CC()));
    }

    [Fact]
    public void testInaccessibleClass()
    {
        TypeParameterMatcher m = TypeParameterMatcher.find(new U<T>() { }, U<>.class, "E");
        Assert.False(m.match(new object()));
        Assert.True(m.match(new T()));
    }


    [Fact]
    public void testArrayAsTypeParam()
    {
        TypeParameterMatcher m = TypeParameterMatcher.find(new U<byte[]>() { }, U<>.class, "E");
        Assert.False(m.match(new object()));
        Assert.True(m.match(new byte[1]));
    }

    [Fact]
    public void testRawType()
    {
        TypeParameterMatcher m = TypeParameterMatcher.find(new U<>() { }, U<>.class, "E");
        Assert.True(m.match(new object()));
    }


    [Fact]
    public void testInnerClass()
    {
        TypeParameterMatcher m = TypeParameterMatcher.find(new V<string>().u, U<>.class, "E");
        Assert.True(m.match(new object()));
    }


    [Fact]
    public void testErasure()
    {
        Assert.Throws<Exception>(() =>
        {
            TypeParameterMatcher m = TypeParameterMatcher.find(new X<string, DateTime>(), W.class, "E");
            Assert.True(m.match(new DateTime()));
            Assert.False(m.match(new object()));
        });
    }
}