/*
 * Copyright 2014 The Netty Project
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

using System;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Reads HTTP/2 frames from an input {@link ByteBuf} and notifies the specified
 * {@link Http2FrameListener} when frames are complete.
 */
// CLR: Closeable keeps close() as Close() and implements IDisposable.Dispose() with it (docs/handler-porting.md:
// "Closeable keeps close() as Close() and implements IDisposable.Dispose() with it").
// CLR: the nested interface Configuration takes the I prefix.
public interface IHttp2FrameReader : IDisposable
{
    /**
     * Configuration specific to {@link Http2FrameReader}
     */
    public interface IConfiguration
    {
        /**
         * Get the {@link Http2HeadersDecoder.Configuration} for this {@link Http2FrameReader}
         */
        IHttp2HeadersDecoder.IConfiguration HeadersConfiguration();

        /**
         * Get the {@link Http2FrameSizePolicy} for this {@link Http2FrameReader}
         */
        IHttp2FrameSizePolicy FrameSizePolicy();
    }

    /**
     * Attempts to read the next frame from the input buffer. If enough data is available to fully
     * read the frame, notifies the listener of the read frame.
     */
    void ReadFrame(IChannelHandlerContext ctx, ByteBuf input, IHttp2FrameListener listener);

    /**
     * Get the configuration related elements for this {@link Http2FrameReader}
     */
    IConfiguration Configuration();

    /**
     * Closes this reader and frees any allocated resources.
     */
    void Close();

    void IDisposable.Dispose()
    {
        Close();
    }
}
