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
 * Filter that is responsible to skip the evaluation of a certain extension
 * according to standard.
 */
// CLR: the Java interface takes the I prefix (owner rule). Its constant fields are static readonly fields of the
// interface, which a C# interface may declare together with nested types
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface); the anonymous classes are
// the private nested classes NeverSkipFilter and AlwaysSkipFilter (docs/codec-base-porting.md, Anonymous classes).
public interface IWebSocketExtensionFilter
{

    /**
     * A {@link WebSocketExtensionFilter} that never skip the evaluation of an
     * any given extensions {@link WebSocketExtension}.
     */
    static readonly IWebSocketExtensionFilter NEVER_SKIP = new NeverSkipFilter();

    /**
     * A {@link WebSocketExtensionFilter} that always skip the evaluation of an
     * any given extensions {@link WebSocketExtension}.
     */
    static readonly IWebSocketExtensionFilter ALWAYS_SKIP = new AlwaysSkipFilter();

    /**
     * Returns {@code true} if the evaluation of the extension must skipped
     * for the given frame otherwise {@code false}.
     */
    bool MustSkip(WebSocketFrame frame);

    private sealed class NeverSkipFilter : IWebSocketExtensionFilter
    {
        public bool MustSkip(WebSocketFrame frame)
        {
            return false;
        }
    }

    private sealed class AlwaysSkipFilter : IWebSocketExtensionFilter
    {
        public bool MustSkip(WebSocketFrame frame)
        {
            return true;
        }
    }

}
