/*
 * Copyright 2026 The Netty Project
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

namespace Netty.NET.Handler.Ssl.Ocsp;

/**
 * Thrown internally by {@link OcspClient} if the certificate being checked does not specify any OCSP Responder.
 * <p>
 * This exception extends {@link NullPointerException} for backwards compatibility reasons.
 */
// CLR: NullPointerException is ArgumentNullException (docs/codec-base-porting.md), so the exception derives from it to
// keep the relation. The message-only constructor passes no parameter name: ArgumentNullException(string) takes the
// parameter name, and ArgumentNullException(string paramName, string message) "Initializes an instance of the
// ArgumentNullException class with a specified error message and the name of the parameter that causes this exception"
// (https://learn.microsoft.com/en-us/dotnet/api/system.argumentnullexception.-ctor?view=net-10.0).
public sealed class NoOcspResponderException : ArgumentNullException
{
    /**
     * Create a new instance with the given message.
     * @param message The error message.
     */
    public NoOcspResponderException(string message) : base(null, message)
    {
    }
}
