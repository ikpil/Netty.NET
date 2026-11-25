/*
 * Copyright 2019 The Netty Project
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
 * Extension filter provider that is responsible to provide filters for a certain {@link WebSocketExtension} extension.
 */
// CLR: the Java interface takes the I prefix (owner rule). DEFAULT is a static readonly field of the interface
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface) and its anonymous class is
// the private nested class DefaultFilterProvider (docs/codec-base-porting.md, Anonymous classes).
public interface IWebSocketExtensionFilterProvider
{

    static readonly IWebSocketExtensionFilterProvider DEFAULT = new DefaultFilterProvider();

    /**
     * Returns the extension filter for {@link WebSocketExtensionEncoder} encoder.
     */
    IWebSocketExtensionFilter EncoderFilter();

    /**
     * Returns the extension filter for {@link WebSocketExtensionDecoder} decoder.
     */
    IWebSocketExtensionFilter DecoderFilter();

    private sealed class DefaultFilterProvider : IWebSocketExtensionFilterProvider
    {
        public IWebSocketExtensionFilter EncoderFilter()
        {
            return IWebSocketExtensionFilter.NEVER_SKIP;
        }

        public IWebSocketExtensionFilter DecoderFilter()
        {
            return IWebSocketExtensionFilter.NEVER_SKIP;
        }
    }

}
