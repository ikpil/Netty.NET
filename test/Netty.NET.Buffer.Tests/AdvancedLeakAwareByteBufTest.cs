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
using System.Text;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

public class AdvancedLeakAwareByteBufTest : SimpleLeakAwareByteBufTest
{
    protected override Type LeakClass()
    {
        return typeof(AdvancedLeakAwareByteBuf);
    }

    private protected override SimpleLeakAwareByteBuf Wrap(ByteBuf buffer, IResourceLeakTracker<ByteBuf> tracker)
    {
        return new AdvancedLeakAwareByteBuf(buffer, tracker);
    }

    [Fact]
    public void TestAddComponentWithLeakAwareByteBuf()
    {
        NoopResourceLeakTracker<ByteBuf> tracker = new NoopResourceLeakTracker<ByteBuf>();

        // CLR: CharsetUtil.US_ASCII is System.Text.Encoding.ASCII
        // (https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.ascii?view=net-10.0).
        ByteBuf buffer = Unpooled.WrappedBuffer(Encoding.ASCII.GetBytes("hello world")).Slice(6, 5);
        ByteBuf leakAwareBuf = Wrap(buffer, tracker);

        CompositeByteBuf composite = Unpooled.CompositeBuffer();
        // CLR: addComponent(boolean increaseWriterIndex, ByteBuf buffer) is
        // AddComponent(ByteBuf buffer, bool increaseWriterIndex) (src/Netty.NET.Buffer/CompositeByteBuf.cs).
        composite.AddComponent(leakAwareBuf, true);
        byte[] result = new byte[5];
        ByteBuf bb = composite.Component(0);
        bb.ReadBytes(result);
        Assert.Equal(Encoding.ASCII.GetBytes("world"), result);
        composite.Release();
    }
}
