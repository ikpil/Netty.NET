/*
 * Copyright 2025 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the subclass in another package of Java is a subclass in the test assembly; Http2FrameCodec's protected
// constructor is protected internal (src/Netty.NET.Handler.Codec.Http2/Http2FrameCodec.cs), which a derived class
// calls through its protected part, as Java's subclass does. The package-private constructor of MyHttp2FrameCodec is
// internal.
public class Http2FrameCodecSubClassTest
{

    private class MyHttp2FrameCodec : Http2FrameCodec
    {
        internal MyHttp2FrameCodec(IHttp2ConnectionEncoder encoder, IHttp2ConnectionDecoder decoder,
                                 Http2Settings initialSettings, bool decoupleCloseAndGoAway, bool flushPreface)
            : base(encoder, decoder, initialSettings, decoupleCloseAndGoAway, flushPreface)
        {
        }
    }

    [Fact]
    public void TestCompiles()
    {
        IHttp2Connection conn = new DefaultHttp2Connection(true);
        IHttp2ConnectionEncoder enc = new DefaultHttp2ConnectionEncoder(conn, new DefaultHttp2FrameWriter());
        IHttp2ConnectionDecoder dec = new DefaultHttp2ConnectionDecoder(conn, enc, new DefaultHttp2FrameReader());
        _ = new MyHttp2FrameCodec(enc, dec, new Http2Settings(), false, true);
    }
}
