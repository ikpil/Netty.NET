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
using Netty.NET.Handler.Codec.Socksx.V4;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V4;

internal sealed class Socks4CommonTestUtils
{
    /**
     * A constructor to stop this class being constructed.
     */
    private Socks4CommonTestUtils()
    {
        //NOOP
    }

    public static void WriteMessageIntoEmbedder(EmbeddedChannel embedder, ISocks4Message msg)
    {
        EmbeddedChannel @out;

        if (msg is ISocks4CommandRequest)
        {
            @out = new EmbeddedChannel(Socks4ClientEncoder.INSTANCE);
        }
        else
        {
            @out = new EmbeddedChannel(Socks4ServerEncoder.INSTANCE);
        }

        @out.WriteOutbound(msg);
        embedder.WriteInbound(@out.ReadOutbound<ByteBuf>());
        @out.Finish();
    }
}
