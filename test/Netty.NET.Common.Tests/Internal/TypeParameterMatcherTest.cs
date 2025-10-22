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

namespace Netty.NET.Common.Tests.Internal;


public class TypeParameterMatcherTest {

    [Fact]
    public void testConcreteClass() {
        TypeParameterMatcher m = TypeParameterMatcher.find(new TypeQ<>(), TypeX.class, "A");
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
    public void testUnsolvedParameter() {
        Assert.Throws<IllegalStateException>(new Executable() {
        @Override
        public void execute() {
        TypeParameterMatcher.find(new TypeQ(), TypeX.class, "B");
    }
});

}

[Fact]
public void testAnonymousClass() {
    TypeParameterMatcher m = TypeParameterMatcher.find(new TypeQ<BBB>() { }, TypeX.class, "B");
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
public void testAbstractClass() {
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

public static class TypeX<A, B, C> {
    A a;
    B b;
    C c;
}

public static class TypeY<D extends C, E extends A, F extends B> extends TypeX<E, F, D> { }

public abstract static class TypeZ<G extends AA, H extends BB> extends TypeY<CC, G, H> { }

public static class TypeQ<I extends BBB> extends TypeZ<AAA, I> { }

public static class A { }
public static class AA extends A { }
public static class AAA extends AA { }

public static class B { }
public static class BB extends B { }
public static class BBB extends BB { }

public static class C { }
public static class CC extends C { }

[Fact]
public void testInaccessibleClass() {
    TypeParameterMatcher m = TypeParameterMatcher.find(new U<T>() { }, U<>.class, "E");
    Assert.False(m.match(new object()));
    Assert.True(m.match(new T()));
}

private static class T { }
private static class U<E> { E a; }

[Fact]
public void testArrayAsTypeParam() {
    TypeParameterMatcher m = TypeParameterMatcher.find(new U<byte[]>() { }, U<>.class, "E");
    Assert.False(m.match(new object()));
    Assert.True(m.match(new byte[1]));
}

[Fact]
public void testRawType() {
    TypeParameterMatcher m = TypeParameterMatcher.find(new U<>() { }, U<>.class, "E");
    Assert.True(m.match(new object()));
}

private static class V<E> {
    U<E> u = new U<E>() { };
}

[Fact]
public void testInnerClass() {
    TypeParameterMatcher m = TypeParameterMatcher.find(new V<string>().u, U<>.class, "E");
    Assert.True(m.match(new object()));
}

private abstract static class W<E> {
    E e;
}

private static class X<T, E> extends W<E> {
    T t;
}

[Fact]
public void testErasure() {
    Assert.Throws<IllegalStateException>(new Executable() {
        @Override
        public void execute() {
        TypeParameterMatcher m = TypeParameterMatcher.find(new X<string, Date>(), W.class, "E");
        Assert.True(m.match(new Date()));
        Assert.False(m.match(new object()));
    }
    });
}
}