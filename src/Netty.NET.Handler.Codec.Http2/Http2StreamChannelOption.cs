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

using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * {@link ChannelOption}s that are specific to {@link Http2StreamChannel}s.
 *
 * @param <T>   the type of the value which is valid for the {@link ChannelOption}
 */
// CLR: the Java class is a generic subclass of ChannelOption<T> whose only members are a private constructor and the
// constant, so nothing can create or extend it. A static field of a generic C# class needs a type argument at every
// use, so the constant lives on a static class, as the Transport keeps its built-in options on the non-generic
// ChannelOption ("the registry and built-in fields live on a non-generic base",
// src/Netty.NET.Transport/Channels/ChannelOption.cs); valueOf(String) is ChannelOption.ValueOf<T>(string).
public static class Http2StreamChannelOption
{

    /**
     * When set to {@code true} {@link Http2WindowUpdateFrame}s will be automatically be generated and written for
     * {@link Http2StreamChannel}s as soon as frames are passed to the user via
     * {@link io.netty.channel.ChannelPipeline#fireChannelRead(Object)}. If the user wants more control on when a
     * window update is send its possible to set it to {@code false}. In this case the user is responsible to
     * generate the correct {@link Http2WindowUpdateFrame}s and eventually write these to the channel.
     * <p>
     * See <a href="https://datatracker.ietf.org/doc/html/rfc9113#section-5.2">RFC9113 5.2. Flow Control</a> for more
     * details.
     */
    public static readonly ChannelOption<bool> AUTO_STREAM_FLOW_CONTROL =
            ChannelOption.ValueOf<bool>("AUTO_STREAM_FLOW_CONTROL");
}
