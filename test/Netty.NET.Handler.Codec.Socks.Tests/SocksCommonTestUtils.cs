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
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests;

internal sealed class SocksCommonTestUtils
{
    /**
     * A constructor to stop this class being constructed.
     */
    private SocksCommonTestUtils()
    {
        //NOOP
    }

    // CLR: @SuppressWarnings("deprecation") is a CS0618 suppression around the call of the [Obsolete] EncodeAsByteBuf.
    public static void WriteMessageIntoEmbedder(EmbeddedChannel embedder, SocksMessage msg)
    {
        ByteBuf buf = Unpooled.Buffer();
#pragma warning disable CS0618
        msg.EncodeAsByteBuf(buf);
#pragma warning restore CS0618
        embedder.WriteInbound(buf);
    }
}
