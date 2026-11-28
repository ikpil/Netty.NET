/*
 * Copyright 2015 The Netty Project
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

using Netty.NET.Common;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: sendUnsupportedVersionResponse(ch) is SendUnsupportedVersionResponseAsync(ch) with the Task discarded, as the
// source discards the ChannelFuture; fail(...) is Assert.Fail and readOutbound() is ReadOutbound<T>(). The message of
// fail prints the object with its ToString(), as Java's string concatenation does (null prints "null").
public class WebSocketServerHandshakerFactoryTest
{

    [Fact]
    public void TestUnsupportedVersion()
    {
        EmbeddedChannel ch = new EmbeddedChannel();
        _ = WebSocketServerHandshakerFactory.SendUnsupportedVersionResponseAsync(ch);
        ch.RunPendingTasks();
        object msg = ch.ReadOutbound<object>();

        if (!(msg is IFullHttpResponse))
        {
            Assert.Fail("Got wrong response " + (msg?.ToString() ?? "null"));
        }

        IFullHttpResponse response = (IFullHttpResponse) msg;

        Assert.Equal(HttpResponseStatus.UPGRADE_REQUIRED, response.Status());
        Assert.Equal(WebSocketVersion.V13.ToHttpHeaderValue(),
                response.Headers().Get(HttpHeaderNames.SEC_WEBSOCKET_VERSION));
        Assert.True(HttpUtil.IsContentLengthSet(response));
        Assert.Equal(0, HttpUtil.GetContentLength(response));

        ReferenceCountUtil.Release(response);
        Assert.False(ch.Finish());
    }
}
