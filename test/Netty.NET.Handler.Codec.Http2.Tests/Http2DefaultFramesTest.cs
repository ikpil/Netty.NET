/*
 * Copyright 2019 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: equals/hashCode are Equals/GetHashCode; assertEquals(a, b) of two frames is Assert.Equal on the frame type,
// which calls Equals, and assertNotEquals of two hash codes is Assert.NotEqual. @SuppressWarnings has no counterpart.
// The private nested TestStreamFrame overrides the abstract Name() of AbstractHttp2StreamFrame.
public class Http2DefaultFramesTest
{

    [Fact]
    public void TestEqualOperation()
    {
        // in this case, 'goAwayFrame' and 'unknownFrame' will also have an EMPTY_BUFFER data
        // so we want to check that 'dflt' will not consider them equal.
        DefaultHttp2GoAwayFrame goAwayFrame = new DefaultHttp2GoAwayFrame(1);
        DefaultHttp2UnknownFrame unknownFrame = new DefaultHttp2UnknownFrame((byte) 1, new Http2Flags((short) 1));
        DefaultByteBufHolder dflt = new DefaultByteBufHolder(Unpooled.EmptyBuffer);
        try
        {
            // not using 'assertNotEquals' to be explicit about which object we are calling .equals() on
            Assert.False(dflt.Equals(goAwayFrame));
            Assert.False(dflt.Equals(unknownFrame));
        }
        finally
        {
            goAwayFrame.Release();
            unknownFrame.Release();
            dflt.Release();
        }
    }

    // Reproduces https://github.com/netty/netty/issues/13659
    // AbstractHttp2StreamFrame.equals() treats two frames with a null stream as equal, but
    // AbstractHttp2StreamFrame.hashCode() previously returned super.hashCode() (identity hash)
    // in that case, producing different hashes for equal instances and violating the
    // Object.hashCode contract.
    [Fact]
    public void TestAbstractHttp2StreamFrameEqualInstancesHaveEqualHashCodes()
    {
        AbstractHttp2StreamFrame a = new TestStreamFrame();
        AbstractHttp2StreamFrame b = new TestStreamFrame();
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // Reproduces https://github.com/netty/netty/issues/13659 for DefaultHttp2PingFrame.
    // equals() compares ack and content; before the fix hashCode() folded in the identity
    // hash of Object, so two equal pings had different hash codes.
    [Fact]
    public void TestDefaultHttp2PingFrameEqualInstancesHaveEqualHashCodes()
    {
        DefaultHttp2PingFrame a = new DefaultHttp2PingFrame(42L, true);
        DefaultHttp2PingFrame b = new DefaultHttp2PingFrame(42L, true);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // Smoke test that the hash actually folds in content and ack. This is a regression guard
    // against the pre-fix implementation, which seeded the hash with Object identity and never
    // folded content into the result at all. Specific hashCode values are an implementation
    // detail; we only assert that the chosen widely-spread inputs do not collide.
    [Fact]
    public void TestDefaultHttp2PingFrameHashCodeDistinguishesDifferentValues()
    {
        DefaultHttp2PingFrame a = new DefaultHttp2PingFrame(0L, false);
        DefaultHttp2PingFrame differentContent = new DefaultHttp2PingFrame(long.MaxValue, false);
        DefaultHttp2PingFrame differentAck = new DefaultHttp2PingFrame(0L, true);
        Assert.NotEqual(a.GetHashCode(), differentContent.GetHashCode());
        Assert.NotEqual(a.GetHashCode(), differentAck.GetHashCode());
    }

    private sealed class TestStreamFrame : AbstractHttp2StreamFrame
    {
        public override string Name()
        {
            return "TEST";
        }
    }
}
