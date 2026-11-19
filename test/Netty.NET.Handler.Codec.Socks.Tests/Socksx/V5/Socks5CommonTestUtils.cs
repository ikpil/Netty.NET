/*
 * Copyright 2012 The Netty Project
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

using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

// CLR: the Java interface Socks5Message is ISocks5Message; out is @out.
internal sealed class Socks5CommonTestUtils
{
    /**
     * A constructor to stop this class being constructed.
     */
    private Socks5CommonTestUtils()
    {
        //NOOP
    }

    public static void WriteFromClientToServer(EmbeddedChannel embedder, ISocks5Message msg)
    {
        embedder.WriteInbound(EncodeClient(msg));
    }

    public static void WriteFromServerToClient(EmbeddedChannel embedder, ISocks5Message msg)
    {
        embedder.WriteInbound(EncodeServer(msg));
    }

    public static ByteBuf EncodeClient(ISocks5Message msg)
    {
        EmbeddedChannel @out = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);
        @out.WriteOutbound(msg);

        ByteBuf encoded = @out.ReadOutbound<ByteBuf>();
        @out.Finish();

        return encoded;
    }

    public static ByteBuf EncodeServer(ISocks5Message msg)
    {
        EmbeddedChannel @out = new EmbeddedChannel(Socks5ServerEncoder.DEFAULT);
        @out.WriteOutbound(msg);

        ByteBuf encoded = @out.ReadOutbound<ByteBuf>();
        @out.Finish();

        return encoded;
    }
}
