/*
 * Copyright 2016 The Netty Project
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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Ssl;

/**
 * A marker interface for PEM encoded values.
 */
// CLR: ByteBufHolder is IByteBufHolder, whose content() is the Content property. copy(), duplicate(),
// retainedDuplicate(), replace(ByteBuf), retain(), retain(int), touch() and touch(Object) are re-declared only to
// narrow the return type, so they are inherited from IByteBufHolder, not repeated; callers cast the IByteBufHolder
// results to IPemEncoded, as the implementations return their own type.
internal interface IPemEncoded : IByteBufHolder
{

    /**
     * Returns {@code true} if the PEM encoded value is considered
     * sensitive information such as a private key.
     */
    bool IsSensitive();
}
