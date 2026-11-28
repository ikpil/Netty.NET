/*
 * Copyright 2021 The Netty Project
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
using System.Threading.Tasks;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: Class<T> is System.Type, so isValid(frameType, msg) is Type.IsInstanceOfType and the generic validate
// methods take the expected type as their type argument T (a class constraint, as the result is null when msg is
// not a T) and the Type value as in Java. ChannelPromise is TaskCompletionSource; promise.setFailure(e) is
// SetException, which throws on a completed promise as setFailure does. The @Nullable (org.jetbrains.annotations)
// annotations have no counterpart.
internal static class Http3FrameValidationUtils
{

    // CLR: the private constructor of the final class is the static class itself.
    // no instances

    private static T Cast<T>(object msg) where T : class
    {
        return (T) msg;
    }

    // CLR: the unused type parameter of isValid(Class<T>, Object) is left out (Type is not generic).
    private static bool IsValid(Type frameType, object msg)
    {
        return frameType.IsInstanceOfType(msg);
    }

    /**
     * Check if the passed {@code msg} is of the {@code expectedFrameType} and return the expected type, else return
     * {@code null}.
     *
     * @param expectedFrameType {@link Class} of the expected frame type.
     * @param msg to validate.
     * @param <T> Expected type.
     * @return {@code msg} as expected frame type or {@code null} if it can not be converted to the expected type.
     */
    internal static T ValidateFrameWritten<T>(Type expectedFrameType, object msg) where T : class
    {
        if (IsValid(expectedFrameType, msg))
        {
            return Cast<T>(msg);
        }

        return null;
    }

    /**
     * Check if the passed {@code msg} is of the {@code expectedFrameType} and return the expected type, else return
     * {@code null}.
     *
     * @param expectedFrameType {@link Class} of the expected frame type.
     * @param msg to validate.
     * @param <T> Expected type.
     * @return {@code msg} as expected frame type or {@code null} if it can not be converted to the expected type.
     */
    internal static T ValidateFrameRead<T>(Type expectedFrameType, object msg) where T : class
    {
        if (IsValid(expectedFrameType, msg))
        {
            return Cast<T>(msg);
        }

        return null;
    }

    /**
     * Handle unexpected frame type by failing the passed {@link ChannelPromise}.
     *
     * @param promise to fail.
     * @param frame which is unexpected.
     */
    internal static void FrameTypeUnexpected(TaskCompletionSource promise, object frame)
    {
        string type = StringUtil.SimpleClassName(frame);
        ReferenceCountUtil.Release(frame);
        promise.SetException(new Http3Exception(Http3ErrorCode.H3_FRAME_UNEXPECTED,
                "Frame of type " + type + " unexpected"));
    }

    /**
     * Handle unexpected frame type by propagating a connection error with code:
     * {@link Http3ErrorCode#H3_FRAME_UNEXPECTED}.
     *
     * @param ctx to use for propagation of failure.
     * @param frame which is unexpected.
     */
    internal static void FrameTypeUnexpected(IChannelHandlerContext ctx, object frame)
    {
        ReferenceCountUtil.Release(frame);
        Http3CodecUtils.ConnectionError(ctx, Http3ErrorCode.H3_FRAME_UNEXPECTED, "Frame type unexpected", true);
    }
}
