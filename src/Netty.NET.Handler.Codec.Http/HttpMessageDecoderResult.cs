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

namespace Netty.NET.Handler.Codec.Http;

/**
 * A {@link DecoderResult} for {@link HttpMessage}s as produced by an {@link HttpObjectDecoder}.
 * <p>
 * Please note that there is no guarantee that a {@link HttpObjectDecoder} will produce a {@link
 * HttpMessageDecoderResult}. It may simply produce a regular {@link DecoderResult}. This result is intended for
 * successful {@link HttpMessage} decoder results.
 */
// CLR: the package-private constructor is internal; SIGNAL_SUCCESS is the protected codec-base DecoderResult field.
public sealed class HttpMessageDecoderResult : DecoderResult
{

    private readonly int initialLineLength;
    private readonly int headerSize;

    internal HttpMessageDecoderResult(int initialLineLength, int headerSize) : base(SIGNAL_SUCCESS)
    {
        this.initialLineLength = initialLineLength;
        this.headerSize = headerSize;
    }

    /**
     * The decoded initial line length (in bytes), as controlled by {@code maxInitialLineLength}.
     */
    public int InitialLineLength()
    {
        return initialLineLength;
    }

    /**
     * The decoded header size (in bytes), as controlled by {@code maxHeaderSize}.
     */
    public int HeaderSize()
    {
        return headerSize;
    }

    /**
     * The decoded initial line length plus the decoded header size (in bytes).
     */
    public int TotalSize()
    {
        return initialLineLength + headerSize;
    }
}
