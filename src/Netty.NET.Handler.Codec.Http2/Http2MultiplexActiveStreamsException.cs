/*
 * Copyright 2023 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * {@link Exception} that can be used to wrap some {@link Throwable} and fire it through the pipeline.
 * The {@link Http2MultiplexHandler} will unwrap the original {@link Throwable} and fire it to all its
 * active {@link Http2StreamChannel}.
 */
// CLR: Throwable is Exception. The cause-only constructor keeps Java's detail message cause.toString() through
// Common's ThrowableUtil.ThrowableToString (docs/codec-base-porting.md, Exceptions). The fillInStackTrace() override,
// which returns this without recording frames, is an override of the virtual Exception.StackTrace that returns null,
// as the handler's TimeoutException does (docs/handler-porting.md, Exceptions).
public sealed class Http2MultiplexActiveStreamsException : Exception
{

    public Http2MultiplexActiveStreamsException(Exception cause)
        : base(cause == null ? null : ThrowableUtil.ThrowableToString(cause), cause)
    {
    }

    public override string StackTrace => null;
}
