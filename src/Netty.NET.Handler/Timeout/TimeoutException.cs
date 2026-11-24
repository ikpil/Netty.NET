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

using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Timeout;

/**
 * A {@link TimeoutException} when no data was either read or written within a
 * certain period of time.
 */
// CLR: serialVersionUID is JVM serialization metadata and has no counterpart. The package-private constructors are
// internal.
public class TimeoutException : ChannelException
{
    internal TimeoutException()
    {
    }

    internal TimeoutException(string message, bool shared) : base(message, null, shared)
    {
    }

    // Suppress a warning since the method doesn't need synchronization
    // CLR: Throwable.fillInStackTrace() has no CLR counterpart. Its override returns this without recording frames, so
    // a Java TimeoutException never carries a stack trace; the CLR records the trace of an exception when it is thrown
    // and StackTrace is virtual, so returning null keeps that result
    // (https://learn.microsoft.com/dotnet/api/system.exception.stacktrace: "If no stack trace is available ... the value
    // is null"; "The StackTrace property is overridden in classes that require control over the stack trace content").
    public override string StackTrace => null;
}
