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
using System.Runtime.CompilerServices;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Buffer.Tests;

public class SimpleLeakAwareByteBufTest : BigEndianHeapByteBufTest
{
    private readonly Type clazz;
    private readonly Queue<NoopResourceLeakTracker<ByteBuf>> trackers = new Queue<NoopResourceLeakTracker<ByteBuf>>();

    // CLR: the base constructor runs the original init() (it allocates the fixture buffer through
    // NewBuffer); the derived constructor body then performs this fixture's init() step.
    public SimpleLeakAwareByteBufTest()
    {
        // CLR: a C# field initializer cannot call the instance method leakClass() (CS0236,
        // https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0236), so it is assigned here.
        clazz = LeakClass();
        trackers.Clear();
    }

    protected sealed override ByteBuf NewBuffer(int capacity, int maxCapacity)
    {
        return Wrap(base.NewBuffer(capacity, maxCapacity));
    }

    private ByteBuf Wrap(ByteBuf buffer)
    {
        NoopResourceLeakTracker<ByteBuf> tracker = new NoopResourceLeakTracker<ByteBuf>();
        ByteBuf leakAwareBuf = Wrap(buffer, tracker);
        trackers.Enqueue(tracker);
        return leakAwareBuf;
    }

    // CLR: private protected keeps the original subclass hook while returning the internal
    // SimpleLeakAwareByteBuf type from a public fixture (CS0051,
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/private-protected).
    private protected virtual SimpleLeakAwareByteBuf Wrap(ByteBuf buffer, IResourceLeakTracker<ByteBuf> tracker)
    {
        return new SimpleLeakAwareByteBuf(buffer, tracker);
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

    // CLR: xUnit runs only public test methods, so the enum used as their parameter is public (CS0051).
    public enum ReleaseMechanism
    {
        RELEASE, RELEASE_COUNTED
    }

    // CLR: @EnumSource supplies every constant; MemberData enumerates Enum.GetValues the same way.
    public static IEnumerable<object[]> ReleaseMechanisms()
    {
        foreach (ReleaseMechanism mechanism in Enum.GetValues<ReleaseMechanism>())
            yield return [mechanism];
    }

    [Theory]
    [MemberData(nameof(ReleaseMechanisms))]
    public void MustAddStackTraceForOriginalCloseMethodOnDoubleFree(ReleaseMechanism mechanism)
    {
        int trackEveryBuffer = 1;
        ResourceLeakDetector<ByteBuf> detector = new ResourceLeakDetector<ByteBuf>(typeof(ByteBuf), trackEveryBuffer);
        ByteBuf origin = Unpooled.Buffer();
        ByteBuf buf = Wrap(origin, detector.Track(origin));
        OriginalCloseCall(buf, mechanism);
        // CLR: the package-private ByteBuf.isAccessible() (refCnt() != 0) is ByteBufUtil.IsAccessible
        // (src/Netty.NET.Buffer/ByteBufUtil.cs).
        Assert.False(ByteBufUtil.IsAccessible(buf));
        IllegalReferenceCountException irce = Assert.Throws<IllegalReferenceCountException>(() => buf.Release());
        Assert.Contains(nameof(OriginalCloseCall), StackTraceOf(irce));
    }

    // CLR: AssertJ hasStackTraceContaining inspects Throwable.printStackTrace output, which also prints
    // suppressed exceptions (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Throwable.html#printStackTrace()).
    // Exception.ToString covers only the exception and its inner exceptions
    // (https://learn.microsoft.com/en-us/dotnet/api/system.exception.tostring?view=net-10.0), and the port keeps
    // suppressed exceptions in ThrowableUtil (src/Netty.NET.Common/Internal/ThrowableUtil.cs), so they are appended.
    private static string StackTraceOf(Exception exception)
    {
        StringBuilder text = new StringBuilder(exception.ToString());
        foreach (Exception suppressed in ThrowableUtil.GetSuppressed(exception))
            text.Append(Environment.NewLine).Append("Suppressed: ").Append(StackTraceOf(suppressed));
        return text.ToString();
    }

    // CLR: NoInlining keeps this frame in the recorded close stack trace
    // (https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.methodimploptions?view=net-10.0).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OriginalCloseCall(ByteBuf buf, ReleaseMechanism mechanism)
    {
        switch (mechanism)
        {
            case ReleaseMechanism.RELEASE:
                buf.Release();
                break;
            case ReleaseMechanism.RELEASE_COUNTED:
                // CLR: release() and release(int) are one ByteBuf.Release(int decrement = 1) method
                // (src/Netty.NET.Buffer/ByteBuf.cs); the explicit count keeps the original call shape.
                buf.Release(1);
                break;
            default:
                Assert.Fail("Unknown release mechanism: " + mechanism);
                break;
        }
    }
}
