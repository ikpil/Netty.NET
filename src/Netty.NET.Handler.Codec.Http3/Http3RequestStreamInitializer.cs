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

using System;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * Abstract base class that users can extend to init HTTP/3 request-streams. This initializer
 * will automatically add HTTP/3 codecs etc to the {@link ChannelPipeline} as well.
 */
// CLR: ChannelInitializer<QuicStreamChannel> is Transport's ChannelInitializer<IQuicStreamChannel>; the protected final
// override of initChannel is a protected sealed override. ch.parent().pipeline().get(Http3ConnectionHandler.class) is
// Parent.Pipeline.Get<Http3ConnectionHandler>() and IllegalStateException is InvalidOperationException.
public abstract class Http3RequestStreamInitializer : ChannelInitializer<IQuicStreamChannel>
{

    protected sealed override void InitChannel(IQuicStreamChannel ch)
    {
        IChannelPipeline pipeline = ch.Pipeline;
        Http3ConnectionHandler connectionHandler = ch.Parent.Pipeline.Get<Http3ConnectionHandler>();
        if (connectionHandler == null)
        {
            throw new InvalidOperationException("Couldn't obtain the " +
                    StringUtil.SimpleClassName(typeof(Http3ConnectionHandler)) + " of the parent Channel");
        }

        Http3RequestStreamEncodeStateValidator encodeStateValidator = new Http3RequestStreamEncodeStateValidator();
        Http3RequestStreamDecodeStateValidator decodeStateValidator = new Http3RequestStreamDecodeStateValidator();

        // Add the encoder and decoder in the pipeline so we can handle Http3Frames
        pipeline.AddLast(connectionHandler.NewCodec(encodeStateValidator, decodeStateValidator));
        // Add the handler that will validate what we write and receive on this stream.
        pipeline.AddLast(encodeStateValidator);
        pipeline.AddLast(decodeStateValidator);
        pipeline.AddLast(connectionHandler.NewRequestStreamValidationHandler(ch, encodeStateValidator,
                decodeStateValidator));
        InitRequestStream(ch);
    }

    /**
     * Init the {@link QuicStreamChannel} to handle {@link Http3RequestStreamFrame}s. At the point of calling this
     * method it is already valid to write {@link Http3RequestStreamFrame}s as the codec is already in the pipeline.
     * @param ch    the {QuicStreamChannel} for the request stream.
     */
    protected abstract void InitRequestStream(IQuicStreamChannel ch);
}
