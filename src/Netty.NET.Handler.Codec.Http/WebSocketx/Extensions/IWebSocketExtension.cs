/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions;

/**
 * Created once the handshake phase is done.
 */
// CLR: the Java interface takes the I prefix (owner rule). Its int fields are implicitly public static final constants;
// a C# interface may declare constants
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface), but implementing classes do
// not inherit static members, so they write IWebSocketExtension.RSV1.
public interface IWebSocketExtension
{

    const int RSV1 = 0x04;
    const int RSV2 = 0x02;
    const int RSV3 = 0x01;

    /**
     * @return the reserved bit value to ensure that no other extension should interfere.
     */
    int Rsv();

    /**
     * @return create the extension encoder.
     */
    WebSocketExtensionEncoder NewExtensionEncoder();

    /**
     * @return create the extension decoder.
     */
    WebSocketExtensionDecoder NewExtensionDecoder();

}
