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

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Exception during handshaking process
 */
// CLR: RuntimeException is Exception (docs/codec-base-porting.md); serialVersionUID is JVM serialization metadata and
// has no counterpart.
public class WebSocketHandshakeException : Exception
{

    public WebSocketHandshakeException(string s) : base(s)
    {
    }

    public WebSocketHandshakeException(string s, Exception throwable) : base(s, throwable)
    {
    }
}
