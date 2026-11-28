/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class SimpleLeakAwareCompositeByteBufTest : WrappedCompositeByteBufTest
{
    private readonly Type clazz;
    private readonly Queue<NoopResourceLeakTracker<ByteBuf>> trackers = new Queue<NoopResourceLeakTracker<ByteBuf>>();

    // CLR: the base constructor runs the original init() (it allocates the fixture buffer through
    // NewBuffer); the derived constructor body then performs this fixture's init() step.
    public SimpleLeakAwareCompositeByteBufTest()
    {
        // CLR: a C# field initializer cannot call the instance method leakClass() (CS0236,
        // https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236), so it is assigned here.
        clazz = LeakClass();
        trackers.Clear();
    }

    private protected sealed override WrappedCompositeByteBuf Wrap(CompositeByteBuf buffer)
    {
        NoopResourceLeakTracker<ByteBuf> tracker = new NoopResourceLeakTracker<ByteBuf>();
        WrappedCompositeByteBuf leakAwareBuf = Wrap(buffer, tracker);
        trackers.Enqueue(tracker);
        return leakAwareBuf;
    }

    // CLR: private protected keeps the original subclass hook while returning the internal
    // SimpleLeakAwareCompositeByteBuf type from a public fixture (CS0051,
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/private-protected).
    private protected virtual SimpleLeakAwareCompositeByteBuf Wrap(CompositeByteBuf buffer, IResourceLeakTracker<ByteBuf> tracker)
    {
        return new SimpleLeakAwareCompositeByteBuf(buffer, tracker);
    }

    public override void Dispose()
    {
        base.Dispose();

        for (;;)
        {
            if (!trackers.TryDequeue(out NoopResourceLeakTracker<ByteBuf> tracker))
            {
                break;
            }

            Assert.True(tracker.Get());
        }
    }

    protected virtual Type LeakClass()
    {
        return typeof(SimpleLeakAwareByteBuf);
    }

    [Fact]
    public void TestWrapSlice()
    {
        AssertWrapped(NewBuffer(8).Slice());
    }

    [Fact]
    public void TestWrapSlice2()
    {
        AssertWrapped(NewBuffer(8).Slice(0, 1));
    }

    [Fact]
    public void TestWrapReadSlice()
    {
        ByteBuf buffer = NewBuffer(8);
        if (buffer.IsReadable())
        {
            AssertWrapped(buffer.ReadSlice(1));
        }
        else
        {
            Assert.True(buffer.Release());
        }
    }

    [Fact]
    public void TestWrapRetainedSlice()
    {
        ByteBuf buffer = NewBuffer(8);
        AssertWrapped(buffer.RetainedSlice());
        Assert.True(buffer.Release());
    }

    [Fact]
    public void TestWrapRetainedSlice2()
    {
        ByteBuf buffer = NewBuffer(8);
        if (buffer.IsReadable())
        {
            AssertWrapped(buffer.RetainedSlice(0, 1));
        }

        Assert.True(buffer.Release());
    }

    [Fact]
    public void TestWrapReadRetainedSlice()
    {
        ByteBuf buffer = NewBuffer(8);
        if (buffer.IsReadable())
        {
            AssertWrapped(buffer.ReadRetainedSlice(1));
        }

        Assert.True(buffer.Release());
    }

    [Fact]
    public void TestWrapDuplicate()
    {
        AssertWrapped(NewBuffer(8).Duplicate());
    }

    [Fact]
    public void TestWrapRetainedDuplicate()
    {
        ByteBuf buffer = NewBuffer(8);
        AssertWrapped(buffer.RetainedDuplicate());
        Assert.True(buffer.Release());
    }

    [Fact]
    public void TestWrapReadOnly()
    {
        AssertWrapped(NewBuffer(8).AsReadOnly());
    }

    [Fact]
    public void ForEachByteUnderLeakDetectionShouldNotThrowException()
    {
        CompositeByteBuf buf = (CompositeByteBuf)NewBuffer(8);
        // CLR: JUnit assertInstanceOf accepts subclasses; xUnit Assert.IsAssignableFrom has that meaning
        // (https://learn.microsoft.com/en-us/dotnet/api/system.type.isassignablefrom?view=net-10.0).
        Assert.IsAssignableFrom<SimpleLeakAwareCompositeByteBuf>(buf);
        CompositeByteBuf comp = (CompositeByteBuf)NewBuffer(8);
        Assert.IsAssignableFrom<SimpleLeakAwareCompositeByteBuf>(comp);

        ByteBuf inner = comp.Alloc().DirectBuffer(1).WriteByte(0);
        comp.AddComponent(inner, true);
        buf.AddComponent(comp, true);

        // CLR: the ByteProcessor interface is a Func<byte, bool> predicate (src/Netty.NET.Common/ByteProcessor.cs).
        Assert.Equal(-1, buf.ForEachByte(value => true));
        Assert.True(buf.Release());
    }

    protected void AssertWrapped(ByteBuf buf)
    {
        try
        {
            Assert.Same(clazz, buf.GetType());
        }
        finally
        {
            buf.Release();
        }
    }
}
