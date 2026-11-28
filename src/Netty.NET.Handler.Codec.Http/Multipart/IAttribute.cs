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

namespace Netty.NET.Handler.Codec.Http.Multipart;

/**
 * Attribute interface
 */
// CLR: the Java interface Attribute takes the I prefix (owner rule); inside this namespace IAttribute binds to it,
// not to the Common IAttribute<T> of the attribute map. copy(), duplicate(), retainedDuplicate(),
// replace(ByteBuf), retain(), retain(int), touch() and touch(Object) are re-declared only to narrow the return
// type, so they are inherited from IByteBufHolder; the classes narrow them with covariant override returns.
public interface IAttribute : IHttpData
{
    /**
     * Returns the value of this HttpData.
     */
    string GetValue();

    /**
     * Sets the value of this HttpData.
     */
    void SetValue(string value);
}
