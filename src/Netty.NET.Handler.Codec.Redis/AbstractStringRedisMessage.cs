/*
 * Copyright 2016 The Netty Project
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

using System.Text;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * Abstract class for Simple Strings or Errors.
 */
// CLR: the package-private constructor is internal.
[UnstableApi]
public abstract class AbstractStringRedisMessage : IRedisMessage
{

    private readonly string content;

    internal AbstractStringRedisMessage(string content)
    {
        this.content = ObjectUtil.CheckNotNull(content, "content");
    }

    /**
     * Get string content of this {@link AbstractStringRedisMessage}.
     *
     * @return content of this message.
     */
    public string Content()
    {
        return content;
    }

    public override string ToString()
    {
        return new StringBuilder(StringUtil.SimpleClassName(this))
                .Append('[')
                .Append("content=")
                .Append(content)
                .Append(']').ToString();
    }
}
