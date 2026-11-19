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

using System;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec;

public class DecoderResult
{
    protected static readonly Signal SIGNAL_UNFINISHED = Signal.ValueOf(typeof(DecoderResult), "UNFINISHED");
    protected static readonly Signal SIGNAL_SUCCESS = Signal.ValueOf(typeof(DecoderResult), "SUCCESS");

    public static readonly DecoderResult UNFINISHED = new DecoderResult(SIGNAL_UNFINISHED);
    public static readonly DecoderResult SUCCESS = new DecoderResult(SIGNAL_SUCCESS);

    public static DecoderResult Failure(Exception cause)
    {
        return new DecoderResult(ObjectUtil.CheckNotNull(cause, "cause"));
    }

    private readonly Exception cause;

    protected DecoderResult(Exception cause)
    {
        this.cause = ObjectUtil.CheckNotNull(cause, "cause");
    }

    public bool IsFinished()
    {
        return cause != SIGNAL_UNFINISHED;
    }

    public bool IsSuccess()
    {
        return cause == SIGNAL_SUCCESS;
    }

    public bool IsFailure()
    {
        return cause != SIGNAL_SUCCESS && cause != SIGNAL_UNFINISHED;
    }

    public Exception Cause()
    {
        if (IsFailure())
        {
            return cause;
        }
        else
        {
            return null;
        }
    }

    public override string ToString()
    {
        if (IsFinished())
        {
            if (IsSuccess())
            {
                return "success";
            }

            // CLR: cause().toString() is the Java Throwable text (see CodecException.ThrowableToString).
            string cause = CodecException.ThrowableToString(Cause());
            return new StringBuilder(cause.Length + 17)
                .Append("failure(")
                .Append(cause)
                .Append(')')
                .ToString();
        }
        else
        {
            return "unfinished";
        }
    }
}
