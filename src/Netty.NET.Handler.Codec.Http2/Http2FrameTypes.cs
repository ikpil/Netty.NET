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

namespace Netty.NET.Handler.Codec.Http2;

/**
 * Registry of all standard frame types defined by the HTTP/2 specification.
 */
// CLR: the final class with a private constructor is a static class of byte constants.
public static class Http2FrameTypes
{
    public const byte DATA = 0x0;
    public const byte HEADERS = 0x1;
    public const byte PRIORITY = 0x2;
    public const byte RST_STREAM = 0x3;
    public const byte SETTINGS = 0x4;
    public const byte PUSH_PROMISE = 0x5;
    public const byte PING = 0x6;
    public const byte GO_AWAY = 0x7;
    public const byte WINDOW_UPDATE = 0x8;
    public const byte CONTINUATION = 0x9;
}
