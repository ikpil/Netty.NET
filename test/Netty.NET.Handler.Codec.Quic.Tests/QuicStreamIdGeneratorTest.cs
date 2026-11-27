/*
 * Copyright 2020 The Netty Project
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

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: QuicStreamIdGenerator is internal (package-private in Java).
public class QuicStreamIdGeneratorTest : AbstractQuicTest
{

    [Fact]
    public void TestServerStreamIds()
    {
        QuicStreamIdGenerator generator = new QuicStreamIdGenerator(true);
        Assert.Equal(1, generator.NextStreamId(true));
        Assert.Equal(5, generator.NextStreamId(true));
        Assert.Equal(3, generator.NextStreamId(false));
        Assert.Equal(9, generator.NextStreamId(true));
        Assert.Equal(7, generator.NextStreamId(false));
    }

    [Fact]
    public void TestClientStreamIds()
    {
        QuicStreamIdGenerator generator = new QuicStreamIdGenerator(false);
        Assert.Equal(0, generator.NextStreamId(true));
        Assert.Equal(4, generator.NextStreamId(true));
        Assert.Equal(2, generator.NextStreamId(false));
        Assert.Equal(8, generator.NextStreamId(true));
        Assert.Equal(6, generator.NextStreamId(false));
    }
}
