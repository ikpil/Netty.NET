/*
 * Copyright 2016 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * An HTTP/2 exception for a specific {@link Http2FrameStream}.
 */
// CLR: the checked Java exception is a CLR Exception; serialVersionUID is JVM serialization metadata and has no
// counterpart; Throwable is Exception and cause.getMessage() is cause.Message.
public sealed class Http2FrameStreamException : Exception
{
    private readonly Http2Error error;
    private readonly IHttp2FrameStream stream;

    public Http2FrameStreamException(IHttp2FrameStream stream, Http2Error error, Exception cause)
        : base(cause.Message, cause)
    {
        this.stream = CheckNotNull(stream, "stream");
        this.error = CheckNotNull(error, "error");
    }

    public Http2Error Error()
    {
        return error;
    }

    public IHttp2FrameStream Stream()
    {
        return stream;
    }
}
